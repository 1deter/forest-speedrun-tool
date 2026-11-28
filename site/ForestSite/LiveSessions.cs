using System.Security.Cryptography;
using System.Text;

namespace ForestSite;

// ------------------------------------------------------------------
// Live sessions (T-0488): the relay between the author's test bridge
// (tools/BridgeMcp, admin token) and a runner's game that asked for it
// (the plugin's LiveLink, T-0489). The runner presses a button, the game
// opens a session and shows its 6-character code; the author's side sends
// bridge lines under that code, the game polls them with the session's
// secret and posts the bridge's output and `shot` pictures back.
//
// In memory on purpose: a session lives minutes, nothing from a runner's
// game is written to disk, and a restart of the site ends every session
// (the game sees 403 and stops). The site is one container.
//
//   * The code is what the runner reads out; the secret (48 hex) is the
//     game's alone. The code is only good with the admin token.
//   * Ends: the runner's Stop (close), the admin's close, 30 min with no
//     line sent, the game not polling for 2 min, 4 h in all. A closed
//     session answers the game 410 and stays readable for 10 min.
//   * Caps: 64 KB of lines waiting, 1 MB of output kept (older dropped,
//     the offset keeps counting), 20 pictures of 4 MB at most, 50
//     sessions open, one per address (a new open closes the old one).
// ------------------------------------------------------------------
public sealed class LiveSessions
{
    public const int MaxPending = 64 * 1024;
    public const int MaxOutput = 1024 * 1024;
    public const int MaxOutPost = 256 * 1024;
    public const int MaxFile = 4 * 1024 * 1024;
    public const int MaxFiles = 20;
    public const long MaxFileBytesAll = 200L * 1024 * 1024;   // every session's pictures together (checker, T-0488)
    public const int MaxOpen = 50;
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PollLimit = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan HardLimit = TimeSpan.FromHours(4);
    public static readonly TimeSpan KeepClosed = TimeSpan.FromMinutes(10);

    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O, 1/I

    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _byCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> _bySecret = new(StringComparer.Ordinal);

    public LiveSessions(Func<DateTime> now = null) { _now = now ?? (() => DateTime.UtcNow); }

    private sealed class Session
    {
        public string Code, Secret, Address;
        public DateTime Opened, LastSent, LastPoll;
        public DateTime? Closed;
        public string ClosedWhy;
        public readonly StringBuilder Pending = new();
        public readonly StringBuilder Output = new();
        public long Trimmed;                      // bytes dropped from the front of Output
        public readonly List<KeyValuePair<string, byte[]>> Files = new();
        public long OutTotal => Trimmed + Encoding.UTF8.GetByteCount(Output.ToString());
    }

    public sealed record Opened(string Code, string Secret, int IdleSeconds);
    public sealed record Info(string Code, DateTime Opened, DateTime LastCommand, DateTime LastPoll, long OutBytes, string State);
    public sealed record OutRead(string Text, long Offset, bool Open, bool Trimmed);

    /// Runner side, step 1. Null when the site is full.
    public Opened Open(string address)
    {
        lock (_gate)
        {
            Sweep();
            foreach (Session s in _byCode.Values)
                if (s.Closed == null && s.Address == address) Close(s, "replaced by a new session");
            if (OpenCount() >= MaxOpen) return null;

            string code;
            do code = NewCode(); while (_byCode.ContainsKey(code));
            DateTime now = _now();
            var n = new Session
            {
                Code = code, Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
                Address = address, Opened = now, LastSent = now, LastPoll = now,
            };
            _byCode[code] = n;
            _bySecret[n.Secret] = n;
            return new Opened(code, n.Secret, (int)IdleLimit.TotalSeconds);
        }
    }

    public enum Access { Ok, Unknown, Closed }

    /// The game's poll: the lines waiting, taken. Null unless Ok.
    public Access TakeInput(string secret, out string lines)
    {
        lines = null;
        lock (_gate)
        {
            Access a = Runner(secret, out Session s);
            if (a != Access.Ok) return a;
            s.LastPoll = _now();
            lines = s.Pending.ToString();
            s.Pending.Clear();
            return Access.Ok;
        }
    }

    public Access AddOutput(string secret, string text)
    {
        lock (_gate)
        {
            Access a = Runner(secret, out Session s);
            if (a != Access.Ok) return a;
            s.LastPoll = _now();
            if (!string.IsNullOrEmpty(text)) Append(s, text);
            return Access.Ok;
        }
    }

    public Access AddFile(string secret, string name, byte[] png)
    {
        lock (_gate)
        {
            Access a = Runner(secret, out Session s);
            if (a != Access.Ok) return a;
            s.LastPoll = _now();
            s.Files.RemoveAll(f => f.Key == name);
            s.Files.Add(new KeyValuePair<string, byte[]>(name, png));
            while (s.Files.Count > MaxFiles) s.Files.RemoveAt(0);
            // Past the site-wide cap the oldest pictures go, whoever's.
            while (FileBytesAll() > MaxFileBytesAll && DropOldestFile()) { }
            return Access.Ok;
        }
    }

    public Access CloseByRunner(string secret)
    {
        lock (_gate)
        {
            Access a = Runner(secret, out Session s);
            if (a == Access.Unknown) return a;
            if (a == Access.Ok) Close(s, "stopped by the runner");
            return Access.Ok;
        }
    }

    // --- the author's side (admin token checked by the endpoint group) -------

    public List<Info> List()
    {
        lock (_gate)
        {
            Sweep();
            var l = new List<Info>();
            foreach (Session s in _byCode.Values) l.Add(Describe(s));
            l.Sort((x, y) => y.Opened.CompareTo(x.Opened));
            return l;
        }
    }

    public enum SendResult { Ok, Unknown, Closed, TooMuch }

    public SendResult Send(string code, string lines)
    {
        lock (_gate)
        {
            Sweep();
            if (!_byCode.TryGetValue(Norm(code), out Session s)) return SendResult.Unknown;
            if (s.Closed != null) return SendResult.Closed;
            string text = (lines ?? "").Replace("\r", "");
            if (text.Length > 0 && !text.EndsWith('\n')) text += "\n";
            if (s.Pending.Length + text.Length > MaxPending) return SendResult.TooMuch;
            s.Pending.Append(text);
            s.LastSent = _now();
            return SendResult.Ok;
        }
    }

    /// Output after a byte offset of the whole stream. Null: no such code.
    public OutRead ReadOutput(string code, long after)
    {
        lock (_gate)
        {
            Sweep();
            if (!_byCode.TryGetValue(Norm(code), out Session s)) return null;
            byte[] kept = Encoding.UTF8.GetBytes(s.Output.ToString());
            long total = s.Trimmed + kept.Length;
            bool trimmed = after < s.Trimmed;
            long from = Math.Clamp(after, s.Trimmed, total) - s.Trimmed;
            string text = Encoding.UTF8.GetString(kept, (int)from, (int)(kept.Length - from));
            return new OutRead(text, total, s.Closed == null, trimmed);
        }
    }

    public byte[] File(string code, string name)
    {
        lock (_gate)
        {
            Sweep();
            if (!_byCode.TryGetValue(Norm(code), out Session s)) return null;
            for (int i = s.Files.Count - 1; i >= 0; i--)
                if (s.Files[i].Key == name) return s.Files[i].Value;
            return null;
        }
    }

    public bool CloseByAdmin(string code)
    {
        lock (_gate)
        {
            Sweep();
            if (!_byCode.TryGetValue(Norm(code), out Session s)) return false;
            if (s.Closed == null) Close(s, "closed by the author");
            return true;
        }
    }

    public static bool ValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
        foreach (char c in name)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')) return false;
        return true;
    }

    public static bool IsPng(byte[] b) =>
        b != null && b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
        b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    // --- internals -----------------------------------------------------------

    private Access Runner(string secret, out Session s)
    {
        Sweep();
        s = null;
        if (string.IsNullOrEmpty(secret) || !_bySecret.TryGetValue(secret, out s)) return Access.Unknown;
        return s.Closed == null ? Access.Ok : Access.Closed;
    }

    private static void Append(Session s, string text)
    {
        s.Output.Append(text);
        string all = s.Output.ToString();
        int bytes = Encoding.UTF8.GetByteCount(all);
        if (bytes <= MaxOutput) return;
        // Keep the newest half, cut at a line so a reply is not split.
        int keepFrom = all.Length - all.Length / 2;
        int nl = all.IndexOf('\n', keepFrom);
        if (nl >= 0) keepFrom = nl + 1;
        s.Trimmed += Encoding.UTF8.GetByteCount(all.Substring(0, keepFrom));
        s.Output.Clear().Append(all, keepFrom, all.Length - keepFrom);
    }

    private void Close(Session s, string why)
    {
        s.Closed = _now();
        s.ClosedWhy = why;
        s.Pending.Clear();
        Append(s, "== live session closed: " + why + " ==\n");
    }

    private void Sweep()
    {
        DateTime now = _now();
        List<string> gone = null;
        foreach (Session s in _byCode.Values)
        {
            if (s.Closed == null)
            {
                if (now - s.Opened > HardLimit) Close(s, "4 hours in all");
                else if (now - s.LastSent > IdleLimit) Close(s, "30 minutes with no command");
                else if (now - s.LastPoll > PollLimit) Close(s, "the game stopped answering");
            }
            if (s.Closed != null && now - s.Closed.Value > KeepClosed) (gone ??= new List<string>()).Add(s.Code);
        }
        if (gone == null) return;
        foreach (string c in gone)
        {
            _bySecret.Remove(_byCode[c].Secret);
            _byCode.Remove(c);
        }
    }

    private long FileBytesAll()
    {
        long n = 0;
        foreach (Session s in _byCode.Values)
            foreach (var f in s.Files) n += f.Value.Length;
        return n;
    }

    // Sessions are oldest-opened first by their own lists; the oldest
    // session's first picture is the oldest kept.
    private bool DropOldestFile()
    {
        Session oldest = null;
        foreach (Session s in _byCode.Values)
            if (s.Files.Count > 0 && (oldest == null || s.Opened < oldest.Opened)) oldest = s;
        if (oldest == null) return false;
        oldest.Files.RemoveAt(0);
        return true;
    }

    /// For the author's long poll: the output has grown past `after`, or
    /// the session is closed or gone - worth answering now.
    public bool HasNews(string code, long after)
    {
        lock (_gate)
        {
            Sweep();
            if (!_byCode.TryGetValue(Norm(code), out Session s)) return true;
            return s.Closed != null || s.OutTotal > after;
        }
    }

    private int OpenCount()
    {
        int n = 0;
        foreach (Session s in _byCode.Values) if (s.Closed == null) n++;
        return n;
    }

    private static Info Describe(Session s) =>
        new(s.Code, s.Opened, s.LastSent, s.LastPoll, s.OutTotal, s.Closed == null ? "open" : "closed: " + s.ClosedWhy);

    private static string Norm(string code) => (code ?? "").Trim().ToUpperInvariant();

    private static string NewCode()
    {
        Span<char> c = stackalloc char[6];
        for (int i = 0; i < c.Length; i++) c[i] = CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)];
        return new string(c);
    }
}
