using System.Text;
using System.Text.Json.Nodes;
using ForestBot.Code;
using ForestBot.Knowledge;
using ForestBot.Llm;
using ForestBot.Search;

namespace ForestBot.Agent;

// ------------------------------------------------------------------
// What the model can look up. Read-only, bounded output; every lookup is
// recorded so the answer can cite what was actually read.
// ------------------------------------------------------------------
public sealed class Tools
{
    public const int MaxOutput = 14000;

    private readonly Corpus _corpus;
    private readonly HybridSearch _search;
    private readonly CodeIndex _code;

    public Tools(Corpus corpus, HybridSearch search, CodeIndex code)
    {
        _corpus = corpus;
        _search = search;
        _code = code;
    }

    public bool HasCode => _code != null && _code.FileCount > 0;

    public List<ToolSpec> Specs()
    {
        List<ToolSpec> list = new List<ToolSpec>
        {
            Spec("search",
                "Search the knowledge base (cards written for runners, the game-notes reference, FSM exports). " +
                "Returns the best-matching pieces with ids and a snippet. Use the runner's own words and the game's names. " +
                "Search again with other words if the first results miss.",
                Obj(("query", Str("What to look for."), true),
                    ("kinds", Str("Optional comma list to restrict: card, doc, fsm, glossary."), false))),
            Spec("read_card",
                "Read a whole knowledge card by id (from the card index or search results). Cards are the reviewed explanations - prefer them.",
                Obj(("id", Str("Card id, e.g. bomb-boost."), true))),
            Spec("read",
                "Read the full text of a search result by its id (a card section, a game-notes section, an FSM state).",
                Obj(("id", Str("The id exactly as search printed it."), true))),
            Spec("fsm",
                "Read a PlayMaker FSM export: with only a name, its variables, events and every state with its transitions; " +
                "with a state, that state's actions in full. Much of the player's and the AI's behaviour lives in FSMs, not C#.",
                Obj(("name", Str("FSM file name, e.g. player-controlFSM, mutant-action_combatFSM. Omit to list them."), false),
                    ("state", Str("Optional state name."), false))),
        };
        if (HasCode)
        {
            list.Add(Spec("code_search",
                "Search the game's decompiled C# (Assembly-CSharp): type and member names, then every line containing the text. " +
                "Finds who calls a method, including calls by string (SendMessage(\"name\"), StartCoroutine(\"name\")).",
                Obj(("query", Str("A type / method / field name or a code fragment."), true))));
            list.Add(Spec("code_read",
                "Read the game's decompiled C#: \"Type.Method\" gives that member's full code (all overloads); \"Type\" gives its outline " +
                "(fields with values, method signatures). Use it to explain *why* at the code level and to check a claim.",
                Obj(("target", Str("Type or Type.Member, e.g. playerHitReactions.enableExplodeCamera."), true))));
        }
        return list;
    }

    /// Runs one call; never throws (a bad call returns a message the model
    /// can correct from).
    public string Run(ToolCall call, ICollection<string> read)
    {
        try
        {
            string result = call.Name switch
            {
                "search" => DoSearch(call.Arg("query"), call.Arg("kinds")),
                "read_card" => ReadCard(call.Arg("id"), read),
                "read" => Read(call.Arg("id"), read),
                "fsm" => Fsm(call.Arg("name"), call.Arg("state"), read),
                "code_search" when HasCode => _code.Search(call.Arg("query")),
                "code_read" when HasCode => CodeRead(call.Arg("target"), read),
                _ => "Unknown tool '" + call.Name + "'.",
            };
            return result.Length <= MaxOutput ? result : result.Substring(0, MaxOutput) + "\n... (cut)";
        }
        catch (Exception e)
        {
            return "The lookup failed: " + e.Message;
        }
    }

    private string DoSearch(string query, string kinds)
    {
        if (string.IsNullOrWhiteSpace(query)) return "Give a query.";
        HashSet<string> k = null;
        if (!string.IsNullOrWhiteSpace(kinds))
            k = new HashSet<string>(Card.SplitList(kinds.ToLowerInvariant()));
        List<Hit> hits = _search.Search(query, 24, k);
        if (hits.Count == 0) return "Nothing found for '" + query + "'. Try other words (the game's names, or how runners say it).";
        StringBuilder b = new StringBuilder();
        Dictionary<string, int> perSource = new Dictionary<string, int>();
        int shown = 0;
        foreach (Hit h in hits)
        {
            // At most two pieces of one card / doc section / FSM, so one
            // source cannot fill the list.
            string src = h.Chunk.Id;
            int cut = src.IndexOfAny(new[] { '#', '/' });
            if (h.Chunk.Kind == "card" && cut > 0) src = src.Substring(0, cut);
            else if (h.Chunk.Kind == "fsm" && cut > 0) src = src.Substring(0, cut);
            perSource.TryGetValue(src, out int seen);
            if (seen >= 2) continue;
            perSource[src] = seen + 1;
            if (shown++ >= 8) break;
            b.Append("[").Append(h.Chunk.Id).Append("] ").Append(h.Chunk.Title).Append('\n');
            b.Append("  ").Append(Snippet(h.Chunk.Text, 280)).Append('\n');
        }
        return b.ToString();
    }

    private string ReadCard(string id, ICollection<string> read)
    {
        id = (id ?? "").Trim();
        if (id.StartsWith("card:")) id = id.Substring(5);
        int hash = id.IndexOf('#');
        if (hash >= 0) id = id.Substring(0, hash);
        if (!_corpus.Cards.TryGetValue(id, out Card c)) return "No card '" + id + "'. The card index lists them all.";
        read.Add("card:" + c.Id);
        return c.Render();
    }

    private string Read(string id, ICollection<string> read)
    {
        id = (id ?? "").Trim().Trim('[', ']');
        if (_corpus.ById.TryGetValue(id, out Chunk c))
        {
            read.Add(c.Id);
            return c.Title + "\n\n" + c.Text;
        }
        if (id.StartsWith("card:") || _corpus.Cards.ContainsKey(id)) return ReadCard(id, read);
        return "No piece with id '" + id + "'. Use an id exactly as search printed it.";
    }

    private string Fsm(string name, string state, ICollection<string> read)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "FSM exports: " + string.Join(", ", _corpus.Fsms.Keys.OrderBy(x => x)) +
                   "\n(not exported yet: Megan / the boss, the cannibals' motor and vision FSMs)";
        name = name.Trim();
        if (name.StartsWith("fsm:")) name = name.Substring(4);
        if (name.EndsWith(".txt")) name = name.Substring(0, name.Length - 4);
        if (!_corpus.Fsms.TryGetValue(name, out FsmText f))
        {
            string match = _corpus.Fsms.Keys.FirstOrDefault(k => k.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match == null) return "No FSM '" + name + "'. Exports: " + string.Join(", ", _corpus.Fsms.Keys.OrderBy(x => x));
            f = _corpus.Fsms[match];
        }
        if (string.IsNullOrWhiteSpace(state)) { read.Add("fsm:" + f.Name); return f.Overview(); }
        string s = f.State(state.Trim());
        if (s == null) return "No state '" + state + "' in " + f.Name + ". Its states: " + string.Join(", ", f.States.Select(x => x.state));
        read.Add("fsm:" + f.Name + "#" + state.Trim());
        return s;
    }

    private string CodeRead(string target, ICollection<string> read)
    {
        string result = _code.Read(target);
        if (!result.StartsWith("Nothing called") && !result.StartsWith("Give a")) read.Add("code:" + (target ?? "").Trim());
        return result;
    }

    public static string Snippet(string text, int max)
    {
        string s = (text ?? "").Replace('\n', ' ').Replace("  ", " ").Trim();
        return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }

    private static ToolSpec Spec(string name, string description, JsonObject parameters) =>
        new ToolSpec { Name = name, Description = description, Parameters = parameters };

    private static JsonObject Str(string description) => new JsonObject { ["type"] = "string", ["description"] = description };

    private static JsonObject Obj(params (string name, JsonObject schema, bool required)[] props)
    {
        JsonObject p = new JsonObject();
        JsonArray req = new JsonArray();
        foreach ((string name, JsonObject schema, bool required) in props)
        {
            p[name] = schema;
            if (required) req.Add(name);
        }
        JsonObject o = new JsonObject { ["type"] = "object", ["properties"] = p };
        if (req.Count > 0) o["required"] = req;
        return o;
    }
}
