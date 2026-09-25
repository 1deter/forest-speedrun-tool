using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // The tools. Most are one bridge command with typed arguments; `run`
    // takes raw lines and reaches everything the bridge can do. The rest
    // work beside the bridge: screenshots as images, the logs, the IL
    // scanner, and closing / launching / updating the game.
    // ------------------------------------------------------------------
    internal sealed class Tools
    {
        /// The plugin's GameObject: stable across launches, unlike its #handle.
        private const string Plugin = "BepInEx_Manager";
        private const string Host = "OverlayPlugin._host";

        private readonly ForestPaths _paths;
        private readonly BridgeClient _bridge;
        public readonly List<Tool> All = new List<Tool>();

        public Tools(ForestPaths paths, BridgeClient bridge)
        {
            _paths = paths;
            _bridge = bridge;
            Register();
        }

        public const string Instructions =
            "Drives The Forest (running, with ForestOverlay's Settings -> Test bridge on) through the live test bridge: " +
            "each tool writes bridge commands to in.txt and returns the game's replies from out.txt. " +
            "Targets: #<handle> (printed by every listing; valid for this launch only), player, camera, static:<Type>, " +
            "or a GameObject name or path (BepInEx_Manager is the plugin: BepInEx_Manager OverlayPlugin._host...). " +
            "Paths: Component.field.sub[2].x - on a GameObject the first step is a component (GameObject = itself, Comp[1] = the second). " +
            "Values: numbers, true/false, enum names, x,y,z vectors (no spaces), null, #handle. " +
            "set / call / destroy / teleport / screenshot mark the session as practice (expected). " +
            "`run` sends raw bridge lines in order (one a frame; capture / restore / restart / wait hold the queue) - " +
            "use it for scripts and for anything without a typed tool (its `help` lists every command). " +
            "Instructions for the player go on the game screen with `notice` (6-8 s each), not in chat. " +
            "`status` says why the bridge is not answering; `game` closes / launches / restarts the game, `update_game` installs a release.";

        // ------------------------------------------------------------------
        // Registration

        private void Register()
        {
            Add("status",
                "Game and bridge state: is TheForest.exe running, the bridge setting, the plugin version, the scene, " +
                "the player's position / cave state / action FSM state, practice flag, savestate idle, current spot. " +
                "Works with the game closed (says so). Start here when a call fails.",
                Schema(), Status);

            Add("run",
                "Runs raw bridge command lines in order and returns the transcript (each command, its output, ok / error). " +
                "For scripts (notice + wait + action sequences) and commands without a typed tool (`help` lists them all: " +
                "find, type, roots, types, members, inspect, fields, get, set, call, destroy, savestates, capture, restore, " +
                "spots, go, restart, tp, mark, anim, shot, dump, wait, waitidle, echo, status, player). " +
                "Quote an argument with spaces in double quotes. The timeout defaults to 30 s plus every wait plus 150 s " +
                "per capture / restore / restart / waitidle.",
                Schema(
                    P("commands", "string[]", "Command lines, e.g. [\"find _male 40\", \"wait 2\", \"get player Transform.position\"]."),
                    P("file", "string", "A file of command lines (one per line, # comments) to run before `commands`."),
                    P("timeout_s", "number", "Seconds to wait for every reply (default: estimated from the commands).")),
                Run);

            Add("find",
                "Lists GameObjects by name part (or `*`), or by component type, nearest to the player first, " +
                "each with its #handle, path, position and distance.",
                Schema(
                    P("name", "string", "Name part, case-insensitive, or * for all. Give this or `type`."),
                    P("type", "string", "A component type (short or full name) instead of a name."),
                    P("radius", "number", "Only within this many metres of the player."),
                    P("include_inactive", "boolean", "Inactive objects too (slower)."),
                    P("max", "integer", "At most this many (default 100).")),
                Find);

            Add("roots", "Scene root GameObjects per loaded scene, with handles.",
                Schema(P("filter", "string", "Name part."), P("max", "integer", "Per scene (default 60).")),
                a => Opt("roots", a.Str("filter"), MaxArg(a)));

            Add("types", "Loaded types whose name contains the text (every assembly, the game's included).",
                Schema(P("text", "string", "Part of the type name.", true), P("max", "integer", "Default 50.")),
                a => Opt("types", a.Need("text"), MaxArg(a)));

            Add("members", "A type's fields, properties and methods from the live runtime (static ones marked).",
                Schema(P("type", "string", "Type name, short or full.", true), P("filter", "string", "Member name part.")),
                a => Opt("members", a.Need("type"), a.Str("filter")));

            Add("inspect",
                "An object's summary: path, active state, layer, scene, transform and its components (depth > 1: " +
                "each component's fields too). On a static: target, its static fields.",
                Schema(P("target", "string", "Target (see the server instructions).", true),
                       P("depth", "integer", "1 = components only (default).")),
                a => Opt("inspect", a.Need("target"), IntArg(a, "depth")));

            Add("fields", "Every field and property value of a target, or of the object at a path under it.",
                Schema(P("target", "string", "Target.", true), P("path", "string", "Path under the target, e.g. PlayerStats.")),
                a => Opt("fields", a.Need("target"), a.Str("path")));

            Add("get",
                "Reads one or more values: each path under the target, e.g. target `static:TheForest.Utils.LocalPlayer`, " +
                "paths [\"ScriptSetup.pmControl.ActiveStateName\", \"Stats.Health\"].",
                Schema(P("target", "string", "Target.", true),
                       P("paths", "string[]", "Paths to read (one or many).", true)),
                Get);

            Add("set", "Writes a field or property (marks practice). Vectors as x,y,z; enums by name.",
                Schema(P("target", "string", "Target.", true), P("path", "string", "Member path.", true),
                       P("value", "string", "The new value.", true)),
                a => One(BridgeText.Command("set", a.Need("target"), a.Need("path"), a.Need("value"))));

            Add("call",
                "Calls a method (marks practice); an IEnumerator method is started as a coroutine. " +
                "Overloads are chosen by argument count. Returns the return value.",
                Schema(P("target", "string", "Target.", true),
                       P("method", "string", "Path ending in the method, e.g. Inventory.Equip.", true),
                       P("args", "string[]", "Arguments as strings (numbers, true/false, enum names, x,y,z, #handle, text).")),
                a =>
                {
                    List<string> parts = new List<string> { a.Need("target"), a.Need("method") };
                    parts.AddRange(a.List("args"));
                    return One(BridgeText.Command("call", parts.ToArray()));
                });

            Add("destroy", "Destroys a GameObject or component (marks practice).",
                Schema(P("target", "string", "Target.", true)),
                a => One(BridgeText.Command("destroy", a.Need("target"))));

            Add("savestates", "Lists the savestate files (config/ForestOverlay/savestates).",
                Schema(), a => One("savestates"));

            Add("capture", "Captures a savestate under a name; waits until it is written.",
                Schema(P("name", "string", "Savestate name.", true)),
                a => One(BridgeText.Command("capture", a.Need("name")), 200));

            Add("restore",
                "Restores a savestate and waits until it is done: Quick load (in place, default) or Full load (load = true, " +
                "with a scene load).",
                Schema(P("name", "string", "Savestate name.", true),
                       P("load", "boolean", "Full load instead of Quick load.")),
                a => One(BridgeText.Command("restore", a.Need("name"), a.Bool("load") ? "load" : null), 200));

            Add("spots", "Lists the Practice spots / segments (id, name, spawn, timed, start state, current).",
                Schema(P("filter", "string", "Id or name part.")),
                a => Opt("spots", a.Str("filter")));

            Add("go", "Teleports to a Practice spot (Go: teleport only, no restore).",
                Schema(P("id", "string", "Spot id.", true)),
                a => One(BridgeText.Command("go", a.Need("id"))));

            Add("restart_spot",
                "Restarts a spot like F7 (restores its start state if it has one, then teleports) and waits until idle. " +
                "No id: the current spot.",
                Schema(P("id", "string", "Spot id (default: the current spot).")),
                a => One(BridgeText.Command("restart", a.Str("id")), 200));

            Add("teleport", "Teleports the player (cave state follows the destination; marks practice).",
                Schema(P("x", "number", "", true), P("y", "number", "", true), P("z", "number", "", true),
                       P("yaw", "number", "Facing, degrees (default: keep).")),
                a => One(BridgeText.Command("tp", F(a.Num("x")), F(a.Num("y")), F(a.Num("z")),
                                            a.Has("yaw") ? F(a.Num("yaw")) : null)));

            Add("mark",
                "Puts a magenta beacon, visible through walls, on a target or a position - to point the player at " +
                "something (never give compass directions). clear = remove all (max 16).",
                Schema(P("target", "string", "Target to mark."),
                       P("x", "number", ""), P("y", "number", ""), P("z", "number", ""),
                       P("clear", "boolean", "Remove every beacon.")),
                Mark);

            Add("anim",
                "The player's animator: snapshot (layers, states, clips, parameters), watch (log every change for N seconds " +
                "in the background - later calls run meanwhile, the samples appear in later replies) or reset.",
                Schema(P("mode", "string", "snapshot (default), watch or reset.", false, "snapshot", "watch", "reset"),
                       P("seconds", "number", "For watch.")),
                Anim);

            Add("screenshot",
                "Takes an in-game screenshot (with the overlay's UI) and returns it as an image. The PNG stays in the " +
                "bridge folder. A shot on the frame of an action shows that frame - use delay_s to see its outcome.",
                Schema(P("name", "string", "File name without .png (default mcp-<time>)."),
                       P("delay_s", "number", "Seconds to wait before the shot."),
                       P("max_width", "integer", "Scale down to this width (default 1280; 0 = full size)."),
                       P("region", "integer[]", "Crop first: [x, y, width, height] in screen pixels (top-left origin) - " +
                                                "for reading small text at full resolution."),
                       P("png", "boolean", "PNG instead of JPEG (sharper text, bigger).")),
                Screenshot);

            Add("notice",
                "Shows text on the game screen (upper middle) for the player - how to give instructions mid-test. " +
                "At least 6-8 s per notice; the player reacts about 1 s after it appears.",
                Schema(P("text", "string", "What to show.", true),
                       P("seconds", "number", "How long (default 8).")),
                Notice);

            Add("open_tab",
                "Opens the ForestOverlay window on a tab by name (Practice, Savestates, Runs, Deaths, Debug views, " +
                "Inventory, 100%, Settings, QA, Updates...), opens the type explorer (`explorer`), or closes the window " +
                "(`close`). Unknown names list the tabs.",
                Schema(P("tab", "string", "Tab title or module id, or close.", true),
                       P("screenshot", "boolean", "Return a screenshot of it too.")),
                OpenTab);

            Add("wait",
                "Holds for some seconds while the game runs, or until the savestates are idle (until_idle) - " +
                "the same wait the bridge uses between scripted steps.",
                Schema(P("seconds", "number", "Seconds to wait."),
                       P("until_idle", "boolean", "Until no restore / load is running (seconds = timeout).")),
                Wait);

            Add("log",
                "Searches or tails the BepInEx log (LogOutput.log): the plugin's evidence lines (Bridge #n, Restart '<id>', " +
                "Game event:, Perf (30 s):, Death, errors). session 0 = the current (or last) game session, 1 = the one " +
                "before, 2 = ... (the plugin keeps the last 3).",
                Schema(P("pattern", "string", "Case-insensitive regex (plain text works). Omit to tail."),
                       P("session", "integer", "0 = current (default), 1 = previous, 2 = ..."),
                       P("context", "integer", "Lines around each match (default 0)."),
                       P("max", "integer", "At most this many matches, the newest (default 80)."),
                       P("tail", "integer", "Without a pattern: the last N lines (default 60).")),
                Log);

            Add("ilscan",
                "Offline IL query over the game's Assembly-CSharp.dll (tools/ILScan): refs (methods referencing a member), " +
                "writes (methods storing a field / property), body (disassembly of Type::Method), type (members, static " +
                "marked), strings (methods with a string literal - SendMessage / Invoke / StartCoroutine by name).",
                Schema(P("mode", "string", "refs, writes, body, type or strings.", true, "refs", "writes", "body", "type", "strings"),
                       P("needle", "string", "Case-insensitive substring of the full member name, e.g. PlayerStats::Hit.", true),
                       P("max", "integer", "Default 60.")),
                IlScan);

            Add("game",
                "Closes, launches or restarts The Forest and waits until the bridge answers again (the title screen is " +
                "enough). Unsaved progress is lost - fine while testing (author). Launch goes through Steam by default.",
                Schema(P("action", "string", "close, launch or restart.", true, "close", "launch", "restart"),
                       P("via", "string", "steam (default) or exe (TheForest.exe directly).", false, "steam", "exe")),
                Game);

            Add("update_game",
                "Installs the latest GitHub release the way a runner does: the plugin's own update check, Download " +
                "(staged as ForestOverlay.dll.pending), then a game restart so the patcher swaps it in; reports the " +
                "version running afterwards. Calls the GitHub API once (60 an hour per IP, shared with the game): " +
                "never loop it.",
                Schema(P("restart", "boolean", "Restart the game once staged (default true)."),
                       P("via", "string", "steam (default) or exe.", false, "steam", "exe")),
                UpdateGame);
        }

        private void Add(string name, string description, JsonObject schema, Func<Args, CancellationToken, Task<ToolResult>> run)
        {
            All.Add(new Tool { Name = name, Description = description, Schema = schema, Run = run });
        }

        private void Add(string name, string description, JsonObject schema, Func<Args, Func<CancellationToken, Task<ToolResult>>> run)
        {
            Add(name, description, schema, (a, ct) => run(a)(ct));
        }

        internal sealed class Prop
        {
            public string Name, Type, Description;
            public bool Required;
            public string[] Enum;
        }

        internal static Prop P(string name, string type, string description, bool required = false, params string[] values)
        {
            return new Prop { Name = name, Type = type, Description = description, Required = required, Enum = values };
        }

        internal static JsonObject Schema(params Prop[] props)
        {
            JsonObject properties = new JsonObject();
            JsonArray required = new JsonArray();
            foreach (Prop p in props)
            {
                JsonObject s;
                if (p.Type.EndsWith("[]"))
                    s = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = p.Type.Substring(0, p.Type.Length - 2) } };
                else s = new JsonObject { ["type"] = p.Type };
                if (!string.IsNullOrEmpty(p.Description)) s["description"] = p.Description;
                if (p.Enum != null && p.Enum.Length > 0)
                {
                    JsonArray e = new JsonArray();
                    foreach (string v in p.Enum) e.Add(v);
                    s["enum"] = e;
                }
                properties[p.Name] = s;
                if (p.Required) required.Add(p.Name);
            }
            JsonObject schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            if (required.Count > 0) schema["required"] = required;
            return schema;
        }

        private static string F(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : null;
        }

        private static string MaxArg(Args a)
        {
            double? m = a.Num("max");
            return m.HasValue ? "max=" + (int)m.Value : null;
        }

        private static string IntArg(Args a, string name)
        {
            double? v = a.Num(name);
            return v.HasValue ? ((int)v.Value).ToString(CultureInfo.InvariantCulture) : null;
        }

        // ------------------------------------------------------------------
        // Bridge helpers

        private Task<BridgeReply> Send(IList<string> commands, CancellationToken ct, double? timeoutSeconds = null,
                                       TimeSpan? readWithin = null)
        {
            double s = timeoutSeconds ?? BridgeText.EstimateSeconds(commands);
            return _bridge.Send(commands, TimeSpan.FromSeconds(s), ct, readWithin);
        }

        /// One command; its output is the tool's text and an error line
        /// makes it an error.
        private Func<CancellationToken, Task<ToolResult>> One(string command, double? timeoutSeconds = null)
        {
            return async ct => FromReply(await Send(new[] { command }, ct, timeoutSeconds), false);
        }

        /// A command whose trailing arguments are optional (nulls skipped).
        private Func<CancellationToken, Task<ToolResult>> Opt(string cmd, params string[] args)
        {
            return One(BridgeText.Command(cmd, args));
        }

        private static ToolResult FromReply(BridgeReply r, bool headers)
        {
            return ToolResult.Text(r.Text(headers), !r.Complete || r.Transcript.Errors > 0);
        }

        /// The single-command reply's lines, or null with the error set.
        private async Task<(List<string> lines, string error)> Lines(string command, CancellationToken ct, double? timeout = null)
        {
            BridgeReply r = await Send(new[] { command }, ct, timeout);
            if (!r.Complete || r.Transcript.Blocks.Count == 0) return (null, r.Problem ?? "no reply");
            BridgeText.Block b = r.Transcript.Blocks[0];
            return b.Error != null ? (null, b.Error) : (b.Lines, null);
        }

        /// Values of several `get`s in one batch; a failed one is null.
        private async Task<(List<string> values, string problem)> GetValues(IList<string> commands, CancellationToken ct)
        {
            BridgeReply r = await Send(commands, ct);
            if (!r.Complete) return (null, r.Problem);
            List<string> values = new List<string>();
            foreach (BridgeText.Block b in r.Transcript.Blocks)
                values.Add(b.Error == null && b.Lines.Count > 0 ? BridgeText.Value(b.Lines[b.Lines.Count - 1]) : null);
            return (values, null);
        }

        // ------------------------------------------------------------------
        // Tools

        private async Task<ToolResult> Status(Args a, CancellationToken ct)
        {
            StringBuilder sb = new StringBuilder();
            using (Process p = GameProcess.Find())
            {
                if (p == null) sb.Append("game: not running\n");
                else
                {
                    string up;
                    try { up = ((int)(DateTime.Now - p.StartTime).TotalMinutes) + " min"; }
                    catch (Exception) { up = "?"; }
                    sb.Append("game: running (pid ").Append(p.Id).Append(", up ").Append(up).Append(")\n");
                }
            }
            sb.Append("game folder: ").Append(_paths.Root).Append('\n');
            sb.Append("bridge folder: ").Append(_paths.Bridge).Append('\n');
            sb.Append("TestBridge setting: ").Append(_paths.TestBridgeSetting() ?? "(not in the .cfg)").Append('\n');
            sb.Append("plugin (newest bridge banner): v").Append(_bridge.PluginVersionFromBanner() ?? "?").Append('\n');
            sb.Append("plugins folder: ").Append(string.Join(", ", PluginFiles())).Append('\n');

            if (!GameProcess.Running) return ToolResult.Text(sb.ToString().TrimEnd());

            BridgeReply r = await Send(new[]
            {
                "status", "player",
                "get static:TheForest.Utils.LocalPlayer ScriptSetup.pmControl.ActiveStateName",
            }, ct, 20, TimeSpan.FromSeconds(8));
            sb.Append('\n').Append(r.Text(false));
            return ToolResult.Text(sb.ToString().TrimEnd(), !r.Delivered);
        }

        private List<string> PluginFiles()
        {
            try
            {
                return Directory.GetFiles(_paths.Plugins).Select(Path.GetFileName)
                    .Where(f => f.StartsWith("ForestOverlay", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            catch (IOException) { return new List<string> { "(not found)" }; }
        }

        private async Task<ToolResult> Run(Args a, CancellationToken ct)
        {
            List<string> commands = new List<string>();
            string file = a.Str("file");
            if (!string.IsNullOrEmpty(file))
            {
                if (!File.Exists(file)) return ToolResult.Fail("no file " + file);
                foreach (string line in File.ReadAllLines(file))
                    if (BridgeText.Line(line).Length > 0) commands.Add(line);
            }
            commands.AddRange(a.List("commands"));
            if (commands.Count == 0) return ToolResult.Fail("give `commands` or `file`");

            BridgeReply r = await Send(commands, ct, a.Num("timeout_s"));
            string text = r.Text(true);
            int n = r.Transcript.Blocks.Count, errors = r.Transcript.Errors;
            if (r.Delivered)
                text += "\n-- " + n + " command(s), " + errors + " error(s)" + (r.Complete ? "" : ", incomplete");
            return ToolResult.Text(text, !r.Delivered);
        }

        private Task<ToolResult> Find(Args a, CancellationToken ct)
        {
            string type = a.Str("type"), name = a.Str("name");
            if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(name)) throw new ArgumentException("give `name` or `type`");
            // `all` and `max=` are taken out wherever they stand; the radius
            // is the second positional argument.
            string cmd = BridgeText.Command(string.IsNullOrEmpty(type) ? "find" : "type",
                string.IsNullOrEmpty(type) ? name : type,
                a.Has("radius") ? F(a.Num("radius")) : null,
                a.Bool("include_inactive") ? "all" : null,
                MaxArg(a));
            return One(cmd)(ct);
        }

        private async Task<ToolResult> Get(Args a, CancellationToken ct)
        {
            string target = a.Need("target");
            List<string> paths = a.List("paths");
            if (paths.Count == 0 && a.Has("path")) paths.Add(a.Str("path"));
            if (paths.Count == 0) throw new ArgumentException("'paths' is required");
            List<string> cmds = paths.Select(p => BridgeText.Command("get", target, p)).ToList();
            return FromReply(await Send(cmds, ct), false);
        }

        private Task<ToolResult> Mark(Args a, CancellationToken ct)
        {
            if (a.Bool("clear")) return One("mark clear")(ct);
            if (a.Has("x")) return One(BridgeText.Command("mark", F(a.Num("x")), F(a.Num("y")), F(a.Num("z"))))(ct);
            return One(BridgeText.Command("mark", a.Need("target")))(ct);
        }

        private Task<ToolResult> Anim(Args a, CancellationToken ct)
        {
            string mode = a.Str("mode", "snapshot");
            if (mode == "watch") return One(BridgeText.Command("anim", "watch", F(a.Num("seconds", 5))))(ct);
            if (mode == "reset") return One("anim reset")(ct);
            return One("anim")(ct);
        }

        private async Task<ToolResult> Screenshot(Args a, CancellationToken ct)
        {
            string name = a.Str("name");
            if (string.IsNullOrEmpty(name)) name = "mcp-" + DateTime.Now.ToString("HHmmss-fff", CultureInfo.InvariantCulture);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Replace(' ', '_');

            List<string> cmds = new List<string>();
            double delay = a.Num("delay_s", 0);
            if (delay > 0) cmds.Add("wait " + F(delay));
            cmds.Add("shot " + name);

            string path = Path.Combine(_paths.Bridge, name + ".png");
            TryDelete(path);
            BridgeReply r = await Send(cmds, ct);
            if (!r.Complete || r.Transcript.Errors > 0) return FromReply(r, false);

            byte[] png = await Images.ReadWhenComplete(path, TimeSpan.FromSeconds(10), ct);

            Rectangle? region = null;
            List<string> reg = a.List("region");
            if (reg.Count > 0)
            {
                int[] v = reg.Select(s => (int)double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (v.Length != 4) throw new ArgumentException("region is [x, y, width, height]");
                region = new Rectangle(v[0], v[1], v[2], v[3]);
            }
            int maxWidth = (int)a.Num("max_width", region.HasValue ? 0 : 1280);
            Images.Prepared img = Images.Prepare(png, maxWidth, region, a.Bool("png"), 85);
            PruneShots();

            return ToolResult.Text(path + " (" + img.SourceWidth + "x" + img.SourceHeight + ")" +
                                   (region.HasValue ? ", region " + string.Join(",", reg) : "") +
                                   ", sent as " + img.Width + "x" + img.Height)
                .AddImage(img.Data, img.Mime);
        }

        /// Keeps the newest 40 of this server's own screenshots.
        private void PruneShots()
        {
            try
            {
                FileInfo[] shots = new DirectoryInfo(_paths.Bridge).GetFiles("mcp-*.png")
                    .OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
                for (int i = 40; i < shots.Length; i++) shots[i].Delete();
            }
            catch (IOException) { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }

        private Task<ToolResult> Notice(Args a, CancellationToken ct)
        {
            string seconds = F(a.Num("seconds", 8));
            return One(BridgeText.Command("call", Plugin, "OverlayPlugin._notice.Show", a.Need("text"), seconds))(ct);
        }

        private Task<ToolResult> Wait(Args a, CancellationToken ct)
        {
            if (a.Bool("until_idle"))
                return One(BridgeText.Command("waitidle", F(a.Num("seconds", 120))), a.Num("seconds", 120) + 30)(ct);
            double s = a.Num("seconds", 1);
            return One("wait " + F(s), s + 30)(ct);
        }

        // ------------------------------------------------------------------
        // Tabs: the module list read from the running plugin

        private sealed class ModuleInfo
        {
            public int Index;
            public string Id, Title;
            public bool HasTab, HasPanel;
        }

        private List<ModuleInfo> _modules;
        private int _modulesPid;

        private async Task<(List<ModuleInfo> list, string problem)> Modules(CancellationToken ct)
        {
            int pid;
            using (Process p = GameProcess.Find()) pid = p == null ? 0 : p.Id;
            if (_modules != null && pid == _modulesPid) return (_modules, null);

            var (count, problem) = await GetValues(new[] { BridgeText.Command("get", Plugin, Host + ".Count") }, ct);
            if (count == null) return (null, problem);
            int n;
            if (count[0] == null || !int.TryParse(count[0], out n)) return (null, "could not read the module count");

            List<string> cmds = new List<string>();
            for (int i = 0; i < n; i++)
                foreach (string member in new[] { "Id", "TabTitle", "HasTab", "HasPanel" })
                    cmds.Add(BridgeText.Command("get", Plugin, Host + "._modules[" + i + "]." + member));
            var (values, problem2) = await GetValues(cmds, ct);
            if (values == null) return (null, problem2);

            List<ModuleInfo> list = new List<ModuleInfo>();
            for (int i = 0; i < n && i * 4 + 3 < values.Count; i++)
                list.Add(new ModuleInfo
                {
                    Index = i,
                    Id = values[i * 4] ?? "",
                    Title = values[i * 4 + 1] ?? "",
                    HasTab = values[i * 4 + 2] == "True",
                    HasPanel = values[i * 4 + 3] == "True",
                });
            _modules = list;
            _modulesPid = pid;
            return (list, null);
        }

        private static string ModulePath(ModuleInfo m, string member)
        {
            return Host + "._modules[" + m.Index + "]." + member;
        }

        private async Task<ToolResult> OpenTab(Args a, CancellationToken ct)
        {
            string want = a.Need("tab").Trim();
            var (mods, problem) = await Modules(ct);
            if (mods == null) return ToolResult.Fail(problem);

            ModuleInfo main = mods.FirstOrDefault(m => m.Id == "mainwindow");
            string done;

            if (want.Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                // The main window and any module's own window (the explorer).
                List<string> closed = new List<string>();
                foreach (ModuleInfo m in mods.Where(m => m.HasPanel && (m == main || !m.HasTab)))
                    if (await ClosePanel(m, ct)) closed.Add(m.Title);
                done = closed.Count == 0 ? "nothing was open" : "closed " + string.Join(", ", closed);
            }
            else
            {
                ModuleInfo pick = Match(mods, want);
                if (pick == null)
                    return ToolResult.Fail("no tab '" + want + "'. Tabs: " +
                                           string.Join(", ", mods.Where(m => m.HasTab).Select(m => m.Title + " (" + m.Id + ")")) +
                                           "; windows: " +
                                           string.Join(", ", mods.Where(m => !m.HasTab && m.HasPanel && m.Id != "mainwindow").Select(m => m.Title + " (" + m.Id + ")")) +
                                           "; or close");
                if (pick.HasTab)
                {
                    var (_, err) = await Lines(BridgeText.Command("call", Plugin, ModulePath(pick, "OpenMyTab")), ct);
                    if (err != null) return ToolResult.Fail(err);
                    done = "opened the " + pick.Title + " tab";
                }
                else
                {
                    var (open, err) = await GetValues(new[] { BridgeText.Command("get", Plugin, ModulePath(pick, "PanelOpen")) }, ct);
                    if (open == null) return ToolResult.Fail(err);
                    if (open[0] == "True") done = pick.Title + " already open";
                    else
                    {
                        var (_, err2) = await Lines(BridgeText.Command("call", Plugin, ModulePath(pick, "TogglePanel")), ct);
                        if (err2 != null) return ToolResult.Fail(err2);
                        done = "opened " + pick.Title;
                    }
                }
            }

            if (!a.Bool("screenshot")) return ToolResult.Text(done);
            ToolResult shot = await Screenshot(new Args(new JsonObject { ["delay_s"] = 0.3 }), ct);
            return shot.IsError ? ToolResult.Fail(done + "; the screenshot failed") : shot.AddText(done);
        }

        /// TogglePanel toggles: read PanelOpen first. True when it closed one.
        private async Task<bool> ClosePanel(ModuleInfo m, CancellationToken ct)
        {
            var (open, err) = await GetValues(new[] { BridgeText.Command("get", Plugin, ModulePath(m, "PanelOpen")) }, ct);
            if (open == null) throw new ArgumentException(err);
            if (open[0] != "True") return false;
            var (_, err2) = await Lines(BridgeText.Command("call", Plugin, ModulePath(m, "TogglePanel")), ct);
            if (err2 != null) throw new ArgumentException(err2);
            return true;
        }

        private static ModuleInfo Match(List<ModuleInfo> mods, string want)
        {
            Func<ModuleInfo, bool> usable = m => (m.HasTab || m.HasPanel) && m.Id != "mainwindow";
            string w = want.ToLowerInvariant();
            ModuleInfo exact = mods.FirstOrDefault(m => usable(m) &&
                (m.Title.ToLowerInvariant() == w || m.Id.ToLowerInvariant() == w));
            if (exact != null) return exact;
            if (w == "explorer" || w == "type explorer")
                return mods.FirstOrDefault(m => usable(m) && !m.HasTab && m.Title.ToLowerInvariant().Contains("explorer"));
            List<ModuleInfo> partial = mods.Where(m => usable(m) &&
                (m.Title.ToLowerInvariant().Contains(w) || m.Id.ToLowerInvariant().Contains(w))).ToList();
            return partial.Count == 1 ? partial[0] : partial.FirstOrDefault(m => m.HasTab && m.Title.ToLowerInvariant().StartsWith(w));
        }

        // ------------------------------------------------------------------
        // Logs and IL

        private Task<ToolResult> Log(Args a, CancellationToken ct)
        {
            int session = (int)a.Num("session", 0);
            string file;
            if (session <= 0) file = _paths.LogOutput;
            else
            {
                // The plugin mirrors the running log into logs/, so the
                // newest kept copy is the current (or last) session.
                string[] kept = Directory.Exists(_paths.KeptLogs)
                    ? Directory.GetFiles(_paths.KeptLogs, "LogOutput-*.log").OrderByDescending(f => f, StringComparer.Ordinal).ToArray()
                    : new string[0];
                if (session >= kept.Length)
                    return Task.FromResult(ToolResult.Fail("no session " + session + " - " + kept.Length +
                                                           " kept log(s) in " + _paths.KeptLogs));
                file = kept[session];
            }
            if (!File.Exists(file)) return Task.FromResult(ToolResult.Fail("no log at " + file));

            string[] lines = ForestPaths.ReadShared(file).Replace("\r", "").Split('\n');
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0) Array.Resize(ref lines, lines.Length - 1);

            string pattern = a.Str("pattern");
            string head, body;
            if (string.IsNullOrEmpty(pattern))
            {
                int tail = (int)a.Num("tail", 60);
                body = LogSearch.Tail(lines, tail);
                head = file + ": " + lines.Length + " lines, the last " + Math.Min(tail, lines.Length);
            }
            else
            {
                int max = (int)a.Num("max", 80);
                int matches;
                body = LogSearch.Grep(lines, pattern, (int)a.Num("context", 0), max, out matches);
                head = file + ": " + lines.Length + " lines, " + matches + " match(es)" +
                       (matches > max ? ", the newest " + max : "");
            }
            return Task.FromResult(ToolResult.Text(head + "\n" + Cap(body, 60000)));
        }

        private static string Cap(string text, int chars)
        {
            return text.Length <= chars ? text : "...(" + (text.Length - chars) + " chars cut)\n" + text.Substring(text.Length - chars);
        }

        private static string RepoRoot()
        {
            DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "ForestOverlay.csproj"))) d = d.Parent;
            return d == null ? null : d.FullName;
        }

        private async Task<ToolResult> IlScan(Args a, CancellationToken ct)
        {
            string repo = RepoRoot();
            if (repo == null) return ToolResult.Fail("repo not found above " + AppContext.BaseDirectory);
            string dll = Path.Combine(repo, "tools", "ILScan", "bin", "Release", "net8.0", "ilscan.dll");
            if (!File.Exists(dll))
                return ToolResult.Fail("ilscan is not built: dotnet build tools/ILScan -c Release (needs FOREST_ROOT for Cecil)");

            ProcessStartInfo psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add(a.Need("mode"));
            psi.ArgumentList.Add(a.Need("needle"));
            psi.ArgumentList.Add("--max");
            psi.ArgumentList.Add(((int)a.Num("max", 60)).ToString(CultureInfo.InvariantCulture));
            string managed = ForestPaths.Env("FOREST_MANAGED_PATH") ?? Path.Combine(_paths.Root, "TheForest_Data", "Managed");
            psi.Environment["FOREST_MANAGED_PATH"] = managed;

            using Process p = Process.Start(psi);
            Task<string> stdout = p.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = p.StandardError.ReadToEndAsync(ct);
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(180));
                try { await p.WaitForExitAsync(limit.Token); }
                catch (OperationCanceledException)
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    throw;
                }
            }
            string text = (await stdout) + (await stderr);
            return ToolResult.Text(Cap(text.TrimEnd(), 60000), p.ExitCode != 0);
        }

        // ------------------------------------------------------------------
        // The game process

        private async Task<ToolResult> Game(Args a, CancellationToken ct)
        {
            string action = a.Need("action");
            bool exe = a.Str("via", "steam") == "exe";
            StringBuilder sb = new StringBuilder();
            switch (action)
            {
                case "close":
                    sb.Append(await Close(ct));
                    return ToolResult.Text(sb.ToString());
                case "launch":
                    if (GameProcess.Running) return ToolResult.Text("already running - action restart to restart it");
                    return await Launch(exe, sb, ct);
                case "restart":
                    sb.Append(await Close(ct)).Append('\n');
                    return await Launch(exe, sb, ct);
            }
            return ToolResult.Fail("action is close, launch or restart");
        }

        private async Task<string> Close(CancellationToken ct)
        {
            using Process p = GameProcess.Find();
            if (p == null) return "the game was not running";
            Stopwatch sw = Stopwatch.StartNew();
            bool asked = false;
            try { asked = p.CloseMainWindow(); } catch (InvalidOperationException) { }
            if (asked)
            {
                using CancellationTokenSource grace = CancellationTokenSource.CreateLinkedTokenSource(ct);
                grace.CancelAfter(TimeSpan.FromSeconds(12));
                try { await p.WaitForExitAsync(grace.Token); }
                catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
            }
            string how = "closed";
            if (!p.HasExited)
            {
                p.Kill();
                how = asked ? "did not close in 12 s - killed" : "killed";
                using CancellationTokenSource grace = CancellationTokenSource.CreateLinkedTokenSource(ct);
                grace.CancelAfter(TimeSpan.FromSeconds(20));
                await p.WaitForExitAsync(grace.Token);
            }
            // The next launch must not run a batch nobody is waiting for.
            TryDelete(Path.Combine(_paths.Bridge, "in.txt"));
            await Task.Delay(1500, ct);   // Steam notices the exit before a relaunch
            return "game " + how + " (" + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s)";
        }

        private async Task<ToolResult> Launch(bool exe, StringBuilder sb, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (exe)
            {
                string path = Path.Combine(_paths.Root, "TheForest.exe");
                if (!File.Exists(path)) return ToolResult.Fail(sb + "no " + path);
                Process.Start(new ProcessStartInfo(path) { WorkingDirectory = _paths.Root, UseShellExecute = true })?.Dispose();
            }
            else Process.Start(new ProcessStartInfo("steam://rungameid/" + ForestPaths.SteamAppId) { UseShellExecute = true })?.Dispose();

            while (!GameProcess.Running)
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(90))
                    return ToolResult.Fail(sb + "TheForest.exe did not start within 90 s" +
                                           (exe ? "" : " - Steam may be showing a dialog (launch options, update, sign-in); try via exe"));
                await Task.Delay(500, ct);
            }
            sb.Append("process up after ").Append(sw.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)).Append(" s\n");

            // Unity's launcher dialog holds the start until Play is pressed;
            // press it while waiting for the bridge (it starts with the
            // plugin, title screen included).
            using CancellationTokenSource stopClicker = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task<string> clicker = PressPlay(stopClicker.Token);
            BridgeReply r = await _bridge.Send(new[] { "echo ready" }, TimeSpan.FromSeconds(300), ct, TimeSpan.FromSeconds(280));
            stopClicker.Cancel();
            string clicked = await clicker;
            sb.Append(clicked != null ? "pressed " + clicked + " in the launcher\n" : "no launcher dialog seen\n");
            if (!r.Complete) return ToolResult.Fail(sb + "the bridge did not answer: " + r.Problem);
            sb.Append("bridge answering after ").Append(sw.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture))
              .Append(" s, ForestOverlay v").Append(_bridge.PluginVersionFromBanner() ?? "?");
            _modules = null;
            return ToolResult.Text(sb.ToString());
        }

        /// Polls for the launcher's Play button (up to 2 min) and presses it
        /// once; what it pressed, or null.
        private static async Task<string> PressPlay(CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMinutes(2) && !ct.IsCancellationRequested)
            {
                using (Process p = GameProcess.Find())
                {
                    if (p != null)
                    {
                        string clicked = Launcher.ClickPlay(p.Id);
                        if (clicked != null) return clicked;
                    }
                }
                try { await Task.Delay(400, ct); }
                catch (OperationCanceledException) { break; }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Updates, through the plugin's own checker (Core/UpdateChecker)

        private async Task<ToolResult> UpdateGame(Args a, CancellationToken ct)
        {
            var (mods, problem) = await Modules(ct);
            if (mods == null) return ToolResult.Fail(problem);
            ModuleInfo up = mods.FirstOrDefault(m => m.Id == "update");
            if (up == null) return ToolResult.Fail("no update module in the plugin");
            string checker = ModulePath(up, "_checker");
            string[] read =
            {
                BridgeText.Command("get", Plugin, checker + ".State"),
                BridgeText.Command("get", Plugin, checker + ".Message"),
                BridgeText.Command("get", Plugin, checker + ".LatestVersion"),
            };

            StringBuilder sb = new StringBuilder();
            string before = _bridge.PluginVersionFromBanner();
            sb.Append("running v").Append(before ?? "?").Append('\n');

            var (v, err) = await GetValues(read, ct);
            if (v == null) return ToolResult.Fail(err);

            if (v[0] != "UpdateAvailable" && v[0] != "Staged" && v[0] != "DownloadRetry")
            {
                var (_, e) = await Lines(BridgeText.Command("call", Plugin, checker + ".Check"), ct);
                if (e != null) return ToolResult.Fail(sb + "Check: " + e);
                v = await PollState(read, s => s != "Checking", TimeSpan.FromSeconds(40), ct);
                if (v == null) return ToolResult.Fail(sb + "the check did not finish");
            }
            sb.Append("check: ").Append(v[1]).Append('\n');

            if (v[0] == "UpToDate" || v[0] == "Publishing" || v[0] == "Failed")
                return ToolResult.Text(sb.ToString().TrimEnd(), v[0] == "Failed");

            if (v[0] != "Staged")
            {
                var (pathVal, e1) = await GetValues(new[] { BridgeText.Command("get", Plugin, ModulePath(up, "Ctx.PluginPath")) }, ct);
                if (pathVal == null || pathVal[0] == null) return ToolResult.Fail(sb + "plugin path: " + (e1 ?? "unreadable"));
                var (_, e2) = await Lines(BridgeText.Command("call", Plugin, checker + ".Download", pathVal[0]), ct);
                if (e2 != null) return ToolResult.Fail(sb + "Download: " + e2);
                // A fresh release can 404 for a while; the Updates module
                // retries every 30 s by itself (up to 10 times).
                v = await PollState(read, s => s == "Staged" || s == "Failed", TimeSpan.FromMinutes(6), ct);
                if (v == null) return ToolResult.Fail(sb + "the download did not finish in 6 min");
                sb.Append("download: ").Append(v[1]).Append('\n');
                if (v[0] != "Staged") return ToolResult.Fail(sb.ToString().TrimEnd());
            }
            else sb.Append("already downloaded: ").Append(v[1]).Append('\n');

            if (!a.Bool("restart", true))
                return ToolResult.Text(sb.Append("staged - restart the game to install").ToString());

            sb.Append(await Close(ct)).Append('\n');
            ToolResult launched = await Launch(a.Str("via", "steam") == "exe", sb, ct);
            if (launched.IsError) return launched;

            string after = _bridge.PluginVersionFromBanner();
            string want = v[2];
            bool ok = want != null && after == want;
            string files = string.Join(", ", PluginFiles());
            return ToolResult.Text(sb.ToString().TrimEnd() + "\n" +
                                   (ok ? "installed: v" + after : "expected v" + want + ", running v" + after) +
                                   "\nplugins folder: " + files, !ok);
        }

        private async Task<List<string>> PollState(string[] read, Func<string, bool> done, TimeSpan within, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < within)
            {
                await Task.Delay(1000, ct);
                var (v, _) = await GetValues(read, ct);
                if (v != null && v[0] != null && done(v[0])) return v;
            }
            return null;
        }
    }
}
