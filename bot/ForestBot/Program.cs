using ForestBot;
using ForestBot.Agent;
using ForestBot.Eval;
using ForestBot.Gateway;
using ForestBot.Search;
using ForestBot.Store;

// ------------------------------------------------------------------
// forest-bot [mode] - bot/README.md:
//   (none) / run       the Discord bot
//   search <query>     what the search finds (no model, no keys)
//   check              load everything and report problems (CI-safe)
//   code <target>      the code tool's answer (code search <text>)
//   ask <question>     one answer on the console, with its lookups
//   chat               a conversation on the console (follow-ups)
//   eval [ids...]      score knowledge/eval/questions.md
//   queue              the open research queue
// ------------------------------------------------------------------
string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "run";
string rest = string.Join(" ", args.Skip(1));
BotConfig cfg = BotConfig.FromEnvironment();
Action<string> log = s => Console.Error.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + s);

switch (mode)
{
    case "search":
    {
        using Brain brain = new Brain(cfg, log, withModels: false);
        foreach (Hit h in brain.Search.Search(rest, 10))
            Console.WriteLine($"{h.Score:0.0000}  kw#{h.KeywordRank,-3} vec#{h.VectorRank,-3} [{h.Chunk.Id}] {h.Chunk.Title}");
        return 0;
    }
    case "code":
    {
        // code <Type|Type.Member>  or  code search <text>
        ForestBot.Code.CodeIndex code = ForestBot.Code.CodeIndex.Load(cfg.CodeRoot, log);
        Console.WriteLine(args.Length > 2 && args[1] == "search" ? code.Search(string.Join(" ", args.Skip(2))) : code.Read(rest));
        return 0;
    }
    case "check":
    {
        using Brain brain = new Brain(cfg, log, withModels: false);
        Console.WriteLine(brain.Corpus.Problems.Count == 0 ? "OK: no knowledge problems" : brain.Corpus.Problems.Count + " problem(s)");
        Console.WriteLine("Prompt: " + Prompt.Build(brain.Corpus, brain.Code.FileCount > 0).Length + " chars");
        if (args.Contains("--unconfirmed"))
            foreach (ForestBot.Knowledge.Card c in brain.Corpus.Cards.Values)
            {
                Console.WriteLine(c.Id + " (" + c.Confidence + "):");
                foreach (string u in c.Unconfirmed()) Console.WriteLine("  - " + u);
            }
        return brain.Corpus.Problems.Count == 0 && brain.Corpus.Cards.Count > 0 ? 0 : 1;
    }
    case "ask":
    {
        using Brain brain = new Brain(cfg, log);
        Reply r = await brain.AskAsync(rest, "console", "console", null, CancellationToken.None);
        Print(r, brain);
        return 0;
    }
    case "chat":
    {
        using Brain brain = new Brain(cfg, log);
        StoredAnswer last = null;
        Console.WriteLine("Ask away (empty line = a new conversation, Ctrl+C = quit).");
        while (true)
        {
            Console.Write(last == null ? "\n? " : "\n(follow-up) ? ");
            string q = Console.ReadLine();
            if (q == null) break;
            if (q.Trim().Length == 0) { last = null; continue; }
            Reply r = await brain.AskAsync(q, "console", "console", last, CancellationToken.None);
            Print(r, brain);
            if (r.AnswerId > 0) last = brain.Store.GetAnswer(r.AnswerId);
        }
        return 0;
    }
    case "eval":
    {
        // eval [ids...] [--summary <file>]: the report is also appended to
        // the file (CI passes $GITHUB_STEP_SUMMARY).
        int at = Array.IndexOf(args, "--summary");
        string summaryPath = at > 0 && at + 1 < args.Length ? args[at + 1] : null;
        HashSet<string> ids = args.Skip(1).Where((a, i) => at < 0 || (i + 1 != at && i + 1 != at + 1)).ToHashSet();
        using Brain brain = new Brain(cfg, log);
        string path = Path.Combine(cfg.KnowledgeRoot, "knowledge", "eval", "questions.md");
        try { await new EvalRunner(brain, Console.WriteLine).RunAsync(path, ids, CancellationToken.None, summaryPath); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        return 0;
    }
    case "queue":
    {
        using Brain brain = new Brain(cfg, log, withModels: false);
        foreach (QueueItem q in brain.Store.OpenQueue())
            Console.WriteLine($"#{q.Id} {q.Created:yyyy-MM-dd} {q.Reason} (answer #{q.AnswerId}): {q.Question}\n    {q.Detail}");
        return 0;
    }
    case "answer":
    {
        // A stored answer in full (a thumbs-down's comment rarely says enough).
        using Brain brain = new Brain(cfg, log, withModels: false);
        StoredAnswer a = args.Length > 1 && long.TryParse(args[1].TrimStart('#'), out long id) ? brain.Store.GetAnswer(id) : null;
        if (a == null) { Console.Error.WriteLine("usage: answer <id> (an id from `queue`)"); return 1; }
        (int up, int down) = brain.Store.Votes(a.Id);
        Console.WriteLine($"#{a.Id} (conversation #{a.ConversationId}) {a.Status} {a.Model}, +{up} -{down}\nQ: {a.Question}\n\n{a.Answer}\n\nsources: {a.Sources}");
        return 0;
    }
    case "resolve":
    {
        using Brain brain = new Brain(cfg, log, withModels: false);
        if (args.Length < 2 || !long.TryParse(args[1].TrimStart('#'), out long id)) { Console.Error.WriteLine("usage: resolve <queue id>"); return 1; }
        brain.Store.Resolve(id);
        Console.WriteLine("resolved #" + id);
        return 0;
    }
    case "run":
    {
        if (string.IsNullOrEmpty(cfg.DiscordToken)) { log("FOREST_BOT_DISCORD_TOKEN is not set"); return 1; }
        using Brain brain = new Brain(cfg, log);
        using CancellationTokenSource stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
        await new DiscordBot(brain, log).RunAsync(stop.Token);
        return 0;
    }
    default:
        Console.Error.WriteLine("Modes: run, search <q>, check, ask <q>, chat, eval [ids], queue");
        return 2;
}

static void Print(Reply r, Brain brain)
{
    Console.WriteLine();
    Console.WriteLine(r.Answer.Text);
    Console.WriteLine();
    Console.WriteLine(Answerer.SourcesLine(r.Answer, brain.Corpus));
    Console.WriteLine($"-- {r.Answer.Model}, {r.Answer.Status}, {r.Answer.ToolCalls} lookups, {r.Answer.InputTokens}/{r.Answer.OutputTokens} tokens" +
                      (r.FromCache ? ", from the cache" : "") + (r.Queued != null ? ", queued: " + r.Queued : ""));
    foreach (string t in r.Answer.Trace) Console.WriteLine("   " + t);
}
