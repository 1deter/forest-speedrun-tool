using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    //   Restore in place      - LoadNow into the running scene, no load.
    //   Restore with load     - LoadSavedLevel: one scene load.
    //   Current slot's save   - the same two restores fed from the slot the
    //                           game is running on (the author's idea: a
    //                           faster quick-load, and a load-free one).
    //   Check pickups         - would the in-place restore bring an item
    //                           (default 210, the keycard) back?
    //
    // Every action logs one "Savestate ..." line; that line is the test.
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
        private string _dir;

        private bool _busy;
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
            _dir = Path.Combine(ctx.ConfigDirectory, "savestates");
            _dirLabel = new GUIContent("Savestates (" + _dir + ")");
            RefreshFiles();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.savestates", KeyCode.None, "Open Savestates tab", OpenMyTab);
            map.Add("savestate.capture", KeyCode.None, "Savestate: capture here", Capture);
            map.Add("savestate.restoreInPlace", KeyCode.None, "Savestate: restore selected in place", RestoreSelectedInPlace);
            map.Add("savestate.restoreLoad", KeyCode.None, "Savestate: restore selected with load", RestoreSelectedWithLoad);
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            if (Time.unscaledTime >= _nextSlotRefresh)
            {
                _nextSlotRefresh = Time.unscaledTime + 1f;
                _slotLabel.text = "Current save slot: " + _bridge.CurrentSlot;
                _bindLabel.text = "Bound: " + _bridge.Status;
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
                string line = _loadWhat + ": in game after " + elapsed.ToString("0.0") + " s. Loads this session: " +
                              _loadsThisSession + ", Mono heap " + (GC.GetTotalMemory(false) / (1024 * 1024)) + " MB.";
                Ctx.Log.LogInfo("Savestate " + line);
                SetStatus(line);
                return;
            }

            if (elapsed > LoadTimingTimeout)
            {
                _timingLoad = false;
                Ctx.Log.LogWarning("Savestate " + _loadWhat + ": could not time the load (FinishGameLoad " +
                                   (_sawLoading ? "never came back" : "never changed") + ").");
            }
        }

        // ------------------------------------------------------------------
        // Actions

        private void Capture()
        {
            if (_busy) return;
            if (!_bridge.Resolve()) { SetStatus("capture unavailable: " + _bridge.Status); return; }

            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            Ctx.Practice.Mark("savestate capture");
            SetStatus("capturing...");

            string name = _name;
            Vector3 pos = Ctx.Player.Found ? Ctx.Player.Transform.position : Vector3.zero;
            bool inCave = Ctx.Bridge.IsInCaves();

            Ctx.Runner.StartCoroutine(_bridge.Capture(delegate(SavestateBridge.Result r) { OnCaptured(r, name, pos, inCave); }));
        }

        private void OnCaptured(SavestateBridge.Result r, string name, Vector3 pos, bool inCave)
        {
            _busy = false;
            if (!r.Ok)
            {
                Ctx.Log.LogWarning("Savestate capture failed: " + r.Message);
                SetStatus("capture failed: " + r.Message);
                return;
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
                f.Data = r.Data;

                Directory.CreateDirectory(_dir);
                string path = UniquePath(SavestateFile.SafeFileName(name));
                File.WriteAllText(path, f.Write(), new UTF8Encoding(false));

                string line = "captured '" + name + "' -> " + Path.GetFileName(path) + ": " + r.Message;
                Ctx.Log.LogInfo("Savestate " + line);
                SetStatus(line);

                RefreshFiles();
                _selected = _files.IndexOf(path);
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Savestate capture: could not write the file: " + ex.Message);
                SetStatus("capture failed writing the file: " + ex.Message);
            }
        }

        private void RestoreSelectedInPlace()
        {
            SavestateFile f = LoadSelected();
            if (f != null) RestoreInPlace(f.Data, "'" + f.Name + "'");
        }

        private void RestoreSelectedWithLoad()
        {
            SavestateFile f = LoadSelected();
            if (f == null || _busy) return;

            Ctx.Practice.Mark("savestate restore (load)");
            string err = _bridge.RestoreWithLoad(f.Data, f.Difficulty);
            StartLoad("restore '" + f.Name + "' with load", err);
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
            RestoreInPlace(data, "slot " + _bridge.CurrentSlot);
        }

        private void SlotWithoutMenu()
        {
            if (_busy) return;
            Ctx.Practice.Mark("savestate slot load");
            string err = _bridge.LoadSlotWithoutMenu();
            StartLoad("load slot " + _bridge.CurrentSlot + " without the menu", err);
        }

        private void RestoreInPlace(string data, string what)
        {
            if (_busy) return;
            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            Ctx.Practice.Mark("savestate restore (in place)");
            SetStatus("restoring " + what + " in place...");
            Ctx.Log.LogInfo("Savestate restore " + what + " in place: starting.");

            Ctx.Runner.StartCoroutine(_bridge.RestoreInPlace(data, delegate(SavestateBridge.Result r)
            {
                _busy = false;

                // The serializer restores the player's transform but only
                // the Clock/cave-door path sends InACave; bring the cave
                // state in line with where the player now stands.
                string cave = "";
                if (r.Ok && Ctx.Player.Found)
                {
                    try { cave = Ctx.Bridge.SyncCaveState(Ctx.Player.Transform.position); }
                    catch (Exception) { }
                }

                string line = "restore " + what + " in place: " + r.Message +
                              (string.IsNullOrEmpty(cave) ? "" : " | cave: " + cave);
                if (r.Ok) Ctx.Log.LogInfo("Savestate " + line);
                else Ctx.Log.LogWarning("Savestate " + line);
                SetStatus(line);
            }));
        }

        private void StartLoad(string what, string error)
        {
            if (error != null)
            {
                Ctx.Log.LogWarning("Savestate " + what + " failed: " + error);
                SetStatus(what + " failed: " + error);
                return;
            }

            Ctx.Log.LogInfo("Savestate " + what + ": scene load started.");
            SetStatus(what + ": loading...");
            _timingLoad = true;
            _sawLoading = false;
            _loadStarted = Time.realtimeSinceStartup;
            _loadWhat = what;
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
            float contentHeight = 520f + 24f * (_fileLabels.Count + _diagLabels.Count);
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, w, contentHeight));

            float y = 4f;
            GUI.Label(new Rect(0, y, w, 44), Warning);
            y += 48f;

            // Capture
            GUI.Label(new Rect(0, y, 50, 22), "Name");
            _name = GUI.TextField(new Rect(52, y, Mathf.Min(260f, w - 52f), 22), _name ?? "");
            y += 26f;
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(0, y, 200, 24), "Capture here")) Capture();
            GUI.enabled = true;
            y += 32f;

            // Saved states
            GUI.Label(new Rect(0, y, w, 20), _dirLabel);
            y += 22f;
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
            GUI.Label(new Rect(0, y, w, 20), _slotLabel);
            y += 22f;
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
            {
                GUI.Label(new Rect(8, y, w - 8, 22), _diagLabels[i]);
                y += 24f;
            }

            y += 6f;
            GUI.Label(new Rect(0, y, w, 60), _status);
            y += 62f;
            GUI.Label(new Rect(0, y, w, 40), _bindLabel);

            GUI.EndScrollView();
        }
    }
}
