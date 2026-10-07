using System.Text.Json.Nodes;
using ForestBot.Agent;
using ForestBot.Code;
using ForestBot.Knowledge;
using ForestBot.Llm;
using ForestBot.Search;
using ForestBot.Store;
using Xunit;

namespace ForestBot.Tests;

public class ModelAndAgentTests
{
    [Fact]
    public void Latex_in_answers_becomes_plain_text()
    {
        Assert.Equal("velocity = Δposition / Δtime (often)",
            Answerer.PlainMath(@"velocity = $\Delta\text{position} / \Delta\text{time}$ (often)"));
        Assert.Equal("d ≈ 1.3 × fps × t", Answerer.PlainMath(@"$$d \approx 1.3 \times fps \times t$$"));
        Assert.Equal("v = sqrt(2 · 8 · 10)", Answerer.PlainMath(@"\(v = \sqrt{2 \cdot 8 \cdot 10}\)"));
        Assert.Equal("(0.9 v^2) / (27.5)", Answerer.PlainMath(@"$\frac{0.9 v^{2}}{27.5}$"));
        // left alone: money, inline code, code blocks
        Assert.Equal("costs $5 and $6", Answerer.PlainMath("costs $5 and $6"));
        Assert.Equal("`$\\Delta$` x", Answerer.PlainMath("`$\\Delta$` x"));
        string block = "```csharp\nvar s = $\"{x}\\n\";\n```";
        Assert.Equal(block, Answerer.PlainMath(block));
    }

    private static readonly List<ToolSpec> OneTool = new List<ToolSpec>
    {
        new ToolSpec { Name = "search", Description = "d", Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() } },
    };

    // ---- Gemini ----

    // Shaped like a real generateContent reply from a thinking model: a
    // function call carrying a thought signature, usage with thoughts.
    private const string GeminiCall = @"{""candidates"":[{""content"":{""parts"":[{""functionCall"":{""name"":""read_card"",""args"":{""id"":""bomb-boost""}},""thoughtSignature"":""CsoBAVSoXO4...""}],""role"":""model""},""finishReason"":""STOP"",""index"":0}],
""usageMetadata"":{""promptTokenCount"":3051,""candidatesTokenCount"":18,""totalTokenCount"":3170,""thoughtsTokenCount"":101}}";

    [Fact]
    public void Gemini_parses_calls_and_keeps_the_raw_turn()
    {
        ChatResult r = GeminiChat.ParseResponse(GeminiCall);
        Assert.Single(r.ToolCalls);
        Assert.Equal("read_card", r.ToolCalls[0].Name);
        Assert.Equal("bomb-boost", r.ToolCalls[0].Arg("id"));
        Assert.StartsWith(GeminiChat.LocalIdPrefix, r.ToolCalls[0].Id);
        Assert.Equal(3051, r.InputTokens);
        Assert.Equal(119, r.OutputTokens);
        Assert.Contains("thoughtSignature", r.Raw.ToJsonString());
    }

    [Fact]
    public void Gemini_request_echoes_signatures_and_groups_results()
    {
        ChatResult r = GeminiChat.ParseResponse(GeminiCall);
        List<ChatMessage> msgs = new List<ChatMessage>
        {
            ChatMessage.User("q"),
            new ChatMessage { Role = Role.Assistant, ToolCalls = r.ToolCalls, Raw = r.Raw, RawProvider = "gemini" },
            ChatMessage.ToolResult(r.ToolCalls[0], "card text"),
        };
        JsonObject body = GeminiChat.BuildRequest("sys", msgs, OneTool, 1000, "low");
        JsonArray contents = body["contents"].AsArray();
        Assert.Equal(3, contents.Count);
        Assert.Equal("model", contents[1]["role"].GetValue<string>());
        Assert.Equal("CsoBAVSoXO4...", contents[1]["parts"][0]["thoughtSignature"].GetValue<string>());
        JsonNode fr = contents[2]["parts"][0]["functionResponse"];
        Assert.Equal("read_card", fr["name"].GetValue<string>());
        Assert.Equal("card text", fr["response"]["content"].GetValue<string>());
        Assert.Null(fr["id"]);   // ours, not the model's
        Assert.Equal("sys", body["systemInstruction"]["parts"][0]["text"].GetValue<string>());
        Assert.Equal("search", body["tools"][0]["functionDeclarations"][0]["name"].GetValue<string>());
        Assert.Equal("low", body["generationConfig"]["thinkingConfig"]["thinkingLevel"].GetValue<string>());
    }

    [Fact]
    public void Gemini_rebuilds_a_turn_another_provider_wrote()
    {
        ToolCall c = new ToolCall { Id = "call_9", Name = "search", Args = new JsonObject { ["query"] = "bb" } };
        List<ChatMessage> msgs = new List<ChatMessage>
        {
            ChatMessage.User("q"),
            new ChatMessage { Role = Role.Assistant, Text = "looking", ToolCalls = { c }, Raw = new JsonObject(), RawProvider = "mistral" },
            ChatMessage.ToolResult(c, "r"),
        };
        JsonArray contents = GeminiChat.BuildRequest("s", msgs, null, 10, null)["contents"].AsArray();
        Assert.Equal("looking", contents[1]["parts"][0]["text"].GetValue<string>());
        Assert.Equal("bb", contents[1]["parts"][1]["functionCall"]["args"]["query"].GetValue<string>());
        Assert.Equal("call_9", contents[2]["parts"][0]["functionResponse"]["id"].GetValue<string>());
    }

    [Fact]
    public void Gemini_text_skips_thought_parts()
    {
        ChatResult r = GeminiChat.ParseResponse(@"{""candidates"":[{""content"":{""parts"":[{""text"":""thinking..."",""thought"":true},{""text"":""The answer.""}]}}]}");
        Assert.Equal("The answer.", r.Text);
        Assert.Empty(r.ToolCalls);
        Assert.Equal("model", r.Raw["role"].GetValue<string>());
    }

    // ---- OpenAI-compatible ----

    [Fact]
    public void OpenAi_round_trip()
    {
        ChatResult r = OpenAiChat.ParseResponse(@"{""choices"":[{""message"":{""role"":""assistant"",""content"":null,
""tool_calls"":[{""id"":""abc"",""type"":""function"",""function"":{""name"":""search"",""arguments"":""{\""query\"":\""fall damage\""}""}}]},""finish_reason"":""tool_calls""}],
""usage"":{""prompt_tokens"":900,""completion_tokens"":20}}");
        Assert.Equal("", r.Text);
        Assert.Equal("abc", r.ToolCalls[0].Id);
        Assert.Equal("fall damage", r.ToolCalls[0].Arg("query"));
        Assert.Equal(900, r.InputTokens);

        List<ChatMessage> msgs = new List<ChatMessage>
        {
            ChatMessage.User("q"),
            new ChatMessage { Role = Role.Assistant, ToolCalls = r.ToolCalls },
            ChatMessage.ToolResult(r.ToolCalls[0], "res"),
        };
        JsonObject body = OpenAiChat.BuildRequest("m", "sys", msgs, OneTool, 500);
        JsonArray m = body["messages"].AsArray();
        Assert.Equal("system", m[0]["role"].GetValue<string>());
        Assert.Equal("{\"query\":\"fall damage\"}", m[2]["tool_calls"][0]["function"]["arguments"].GetValue<string>());
        Assert.Equal("abc", m[3]["tool_call_id"].GetValue<string>());
        Assert.Equal("function", body["tools"][0]["type"].GetValue<string>());
        Assert.Equal(500, body["max_tokens"].GetValue<int>());
    }

    [Fact]
    public void Model_spec_skips_models_without_keys()
    {
        Dictionary<string, string> env = new Dictionary<string, string> { ["GEMINI_API_KEY"] = "k", ["MISTRAL_API_KEY"] = "m" };
        List<string> log = new List<string>();
        ModelChain chain = ModelChain.FromSpec("gemini:g1, groq:llama, mistral:mm, bogus, custom:x@https://h/v1",
            new HttpClient(), n => env.TryGetValue(n, out string v) ? v : null, null, log.Add);
        Assert.Equal(new[] { "gemini:g1", "mistral:mm" }, chain.Models.Select(m => m.Name));
        Assert.Contains(log, l => l.Contains("no GROQ_API_KEY"));
        Assert.Contains(log, l => l.Contains("no CUSTOM_API_KEY"));
    }

    // ---- the agent loop with a scripted model ----

    private sealed class ScriptedModel : IChatModel
    {
        public string Name { get; set; } = "fake:m";
        public readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatResult>> Steps = new Queue<Func<IReadOnlyList<ChatMessage>, ChatResult>>();
        public readonly List<int> MessageCounts = new List<int>();
        public bool ToolsOffered;

        public Task<ChatResult> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, int max, CancellationToken ct)
        {
            MessageCounts.Add(messages.Count);
            ToolsOffered = tools != null;
            if (Steps.Count == 0) throw new ModelUnavailableException(Name + " quota", TimeSpan.FromMinutes(5));
            ChatResult r = Steps.Dequeue()(messages);
            r.Provider = "fake";
            return Task.FromResult(r);
        }
    }

    private static Corpus SmallCorpus()
    {
        Corpus c = new Corpus();
        c.AddCard(Card.Parse("---\nid: bomb-boost\ntitle: Bomb boost\naliases: bb, pause boost, menu boost\nconfidence: live\n---\n# Bomb boost\n\nPause pushes pile up.\n\n## Numbers\n\n8 m/s per frame.\n", "bomb-boost"));
        return c;
    }

    private static ChatResult Call(string name, string arg, string value) =>
        new ChatResult { ToolCalls = { new ToolCall { Id = "c1", Name = name, Args = new JsonObject { [arg] = value } } } };

    [Fact]
    public async Task Answerer_researches_then_answers_and_parses_the_protocol()
    {
        Corpus corpus = SmallCorpus();
        using HybridSearch search = new HybridSearch(corpus, null, null, null);
        ScriptedModel model = new ScriptedModel();
        model.Steps.Enqueue(_ => Call("search", "query", "bomb boost fps"));
        model.Steps.Enqueue(msgs =>
        {
            Assert.Contains("[card:bomb-boost", msgs[^1].Text);   // the search result came back
            return Call("read_card", "id", "bomb-boost");
        });
        model.Steps.Enqueue(msgs =>
        {
            Assert.Contains("8 m/s per frame", msgs[^1].Text);
            return new ChatResult { Text = "It works because **8 m/s** per frame piles up.\n\nSOURCES: card:bomb-boost, code:playerHitReactions.enableExplodeCamera\nSTATUS: answered" };
        });
        Answerer a = new Answerer(new ModelChain(new[] { model }, null), new Tools(corpus, search, new CodeIndex()), "sys", null);
        Answer ans = await a.AskAsync("why bb?", new List<Turn> { new Turn { Question = "earlier", Answer = "before" } }, CancellationToken.None);
        Assert.Equal("It works because **8 m/s** per frame piles up.", ans.Text);
        Assert.Equal(new[] { "card:bomb-boost", "code:playerHitReactions.enableExplodeCamera" }, ans.Sources);
        Assert.Equal("answered", ans.Status);
        Assert.Equal(2, ans.ToolCalls);
        Assert.Contains("card:bomb-boost", ans.Read);
        Assert.Equal(3, model.MessageCounts[0]);   // history (2) + the question
        Assert.Equal("-# Sources: Bomb boost · code: playerHitReactions.enableExplodeCamera", Answerer.SourcesLine(ans, corpus));
    }

    [Fact]
    public async Task Answerer_forces_an_answer_at_the_step_limit()
    {
        Corpus corpus = SmallCorpus();
        using HybridSearch search = new HybridSearch(corpus, null, null, null);
        ScriptedModel model = new ScriptedModel();
        for (int i = 0; i < 3; i++) model.Steps.Enqueue(_ => Call("search", "query", "x"));
        model.Steps.Enqueue(_ => new ChatResult { Text = "Best I can say.\nSTATUS: partial" });
        Answerer a = new Answerer(new ModelChain(new[] { model }, null), new Tools(corpus, search, new CodeIndex()), "sys", null) { MaxSteps = 3 };
        Answer ans = await a.AskAsync("q", null, CancellationToken.None);
        Assert.False(model.ToolsOffered);   // the last call had no tools
        Assert.Equal("partial", ans.Status);
        Assert.Equal("Best I can say.", ans.Text);
    }

    [Fact]
    public async Task Answerer_falls_back_when_a_model_is_out_of_quota_then_says_busy()
    {
        Corpus corpus = SmallCorpus();
        using HybridSearch search = new HybridSearch(corpus, null, null, null);
        ScriptedModel first = new ScriptedModel { Name = "fake:first" };            // no steps = quota error
        ScriptedModel second = new ScriptedModel { Name = "fake:second" };
        second.Steps.Enqueue(_ => new ChatResult { Text = "From the second.\nSOURCES: card:bomb-boost\nSTATUS: answered" });
        ModelChain chain = new ModelChain(new[] { first, second }, null);
        Answerer a = new Answerer(chain, new Tools(corpus, search, new CodeIndex()), "sys", null);
        Answer ans = await a.AskAsync("q", null, CancellationToken.None);
        Assert.Equal("fake:second", ans.Model);
        Assert.Equal("From the second.", ans.Text);

        Answer busy = await a.AskAsync("q2", null, CancellationToken.None);   // second is empty now too
        Assert.True(busy.Busy);
        Assert.Contains("try again", busy.Text);
        Assert.Null(chain.Pick());
    }

    // ---- the eval: busy (no quota) is skipped, never scored (gotcha 94) ----

    private static async Task<(ForestBot.Eval.EvalScore score, string report)> RunEval(ScriptedModel model, List<string> output = null,
        string questionsMd = "### bb\nquestion: why bb?\ncards: bomb-boost\nmust:\n- pushes pile up\n- 8 m/s per frame\n")
    {
        Corpus corpus = SmallCorpus();
        using HybridSearch search = new HybridSearch(corpus, null, null, null);
        string dir = Path.Combine(Path.GetTempPath(), "forest-bot-eval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string questions = Path.Combine(dir, "questions.md");
            File.WriteAllText(questions, questionsMd);
            ModelChain chain = new ModelChain(new[] { model }, null);
            var runner = new ForestBot.Eval.EvalRunner(chain, new Answerer(chain, new Tools(corpus, search, new CodeIndex()), "sys", null),
                                                       "v", dir, l => output?.Add(l)) { Pause = TimeSpan.Zero, MaxWait = TimeSpan.Zero };
            string summary = Path.Combine(dir, "summary.md");
            var score = await runner.RunAsync(questions, null, CancellationToken.None, summary);
            return (score, File.ReadAllText(summary));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Eval_scores_an_answered_and_judged_question()
    {
        ScriptedModel model = new ScriptedModel();
        model.Steps.Enqueue(_ => new ChatResult { Text = "Pushes pile up, 8 m/s per frame.\nSOURCES: card:bomb-boost\nSTATUS: answered" });
        model.Steps.Enqueue(_ => new ChatResult { Text = "{\"must\":[true,false],\"not\":[]}" });
        var (score, report) = await RunEval(model);
        Assert.Equal((2, 3, 1, 0), (score.Passed, score.Total, score.Answered, score.Busy));   // card read + 1 of 2 facts
        Assert.Contains("- [ ] 8 m/s per frame", report);
    }

    [Fact]
    public async Task Eval_prints_each_failed_check_under_the_score_line()
    {
        ScriptedModel model = new ScriptedModel();
        model.Steps.Enqueue(_ => new ChatResult { Text = "Pushes pile up, and it is long.\nSOURCES: wiki\nSTATUS: answered" });
        model.Steps.Enqueue(_ => new ChatResult { Text = "{\"must\":[true,false],\"not\":[true]}" });
        model.Steps.Enqueue(_ => new ChatResult { Text = "No.\nSTATUS: answered" });
        model.Steps.Enqueue(_ => new ChatResult { Text = "{\"must\":[false],\"not\":[]}" });
        List<string> output = new List<string>();
        await RunEval(model, output, "### bb\nquestion: why bb?\ncards: bomb-boost\nmax-length: 10\nmust:\n- pushes pile up\n- 8 m/s per frame\nnot:\n- says it is patched\n" +
                                     "then: and in v1.12?\nmust:\n- still works\n");
        int score = output.FindIndex(l => l.StartsWith("bb: "));
        Assert.True(score >= 0, string.Join("\n", output));
        string after = string.Join("\n", output.Skip(score + 1));
        Assert.Contains("    - [ ] did not read bomb-boost", after);
        Assert.Contains("    - [ ] 8 m/s per frame", after);
        Assert.Contains("    - [ ] DID: says it is patched", after);
        Assert.Contains("    - [ ] TOO LONG: answer at most 10 characters", after);
        Assert.Contains("    follow-up: - [ ] still works", after);
        Assert.DoesNotContain("pushes pile up", after);   // ticked checks stay in the report only
    }

    [Fact]
    public async Task Eval_skips_a_question_no_model_could_answer()
    {
        var (score, report) = await RunEval(new ScriptedModel());   // no steps: out of quota at once
        Assert.Equal((0, 0, 0, 1), (score.Passed, score.Total, score.Answered, score.Busy));
        Assert.Contains("busy - skipped (no model available (quota", report);
    }

    [Fact]
    public async Task Eval_skips_a_question_the_judge_had_no_quota_for()
    {
        ScriptedModel model = new ScriptedModel();
        model.Steps.Enqueue(_ => new ChatResult { Text = "Pushes pile up.\nSOURCES: card:bomb-boost\nSTATUS: answered" });
        var (score, report) = await RunEval(model);   // the judge's call finds no step left
        Assert.Equal((0, 0, 0, 1), (score.Passed, score.Total, score.Answered, score.Busy));
        Assert.Contains("no model available for the judge", report);
    }

    [Fact]
    public void Tools_never_throw_and_cap_per_source()
    {
        Corpus corpus = SmallCorpus();
        using HybridSearch search = new HybridSearch(corpus, null, null, null);
        Tools t = new Tools(corpus, search, new CodeIndex());
        HashSet<string> read = new HashSet<string>();
        Assert.Contains("Unknown tool", t.Run(new ToolCall { Name = "rm_rf" }, read));
        Assert.Contains("No card", t.Run(new ToolCall { Name = "read_card", Args = new JsonObject { ["id"] = "nope" } }, read));
        Assert.Contains("Give a query", t.Run(new ToolCall { Name = "search" }, read));
        Assert.DoesNotContain(t.Specs(), s => s.Name.StartsWith("code_"));   // no code loaded = no code tools
        Assert.Contains("8 m/s", t.Run(new ToolCall { Name = "read", Args = new JsonObject { ["id"] = "[card:bomb-boost#numbers]" } }, read));
        Assert.Contains("card:bomb-boost#numbers", read);
    }

    // ---- the store ----

    [Fact]
    public void Store_conversations_votes_queue_limits_and_cache()
    {
        using BotStore s = new BotStore(":memory:");
        long a1 = s.AddAnswer(0, "u1", "c", "what is bb?", "a bomb boost", "card:bomb-boost", "answered", "m");
        long a2 = s.AddAnswer(a1, "u1", "c", "and fps?", "more pushes", null, "answered", "m");
        long other = s.AddAnswer(0, "u2", "c", "x", "y", null, "answered", "m");
        s.MapMessage("111", a1);
        s.MapMessage("112", a2);
        Assert.Equal(a2, s.AnswerForMessage("112").Id);
        Assert.Equal(a1, s.AnswerForMessage("112").ConversationId);
        Assert.Null(s.AnswerForMessage("999"));
        Assert.Equal(new[] { a1, a2 }, s.Conversation(a1).Select(x => x.Id));
        Assert.Equal(new[] { a1 }, s.Conversation(a1, a1).Select(x => x.Id));
        Assert.Single(s.Conversation(other));

        s.Vote(a1, "u1", +1);
        s.Vote(a1, "u2", -1);
        s.Vote(a1, "u2", -1, "numbers off");   // same user again replaces
        Assert.Equal((1, 1), s.Votes(a1));

        long q = s.Enqueue(a1, "thumbs-down", "what is bb?", "numbers off");
        Assert.Single(s.OpenQueue());
        s.Resolve(q);
        Assert.Empty(s.OpenQueue());

        Assert.True(s.TryUse("u3", 2, 10));
        Assert.True(s.TryUse("u3", 2, 10));
        Assert.False(s.TryUse("u3", 2, 10));
        Assert.True(s.TryUse("u4", 2, 10));

        string key = BotStore.CacheKey("  What is BB?? ");
        Assert.Equal("what is bb", key);
        s.Cache(key, "v1", a1);
        Assert.Equal(a1, s.Cached(key, "v1").Id);
        Assert.Null(s.Cached(key, "v2"));   // the knowledge changed
        s.Uncache(a1);
        Assert.Null(s.Cached(key, "v1"));
    }
}
