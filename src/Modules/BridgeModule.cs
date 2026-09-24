using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The live test bridge. PRACTICE ONLY, off by default (dev tool).
    //
    // Everything before this was static - IL and logs. The bridge lets a
    // session outside the game ask the running game questions: it polls
    // config/ForestOverlay/bridge/in.txt, runs each line on the main
    // thread, one command at a time, and appends the replies to out.txt:
    //
    //   > #12 find _Dummy 100
    //   #-51234  mutant_male_Dummy(Clone)  (10.2, 30, 55.1)  4.3 m
    //   1 object(s), nearest first
    //   < #12 ok (3 ms)
    //
    // A command that waits (capture, restore, restart, wait) holds the
    // queue until it is done, so a batch reads as a script. Write in.txt
    // whole (write in.tmp, then rename); the bridge deletes it once read.
    // Every command logs one "Bridge #n" line. scripts/bridge.sh is the
    // other end.
    // ------------------------------------------------------------------
    public sealed class BridgeModule : OverlayModule
    {
        public override string Id { get { return "bridge"; } }
        public override string DisplayName { get { return "Test bridge"; } }
        public override bool IsPracticeOnly { get { return true; } }

        private const float PollInterval = 0.2f;
        private const long MaxOutBytes = 4 * 1024 * 1024;
        private const float DefaultWaitTimeout = 120f;

        private ConfigEntry<bool> _enabled;
        private string _dir;
        private string _inPath;
        private string _outPath;
        private float _nextPoll;
        private int _count;
        private bool _announced;

        private readonly ObjectProbe _probe = new ObjectProbe();
        private readonly Queue<string> _queue = new Queue<string>();

        // The command in progress, when it waits.
        private int _pendingId;
        private string _pendingLine;
        private float _pendingStart;
        private float _waitUntil;          // wait / waitidle deadline (realtime)
        private bool _waitIdle;            // waitidle / restart: until savestates are idle
        private int _idleFrames;
        private bool _waitCallback;        // capture / restore: until their callback
        private string _callbackResult;
        private bool _callbackDone;

        private readonly GUIContent _statusText = new GUIContent("");
        private string _lastError = "";

        public bool Enabled
        {
            get { return _enabled != null && _enabled.Value; }
            set { if (_enabled != null) _enabled.Value = value; }
        }

        /// One line for the Settings tab, rebuilt in Tick.
        public GUIContent StatusText { get { return _statusText; } }

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _dir = Path.Combine(ctx.ConfigDirectory, "bridge");
            _inPath = Path.Combine(_dir, "in.txt");
            _outPath = Path.Combine(_dir, "out.txt");
            _enabled = ctx.Config.Bind("Diagnostics", "TestBridge", false,
                "Developer tool: run commands written to config/ForestOverlay/bridge/in.txt (inspect objects, " +
                "read and write fields, capture / restore / teleport) and write the replies to out.txt. " +
                "Can change anything in the game - marks the session as practice when it does.");
            RebuildStatus();
        }

        public override void Tick()
        {
            if (!Enabled)
            {
                if (_announced) { _announced = false; Ctx.Log.LogInfo("Bridge: off."); RebuildStatus(); }
                return;
            }

            if (!_announced)
            {
                _announced = true;
                try { Directory.CreateDirectory(_dir); } catch (Exception ex) { _lastError = ex.Message; }
                Append("== bridge on, ForestOverlay v" + OverlayPlugin.PluginVersion + ", " +
                       DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==\n");
                Ctx.Log.LogInfo("Bridge: on, watching " + _inPath);
                RebuildStatus();
            }

            if (_pendingId != 0) { TickPending(); return; }

            if (_queue.Count == 0)
            {
                if (Time.unscaledTime < _nextPoll) return;
                _nextPoll = Time.unscaledTime + PollInterval;
                ReadInput();
                if (_queue.Count == 0) return;
            }

            // One command a frame: a heavy find is one hitch, not a stall.
            Run(_queue.Dequeue());
        }

        private void ReadInput()
        {
            try
            {
                if (!File.Exists(_inPath)) return;
                string text = File.ReadAllText(_inPath, Encoding.UTF8);
                File.Delete(_inPath);
                string[] lines = text.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Trim().Length > 0) _queue.Enqueue(lines[i]);
                _lastError = "";
            }
            catch (Exception ex)
            {
                // Still being written, most likely; the next poll retries.
                _lastError = "could not read in.txt: " + ex.Message;
            }
            RebuildStatus();
        }

        private void RebuildStatus()
        {
            _statusText.text = !Enabled
                ? "Test bridge off (developer tool)."
                : "Test bridge: watching " + _inPath + " - " + _count + " command(s) run" +
                  (_lastError.Length > 0 ? " - " + _lastError : "") + ".";
        }

        // ------------------------------------------------------------------
        // Running a command

        private void Run(string line)
        {
            int id = ++_count;
            List<string> tokens = BridgeCommand.Tokenize(line);
            if (tokens.Count == 0) return;

            List<string> output = new List<string>();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            string cmd = tokens[0].ToLowerInvariant();
            tokens.RemoveAt(0);

            _probe.Player = Ctx.Player.Found ? Ctx.Player.Transform.root : null;

            string error;
            bool waits = false;
            try { error = Dispatch(cmd, tokens, output, out waits); }
            catch (Exception ex) { error = "threw " + ex.GetType().Name + ": " + ex.Message; }

            StringBuilder sb = new StringBuilder();
            if (cmd != "echo") sb.Append("> #").Append(id).Append(' ').Append(line.Trim()).Append('\n');
            for (int i = 0; i < output.Count; i++) sb.Append(output[i]).Append('\n');

            if (waits && error == null)
            {
                _pendingId = id;
                _pendingLine = line.Trim();
                _pendingStart = Time.realtimeSinceStartup;
            }
            else if (cmd != "echo")
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                sb.Append(Close(id, error, ms.ToString("0", CultureInfo.InvariantCulture) + " ms")).Append('\n');
                Ctx.Log.LogInfo("Bridge #" + id + ": " + BridgeCommand.OneLine(line.Trim(), 120) + " -> " +
                                (error == null ? "ok, " + output.Count + " line(s)" : "error: " + BridgeCommand.OneLine(error, 200)));
            }
            Append(sb.ToString());
            RebuildStatus();
        }

        private static string Close(int id, string error, string took)
        {
            return error == null
                ? "< #" + id + " ok (" + took + ")"
                : "< #" + id + " error: " + error + " (" + took + ")";
        }

        private void TickPending()
        {
            float now = Time.realtimeSinceStartup;
            string error = null;
            bool done;

            if (_waitCallback)
            {
                done = _callbackDone;
                error = _callbackResult;
            }
            else if (_waitIdle)
            {
                SavestateModule s = Host.Find<SavestateModule>();
                bool idle = (s == null || !s.Busy) && Ctx.Player.Found;
                _idleFrames = idle ? _idleFrames + 1 : 0;
                done = _idleFrames >= 3;
            }
            else done = now >= _waitUntil;

            if (!done && (_waitCallback || _waitIdle) && now >= _waitUntil)
            {
                done = true;
                error = "timed out after " + (now - _pendingStart).ToString("0", CultureInfo.InvariantCulture) + " s";
            }
            if (!done) return;

            int id = _pendingId;
            _pendingId = 0;
            _waitCallback = _waitIdle = _callbackDone = false;
            _callbackResult = null;

            string took = (now - _pendingStart).ToString("0.00", CultureInfo.InvariantCulture) + " s";
            Append(Close(id, error, took) + "\n");
            Ctx.Log.LogInfo("Bridge #" + id + ": " + BridgeCommand.OneLine(_pendingLine, 120) + " -> " +
                            (error == null ? "done in " + took : "error: " + error));
        }

        private void WaitFor(float seconds)
        {
            _waitUntil = Time.realtimeSinceStartup + seconds;
        }

        private void WaitIdle(float timeout)
        {
            _waitIdle = true;
            _idleFrames = 0;
            WaitFor(timeout);
        }

        private Action<string> WaitCallback(float timeout)
        {
            _waitCallback = true;
            _callbackDone = false;
            _callbackResult = null;
            WaitFor(timeout);
            return delegate(string error)
            {
                _callbackResult = error;
                _callbackDone = true;
            };
        }

        private void Append(string text)
        {
            try
            {
                if (File.Exists(_outPath) && new FileInfo(_outPath).Length > MaxOutBytes)
                {
                    string old = Path.Combine(_dir, "out.old.txt");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(_outPath, old);
                }
                File.AppendAllText(_outPath, text, new UTF8Encoding(false));
            }
            catch (Exception ex) { _lastError = "could not write out.txt: " + ex.Message; }
        }

        // ------------------------------------------------------------------
        // Commands

        private string Dispatch(string cmd, List<string> a, List<string> o, out bool waits)
        {
            waits = false;
            switch (cmd)
            {
                case "help": Help(o); return null;
                case "echo": o.Add(string.Join(" ", a.ToArray())); return null;
                case "status": return Status(o);
                case "player": return PlayerInfo(o);

                case "wait":
                {
                    float s;
                    if (a.Count < 1 || !BridgeCommand.TryParseFloat(a[0], out s)) return "wait <seconds>";
                    WaitFor(s);
                    waits = true;
                    return null;
                }
                case "waitidle":
                {
                    float s = DefaultWaitTimeout;
                    if (a.Count > 0 && !BridgeCommand.TryParseFloat(a[0], out s)) return "waitidle [timeout seconds]";
                    WaitIdle(s);
                    waits = true;
                    return null;
                }

                case "find":
                {
                    bool all = BridgeCommand.TakeFlag(a, "all");
                    int max = MaxOption(a, 100);
                    if (a.Count < 1) return "find <name part|*> [radius] [all] [max=N]";
                    float radius = 0f;
                    if (a.Count > 1 && !BridgeCommand.TryParseFloat(a[1], out radius)) return "radius must be a number";
                    return _probe.Find(a[0], radius, all, max, o);
                }
                case "type":
                {
                    bool all = BridgeCommand.TakeFlag(a, "all");
                    int max = MaxOption(a, 100);
                    if (a.Count < 1) return "type <Type> [radius] [all] [max=N]";
                    string note;
                    Type t = _probe.FindType(a[0], out note);
                    if (t == null) return note;
                    if (note != null) o.Add(note);
                    float radius = 0f;
                    if (a.Count > 1 && !BridgeCommand.TryParseFloat(a[1], out radius)) return "radius must be a number";
                    return _probe.FindByType(t, radius, all, max, o);
                }
                case "roots": return _probe.Roots(a.Count > 0 ? a[0] : null, o);
                case "types":
                {
                    int max = MaxOption(a, 50);
                    if (a.Count < 1) return "types <text> [max=N]";
                    return _probe.Types(a[0], max, o);
                }
                case "members":
                {
                    if (a.Count < 1) return "members <Type> [filter]";
                    string note;
                    Type t = _probe.FindType(a[0], out note);
                    if (t == null) return note;
                    if (note != null) o.Add(note);
                    return _probe.Members(t, a.Count > 1 ? a[1] : null, o);
                }

                case "inspect":
                {
                    if (a.Count < 1) return "inspect <target> [depth]";
                    object target; Type st;
                    string err = _probe.ResolveTarget(a[0], out target, out st);
                    if (err != null) return err;
                    if (st != null) return _probe.Fields(null, st, null, o);
                    int depth = 1;
                    if (a.Count > 1 && !int.TryParse(a[1], out depth)) return "depth must be a number";
                    return _probe.Inspect(target, depth, o);
                }
                case "fields":
                {
                    if (a.Count < 1) return "fields <target> [path]";
                    object target; Type st;
                    string err = _probe.ResolveTarget(a[0], out target, out st);
                    if (err != null) return err;
                    return _probe.Fields(target, st, a.Count > 1 ? a[1] : null, o);
                }
                case "get":
                {
                    if (a.Count < 2) return "get <target> <path>";
                    object target; Type st;
                    string err = _probe.ResolveTarget(a[0], out target, out st);
                    if (err != null) return err;
                    return _probe.Get(target, st, a[1], o);
                }
                case "set":
                {
                    if (a.Count < 3) return "set <target> <path> <value>";
                    object target; Type st;
                    string err = _probe.ResolveTarget(a[0], out target, out st);
                    if (err != null) return err;
                    Mark("set " + a[1]);
                    return _probe.Set(target, st, a[1], a[2], o);
                }
                case "call":
                {
                    if (a.Count < 2) return "call <target> <path.Method> [args...]";
                    object target; Type st;
                    string err = _probe.ResolveTarget(a[0], out target, out st);
                    if (err != null) return err;
                    Mark("call " + a[1]);
                    return _probe.Call(target, st, a[1], a.GetRange(2, a.Count - 2), Ctx.Runner, o);
                }
                case "destroy":
                {
                    if (a.Count < 1) return "destroy <target>";
                    object target; Type st;
                    string err = _probe.ResolveTarget(a[0], out target, out st);
                    if (err != null) return err;
                    Mark("destroy");
                    return _probe.Destroy(target, o);
                }

                case "savestates":
                {
                    SavestateModule s = Host.Find<SavestateModule>();
                    if (s == null) return "no savestate module";
                    s.ListFiles(o);
                    o.Add(o.Count + " savestate(s)");
                    return null;
                }
                case "capture":
                {
                    SavestateModule s = Host.Find<SavestateModule>();
                    if (s == null) return "no savestate module";
                    if (a.Count < 1) return "capture <name>";
                    waits = true;
                    s.CaptureNamed(string.Join(" ", a.ToArray()), WaitCallback(DefaultWaitTimeout));
                    return null;
                }
                case "restore":
                {
                    SavestateModule s = Host.Find<SavestateModule>();
                    if (s == null) return "no savestate module";
                    bool load = BridgeCommand.TakeFlag(a, "load");
                    BridgeCommand.TakeFlag(a, "inplace");
                    if (a.Count < 1) return "restore <name> [load]";
                    waits = true;
                    s.RestoreNamed(string.Join(" ", a.ToArray()), load, WaitCallback(DefaultWaitTimeout));
                    return null;
                }

                case "spots": return Spots(a.Count > 0 ? a[0] : null, o);
                case "go":
                {
                    PracticeModule p = Host.Find<PracticeModule>();
                    if (p == null) return "no practice module";
                    if (a.Count < 1) return "go <spot id>";
                    return p.BridgeGo(a[0]);
                }
                case "restart":
                {
                    PracticeModule p = Host.Find<PracticeModule>();
                    if (p == null) return "no practice module";
                    string err = p.BridgeRestart(a.Count > 0 ? a[0] : null);
                    if (err != null) return err;
                    WaitIdle(DefaultWaitTimeout);
                    waits = true;
                    return null;
                }
                case "tp": return Teleport(a, o);
                case "dump":
                {
                    DumpModule d = Host.Find<DumpModule>();
                    if (d == null) return "no dump module";
                    d.WriteDumps();
                    o.Add("dumps -> " + GameDumper.DumpDirectory);
                    return null;
                }
            }
            return "unknown command '" + cmd + "' (help lists them)";
        }

        private static int MaxOption(List<string> a, int fallback)
        {
            string v = BridgeCommand.TakeOption(a, "max");
            int n;
            return v != null && int.TryParse(v, out n) && n > 0 ? n : fallback;
        }

        private void Mark(string what)
        {
            Ctx.Practice.Mark("test bridge: " + what);
        }

        private static readonly string[] HelpLines =
        {
            "targets: #<handle> (from any listing) | player | camera | static:<Type> | a GameObject name or path",
            "paths: Component.field.sub[2].x - on a GameObject the first step is a component (GameObject = itself, Comp[1] = the 2nd)",
            "help | echo <text> | status | player",
            "wait <s> | waitidle [timeout]  - hold the queue (the game keeps running)",
            "find <name part|*> [radius] [all] [max=N]  - GameObjects, nearest first; all = inactive too",
            "type <Type> [radius] [all] [max=N]  - objects carrying that component",
            "roots [filter] | types <text> | members <Type> [filter]",
            "inspect <target> [depth] | fields <target> [path] | get <target> <path>",
            "set <target> <path> <value> | call <target> <path.Method> [args] | destroy <target>   (practice)",
            "  values: numbers, true/false, enum names, x,y,z, null, #handle; an IEnumerator method starts as a coroutine",
            "savestates | capture <name> | restore <name> [load]   (capture / restore wait until done)",
            "spots [filter] | go <id> | restart [id] (waits until idle) | tp x y z [yaw] | dump",
        };

        private static void Help(List<string> o)
        {
            o.AddRange(HelpLines);
        }

        private string Status(List<string> o)
        {
            SavestateModule s = Host.Find<SavestateModule>();
            PracticeModule p = Host.Find<PracticeModule>();
            o.Add("scene '" + SceneManager.GetActiveScene().name + "', frame " + Time.frameCount +
                  ", time " + Time.time.ToString("0.0", CultureInfo.InvariantCulture) +
                  ", timeScale " + Time.timeScale.ToString("0.##", CultureInfo.InvariantCulture));
            o.Add("player " + (Ctx.Player.Found ? ObjectProbe.Vec(Ctx.Player.Transform.position) : "not found"));
            o.Add("practice: " + (Ctx.Practice.Used ? Ctx.Practice.Reason + " (x" + Ctx.Practice.UseCount + ")" : "clean"));
            o.Add("savestates " + (s == null ? "missing" : s.Busy ? "busy" : "idle") +
                  ", current spot " + (p != null && p.CurrentSegment != null ? "'" + p.CurrentSegment.Id + "'" : "none"));
            return null;
        }

        private string PlayerInfo(List<string> o)
        {
            if (!Ctx.Player.Found) return "no player";
            Transform t = Ctx.Player.Transform;
            o.Add(_probe.Format(t.root.gameObject) + " at " + ObjectProbe.Vec(t.position) +
                  ", yaw " + t.eulerAngles.y.ToString("0.#", CultureInfo.InvariantCulture) +
                  ", pitch " + Ctx.Bridge.GetLookPitch().ToString("0.#", CultureInfo.InvariantCulture));
            o.Add("in caves " + Ctx.Bridge.IsInCaves() + ", speed " + Ctx.Player.Speed.ToString("0.00", CultureInfo.InvariantCulture) +
                  " m/s, velocity " + ObjectProbe.Vec(Ctx.Player.Velocity));
            return null;
        }

        private string Spots(string filter, List<string> o)
        {
            PracticeModule p = Host.Find<PracticeModule>();
            SavestateModule s = Host.Find<SavestateModule>();
            if (p == null) return "no practice module";
            IList<Segment> all = p.Library.All;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Segment seg = all[i];
                if (filter != null && seg.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    seg.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                n++;
                o.Add(seg.Id + "  '" + seg.Name + "'  " + (seg.HasSpawn ? ObjectProbe.Vec(seg.SpawnPosition) : "no spawn") +
                      (seg.IsTimed ? "  timed" : "") +
                      (s != null && s.HasStartState(seg) ? "  start state" + (seg.StartRestoreWithLoad ? " (load)" : "") : "") +
                      (p.CurrentSegment == seg ? "  [current]" : ""));
            }
            o.Add(n + " entr" + (n == 1 ? "y" : "ies"));
            return null;
        }

        private string Teleport(List<string> a, List<string> o)
        {
            if (!Ctx.Player.Found) return "no player";
            float x, y, z, yaw = Ctx.Player.Transform.eulerAngles.y;
            if (a.Count < 3 || !BridgeCommand.TryParseFloat(a[0], out x) || !BridgeCommand.TryParseFloat(a[1], out y) ||
                !BridgeCommand.TryParseFloat(a[2], out z)) return "tp <x> <y> <z> [yaw]";
            if (a.Count > 3 && !BridgeCommand.TryParseFloat(a[3], out yaw)) return "yaw must be a number";

            Vector3 to = new Vector3(x, y, z);
            string cave = Ctx.Bridge.SyncCaveState(to);
            if (!Ctx.Player.MoveTo(to, Quaternion.Euler(0f, yaw, 0f))) return "could not move the player";
            string fall = Ctx.Bridge.EndFall();
            Mark("teleport");
            o.Add("at " + ObjectProbe.Vec(to) + (cave.Length > 0 ? ", " + cave : "") + (fall.Length > 0 ? ", " + fall : ""));
            return null;
        }
    }
}
