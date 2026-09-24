using System;
using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Practice spots and segments - ONE list.
    //
    // A spot and a segment used to be separate features with separate
    // lists, files and editors, which was duplication for a single idea:
    // somewhere to stand, optionally with a start and an end attached.
    // Every entry here is a place you can teleport to; tick "Timed
    // segment" and it additionally becomes a run with triggers and splits.
    //
    // That also removed the old "anchor": the entry you last went to IS
    // where the next attempt starts from.
    //
    // Everything positional is captured from where the player is standing
    // via Here buttons, and zones are previewed in the world while
    // editing, because typing a radius and hoping is guesswork.
    //
    // Edits live in memory until Save, so a half-made entry costs nothing
    // and a bad edit cannot corrupt a shared file. Selection never waits
    // on Save: an edit stays on its Segment, the list marks it unsaved and
    // Save writes every unsaved entry. A guard that refused the click
    // while anything was unsaved read as a stuck list (runner maks,
    // v0.23.1: "clicking on the others and nothing is happening").
    // ------------------------------------------------------------------
    public sealed class PracticeModule : OverlayModule
    {
        private const float RowHeight = 22f;
        private const float DefaultRadius = 3f;
        private const float ListWidth = 250f;

        public override string Id { get { return "practice"; } }
        public override string DisplayName { get { return "Practice"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Practice"; } }
        public override int TabOrder { get { return 10; } }
        public override bool IsPracticeOnly { get { return true; } }

        private SegmentLibrary _library;
        private Segment _selected;
        private readonly List<Segment> _unsaved = new List<Segment>();
        private readonly GUIContent _saveLabel = new GUIContent("Save");
        private string _status = "";
        private string _filter = "";

        // --- current entry (what a practice attempt starts from) ----------
        private Segment _current;
        public bool HasSpot { get { return _current != null && _current.HasSpawn; } }
        public Vector3 SpotPosition { get { return _current != null ? _current.SpawnPosition : Vector3.zero; } }
        public string SpotLabel { get { return _current != null ? _current.Name : ""; } }
        public Segment CurrentSegment { get { return _current; } }

        /// The entry selected in the editor list (not necessarily current).
        public Segment SelectedSegment { get { return _selected; } }

        /// A file check - for a death, not per frame.
        public bool CurrentHasStartState
        {
            get { return HasSpot && _savestates != null && _savestates.HasStartState(_current); }
        }

        /// Raised when the player is placed at the current entry, so a run
        /// can arm without this module knowing the timer exists.
        public Action OnPlacedAtSpot;

        /// Raised when a start-state restore begins, before the world
        /// changes: a run in progress is void from here (author, v0.24.11:
        /// the clock ran on through a load restore until it finished).
        public Action OnRestartStarting;

        private float _tabW;
        private float _tabH;
        private Vector2 _listScroll;
        private Vector2 _editScroll;
        private float _editHeight = 700f;

        private GUIStyle _rowStyle;
        private GUIStyle _selectedRowStyle;
        private GUIStyle _dimStyle;
        private GUIStyle _headerStyle;

        // The list is grouped by category with collapsible headers.
        // Merging spots and segments flattened this by accident, and a
        // flat list stops being navigable the moment someone has more
        // than a screenful of spots.
        private struct Row
        {
            public bool IsHeader;
            public string Category;
            public Segment Entry;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<GUIContent> _rowLabels = new List<GUIContent>();
        private readonly Dictionary<string, bool> _collapsed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private int _entryCount;

        // The list in display order. The library is sorted only when it
        // loads, so an entry whose category was edited to an existing one
        // ("Caves") made a second "Caves" header at the bottom that opened
        // and closed with the first (author, v0.24.7).
        private readonly List<Segment> _sorted = new List<Segment>();
        private static readonly Comparison<Segment> ByCategoryThenName = SegmentLibrary.Compare;

        // A name / category edit regroups the list once typing pauses, not
        // on every keystroke (the row would jump while being typed).
        private float _regroupAt = -1f;

        // Zone preview.
        private GameObject _previewHost;
        private ZonePreviewBehaviour _preview;
        private bool _showPreview = true;

        // While a run is in progress the run module takes over the
        // preview and shows only the NEXT objective - the whole route
        // drawn at once is a field of overlapping spheres with no
        // indication of where to go.
        private bool _runPreviewActive;
        private Trigger _runPreviewTrigger;
        private int _runPreviewKind;

        // Item search state. The target identifies which trigger is being
        // searched for: -2 start, -3 end, >= 0 a checkpoint index.
        private string _itemQuery = "";
        private readonly List<ItemInfo> _itemResults = new List<ItemInfo>();
        private int _itemSearchTarget = -1;

        public SegmentLibrary Library { get { return _library; } }

        // Segment start states live in the Savestates module; this module
        // only decides when to restore one (every restart) and shows it in
        // the editor. The label is rebuilt on selection change or after a
        // capture/delete, never per frame.
        private SavestateModule _savestates;
        private AreaKeeper _areas;
        private Segment _startStateFor;
        private string _startStateForId;
        private readonly GUIContent _startStateLabel = new GUIContent("");
        private readonly GUIContent _startStatusLabel = new GUIContent("");
        private static readonly GUIContent QuickLoadHint =
            new GUIContent("Quick load: in place, fastest. Full load is the game's full reset (a scene load) for states Quick load misses.");
        private static readonly GUIContent FullLoadHint =
            new GUIContent("Full load: the game's full reset with a scene load, slower. Quick load is in place and fastest.");
        private float _deleteStartArmedUntil;
        private float _captureStartArmedUntil;

        // Only to count what a start-state change would retire.
        private AttemptStore _attempts;

        // ------------------------------------------------------------------
        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _library = new SegmentLibrary(ctx.Log, ctx.ConfigDirectory);
            Reload();

            _savestates = Host.Find<SavestateModule>();
            _areas = new AreaKeeper(ctx.Log);
            _attempts = new AttemptStore(ctx.Log, ctx.ConfigDirectory);

            _previewHost = new GameObject("ForestOverlay_ZonePreview");
            _previewHost.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_previewHost);
            _preview = _previewHost.AddComponent<ZonePreviewBehaviour>();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("practice.saveSpot", KeyCode.F6, "Save spot here", QuickSaveSpot);
            map.Add("practice.goToSpot", KeyCode.F7, "Restart current spot (restores its start state)", ReturnToSpot);
            map.Add("tab.practice", KeyCode.None, "Open Practice tab", OpenMyTab);
        }

        private void Reload()
        {
            // Reload rebuilds every Segment object, so a remembered
            // reference becomes an orphan: it is no longer in the
            // library and still holds the OLD spawn and zones. That is
            // how Restart could teleport you to a start position you had
            // already moved. Re-resolve it by id instead.
            string currentId = _current != null ? _current.Id : null;

            _library.Reload();
            _selected = null;
            _unsaved.Clear();

            _current = currentId != null ? _library.ById(currentId) : null;

            RebuildVisible();
        }

        public override void Tick()
        {
            // What the hands rest in, so a reset can cut an action back to it.
            AnimReset.Track();
            UpdatePreview();

            if (_regroupAt > 0f && Time.realtimeSinceStartup >= _regroupAt)
            {
                _regroupAt = -1f;
                RebuildVisible();
            }
        }

        public override void Shutdown()
        {
            if (_previewHost != null) UnityEngine.Object.Destroy(_previewHost);
        }

        // ------------------------------------------------------------------
        private void RebuildVisible()
        {
            _rows.Clear();
            _entryCount = 0;

            string f = _filter.Length > 0 ? _filter.ToLowerInvariant() : null;

            // Sorted here by category then name, so a single pass emits a
            // header whenever the category changes.
            _sorted.Clear();
            _sorted.AddRange(_library.All);
            _sorted.Sort(ByCategoryThenName);

            string current = null;
            bool collapsed = false;

            for (int i = 0; i < _sorted.Count; i++)
            {
                Segment e = _sorted[i];

                if (f != null &&
                    e.Name.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0 &&
                    e.Category.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0)
                    continue;

                if (current == null || !SegmentLibrary.SameCategory(e.Category, current))
                {
                    current = SegmentLibrary.CategoryKey(e.Category);
                    collapsed = IsCollapsed(current);

                    Row header;
                    header.IsHeader = true;
                    header.Category = current;
                    header.Entry = null;
                    _rows.Add(header);
                }

                _entryCount++;
                if (collapsed) continue;

                Row row;
                row.IsHeader = false;
                row.Category = current;
                row.Entry = e;
                _rows.Add(row);
            }

            RebuildLabels();
        }

        private void RebuildLabels()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                string text;

                if (_rows[i].IsHeader)
                {
                    int count = CountIn(_rows[i].Category);
                    text = (IsCollapsed(_rows[i].Category) ? "+ " : "- ") +
                           _rows[i].Category + "   (" + count + ")";
                }
                else
                {
                    // A star marks a timed segment; plain entries are just
                    // somewhere to teleport.
                    text = (_rows[i].Entry.IsTimed ? "  * " : "     ") + _rows[i].Entry.Name +
                           (_unsaved.Contains(_rows[i].Entry) ? "  (unsaved)" : "");
                }

                if (i < _rowLabels.Count) _rowLabels[i].text = text;
                else _rowLabels.Add(new GUIContent(text));
            }

            _saveLabel.text = _unsaved.Count == 0 ? "Save" : "Save (" + _unsaved.Count + ")";
        }

        private int CountIn(string category)
        {
            IList<Segment> all = _library.All;
            int n = 0;

            for (int i = 0; i < all.Count; i++)
                if (SegmentLibrary.SameCategory(all[i].Category, category)) n++;

            return n;
        }

        private bool IsCollapsed(string category)
        {
            bool v;
            return _collapsed.TryGetValue(category, out v) && v;
        }

        private void ToggleCategory(string category)
        {
            _collapsed[category] = !IsCollapsed(category);
            RebuildVisible();
        }

        /// Show a single trigger, overriding the editing preview. Used by
        /// the run module to show only the next objective.
        public void SetRunPreview(Trigger t, int kind)
        {
            _runPreviewActive = true;
            _runPreviewTrigger = t;
            _runPreviewKind = kind;
        }

        public void ClearRunPreview()
        {
            _runPreviewActive = false;
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;

            if (_runPreviewActive)
            {
                if (!_showPreview || _runPreviewTrigger.Kind != TriggerKind.Zone)
                {
                    _preview.Show = false;
                    _preview.Count = 0;
                    return;
                }

                if (_preview.Zones == null || _preview.Zones.Length < 1)
                    _preview.Zones = new PreviewZone[8];

                _preview.Count = AddZone(_runPreviewTrigger, _runPreviewKind, 0);
                _preview.Show = _preview.Count > 0;
                return;
            }

            if (!_showPreview || _selected == null || !_selected.IsTimed)
            {
                _preview.Show = false;
                _preview.Count = 0;
                return;
            }

            int needed = 2 + _selected.Checkpoints.Count;
            if (_preview.Zones == null || _preview.Zones.Length < needed)
                _preview.Zones = new PreviewZone[needed + 8];

            int n = 0;
            n = AddZone(_selected.Start, 0, n);

            for (int i = 0; i < _selected.Checkpoints.Count; i++)
                n = AddZone(_selected.Checkpoints[i], 1, n);

            n = AddZone(_selected.End, 2, n);

            _preview.Count = n;
            _preview.Show = n > 0;
        }

        private int AddZone(Trigger t, int kind, int n)
        {
            if (t.Kind != TriggerKind.Zone) return n;

            PreviewZone z;
            z.Center = t.Position;
            z.Radius = t.Radius;
            z.Extents = t.Extents;
            z.IsBox = t.Shape == ZoneShape.Box;
            z.Kind = kind;

            _preview.Zones[n] = z;
            return n + 1;
        }

        // ------------------------------------------------------------------
        // Teleporting
        // ------------------------------------------------------------------
        /// A restart. With a start state the world is restored first (in
        /// place, or with a load per the segment), then the teleport runs as
        /// before - it sets the view angles and the cave state, and fires
        /// OnPlacedAtSpot for the run module once the world is final.
        private void Restart(Segment s)
        {
            if (s == null || !s.HasSpawn) { _status = "That entry has no spawn point."; return; }

            if (_savestates != null && _savestates.HasStartState(s))
            {
                if (_savestates.Busy) { _status = "A savestate action is still running."; return; }

                _current = s;
                if (OnRestartStarting != null) OnRestartStarting();
                StartStatus(s.StartRestoreWithLoad ? "Full load..." : "Quick load...");
                Ctx.Log.LogInfo("Restart '" + s.Id + "': restoring its start state " +
                                (s.StartRestoreWithLoad ? "with a load." : "in place."));
                _savestates.RestoreStartState(s, delegate(string error)
                {
                    // A restored state has set the cave state from its file;
                    // the terrain guess below can be wrong at a cave mouth.
                    PlaceAt(s, error != null);
                    if (error == null) return;

                    // After the teleport, which writes its own status. Shown
                    // beside the buttons, and on screen when the window is
                    // closed (F7, a death): the log alone went unseen.
                    Ctx.Log.LogWarning("Start state of '" + s.Id + "' not restored: " + error);
                    string msg = "Start state not restored - " + error + ". Teleported only.";
                    _status = "";   // said once, under the Start state buttons
                    StartStatus(msg);
                    if (!Host.AnyPanelOpen()) Ctx.Notice.Show(msg, 7f);
                });
                return;
            }

            if (_savestates != null) Ctx.Log.LogInfo("Restart '" + s.Id + "': no start state - teleport only.");
            PlaceAt(s, true);
        }

        /// Go: a teleport and nothing else, start state or not (author,
        /// v0.22.0: one button, one job - restoring is Restart / F7).
        private void Teleport(Segment s)
        {
            if (s == null || !s.HasSpawn) { _status = "That entry has no spawn point."; return; }
            PlaceAt(s, true);
        }

        private void PlaceAt(Segment s, bool syncCave)
        {
            Quaternion rot = Quaternion.Euler(0f, s.SpawnYaw, 0f);

            // Before moving, as the game's own Goto does: a spot inside a
            // cave needs the cave state (no terrain collision, cave
            // lighting), or you arrive under the terrain in the dark.
            string cave = Ctx.Player.Found && syncCave ? Ctx.Bridge.SyncCaveState(s.SpawnPosition) : "";
            // The endgame's area and overlook flag (the red elevator's ride)
            // outlive leaving the endgame (AreaKeeper). A restore sets its own.
            string area = syncCave ? _areas.ForTeleport(s.SpawnPosition) : "";
            if (area.Length > 0) cave += (cave.Length > 0 ? ", " : "") + area;

            if (!Ctx.Player.MoveTo(s.SpawnPosition, rot)) { _status = "No player ref."; return; }

            // MoveTo zeroes the speed; the fall's air time and last impact
            // speed live in the game's controller, and a Go in mid-air kept
            // them for the landing.
            string fall = Ctx.Bridge.EndFall();
            // A swing / action in progress is cut (runner maks).
            string anim = AnimReset.Cancel();
            if (anim.Length > 0) fall += (fall.Length > 0 ? ", " : "") + anim;

            Ctx.Bridge.ApplyLook(Ctx.Player.Transform, s.SpawnYaw, s.SpawnPitch);
            Ctx.Practice.Mark("teleport: " + s.Name);

            _current = s;
            _status = "-> " + s.Name + (cave.Length > 0 ? " (" + cave + ")" : "");

            // Logged too: the status line is easy to miss, and the log is
            // what a runner sends when a cave teleport misbehaves.
            if (cave.Length > 0 || fall.Length > 0)
                Ctx.Log.LogInfo("Teleport to '" + s.Name + "': " + cave +
                                (cave.Length > 0 && fall.Length > 0 ? ", " : "") + fall + ".");

            if (OnPlacedAtSpot != null) OnPlacedAtSpot();
        }

        public void ReturnToSpot()
        {
            if (_current == null) { _status = "No entry selected."; return; }
            Restart(_current);
        }

        // The test bridge (Modules/BridgeModule): null, or why not.
        public string BridgeGo(string id)
        {
            Segment s = _library.ById(id);
            if (s == null) return "no practice entry '" + id + "' (spots lists them)";
            if (!s.HasSpawn) return "'" + id + "' has no spawn point";
            Teleport(s);
            return null;
        }

        /// `id` null: the current spot, as F7.
        public string BridgeRestart(string id)
        {
            Segment s = id == null ? _current : _library.ById(id);
            if (s == null) return id == null ? "no current spot" : "no practice entry '" + id + "' (spots lists them)";
            if (!s.HasSpawn) return "'" + s.Id + "' has no spawn point";
            if (_savestates != null && _savestates.Busy) return "a savestate action is still running";
            Restart(s);
            return null;
        }

        /// Saves where you stand as a new entry and selects it.
        private void QuickSaveSpot()
        {
            Segment s = NewFromHere("spot " + DateTime.Now.ToString("HH:mm:ss"));
            if (s == null) return;

            _library.Add(s);
            _selected = s;
            _current = s;

            RebuildVisible();

            // Written straight through: a quick-save that only lived in
            // memory would vanish on the next reload.
            if (WriteFile(s.SourceFile)) _status = "Saved '" + s.Name + "'";
            else _status = "Save failed - see log.";
        }

        private Segment NewFromHere(string name)
        {
            if (!Ctx.Player.Found) { _status = "No player ref."; return null; }

            Segment s = new Segment();
            s.Name = name;
            s.Category = "My spots";
            s.Id = NextFreeId("spot.my." + Slug(name));
            s.SourceFile = SegmentLibrary.UserFileName;

            s.SpawnPosition = Ctx.Player.Transform.position;
            s.SpawnYaw = Ctx.Player.Transform.eulerAngles.y;
            s.SpawnPitch = Ctx.Bridge.GetLookPitch();
            s.HasSpawn = true;

            return s;
        }

        // ------------------------------------------------------------------
        public override void ContributeHud(HudBuilder hud)
        {
            if (_current != null) hud.Pair("Spot", _current.Name);
            if (_status.Length > 0) hud.Pair("Prac", _status);
        }

        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;
            EnsureStyles();

            float w = _tabW;

            // --- toolbar ---------------------------------------------------
            if (GUI.Button(new Rect(0, 0, 70, 24), "New")) CreateNew();

            GUI.enabled = _selected != null;
            if (GUI.Button(new Rect(74, 0, 80, 24), "Duplicate")) Duplicate();
            if (GUI.Button(new Rect(158, 0, 70, 24), "Delete")) Delete();
            GUI.enabled = true;

            GUI.enabled = _unsaved.Count > 0;
            if (GUI.Button(new Rect(w - 160, 0, 70, 24), _saveLabel)) Save();
            GUI.enabled = true;
            if (GUI.Button(new Rect(w - 86, 0, 86, 24), "Reload")) Reload();

            // --- filter ----------------------------------------------------
            GUI.Label(new Rect(0, 30, 36, 20), "Find");
            string filter = GUI.TextField(new Rect(38, 28, ListWidth - 80, 22), _filter);
            if (filter != _filter) { _filter = filter; RebuildVisible(); }
            if (GUI.Button(new Rect(ListWidth - 38, 28, 38, 22), "x")) { _filter = ""; RebuildVisible(); }

            bool preview = GUI.Toggle(new Rect(ListWidth + 14, 30, 120, 20), _showPreview, " show zones");
            if (preview != _showPreview) _showPreview = preview;

            // Over the spot panel it is about, wrapped to that panel and as
            // tall as it needs (UiText); the editor starts below it.
            // Start-state messages sit under their own buttons.
            float statusH = Mathf.Max(22f, UiText.Draw(ListWidth + 14, 54, w - ListWidth - 14, _status));

            DrawList(new Rect(0, 56, ListWidth, _tabH - 60));
            DrawEditor(new Rect(ListWidth + 14, 56 + statusH, w - ListWidth - 14, _tabH - 60 - statusH));
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null) return;

            _rowStyle = new GUIStyle(GUI.skin.button);
            _rowStyle.alignment = TextAnchor.MiddleLeft;
            _rowStyle.padding = new RectOffset(6, 4, 0, 0);

            _selectedRowStyle = new GUIStyle(_rowStyle);
            _selectedRowStyle.fontStyle = FontStyle.Bold;

            _dimStyle = new GUIStyle(GUI.skin.label);
            _dimStyle.alignment = TextAnchor.MiddleLeft;

            _headerStyle = new GUIStyle(GUI.skin.box);
            _headerStyle.alignment = TextAnchor.MiddleLeft;
            _headerStyle.padding = new RectOffset(6, 4, 0, 0);
            _headerStyle.fontStyle = FontStyle.Bold;
        }

        private void DrawList(Rect area)
        {
            GUI.Box(area, GUIContent.none);

            Rect content = new Rect(0, 0, area.width - 20f, _rows.Count * RowHeight + 4f);
            _listScroll = GUI.BeginScrollView(area, _listScroll, content);

            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= _rowLabels.Count) break;

                float rowY = 2f + i * RowHeight;
                if (rowY + RowHeight < _listScroll.y || rowY > _listScroll.y + area.height) continue;

                if (_rows[i].IsHeader)
                {
                    if (GUI.Button(new Rect(2f, rowY, content.width - 4f, RowHeight - 2f),
                                   _rowLabels[i], _headerStyle))
                    {
                        ToggleCategory(_rows[i].Category);
                        break;   // _rows was rebuilt underneath us
                    }
                    continue;
                }

                Segment entry = _rows[i].Entry;
                Rect r = new Rect(2f, rowY, content.width - 52f, RowHeight - 2f);
                bool isSelected = ReferenceEquals(entry, _selected);

                if (GUI.Button(r, _rowLabels[i], isSelected ? _selectedRowStyle : _rowStyle) && !isSelected)
                    Select(entry);

                GUI.enabled = entry.HasSpawn;
                if (GUI.Button(new Rect(content.width - 48f, rowY, 44f, RowHeight - 2f), "Go"))
                    Teleport(entry);
                GUI.enabled = true;
            }

            GUI.EndScrollView();

            if (_entryCount == 0)
            {
                GUI.Label(new Rect(area.x + 8, area.y + 8, area.width - 16, 80),
                          _library.All.Count == 0
                              ? "Nothing yet.\n\nStand somewhere and\npress New (or F6)."
                              : "No matches for that filter.");
            }
        }

        // ------------------------------------------------------------------
        private void DrawEditor(Rect area)
        {
            if (_selected == null)
            {
                GUI.Label(new Rect(area.x, area.y + 8, area.width, 80),
                          "Select an entry on the left, or press New.\n\n" +
                          "Every entry is somewhere you can teleport to.\n" +
                          "Tick 'Timed segment' to make it a timed run.");
                return;
            }

            Segment s = _selected;
            float w = area.width;

            // The height drawn last pass - wrapped text makes it vary.
            Rect content = new Rect(0, 0, w - 20f, Mathf.Max(_editHeight, 200f));
            _editScroll = GUI.BeginScrollView(area, _editScroll, content);

            // Not 0: a text field sits 2 px above its row and the scroll
            // view clipped its top edge (author, v0.22.3).
            float y = 4f;
            float cw = content.width;

            string name = s.Name, category = s.Category;
            y = Field(y, cw, "Name", ref s.Name);
            y = Field(y, cw, "Category", ref s.Category);
            if (s.Name != name || s.Category != category) _regroupAt = Time.realtimeSinceStartup + 0.6f;
            y = Field(y, cw, "Id", ref s.Id);

            if (!_library.IsIdAvailable(s.Id, s))
            {
                GUI.Label(new Rect(80, y, cw - 90, 18), "id already used - pick another");
                y += 18f;
            }

            y = Field(y, cw, "Notes", ref s.Notes);
            y += 6f;

            // --- spawn -----------------------------------------------------
            GUI.Label(new Rect(0, y, 74, 20), "Spawn");
            if (s.HasSpawn) GUI.Label(new Rect(80, y, cw - 220, 20), CoordsLabel(SpawnSlot, 0, s.SpawnPosition));
            else GUI.Label(new Rect(80, y, cw - 220, 20), "(none - cannot teleport here)");

            if (GUI.Button(new Rect(cw - 136, y - 2, 56, 22), "Here")) SetSpawnHere(s);

            GUI.enabled = s.HasSpawn;
            if (GUI.Button(new Rect(cw - 76, y - 2, 42, 22), "Go")) Teleport(s);
            GUI.enabled = true;
            y += 30f;

            y = DrawStartState(y, cw, s);

            // --- timed toggle ----------------------------------------------
            bool timed = GUI.Toggle(new Rect(0, y, 150, 20), s.IsTimed, " Timed segment");
            if (timed != s.IsTimed) ToggleTimed(s, timed);

            GUI.Label(new Rect(156, y, cw - 166, 20),
                      timed ? "runs start -> end, splitting at checkpoints"
                            : "teleport only - tick to add a start and end",
                      _dimStyle);
            y += 28f;

            if (s.IsTimed || s.Start.IsSet || s.End.IsSet)
            {
                y = DrawTrigger(y, cw, "Start", ref s.Start, -2);

                for (int i = 0; i < s.Checkpoints.Count; i++)
                {
                    Trigger t = s.Checkpoints[i];
                    float before = y;
                    y = DrawTrigger(y, cw, CheckName(i), ref t, i);
                    s.Checkpoints[i] = t;

                    if (GUI.Button(new Rect(cw - 26f, before - 2f, 22f, 22f), "x"))
                    {
                        s.Checkpoints.RemoveAt(i);
                        Touch();
                        break;
                    }
                }

                if (GUI.Button(new Rect(0, y, 160, 22), "Add checkpoint here"))
                {
                    s.Checkpoints.Add(ZoneHere());
                    Touch();
                }
                y += 28f;

                y = DrawTrigger(y, cw, "End", ref s.End, -3);
            }

            _editHeight = y + 40f;   // room for the item search results below a trigger
            GUI.EndScrollView();
        }

        private void ToggleTimed(Segment s, bool on)
        {
            if (on)
            {
                // Both ends default to where you stand, so a fresh segment is
                // immediately coherent rather than pointing at world origin.
                if (!s.Start.IsSet) s.Start = ZoneHere();
                if (!s.End.IsSet) s.End = ZoneHere();
            }
            else
            {
                s.Start = new Trigger();
                s.End = new Trigger();
                s.Checkpoints.Clear();
            }

            Touch();
            RebuildVisible();
        }

        // ------------------------------------------------------------------
        private float DrawTrigger(float y, float w, string label, ref Trigger t, int slot)
        {
            const float labelW = 74f;
            float x0 = labelW + 6f;

            GUI.Label(new Rect(0, y, labelW, 20), label);

            float x = x0;
            x = KindButton(x, y, "zone", TriggerKind.Zone, ZoneShape.Sphere, ref t);
            x = KindButton(x, y, "box", TriggerKind.Zone, ZoneShape.Box, ref t);
            x = KindButton(x, y, "item", TriggerKind.Item, ZoneShape.Sphere, ref t);
            x = KindButton(x, y, "event", TriggerKind.Event, ZoneShape.Sphere, ref t);
            x = KindButton(x, y, "manual", TriggerKind.Manual, ZoneShape.Sphere, ref t);

            y += 26f;

            switch (t.Kind)
            {
                case TriggerKind.Zone:
                    {
                        GUI.Label(new Rect(x0, y, w - x0 - 70f, 20), CoordsLabel(slot, 0, t.Position));

                        if (GUI.Button(new Rect(w - 64f, y - 2f, 58f, 22f), "Here"))
                        {
                            Vector3 p;
                            if (TryPlayerPosition(out p)) { t.Position = p; Touch(); }
                        }
                        y += 24f;

                        if (t.Shape == ZoneShape.Box) y = BoxFields(y, w, x0, ref t, slot);
                        else y = SphereFields(y, w, x0, ref t, slot);
                        break;
                    }

                case TriggerKind.Item:
                    y = ItemFields(y, w, x0, ref t, slot);
                    break;

                case TriggerKind.Event:
                    {
                        // Typed, or stepped through the known list - the
                        // names are ids, and a typo would never fire.
                        if (GUI.Button(new Rect(x0, y - 2f, 26f, 22f), "<")) { t.EventName = StepEvent(t.EventName, -1); Touch(); }
                        if (GUI.Button(new Rect(x0 + 28f, y - 2f, 26f, 22f), ">")) { t.EventName = StepEvent(t.EventName, 1); Touch(); }

                        string name = GUI.TextField(new Rect(x0 + 58f, y - 2f, w - x0 - 64f, 22f), t.EventName ?? "");
                        if (name != t.EventName) { t.EventName = name; Touch(); }
                        y += 24f;

                        GUI.Label(new Rect(x0, y, w - x0 - 6f, 20), EventLabel(t.EventName), _dimStyle);
                        y += 22f;
                        break;
                    }

                case TriggerKind.Manual:
                    GUI.Label(new Rect(x0, y, w - x0 - 6f, 20), "only fires on the hotkey", _dimStyle);
                    y += 22f;
                    break;

                default:
                    GUI.Label(new Rect(x0, y, w - x0 - 6f, 20), "not set - pick a kind above", _dimStyle);
                    y += 22f;
                    break;
            }

            return y + 6f;
        }

        private static string[] KnownEvents()
        {
            return GameEvents.RouteOrder;
        }

        private static string StepEvent(string current, int dir)
        {
            string[] known = KnownEvents();
            int at = -1;
            for (int i = 0; i < known.Length; i++)
                if (string.Equals(known[i], current, StringComparison.OrdinalIgnoreCase)) { at = i; break; }

            int next = at < 0 ? (dir > 0 ? 0 : known.Length - 1)
                              : (at + dir + known.Length) % known.Length;
            return known[next];
        }

        // Labels are cached per name: OnGUI runs several times a frame and
        // LabelFor builds a string for keycard-door-<id>.
        private readonly Dictionary<string, string> _eventLabels = new Dictionary<string, string>();

        private string EventLabel(string name)
        {
            if (string.IsNullOrEmpty(name)) return "pick an event with < >";

            string label;
            if (_eventLabels.TryGetValue(name, out label)) return label;

            label = GameEvents.LabelFor(name) ?? "unknown event - this will never fire";
            _eventLabels[name] = label;
            return label;
        }

        private float SphereFields(float y, float w, float x0, ref Trigger t, int slot)
        {
            GUI.Label(new Rect(x0, y, 110f, 20), MetresLabel(slot, 1, "radius", t.Radius));

            // Written only when dragged: the slider shows a default for a
            // zero size and clamps to its range, and writing that back
            // marked an entry edited just for being looked at.
            float sliderX = x0 + 114f;
            float shown = Mathf.Clamp(t.Radius <= 0f ? DefaultRadius : t.Radius, 0.5f, 25f);
            float r = GUI.HorizontalSlider(new Rect(sliderX, y + 6f, w - sliderX - 6f, 18f), shown, 0.5f, 25f);
            if (!Mathf.Approximately(r, shown)) { t.Radius = r; Touch(); }

            return y + 26f;
        }

        private float BoxFields(float y, float w, float x0, ref Trigger t, int slot)
        {
            Vector3 e = t.Extents;
            if (e.x <= 0f && e.y <= 0f && e.z <= 0f) e = new Vector3(3f, 3f, 3f);

            // Written only when dragged (see SphereFields).
            Vector3 shown = new Vector3(Mathf.Clamp(e.x, 0.5f, 30f), Mathf.Clamp(e.y, 0.5f, 30f), Mathf.Clamp(e.z, 0.5f, 30f));
            e = shown;

            // Shown as full size because "width 6m" is what a player can
            // pace out; extents are the half-size the maths wants.
            e.x = ExtentSlider(y, w, x0, slot, 2, "width", e.x);
            y += 24f;
            e.y = ExtentSlider(y, w, x0, slot, 3, "height", e.y);
            y += 24f;
            e.z = ExtentSlider(y, w, x0, slot, 4, "depth", e.z);
            y += 26f;

            if (e != shown) { t.Extents = e; Touch(); }
            return y;
        }

        private float ExtentSlider(float y, float w, float x0, int slot, int field, string label, float value)
        {
            GUI.Label(new Rect(x0, y, 110f, 20), MetresLabel(slot, field, label, value * 2f));

            float sliderX = x0 + 114f;
            return GUI.HorizontalSlider(new Rect(sliderX, y + 6f, w - sliderX - 6f, 18f),
                                        value, 0.5f, 30f);
        }

        // Item triggers search by NAME, because nobody knows item ids.
        private float ItemFields(float y, float w, float x0, ref Trigger t, int slot)
        {
            GUI.Label(new Rect(x0, y, w - x0 - 6f, 20), ItemLabel(slot, t.ItemId));
            y += 24f;

            bool searching = _itemSearchTarget == slot;

            if (GUI.Button(new Rect(x0, y - 2f, 70f, 22f), searching ? "close" : "find"))
            {
                _itemSearchTarget = searching ? -1 : slot;
                _itemQuery = "";
                _itemResults.Clear();
                RebuildItemResultLabels();
            }

            if (GUI.Button(new Rect(x0 + 76f, y - 2f, 44f, 22f), Trigger.OpText(t.Compare)))
            {
                t.Compare = (Comparison)(((int)t.Compare + 1) % 3);
                Touch();
            }

            string amtText = GUI.TextField(new Rect(x0 + 126f, y - 2f, 50f, 22f), AmountText(slot, t.Amount));
            if (!ReferenceEquals(amtText, AmountText(slot, t.Amount)))
            {
                _amountText[slot] = amtText;
                int parsedAmt;
                if (int.TryParse(amtText, out parsedAmt) && parsedAmt != t.Amount) { t.Amount = parsedAmt; Touch(); }
            }

            bool rel = GUI.Toggle(new Rect(x0 + 184f, y, 100f, 20), t.Relative, " relative");
            if (rel != t.Relative) { t.Relative = rel; Touch(); }
            y += 24f;

            GUI.Label(new Rect(x0, y, w - x0 - 6f, 20),
                      t.Relative
                          ? "fires after gaining this many MORE than at the start"
                          : "fires when the total held crosses this",
                      _dimStyle);
            y += 22f;

            if (!searching) return y;

            // --- search ----------------------------------------------------
            GUI.Label(new Rect(x0, y, 46f, 20), "name");
            string q = GUI.TextField(new Rect(x0 + 48f, y - 2f, w - x0 - 58f, 22f), _itemQuery);

            if (q != _itemQuery)
            {
                _itemQuery = q;
                Ctx.Inventory.SearchItems(q, _itemResults, 8);
                RebuildItemResultLabels();
            }
            y += 26f;

            for (int i = 0; i < _itemResults.Count && i < _itemResultLabels.Count; i++)
            {
                if (GUI.Button(new Rect(x0 + 10f, y, w - x0 - 20f, 20f), _itemResultLabels[i], _rowStyle))
                {
                    t.ItemId = _itemResults[i].Id;
                    _itemSearchTarget = -1;
                    _itemResults.Clear();
                    Touch();
                    break;
                }
                y += 21f;
            }

            if (_itemQuery.Length > 0 && _itemResults.Count == 0)
            {
                // Distinguishes "nothing matched" from "the catalogue never
                // loaded", which previously looked identical.
                y += UiText.DrawDim(x0 + 10f, y, w - x0 - 20f, _noMatchesLabel);
            }

            return y + 4f;
        }

        private float KindButton(float x, float y, string text, TriggerKind kind,
                                 ZoneShape shape, ref Trigger t)
        {
            bool on = t.Kind == kind && (kind != TriggerKind.Zone || t.Shape == shape);

            if (GUI.Button(new Rect(x, y - 2f, 54f, 22f), text, on ? _selectedRowStyle : _rowStyle))
            {
                if (!on)
                {
                    t.Kind = kind;

                    if (kind == TriggerKind.Zone)
                    {
                        t.Shape = shape;

                        Vector3 p;
                        if (TryPlayerPosition(out p)) t.Position = p;

                        if (shape == ZoneShape.Sphere && t.Radius <= 0f) t.Radius = DefaultRadius;
                        if (shape == ZoneShape.Box && t.Extents.x <= 0f)
                            t.Extents = new Vector3(3f, 3f, 3f);
                    }

                    Touch();
                }
            }

            return x + 58f;
        }

        // ------------------------------------------------------------------
        private void CreateNew()
        {
            Segment s = NewFromHere("New spot");
            if (s == null) return;

            _library.Add(s);
            _selected = s;
            RebuildVisible();
            Touch();
            _status = "New entry - tick 'Timed segment' to make it a run.";
        }

        private void Duplicate()
        {
            Segment src = _selected;
            Segment s = new Segment();

            s.Id = NextFreeId(src.Id);
            s.Name = src.Name + " (copy)";
            s.Category = src.Category;
            s.Notes = src.Notes;
            s.HasSpawn = src.HasSpawn;
            s.SpawnPosition = src.SpawnPosition;
            s.SpawnYaw = src.SpawnYaw;
            s.SpawnPitch = src.SpawnPitch;
            s.StartRestoreWithLoad = src.StartRestoreWithLoad;
            s.Start = src.Start;
            s.End = src.End;
            s.Checkpoints.AddRange(src.Checkpoints);

            // Copies land in the user's own file, never back in a shared set.
            s.SourceFile = SegmentLibrary.UserFileName;

            _library.Add(s);
            _selected = s;
            RebuildVisible();
            Touch();
        }

        private void Delete()
        {
            string file = _selected.SourceFile;

            if (ReferenceEquals(_current, _selected)) _current = null;

            _library.Remove(_selected);
            _unsaved.Remove(_selected);
            _selected = null;

            // Written straight through: a delete that only existed in memory
            // would reappear on reload and look like a bug.
            _status = WriteFile(file) ? "Deleted." : "Deleted here, but writing the file failed - see log.";
        }

        /// Writes every unsaved entry. One that cannot be saved is selected
        /// and named, and nothing is written, so no file is half-saved.
        private void Save()
        {
            if (_unsaved.Count == 0) return;

            for (int i = 0; i < _unsaved.Count; i++)
            {
                Segment u = _unsaved[i];
                string why = !_library.IsIdAvailable(u.Id, u) ? "id '" + u.Id + "' is already used"
                           : !u.IsValid ? "needs an id, and a spawn or a start and end"
                           : null;
                if (why == null) continue;

                _selected = u;
                _status = "Cannot save '" + u.Name + "': " + why + ".";
                return;
            }

            List<string> files = new List<string>();
            for (int i = 0; i < _unsaved.Count; i++)
            {
                string f = string.IsNullOrEmpty(_unsaved[i].SourceFile) ? SegmentLibrary.UserFileName : _unsaved[i].SourceFile;
                bool seen = false;
                for (int j = 0; j < files.Count; j++)
                    if (string.Equals(files[j], f, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }
                if (!seen) files.Add(f);
            }

            int count = _unsaved.Count;
            bool ok = true;
            for (int i = 0; i < files.Count; i++)
                if (!WriteFile(files[i])) ok = false;

            string names = string.Join(", ", files.ToArray());
            if (ok)
            {
                _status = "Saved " + count + (count == 1 ? " entry" : " entries") + " to " + names;
                Ctx.Log.LogInfo("Practice: saved " + count + " unsaved entr" + (count == 1 ? "y" : "ies") + " to " + names + ".");
            }
            else _status = "Save failed - see log (" + _unsaved.Count + " still unsaved).";
        }

        private string NextFreeId(string basis)
        {
            if (_library.IsIdAvailable(basis, null)) return basis;

            for (int i = 2; i < 500; i++)
            {
                string candidate = basis + "-" + i;
                if (_library.IsIdAvailable(candidate, null)) return candidate;
            }
            return basis + "-" + UnityEngine.Random.Range(1000, 9999);
        }

        private static string Slug(string text)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);

                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }

            string slug = sb.ToString().Trim('-');
            return slug.Length == 0 ? "unnamed" : slug;
        }

        private void Touch() { Touch(_selected); }

        private void Touch(Segment s)
        {
            if (s == null) return;

            // Anything holding this segment - a run armed against its
            // start zone - can see that it changed underneath them.
            s.Revision++;

            if (_unsaved.Contains(s)) return;
            _unsaved.Add(s);
            RebuildLabels();
        }

        /// Switching never loses an edit, so it is never refused - it only
        /// says what was left unsaved, where the click was.
        private void Select(Segment entry)
        {
            Segment left = _selected;
            bool leftUnsaved = left != null && _unsaved.Contains(left);

            _selected = entry;
            _status = leftUnsaved
                ? "'" + left.Name + "' has unsaved changes - Save keeps them, Reload drops them."
                : "";

            // Selection was invisible in the log, so a stuck list could
            // not be told from a click that never arrived.
            Ctx.Log.LogInfo("Practice: selected '" + entry.Id + "'" +
                            (leftUnsaved ? ", '" + left.Id + "' left unsaved" : "") +
                            " (" + _unsaved.Count + " unsaved).");
        }

        /// Every write goes through here. SaveFile writes every entry of
        /// that file, unsaved edits included, so all of them are saved now.
        private bool WriteFile(string file)
        {
            if (string.IsNullOrEmpty(file)) file = SegmentLibrary.UserFileName;
            if (!_library.SaveFile(file)) return false;

            for (int i = _unsaved.Count - 1; i >= 0; i--)
                if (string.Equals(_unsaved[i].SourceFile, file, StringComparison.OrdinalIgnoreCase))
                    _unsaved.RemoveAt(i);

            RebuildVisible();
            return true;
        }

        // --- start state (a savestate restored on every restart) -----------
        private float DrawStartState(float y, float cw, Segment s)
        {
            if (_savestates == null) return y;
            if (!ReferenceEquals(_startStateFor, s) || _startStateForId != s.Id)
            {
                if (!ReferenceEquals(_startStateFor, s)) StartStatus("");
                RefreshStartStateLabel(s);
            }

            // Buttons on the label's row, the description on a full-width
            // line of its own: squeezed beside the buttons it wrapped into
            // two half-visible lines (author, v0.21.0).
            GUI.Label(new Rect(0, y, 74, 20), "Start state");

            GUI.enabled = !_savestates.Busy && s.Id.Length > 0;
            if (GUI.Button(new Rect(80, y - 2, 110, 22),
                           Time.unscaledTime <= _captureStartArmedUntil ? "Sure?" : "Capture here")) CaptureStartState(s);
            GUI.enabled = !_savestates.Busy && _savestates.HasStartState(s);
            if (GUI.Button(new Rect(196, y - 2, 70, 22),
                           Time.unscaledTime <= _deleteStartArmedUntil ? "Sure?" : "Delete")) DeleteStartState(s);
            GUI.enabled = !_savestates.Busy && _savestates.HasStartState(s) && s.HasSpawn;
            // Says which load it does (maks: the old toggle was unclear).
            if (GUI.Button(new Rect(272, y - 2, 110, 22),
                           s.StartRestoreWithLoad ? "Restart (Full)" : "Restart (Quick)")) Restart(s);
            GUI.enabled = true;
            y += 26f;

            y += UiText.DrawDim(80, y, cw - 90, _startStateLabel);

            // What the buttons above just did, right under them. Cleared on
            // selection change so it never describes another spot.
            y += UiText.Draw(80, y, cw - 90, _startStatusLabel);

            // A two-button switch: the active mode shows pressed at a glance.
            GUI.Label(new Rect(80, y, 70, 20), "Restore by");
            bool quick = GUI.Toggle(new Rect(152, y - 2, 100, 22), !s.StartRestoreWithLoad, "Quick load", GUI.skin.button);
            bool full = GUI.Toggle(new Rect(256, y - 2, 100, 22), s.StartRestoreWithLoad, "Full load", GUI.skin.button);
            if (quick && s.StartRestoreWithLoad) { s.StartRestoreWithLoad = false; Touch(); }
            else if (full && !s.StartRestoreWithLoad) { s.StartRestoreWithLoad = true; Touch(); }
            y += 26f;
            y += UiText.DrawDim(80, y, cw - 90, s.StartRestoreWithLoad ? FullLoadHint : QuickLoadHint);
            y += 6f;
            return y;
        }

        private void StartStatus(string text)
        {
            _startStatusLabel.text = text ?? "";
        }

        private void RefreshStartStateLabel(Segment s)
        {
            _startStateFor = s;
            _startStateForId = s.Id;
            _startStateLabel.text = _savestates != null ? _savestates.DescribeStartState(s) : "";
        }

        // The state includes where you stand, so the spawn moves here too -
        // a restart then restores and teleports to the same place.
        private void CaptureStartState(Segment s)
        {
            if (!_library.IsIdAvailable(s.Id, s)) { StartStatus("Pick a free id first - the start state is named after it."); return; }

            // A new start state is a new route: times recorded from the old
            // one are retired (author, 2026-09-23) - so say so first.
            if (Time.unscaledTime > _captureStartArmedUntil)
            {
                string warning = RetireWarning(s);
                if (warning != null)
                {
                    _captureStartArmedUntil = Time.unscaledTime + 3f;
                    StartStatus(warning + " Click Capture again within 3 s.");
                    return;
                }
            }
            _captureStartArmedUntil = 0f;

            SetSpawnHere(s);

            // F7 restarts the CURRENT spot. Capturing on the editor's entry
            // left another spot current, so F7 teleported there as if no
            // start state existed (author, v0.21.0). Capturing is choosing.
            _current = s;
            StartStatus("Capturing...");
            _savestates.CaptureStartState(s, delegate(string error)
            {
                RefreshStartStateLabel(s);
                if (error != null) { StartStatus("Not captured: " + error); return; }

                Touch(s);
                StartStatus("Captured - F7 now restarts '" + s.Name + "'. " + SaveStartStateChange(s));
            });
        }

        /// Null when nothing would be retired.
        private string RetireWarning(Segment s)
        {
            int n = _attempts.CountOnRoute(s.Id, s.RouteFingerprint());
            if (n == 0) return null;
            return "This retires " + n + " recorded time" + (n == 1 ? "" : "s") +
                   " for '" + s.Name + "' (kept on disk, left out of comparisons).";
        }

        // The .fosave is already written or gone, so the segment's note of
        // which state it expects is written straight through too - left
        // unsaved, a reload would pair the new state with the old times.
        private string SaveStartStateChange(Segment s)
        {
            if (!s.IsValid) return "Save the segment to keep it.";
            if (!WriteFile(s.SourceFile)) return "Saving the segment failed - see log.";
            return "Segment saved.";
        }

        private void DeleteStartState(Segment s)
        {
            if (Time.unscaledTime > _deleteStartArmedUntil)
            {
                _deleteStartArmedUntil = Time.unscaledTime + 3f;
                string warning = RetireWarning(s);
                StartStatus((warning != null ? warning + " " : "") + "Click Delete again within 3 s.");
                return;
            }

            _deleteStartArmedUntil = 0f;
            string error = _savestates.DeleteStartState(s);
            RefreshStartStateLabel(s);
            if (error != null) { StartStatus("Delete failed: " + error); return; }

            Touch(s);
            StartStatus("Deleted - restarts now keep the game as it is. " + SaveStartStateChange(s));
        }

        private void SetSpawnHere(Segment s)
        {
            Vector3 p;
            if (!TryPlayerPosition(out p)) { _status = "No player ref."; return; }

            s.SpawnPosition = p;
            s.SpawnYaw = Ctx.Player.Transform.eulerAngles.y;
            s.SpawnPitch = Ctx.Bridge.GetLookPitch();
            s.HasSpawn = true;
            Touch();
        }

        private Trigger ZoneHere()
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Shape = ZoneShape.Sphere;
            t.Radius = DefaultRadius;

            Vector3 p;
            if (TryPlayerPosition(out p)) t.Position = p;
            return t;
        }

        private bool TryPlayerPosition(out Vector3 p)
        {
            if (Ctx.Player.Found) { p = Ctx.Player.Transform.position; return true; }
            p = Vector3.zero;
            return false;
        }

        // --- labels ------------------------------------------------------
        // Numbers the editor shows, formatted only when they change: doing
        // it inline allocated on every OnGUI pass, several times a frame.
        // Keyed by trigger slot (-2 start, -3 end, >= 0 checkpoint, -4 the
        // spawn) and field.
        private const int SpawnSlot = -4;

        private sealed class NumLabel
        {
            public bool Set;
            public Vector3 Value;
            public readonly GUIContent Content = new GUIContent("");
        }

        private readonly Dictionary<int, NumLabel> _numLabels = new Dictionary<int, NumLabel>();
        private readonly Dictionary<int, string> _amountText = new Dictionary<int, string>();
        private readonly List<string> _checkNames = new List<string>();
        private readonly List<GUIContent> _itemResultLabels = new List<GUIContent>();
        private readonly GUIContent _noMatchesLabel = new GUIContent("");

        private NumLabel Num(int slot, int field, Vector3 value, out bool changed)
        {
            int key = (slot + 8) * 8 + field;
            NumLabel l;
            if (!_numLabels.TryGetValue(key, out l)) _numLabels[key] = l = new NumLabel();
            changed = !l.Set || l.Value != value;
            l.Set = true;
            l.Value = value;
            return l;
        }

        private GUIContent CoordsLabel(int slot, int field, Vector3 v)
        {
            bool changed;
            NumLabel l = Num(slot, field, v, out changed);
            if (changed) l.Content.text = Coords(v);
            return l.Content;
        }

        /// `name` must be the same text every call for a given slot/field.
        private GUIContent MetresLabel(int slot, int field, string name, float metres)
        {
            bool changed;
            NumLabel l = Num(slot, field, new Vector3(metres, 0f, 0f), out changed);
            if (changed) l.Content.text = name + " " + metres.ToString("F1") + "m";
            return l.Content;
        }

        // The item's name arrives once the catalogue loads, so it is part
        // of what is compared.
        private GUIContent ItemLabel(int slot, int itemId)
        {
            string name = Ctx.Inventory.NameForId(itemId);
            bool changed;
            NumLabel l = Num(slot, 5, new Vector3(itemId, name != null ? 1f : 0f, 0f), out changed);
            if (changed) l.Content.text = "item " + itemId + (name != null ? "  -  " + name : "");
            return l.Content;
        }

        private string AmountText(int slot, int amount)
        {
            string text;
            int parsed;
            if (_amountText.TryGetValue(slot, out text) &&
                (!int.TryParse(text, out parsed) || parsed == amount))
                return text;   // what was typed, even half-typed
            _amountText[slot] = text = amount.ToString();
            return text;
        }

        private string CheckName(int i)
        {
            while (_checkNames.Count <= i) _checkNames.Add("Check " + (_checkNames.Count + 1));
            return _checkNames[i];
        }

        private void RebuildItemResultLabels()
        {
            for (int i = 0; i < _itemResults.Count; i++)
            {
                string text = _itemResults[i].Name + "   (" + _itemResults[i].Id + ")";
                if (i < _itemResultLabels.Count) _itemResultLabels[i].text = text;
                else _itemResultLabels.Add(new GUIContent(text));
            }
            _noMatchesLabel.text = "no matches  (" + Ctx.Inventory.CatalogStatus + ")";
        }

        private static string Coords(Vector3 v)
        {
            return v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1");
        }

        private float Field(float y, float w, string label, ref string value)
        {
            GUI.Label(new Rect(0, y, 74, 20), label);

            string edited = GUI.TextField(new Rect(80, y - 2, w - 90, 22), value ?? "");
            if (edited != value) { value = edited; Touch(); }

            return y + 26f;
        }
    }
}
