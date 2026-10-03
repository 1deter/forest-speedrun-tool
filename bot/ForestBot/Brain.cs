using ForestBot.Agent;
using ForestBot.Code;
using ForestBot.Knowledge;
using ForestBot.Llm;
using ForestBot.Search;
using ForestBot.Store;

namespace ForestBot;

/// What a question produced: the answer and its stored id.
public sealed class Reply
{
    public Answer Answer;
    public long AnswerId;
    public bool FromCache;
    /// Set when the answer went to the research queue (and why).
    public string Queued;
}

// ------------------------------------------------------------------
// Everything behind a question, for Discord and the command line alike:
// knowledge, search, code, models, the store. A follow-up carries its
// conversation's earlier turns; first questions may come from the cache;
// partial / undocumented answers go to the research queue.
// ------------------------------------------------------------------
public sealed class Brain : IDisposable
{
    public readonly BotConfig Config;
    public readonly Corpus Corpus;
    public readonly HybridSearch Search;
    public readonly CodeIndex Code;
    public readonly ModelChain Models;
    public readonly Answerer Answerer;
    public readonly BotStore Store;
    public readonly Action<string> Log;

    public Brain(BotConfig config, Action<string> log, bool withModels = true)
    {
        Config = config;
        Log = log;
        Corpus = Corpus.Load(config.KnowledgeRoot);
        log("Knowledge: " + Corpus.Cards.Count + " cards, " + Corpus.Fsms.Count + " FSMs, " + Corpus.Chunks.Count + " pieces, version " + Corpus.Version);
        foreach (string p in Corpus.Problems) log("Knowledge problem: " + p);
        Search = new HybridSearch(Corpus, OnnxEmbedder.TryLoad(config.EmbedModelDir, log), Path.Combine(config.DataDir, "vectors.db"), log);
        Code = CodeIndex.Load(config.CodeRoot, log);
        Store = new BotStore(Path.Combine(config.DataDir, "bot.db"));
        if (withModels)
        {
            HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            Models = ModelChain.FromSpec(config.Models, http, config.Get, config.ThinkingLevel, log);
            Tools tools = new Tools(Corpus, Search, Code);
            Answerer = new Answerer(Models, tools, Prompt.Build(Corpus, tools.HasCode), log);
        }
    }

    /// `followUpOf`: the answer being replied to (null = a new question).
    public async Task<Reply> AskAsync(string question, string userId, string channelId, StoredAnswer followUpOf, CancellationToken ct)
    {
        List<Turn> history = new List<Turn>();
        long conversation = 0;
        if (followUpOf != null)
        {
            conversation = followUpOf.ConversationId;
            foreach (StoredAnswer a in Store.Conversation(conversation, followUpOf.Id))
                history.Add(new Turn { Question = a.Question, Answer = a.Answer });
        }

        string key = BotStore.CacheKey(question);
        if (followUpOf == null && key.Length > 0)
        {
            StoredAnswer cached = Store.Cached(key, Corpus.Version);
            if (cached != null)
            {
                Answer ca = new Answer { Text = cached.Answer, Status = cached.Status ?? "answered", Model = "cache" };
                if (!string.IsNullOrEmpty(cached.Sources)) ca.Sources.AddRange(cached.Sources.Split(", "));
                long cid = Store.AddAnswer(0, userId, channelId, question, cached.Answer, cached.Sources, ca.Status, "cache:" + cached.Id);
                Log("Ask #" + cid + " (cache of #" + cached.Id + "): " + Short(question));
                return new Reply { Answer = ca, AnswerId = cid, FromCache = true };
            }
        }

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        Answer answer = await Answerer.AskAsync(question, history, ct);
        if (answer.Busy)
        {
            Log("Ask busy: " + Short(question));
            return new Reply { Answer = answer };
        }
        string sources = string.Join(", ", answer.Sources);
        long id = Store.AddAnswer(conversation, userId, channelId, question, answer.Text, sources, answer.Status, answer.Model);
        Log("Ask #" + id + (followUpOf != null ? " (follow-up of #" + followUpOf.Id + ")" : "") + ": " + answer.Model +
            ", " + answer.ToolCalls + " lookups, " + answer.InputTokens + "/" + answer.OutputTokens + " tokens, " +
            sw.ElapsedMilliseconds + " ms, " + answer.Status + " - " + Short(question));

        Reply reply = new Reply { Answer = answer, AnswerId = id };
        if (answer.Status == "partial" || answer.Status == "not_documented")
        {
            Store.Enqueue(id, answer.Status, question, "read: " + string.Join(", ", answer.Read));
            reply.Queued = answer.Status;
        }
        else if (answer.Status == "answered" && followUpOf == null && key.Length > 0)
            Store.Cache(key, Corpus.Version, id);
        return reply;
    }

    public static string Short(string s)
    {
        s = (s ?? "").Replace('\n', ' ');
        return s.Length <= 120 ? s : s.Substring(0, 117) + "...";
    }

    public void Dispose()
    {
        Search.Dispose();
        Store.Dispose();
    }
}
