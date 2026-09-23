using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Savestates - PHASE 0, EXPERIMENTAL. PRACTICE ONLY.
    //
    // A probe, not the finished feature: capture the game's own level
    // serialization into a file (no save slot, no Steam Cloud), then
    // restore it two ways and log what each did, so an in-game test can
    // decide which restore to build on. See Game/SavestateBridge.cs and
    // game-notes "Saving and loading".
    //
    //   Capture here          - the game's save routine, redirected to
    //                           BepInEx/config/ForestOverlay/savestates.
    //   Restore in place      - LoadNow into the running scene, no load;
    //                           deletes what the save does not know and
    //                           puts back world pickups taken since
    //                           (Game/PickupKeeper).
    //   Restore with load     - LoadSavedLevel: one scene load.
    //   Current slot's save   - the same two restores fed from the slot the
    //                           game is running on (the author's idea: a
    //                           faster quick-load, and a load-free one).
    //   Check pickups         - would the in-place restore bring an item
    //                           (default 210, the keycard) back?
    //
    // Every action logs one "Savestate ..." line; that line is the test.
    //
    // SEGMENT START STATES (phase 1): a segment may keep a savestate at
    // savestates/segments/<safe segment id>.fosave - named from the id, so
    // a shared segment file and its start state travel together. The
    // Practice module restores it on every restart (return to spot), in
    // place or with a load per Segment.StartRestoreWithLoad, then teleports
    // to the spawn as before. No file: the restart keeps the game state,
    // which some routes need (runner request).
    // ------------------------------------------------------------------
    public sealed class SavestateModule : OverlayModule
    {
        private const float LoadTimingTimeout = 180f;

        public override string Id { get { return "savestates"; } }
        public override string DisplayName { get { return "Savestates"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Savestates"; } }
        public override int TabOrder { get { return 20; } }
        public override bool IsPracticeOnly { get { return true; } }

        private SavestateBridge _bridge;
        private PickupKeeper _keeper;
        private string _dir;

        private bool _busy;
        private ConfigEntry<bool> _allowCrossMode;
        private float _contentHeight = 520f;
        private string _lastCaptureHash = "";
        private float _busySince;
        private string _name = "savestate";
        private string _itemIdText = "210";

        // File list, rebuilt only on refresh.
        private readonly List<string> _files = new List<string>();
        private readonly List<GUIContent> _fileLabels = new List<GUIContent>();
        private int _selected = -1;
        private float _deleteArmedUntil;

        private readonly List<string> _diag = new List<string>();
        private readonly List<GUIContent> _diagLabels = new List<GUIContent>();

        private GUIContent _status = new GUIContent("");
        private GUIContent _slotLabel = new GUIContent("");
        private GUIContent _bindLabel = new GUIContent("");
        private GUIContent _dirLabel = new GUIContent("");
        private float _nextSlotRefresh;

        // Timing a scene-load restore across the load.
        private bool _timingLoad;
        private Action<string> _loadAfter;
        private bool _sawLoading;
        private float _loadStarted;
        private string _loadWhat;
        private int _loadsThisSession;

        private Vector2 _scroll;

        private static readonly GUIContent Warning = new GUIContent(
            "EXPERIMENTAL (phase 0). Stores the game's own save data in a file - no save slot is used. " +
            "Capture and restores mark the session as practice. Every action logs one 'Savestate' line.");

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _bridge = new SavestateBridge(ctx.Log);
            _keeper = new PickupKeeper(ctx.Log);
            _keeper.Install(OverlayPlugin.PluginGuid);
            _dir = Path.Combine(ctx.ConfigDirectory, "savestates");
            _dirLabel = new GUIContent("Savestates (" + _dir + ")");
            RefreshFiles();

            _allowCrossMode = ctx.Config.Bind("Savestates", "AllowCrossModeRestore", false,
                "Restore a savestate captured in a Creative game into a survival game, or the other way round. " +
                "For testing: the game mode is not in the save, so the world comes back in this game's mode.");
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.savestates", KeyCode.None, "Open Savestates tab", OpenMyTab);
            map.Add("savestate.capture", KeyCode.None, "Savestate: capture here", Capture);
            map.Add("savestate.restoreInPlace", KeyCode.None, "Savestate: restore selected in place", RestoreSelectedInPlace);
            map.Add("savestate.restoreLoad", KeyCode.None, "Savestate: restore selected with load", RestoreSelectedWithLoad);
        }

        public override void Shutdown()
        {
            PickupKeeper.Armed = false;
            if (_keeper != null) _keeper.Uninstall();
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            if (Time.unscaledTime >= _nextSlotRefresh)
            {
                _nextSlotRefresh = Time.unscaledTime + 1f;
                _slotLabel.text = "Current save slot: " + _bridge.CurrentSlot;
                _bindLabel.text = "Bound: " + _bridge.Status + " | pickups: " + _keeper.Status +
                                  (PickupKeeper.Armed ? ", " + _keeper.KeptCount + " kept for a restore" : ", armed by the first capture/restore");
            }

            if (_timingLoad) TimeLoad();

            // A coroutine that dies on an exception never calls back; do not
            // leave every button disabled for the rest of the session.
            if (_busy && Time.realtimeSinceStartup - _busySince > LoadTimingTimeout)
            {
                _busy = false;
                SetStatus("the last action never finished - see LogOutput.log");
                Ctx.Log.LogWarning("Savestate: an action never finished; buttons re-enabled.");
            }
        }

        // A scene-load restore is timed from the click to the game's own
        // FinishGameLoad flag (cleared by LoadSave.Awake, set when the
        // activation sequence ends). The heap figure feeds the load-leak
        // investigation.
        private void TimeLoad()
        {
            float elapsed = Time.realtimeSinceStartup - _loadStarted;
            bool finished = _bridge.GameLoadFinished;

            if (!finished) _sawLoading = true;
            else if (_sawLoading)
            {
                _timingLoad = false;
                _loadsThisSession++;
                // A full collection first, so the heap figure is what survived
                // the load, not garbage waiting for the GC - one hitch, at the
                // end of a load.
                string line = _loadWhat + ": in game after " + elapsed.ToString("0.0") + " s. Loads this session: " +
                              _loadsThisSession + ", Mono heap after GC " + (GC.GetTotalMemory(true) / (1024 * 1024)) + " MB.";
                Ctx.Log.LogInfo("Savestate " + line);
                SetStatus(line);
                Continue(null);
                return;
            }

            if (elapsed > LoadTimingTimeout)
            {
                _timingLoad = false;
                string why = "could not time the load (FinishGameLoad " +
                             (_sawLoading ? "never came back" : "never changed") + ")";
                Ctx.Log.LogWarning("Savestate " + _loadWhat + ": " + why + ".");
                Continue(why);
            }
        }

        private void Continue(string error)
        {
            Action<string> after = _loadAfter;
            _loadAfter = null;
            if (after == null) return;
            try { after(error); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: continuation failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // Actions

        private void Capture()
        {
            CaptureTo(_name, null, null);
        }

        /// `path` null: a new file in the savestates folder, never
        /// overwriting. `after` gets null on success or the reason.
        private void CaptureTo(string name, string path, Action<string> after)
        {
            if (_busy) { if (after != null) after("a savestate action is still running"); return; }
            if (!_bridge.Resolve())
            {
                SetStatus("capture unavailable: " + _bridge.Status);
                if (after != null) after("capture unavailable: " + _bridge.Status);
                return;
            }

            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            Ctx.Practice.Mark("savestate capture");
            PickupKeeper.Armed = true;
            SetStatus("capturing...");

            Vector3 pos = Ctx.Player.Found ? Ctx.Player.Transform.position : Vector3.zero;
            bool inCave = Ctx.Bridge.IsInCaves();

            // Before the capture unloads streaming, while every pickup here is
            // still loaded.
            List<string> pickups = new List<string>();
            try { _keeper.Snapshot(pickups); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: pickup snapshot failed: " + ex.Message); }

            Ctx.Runner.StartCoroutine(_bridge.Capture(delegate(SavestateBridge.Result r)
            {
                string error = OnCaptured(r, name, path, pos, inCave, pickups);
                if (after != null) after(error);
            }));
        }

        private string OnCaptured(SavestateBridge.Result r, string name, string path, Vector3 pos, bool inCave, List<string> pickups)
        {
            _busy = false;
            if (!r.Ok)
            {
                Ctx.Log.LogWarning("Savestate capture failed: " + r.Message);
                SetStatus("capture failed: " + r.Message);
                return r.Message;
            }

            try
            {
                SavestateFile f = new SavestateFile();
                f.Name = name;
                f.Level = r.Level;
                f.Difficulty = r.Difficulty;
                f.Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                f.PluginVersion = OverlayPlugin.PluginVersion;
                f.X = pos.x; f.Y = pos.y; f.Z = pos.z;
                f.InCave = inCave;
                f.StreamingUnloaded = r.StreamingUnloaded;
                f.Pickups = pickups;
                f.Data = r.Data;

                if (path == null)
                {
                    Directory.CreateDirectory(_dir);
                    path = UniquePath(SavestateFile.SafeFileName(name));
                }
                else Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, f.Write(), new UTF8Encoding(false));
                _lastCaptureHash = Segment.HashText(f.Data);

                string line = "captured '" + name + "' -> " + Path.GetFileName(path) + ": " + r.Message +
                              ", " + pickups.Count + " world pickups listed";
                Ctx.Log.LogInfo("Savestate " + line);
                SetStatus(line);

                RefreshFiles();
                int listed = _files.IndexOf(path);
                if (listed >= 0) _selected = listed;
                return null;
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Savestate capture: could not write the file: " + ex.Message);
                SetStatus("capture failed writing the file: " + ex.Message);
                return "could not write the file: " + ex.Message;
            }
        }

        private void RestoreSelectedInPlace()
        {
            SavestateFile f = LoadSelected();
            if (f == null) return;

            string mode = ModeMismatch(f);
            if (mode != null) { SetStatus("restore '" + f.Name + "' refused: " + mode); return; }

            HashSet<string> present = f.Pickups != null ? new HashSet<string>(f.Pickups) : null;
            RestoreInPlace(f.Data, f.StreamingUnloaded, present, "'" + f.Name + "'", f.InCave ? 1 : 0, null);
        }

        private void RestoreSelectedWithLoad()
        {
            SavestateFile f = LoadSelected();
            if (f == null || _busy) return;

            string mode = ModeMismatch(f);
            if (mode != null) { SetStatus("restore '" + f.Name + "' refused: " + mode); return; }

            Ctx.Practice.Mark("savestate restore (load)");
            PickupKeeper.Armed = true;
            string err = _bridge.RestoreWithLoad(f.Data, f.Difficulty);
            StartLoad("restore '" + f.Name + "' with load", err, null);
        }

        private void SlotInPlace()
        {
            string error;
            string data = _bridge.ReadSlotData(out error);
            if (data == null)
            {
                SetStatus("slot reload failed: " + error);
                Ctx.Log.LogWarning("Savestate slot reload in place: " + error);
                return;
            }
            // A slot save was made the game's way: streaming unloaded only in
            // MemorySafeSaveMode. No pickup list - a menu load would bring
            // them all back, so every kept pickup is put back.
            RestoreInPlace(data, _bridge.MemorySafeSaveMode, null, "slot " + _bridge.CurrentSlot, -1, null);
        }

        private void SlotWithoutMenu()
        {
            if (_busy) return;
            Ctx.Practice.Mark("savestate slot load");
            PickupKeeper.Armed = true;
            string err = _bridge.LoadSlotWithoutMenu();
            StartLoad("load slot " + _bridge.CurrentSlot + " without the menu", err, null);
        }

        /// `savedInCave`: the file's cave flag (1 / 0), or -1 when unknown
        /// (a slot save) and the player's position has to decide.
        private void RestoreInPlace(string data, bool unloadStreaming, HashSet<string> presentPickups, string what,
                                    int savedInCave, Action<string> after)
        {
            if (_busy) { if (after != null) after("a savestate action is still running"); return; }
            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            Ctx.Practice.Mark("savestate restore (in place)");
            PickupKeeper.Armed = true;
            SetStatus("restoring " + what + " in place...");
            Ctx.Log.LogInfo("Savestate restore " + what + " in place: starting.");

            Transform keep = Ctx.Player.Found ? Ctx.Player.Transform.root : null;
            Ctx.Runner.StartCoroutine(_bridge.RestoreInPlace(data, unloadStreaming, keep, delegate(SavestateBridge.Result r)
            {
                _busy = false;

                int pickups = 0;
                if (r.Ok)
                {
                    try { pickups = _keeper.Restore(presentPickups); }
                    catch (Exception ex) { Ctx.Log.LogWarning("Savestate: pickup restore failed: " + ex.Message); }
                }

                // The serializer restores the IsInCaves flag but not what
                // the cave doors did (terrain collision, lighting,
                // streaming), so a flag test sees nothing to change. The
                // file knows where it was captured: send that state outright.
                string cave = "";
                if (r.Ok && Ctx.Player.Found)
                {
                    try
                    {
                        cave = savedInCave >= 0 ? Ctx.Bridge.ForceCaveState(savedInCave == 1)
                                                : Ctx.Bridge.SyncCaveState(Ctx.Player.Transform.position);
                    }
                    catch (Exception) { }
                }

                string line = "restore " + what + " in place: " + r.Message +
                              ", pickups put back " + pickups +
                              (presentPickups == null ? " (all kept)" : "") +
                              (string.IsNullOrEmpty(cave) ? "" : " | cave: " + cave);
                if (r.Ok) Ctx.Log.LogInfo("Savestate " + line);
                else Ctx.Log.LogWarning("Savestate " + line);
                SetStatus(line);

                if (after != null)
                {
                    try { after(r.Ok ? null : r.Message); }
                    catch (Exception ex) { Ctx.Log.LogWarning("Savestate: continuation failed: " + ex.Message); }
                }
            }));
        }

        private void StartLoad(string what, string error, Action<string> after)
        {
            if (error != null)
            {
                Ctx.Log.LogWarning("Savestate " + what + " failed: " + error);
                SetStatus(what + " failed: " + error);
                if (after != null) after(error);
                return;
            }

            _loadAfter = after;
            Ctx.Log.LogInfo("Savestate " + what + ": scene load started.");
            SetStatus(what + ": loading...");
            _timingLoad = true;
            _sawLoading = false;
            _loadStarted = Time.realtimeSinceStartup;
            _loadWhat = what;
        }

        // ------------------------------------------------------------------
        // Segment start states - called by the Practice module.

        public bool Busy { get { return _busy || _timingLoad; } }

        public string StartStatePath(Segment s)
        {
            return Path.Combine(Path.Combine(_dir, "segments"), SavestateFile.SafeFileName(s.Id) + SavestateFile.Extension);
        }

        public bool HasStartState(Segment s)
        {
            if (s == null || string.IsNullOrEmpty(s.Id)) return false;
            try { return File.Exists(StartStatePath(s)); }
            catch (Exception) { return false; }
        }

        /// One line for the segment editor; built on demand, not per frame.
        public string DescribeStartState(Segment s)
        {
            if (!HasStartState(s)) return "none - a restart keeps the game as it is";
            try
            {
                FileInfo fi = new FileInfo(StartStatePath(s));
                return "saved " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ", " +
                       SavestateBridge.Kb((int)Math.Min(fi.Length, int.MaxValue));
            }
            catch (Exception ex) { return "unreadable: " + ex.Message; }
        }

        /// On success the segment names the new state (`StartState`), which
        /// changes its route fingerprint; saving the segment is the caller's.
        public void CaptureStartState(Segment s, Action<string> done)
        {
            CaptureTo("start of " + s.Name + " (" + s.Id + ")", StartStatePath(s), delegate(string error)
            {
                if (error == null)
                {
                    s.StartState = _lastCaptureHash;
                    Ctx.Log.LogInfo("Savestate: start state of '" + s.Id + "' is now " + s.StartState +
                                    " - route " + s.RouteFingerprint() + ".");
                }
                done(error);
            });
        }

        public string DeleteStartState(Segment s)
        {
            try
            {
                string path = StartStatePath(s);
                if (File.Exists(path)) File.Delete(path);
                s.StartState = "";
                Ctx.Log.LogInfo("Savestate: start state of '" + s.Id + "' deleted.");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// Restores the segment's start state the segment's way; `done` gets
        /// null once the game is back in play (after the load, for a load),
        /// or the reason it could not.
        public void RestoreStartState(Segment s, Action<string> done)
        {
            if (Busy) { done("a savestate action is still running"); return; }

            SavestateFile f;
            try
            {
                string error;
                f = SavestateFile.Parse(File.ReadAllText(StartStatePath(s), Encoding.UTF8), out error);
                if (f == null) { done("start state unreadable: " + error); return; }
            }
            catch (Exception ex) { done("start state unreadable: " + ex.Message); return; }

            // A shared segment names the state it was timed from; a file
            // that is not that state still restores, but its times will not
            // compare fairly - say so.
            if (s.StartState.Length > 0 && Segment.HashText(f.Data) != s.StartState)
                Ctx.Log.LogWarning("Savestate: the start state file of '" + s.Id + "' is not the one the segment expects (" +
                                   s.StartState + ") - recapture it to make it so.");

            string mode = ModeMismatch(f);
            if (mode != null) { done(mode); return; }

            string what = "start state of '" + s.Name + "'";
            if (s.StartRestoreWithLoad)
            {
                Ctx.Practice.Mark("savestate restore (load)");
                PickupKeeper.Armed = true;
                StartLoad(what + " with load", _bridge.RestoreWithLoad(f.Data, f.Difficulty), done);
            }
            else
            {
                HashSet<string> present = f.Pickups != null ? new HashSet<string>(f.Pickups) : null;
                RestoreInPlace(f.Data, f.StreamingUnloaded, present, what, f.InCave ? 1 : 0, done);
            }
        }

        // A savestate from another save restores - in place the bridge
        // adopts the saved player, a load rebuilds everything - but not
        // across game modes: Creative is set up before the game loads and
        // is not in the save, so a Hard world would come back in Creative
        // (the author's suggestion, v0.22.1: "perhaps the same game mode").
        private string ModeMismatch(SavestateFile f)
        {
            if (!_bridge.Resolve()) return null;
            string here = _bridge.CurrentDifficulty;
            if (here.Length == 0 || f.Difficulty.Length == 0) return null;
            if ((here == "Creative") == (f.Difficulty == "Creative")) return null;

            if (_allowCrossMode.Value)
            {
                Ctx.Log.LogWarning("Savestate: '" + f.Name + "' was captured in a " + f.Difficulty +
                                   " game, this one is " + here + " - restoring anyway (AllowCrossModeRestore).");
                return null;
            }

            Ctx.Log.LogWarning("Savestate: refused '" + f.Name + "' - captured in a " + f.Difficulty +
                               " game, this one is " + here + ".");
            return "captured in a " + f.Difficulty + " game - this one is " + here + " (Creative and survival do not mix)";
        }

        private void CheckPickups()
        {
            int id;
            if (!int.TryParse(_itemIdText.Trim(), out id)) { SetStatus("item id must be a number"); return; }

            _bridge.DescribePickups(id, _diag);
            _diagLabels.Clear();
            for (int i = 0; i < _diag.Count; i++)
            {
                _diagLabels.Add(new GUIContent(_diag[i]));
                Ctx.Log.LogInfo("Savestate pickups: " + _diag[i]);
            }
        }

        private void DeleteSelected()
        {
            if (_selected < 0 || _selected >= _files.Count) return;

            if (Time.unscaledTime > _deleteArmedUntil)
            {
                _deleteArmedUntil = Time.unscaledTime + 3f;
                SetStatus("click Delete again within 3 s to delete " + Path.GetFileName(_files[_selected]));
                return;
            }

            _deleteArmedUntil = 0f;
            string path = _files[_selected];
            try
            {
                File.Delete(path);
                Ctx.Log.LogInfo("Savestate deleted " + Path.GetFileName(path));
                SetStatus("deleted " + Path.GetFileName(path));
            }
            catch (Exception ex) { SetStatus("delete failed: " + ex.Message); }

            _selected = -1;
            RefreshFiles();
        }

        // ------------------------------------------------------------------
        private SavestateFile LoadSelected()
        {
            if (_selected < 0 || _selected >= _files.Count) { SetStatus("select a savestate first"); return null; }

            string path = _files[_selected];
            try
            {
                string error;
                SavestateFile f = SavestateFile.Parse(File.ReadAllText(path, Encoding.UTF8), out error);
                if (f == null)
                {
                    SetStatus(Path.GetFileName(path) + ": " + error);
                    Ctx.Log.LogWarning("Savestate " + Path.GetFileName(path) + ": " + error);
                }
                return f;
            }
            catch (Exception ex)
            {
                SetStatus("could not read " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        private void RefreshFiles()
        {
            string keep = _selected >= 0 && _selected < _files.Count ? _files[_selected] : null;
            _files.Clear();
            _fileLabels.Clear();

            try
            {
                if (Directory.Exists(_dir))
                {
                    string[] found = Directory.GetFiles(_dir, "*" + SavestateFile.Extension);
                    Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < found.Length; i++)
                    {
                        FileInfo fi = new FileInfo(found[i]);
                        _files.Add(found[i]);
                        _fileLabels.Add(new GUIContent(Path.GetFileNameWithoutExtension(found[i]) + "   (" +
                            SavestateBridge.Kb((int)Math.Min(fi.Length, int.MaxValue)) + ", " +
                            fi.LastWriteTime.ToString("MM-dd HH:mm") + ")"));
                    }
                }
            }
            catch (Exception ex) { SetStatus("could not list savestates: " + ex.Message); }

            _selected = keep != null ? _files.IndexOf(keep) : -1;
        }

        private string UniquePath(string baseName)
        {
            string path = Path.Combine(_dir, baseName + SavestateFile.Extension);
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(_dir, baseName + "-" + n + SavestateFile.Extension);
            return path;
        }

        private void SetStatus(string s)
        {
            _status = new GUIContent(s);
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            float w = area.width - 20f;
            // The height drawn last pass: wrapped text makes it vary.
            float contentHeight = Mathf.Max(_contentHeight, 200f);
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, w, contentHeight));

            float y = 4f;
            y += UiText.Draw(0, y, w, Warning) + 4f;

            bool cross = GUI.Toggle(new Rect(0, y, w, 22), _allowCrossMode.Value,
                                    " Allow restoring across Creative and survival (testing)");
            if (cross != _allowCrossMode.Value) _allowCrossMode.Value = cross;
            y += 26f;

            // Capture
            GUI.Label(new Rect(0, y, 50, 22), "Name");
            _name = GUI.TextField(new Rect(52, y, Mathf.Min(260f, w - 52f), 22), _name ?? "");
            y += 26f;
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(0, y, 200, 24), "Capture here")) Capture();
            GUI.enabled = true;
            y += 32f;

            // Saved states
            y += UiText.Draw(0, y, w, _dirLabel);
            if (_fileLabels.Count == 0)
            {
                GUI.Label(new Rect(8, y, w, 20), "none yet");
                y += 22f;
            }
            for (int i = 0; i < _fileLabels.Count; i++)
            {
                bool on = GUI.Toggle(new Rect(8, y, w - 8, 22), _selected == i, _fileLabels[i]);
                if (on && _selected != i) { _selected = i; _deleteArmedUntil = 0f; }
                y += 24f;
            }
            if (GUI.Button(new Rect(0, y, 120, 22), "Refresh list")) RefreshFiles();
            y += 30f;

            GUI.enabled = !_busy && _selected >= 0;
            if (GUI.Button(new Rect(0, y, 260, 24), "Restore in place (no load)")) RestoreSelectedInPlace();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 260, 24), "Restore with load")) RestoreSelectedWithLoad();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 120, 22), Time.unscaledTime <= _deleteArmedUntil ? "Delete - sure?" : "Delete")) DeleteSelected();
            GUI.enabled = true;
            y += 32f;

            // The slot the game is running on
            y += UiText.Draw(0, y, w, _slotLabel);
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(0, y, 260, 24), "Reload slot save in place (no load)")) SlotInPlace();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 260, 24), "Load slot save without the menu")) SlotWithoutMenu();
            GUI.enabled = true;
            y += 32f;

            // Diagnostics
            GUI.Label(new Rect(0, y, 60, 22), "Item id");
            _itemIdText = GUI.TextField(new Rect(62, y, 60, 22), _itemIdText ?? "");
            if (GUI.Button(new Rect(130, y, 130, 22), "Check pickups")) CheckPickups();
            y += 28f;
            for (int i = 0; i < _diagLabels.Count; i++)
                y += UiText.Draw(8, y, w - 8, _diagLabels[i]);

            y += 6f;
            y += UiText.Draw(0, y, w, _status);
            y += UiText.Draw(0, y, w, _bindLabel);
            _contentHeight = y + 8f;

            GUI.EndScrollView();
        }
    }
}
