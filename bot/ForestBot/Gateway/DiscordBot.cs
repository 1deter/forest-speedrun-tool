using Discord;
using Discord.WebSocket;
using ForestBot.Agent;
using ForestBot.Store;
using ForestOverlay.BridgeMcp;

namespace ForestBot.Gateway;

// ------------------------------------------------------------------
// The gateway bot (Discord.Net). A question arrives as /ask, a mention,
// or a reply to one of its answers - the reply continues that answer's
// conversation (author, 2026-10-03: "runners should be also able to ask
// follow-up questions about previous answers"). Each answer carries
// 👍 / 👎; 👎 asks what was wrong and queues it for research. Handlers
// hand the work to a task at once so the gateway never waits on a model.
// ------------------------------------------------------------------
public sealed class DiscordBot
{
    /// Room left in a message for the sources line.
    private const int PartChars = 1900;

    private readonly Brain _brain;
    private readonly BotConfig _cfg;
    private readonly Action<string> _log;
    private readonly DiscordSocketClient _client;

    private readonly SiteSettings _site;

    public DiscordBot(Brain brain, Action<string> log)
    {
        _brain = brain;
        _cfg = brain.Config;
        _log = log;
        // Live settings from the site (FOREST_BOT_TOKEN set): the cached last good ones apply now.
        _site = SiteSettings.FromConfig(_cfg, new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
            BuildId() + " / kb " + brain.Corpus.Version, log);
        if (_site != null)
        {
            _site.Applied = _brain.ReloadModels;
            if (_site.LoadCached()) { _log("Site settings: cached revision " + _site.Rev + " applied"); }
            else _log("Site settings: no cache - .env values until the site answers");
        }
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.DirectMessages,
            LogLevel = LogSeverity.Info,
        });
        _client.Log += m => { _log("Discord: " + m.ToString()); return Task.CompletedTask; };
        _client.Ready += OnReady;
        _client.SlashCommandExecuted += c => { _ = Task.Run(() => Guard(() => OnSlash(c))); return Task.CompletedTask; };
        _client.MessageReceived += m => { _ = Task.Run(() => Guard(() => OnMessage(m))); return Task.CompletedTask; };
        _client.ButtonExecuted += c => { _ = Task.Run(() => Guard(() => OnButton(c))); return Task.CompletedTask; };
        _client.ModalSubmitted += m => { _ = Task.Run(() => Guard(() => OnModal(m))); return Task.CompletedTask; };
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await _client.LoginAsync(TokenType.Bot, _cfg.DiscordToken);
        await _client.StartAsync();
        if (_site != null) _ = Task.Run(() => _site.RunAsync(SeenChannels, ct));
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (TaskCanceledException) { }
        await _client.StopAsync();
    }

    /// What identifies the running build: the commit the deploy published (bot.yml passes it as
    /// SourceRevisionId, which the SDK appends to the informational version after a +); "dev" locally.
    public static string BuildId() =>
        BuildId(typeof(DiscordBot).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion);

    /// "1.0.0+<SourceRevisionId>" -> the first 12 characters of the revision id; no id -> "dev".
    public static string BuildId(string informationalVersion)
    {
        string v = informationalVersion ?? "";
        int plus = v.IndexOf('+');
        return plus >= 0 && v.Length > plus + 1 ? v.Substring(plus + 1, Math.Min(12, v.Length - plus - 1)) : "dev";
    }

    /// The text channels the bot can see, for the site's Bot tab (empty until connected).
    private IReadOnlyList<SeenChannel> SeenChannels() =>
        _client.ConnectionState != ConnectionState.Connected ? new List<SeenChannel>()
            : _client.Guilds.SelectMany(g => g.TextChannels.Select(c => new SeenChannel(c.Id, c.Name, g.Name))).ToList();

    private async Task OnReady()
    {
        SlashCommandProperties ask = new SlashCommandBuilder()
            .WithName("ask")
            .WithDescription("Ask about The Forest's mechanics, tech and speedrunning")
            .AddOption("question", ApplicationCommandOptionType.String, "Your question", isRequired: true)
            .Build();
        try
        {
            if (_cfg.TestGuild != 0 && _client.GetGuild(_cfg.TestGuild) is SocketGuild g)
                await g.CreateApplicationCommandAsync(ask);
            else
                await _client.CreateGlobalApplicationCommandAsync(ask);
            _log("Discord: ready as " + _client.CurrentUser + ", /ask registered" + (_cfg.TestGuild != 0 ? " in the test guild" : " globally"));
        }
        catch (Exception e) { _log("Discord: /ask registration failed: " + e.Message); }
    }

    private async Task OnSlash(SocketSlashCommand cmd)
    {
        if (cmd.CommandName != "ask") return;
        string question = (cmd.Data.Options.FirstOrDefault(o => o.Name == "question")?.Value as string ?? "").Trim();
        // cmd.Channel is null when the bot user is not in the server (only the
        // command was installed) - the interaction still works, so go by ids.
        if (!AllowedId(cmd.ChannelId, cmd.GuildId == null))
        {
            await cmd.RespondAsync("I only answer in the bot's channels.", ephemeral: true);
            return;
        }
        if (question.Length == 0) { await cmd.RespondAsync("Ask me something about The Forest.", ephemeral: true); return; }
        if (!_brain.Store.TryUse(cmd.User.Id.ToString(), _cfg.PerHour, _cfg.PerDay))
        {
            await cmd.RespondAsync(LimitText(), ephemeral: true);
            return;
        }
        await cmd.DeferAsync();
        Reply reply = await _brain.AskAsync(question, cmd.User.Id.ToString(), cmd.ChannelId?.ToString(), null, CancellationToken.None);
        List<string> parts = Parts("> **" + Quote(question) + "**\n\n" + reply.Answer.Text, reply);
        for (int i = 0; i < parts.Count; i++)
        {
            bool last = i == parts.Count - 1;
            IUserMessage sent = await cmd.FollowupAsync(parts[i], allowedMentions: AllowedMentions.None,
                components: last && reply.AnswerId > 0 ? Buttons(reply.AnswerId) : null);
            if (reply.AnswerId > 0) _brain.Store.MapMessage(sent.Id.ToString(), reply.AnswerId);
        }
        await PostQueued(reply, question);
    }

    private async Task OnMessage(SocketMessage raw)
    {
        if (raw is not SocketUserMessage msg || msg.Author.IsBot || _client.CurrentUser == null) return;
        ulong me = _client.CurrentUser.Id;

        StoredAnswer followUp = null;
        bool repliedToMe = false;
        if (msg.Reference != null && msg.Reference.MessageId.IsSpecified)
        {
            string refId = msg.Reference.MessageId.Value.ToString();
            followUp = _brain.Store.AnswerForMessage(refId);
            repliedToMe = followUp != null || msg.ReferencedMessage?.Author?.Id == me;
        }
        bool mentioned = msg.MentionedUsers.Any(u => u.Id == me);
        bool dm = msg.Channel is IDMChannel;
        if (!repliedToMe && !mentioned && !dm) return;
        if (!Allowed(msg.Channel)) return;

        string question = msg.Content.Replace("<@" + me + ">", "").Replace("<@!" + me + ">", "").Trim();
        if (question.Length == 0)
        {
            await msg.ReplyAsync("Ask me anything about The Forest's mechanics and speedrun tech - or reply to one of my answers to follow up.",
                allowedMentions: AllowedMentions.None);
            return;
        }
        if (!_brain.Store.TryUse(msg.Author.Id.ToString(), _cfg.PerHour, _cfg.PerDay))
        {
            await msg.ReplyAsync(LimitText(), allowedMentions: AllowedMentions.None);
            return;
        }

        Reply reply;
        using (msg.Channel.EnterTypingState())
            reply = await _brain.AskAsync(question, msg.Author.Id.ToString(), msg.Channel.Id.ToString(), followUp, CancellationToken.None);

        List<string> parts = Parts(reply.Answer.Text, reply);
        IUserMessage previous = msg;
        for (int i = 0; i < parts.Count; i++)
        {
            bool last = i == parts.Count - 1;
            MessageComponent buttons = last && reply.AnswerId > 0 ? Buttons(reply.AnswerId) : null;
            IUserMessage sent = i == 0
                ? await msg.ReplyAsync(parts[i], allowedMentions: AllowedMentions.None, components: buttons)
                : await msg.Channel.SendMessageAsync(parts[i], allowedMentions: AllowedMentions.None, components: buttons);
            if (reply.AnswerId > 0) _brain.Store.MapMessage(sent.Id.ToString(), reply.AnswerId);
            previous = sent;
        }
        await PostQueued(reply, question);
    }

    private async Task OnButton(SocketMessageComponent c)
    {
        string[] p = c.Data.CustomId.Split(':');
        if (p.Length != 3 || p[0] != "fb" || !long.TryParse(p[2], out long answerId)) return;
        string user = c.User.Id.ToString();
        if (p[1] == "up")
        {
            _brain.Store.Vote(answerId, user, +1);
            await c.RespondAsync("Thanks - glad it helped.", ephemeral: true);
            return;
        }
        _brain.Store.Vote(answerId, user, -1);
        _brain.Store.Uncache(answerId);
        Modal modal = new ModalBuilder()
            .WithTitle("What was wrong or missing?")
            .WithCustomId("fbm:" + answerId)
            .AddTextInput("Tell us (optional) - it goes to research", "comment", TextInputStyle.Paragraph,
                placeholder: "e.g. the numbers are off, it missed the part about...", required: false, maxLength: 1000)
            .Build();
        await c.RespondWithModalAsync(modal);
    }

    private async Task OnModal(SocketModal m)
    {
        string[] p = m.Data.CustomId.Split(':');
        if (p.Length != 2 || p[0] != "fbm" || !long.TryParse(p[1], out long answerId)) return;
        string comment = m.Data.Components.FirstOrDefault(x => x.CustomId == "comment")?.Value?.Trim();
        _brain.Store.Vote(answerId, m.User.Id.ToString(), -1, string.IsNullOrEmpty(comment) ? null : comment);
        StoredAnswer a = _brain.Store.GetAnswer(answerId);
        long q = _brain.Store.Enqueue(answerId, "thumbs-down", a?.Question, comment);
        _log("Feedback: 👎 on #" + answerId + " -> queue #" + q + (string.IsNullOrEmpty(comment) ? "" : ": " + Brain.Short(comment)));
        await m.RespondAsync("Thanks - noted for research. The knowledge base gets corrected from these.", ephemeral: true);
        await PostQueue("👎 on answer #" + answerId + " (queue #" + q + ")\n> " + Quote(a?.Question ?? "?") +
                        (string.IsNullOrEmpty(comment) ? "" : "\nWhat was wrong: " + comment));
    }

    /// The answer split into messages, the sources line on the last one.
    private List<string> Parts(string text, Reply reply)
    {
        string sources = Answerer.SourcesLine(reply.Answer, _brain.Corpus);
        if (reply.FromCache) sources = (sources.Length > 0 ? sources + " · " : "-# ") + "answered before";
        List<string> parts = DiscordText.Split(text, PartChars);
        if (parts.Count == 0) parts.Add("(no answer)");
        if (sources.Length > 0)
        {
            if (parts[^1].Length + 1 + sources.Length <= DiscordText.MaxChars) parts[^1] += "\n" + sources;
            else parts.Add(sources);
        }
        return parts;
    }

    private static MessageComponent Buttons(long answerId) => new ComponentBuilder()
        .WithButton("Helpful", "fb:up:" + answerId, ButtonStyle.Secondary, new Emoji("👍"))
        .WithButton("Wrong / missing something", "fb:down:" + answerId, ButtonStyle.Secondary, new Emoji("👎"))
        .Build();

    private async Task PostQueued(Reply reply, string question)
    {
        if (reply.Queued == null) return;
        await PostQueue("Queued (" + reply.Queued + ", answer #" + reply.AnswerId + ")\n> " + Quote(question));
    }

    private async Task PostQueue(string text)
    {
        if (_cfg.QueueChannel == 0) return;
        try
        {
            if (await _client.GetChannelAsync(_cfg.QueueChannel) is IMessageChannel ch)
                await ch.SendMessageAsync(text.Length <= DiscordText.MaxChars ? text : text.Substring(0, DiscordText.MaxChars - 3) + "...",
                    allowedMentions: AllowedMentions.None);
        }
        catch (Exception e) { _log("Discord: queue post failed: " + e.Message); }
    }

    /// The same rule by ids, for interactions whose channel the bot cannot see.
    private bool AllowedId(ulong? channelId, bool dm)
    {
        if (dm) return _cfg.AllowDms;
        BotConfig.LiveSettings live = _cfg.Live;   // one snapshot for the whole decision
        if (live.AllChannels) return true;
        if (channelId is ulong id && live.Channels.Contains(id)) return true;
        return channelId is ulong tid && _client.GetChannel(tid) is SocketThreadChannel t && t.ParentChannel != null && live.Channels.Contains(t.ParentChannel.Id);
    }

    private bool Allowed(IChannel channel)
    {
        if (channel == null) return false;
        if (channel is IDMChannel) return _cfg.AllowDms;
        BotConfig.LiveSettings live = _cfg.Live;
        if (live.AllChannels) return true;
        if (live.Channels.Contains(channel.Id)) return true;
        return channel is SocketThreadChannel t && t.ParentChannel != null && live.Channels.Contains(t.ParentChannel.Id);
    }

    private string LimitText() =>
        "You've asked a lot in a short time - the bot runs on a free quota shared by everyone (" + _cfg.PerHour + " an hour, " +
        _cfg.PerDay + " a day each). Try again a little later.";

    private static string Quote(string q)
    {
        q = (q ?? "").Replace('\n', ' ').Trim();
        return q.Length <= 300 ? q : q.Substring(0, 297) + "...";
    }

    private async Task Guard(Func<Task> work)
    {
        try { await work(); }
        catch (Exception e) { _log("Discord: handler failed: " + e); }
    }
}
