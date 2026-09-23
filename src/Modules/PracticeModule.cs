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
    // and a bad edit cannot corrupt a shared file.
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
        private bool _dirty;
        private string _status = "";
        private string _filter = "";

        // --- current entry (what a practice attempt starts from) ----------
        private Segment _current;
        public bool HasSpot { get { return _current != null && _current.HasSpawn; } }
        public Vector3 SpotPosition { get { return _current != null ? _current.SpawnPosition : Vector3.zero; } }
        public string SpotLabel { get { return _current != null ? _current.Name : ""; } }
        public Segment CurrentSegment { get { return _current; } }

        /// Raised when the player is placed at the current entry, so a run
        /// can arm without this module knowing the timer exists.
        public Action OnPlacedAtSpot;

        private float _tabW;
        private float _tabH;
        private Vector2 _listScroll;
        private Vector2 _editScroll;

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
        private readonly Dictionary<string, bool> _collapsed = new Dictionary<string, bool>();
        private int _entryCount;

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
        private Segment _startStateFor;
        private string _startStateForId;
        private readonly GUIContent _startStateLabel = new GUIContent("");
        private float _deleteStartArmedUntil;

        // ------------------------------------------------------------------
        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _library = new SegmentLibrary(ctx.Log, ctx.ConfigDirectory);
            Reload();

            _savestates = Host.Find<SavestateModule>();

            _previewHost = new GameObject("ForestOverlay_ZonePreview");
            _previewHost.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_previewHost);
            _preview = _previewHost.AddComponent<ZonePreviewBehaviour>();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("practice.saveSpot", KeyCode.F6, "Save spot here", QuickSaveSpot);
            map.Add("practice.goToSpot", KeyCode.F7, "Return to current spot", ReturnToSpot);
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
            _dirty = false;

            _current = currentId != null ? _library.ById(currentId) : null;

            RebuildVisible();
        }

        public override void Tick()
        {
            UpdatePreview();
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
            IList<Segment> all = _library.All;

            // The library sorts by category then name, so a single pass
            // emits a header whenever the category changes.
            string current = null;
            bool collapsed = false;

            for (int i = 0; i < all.Count; i++)
            {
                Segment e = all[i];

                if (f != null &&
                    e.Name.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0 &&
                    e.Category.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0)
                    continue;

                if (e.Category != current)
                {
                    current = e.Category;
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
                    text = (_rows[i].Entry.IsTimed ? "  * " : "     ") + _rows[i].Entry.Name;
                }

                if (i < _rowLabels.Count) _rowLabels[i].text = text;
                else _rowLabels.Add(new GUIContent(text));
            }
        }

        private int CountIn(string category)
        {
            IList<Segment> all = _library.All;
            int n = 0;

            for (int i = 0; i < all.Count; i++)
                if (all[i].Category == category) n++;

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
        private void GoTo(Segment s)
        {
            if (s == null || !s.HasSpawn) { _status = "That entry has no spawn point."; return; }

            if (_savestates != null && _savestates.HasStartState(s))
            {
                if (_savestates.Busy) { _status = "A savestate action is still running."; return; }

                _current = s;
                _status = "Restoring the start state of '" + s.Name + "'" + (s.StartRestoreWithLoad ? " (load)..." : "...");
                Ctx.Log.LogInfo("Restart '" + s.Id + "': restoring its start state " +
                                (s.StartRestoreWithLoad ? "with a load." : "in place."));
                _savestates.RestoreStartState(s, delegate(string error)
                {
                    if (error != null)
                    {
                        Ctx.Log.LogWarning("Start state of '" + s.Id + "' not restored: " + error);
                        _status = "Start state not restored: " + error;
                    }
                    PlaceAt(s);
                });
                return;
            }

            if (_savestates != null) Ctx.Log.LogInfo("Restart '" + s.Id + "': no start state - teleport only.");
            PlaceAt(s);
        }

        private void PlaceAt(Segment s)
        {
            Quaternion rot = Quaternion.Euler(0f, s.SpawnYaw, 0f);

            // Before moving, as the game's own Goto does: a spot inside a
            // cave needs the cave state (no terrain collision, cave
            // lighting), or you arrive under the terrain in the dark.
            string cave = Ctx.Player.Found ? Ctx.Bridge.SyncCaveState(s.SpawnPosition) : "";

            if (!Ctx.Player.MoveTo(s.SpawnPosition, rot)) { _status = "No player ref."; return; }

            Ctx.Bridge.ApplyLook(Ctx.Player.Transform, s.SpawnYaw, s.SpawnPitch);
            Ctx.Practice.Mark("teleport: " + s.Name);

            _current = s;
            _status = "-> " + s.Name + (cave.Length > 0 ? " (" + cave + ")" : "");

            // Logged too: the status line is easy to miss, and the log is
            // what a runner sends when a cave teleport misbehaves.
            if (cave.Length > 0) Ctx.Log.LogInfo("Teleport to '" + s.Name + "': " + cave + ".");

            if (OnPlacedAtSpot != null) OnPlacedAtSpot();
        }

        public void ReturnToSpot()
        {
            if (_current == null) { _status = "No entry selected."; return; }
            GoTo(_current);
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
            if (_library.SaveFile(s.SourceFile)) _status = "Saved '" + s.Name + "'";
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

            GUI.enabled = _dirty;
            if (GUI.Button(new Rect(w - 160, 0, 70, 24), "Save")) Save();
            GUI.enabled = true;
            if (GUI.Button(new Rect(w - 86, 0, 86, 24), "Reload")) Reload();

            // --- filter ----------------------------------------------------
            GUI.Label(new Rect(0, 30, 36, 20), "Find");
            string filter = GUI.TextField(new Rect(38, 28, ListWidth - 80, 22), _filter);
            if (filter != _filter) { _filter = filter; RebuildVisible(); }
            if (GUI.Button(new Rect(ListWidth - 38, 28, 38, 22), "x")) { _filter = ""; RebuildVisible(); }

            bool preview = GUI.Toggle(new Rect(ListWidth + 14, 30, 120, 20), _showPreview, " show zones");
            if (preview != _showPreview) _showPreview = preview;

            GUI.Label(new Rect(ListWidth + 140, 30, w - ListWidth - 140, 20), _status, _dimStyle);

            DrawList(new Rect(0, 56, ListWidth, _tabH - 60));
            DrawEditor(new Rect(ListWidth + 14, 56, w - ListWidth - 14, _tabH - 60));
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

                if (GUI.Button(r, _rowLabels[i], isSelected ? _selectedRowStyle : _rowStyle))
                {
                    if (_dirty) _status = "Unsaved changes - Save or Reload first.";
                    else { _selected = entry; _status = ""; }
                }

                GUI.enabled = entry.HasSpawn;
                if (GUI.Button(new Rect(content.width - 48f, rowY, 44f, RowHeight - 2f), "Go"))
                    GoTo(entry);
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

            float height = 338f;
            if (s.IsTimed || s.Start.IsSet || s.End.IsSet)
                height = 698f + s.Checkpoints.Count * 110f;

            Rect content = new Rect(0, 0, w - 20f, height);
            _editScroll = GUI.BeginScrollView(area, _editScroll, content);

            float y = 0f;
            float cw = content.width;

            y = Field(y, cw, "Name", ref s.Name);
            y = Field(y, cw, "Category", ref s.Category);
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
            GUI.Label(new Rect(80, y, cw - 220, 20),
                      s.HasSpawn ? Coords(s.SpawnPosition) : "(none - cannot teleport here)");

            if (GUI.Button(new Rect(cw - 136, y - 2, 56, 22), "Here")) SetSpawnHere(s);

            GUI.enabled = s.HasSpawn;
            if (GUI.Button(new Rect(cw - 76, y - 2, 42, 22), "Go")) GoTo(s);
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
                    y = DrawTrigger(y, cw, "Check " + (i + 1), ref t, i);
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
                        GUI.Label(new Rect(x0, y, w - x0 - 70f, 20), Coords(t.Position));

                        if (GUI.Button(new Rect(w - 64f, y - 2f, 58f, 22f), "Here"))
                        {
                            Vector3 p;
                            if (TryPlayerPosition(out p)) { t.Position = p; Touch(); }
                        }
                        y += 24f;

                        if (t.Shape == ZoneShape.Box) y = BoxFields(y, w, x0, ref t);
                        else y = SphereFields(y, w, x0, ref t);
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

        private float SphereFields(float y, float w, float x0, ref Trigger t)
        {
            GUI.Label(new Rect(x0, y, 110f, 20), "radius " + t.Radius.ToString("F1") + "m");

            float sliderX = x0 + 114f;
            float r = GUI.HorizontalSlider(new Rect(sliderX, y + 6f, w - sliderX - 6f, 18f),
                                           t.Radius <= 0f ? DefaultRadius : t.Radius, 0.5f, 25f);
            if (!Mathf.Approximately(r, t.Radius)) { t.Radius = r; Touch(); }

            return y + 26f;
        }

        private float BoxFields(float y, float w, float x0, ref Trigger t)
        {
            Vector3 e = t.Extents;
            if (e.x <= 0f && e.y <= 0f && e.z <= 0f) e = new Vector3(3f, 3f, 3f);

            // Shown as full size because "width 6m" is what a player can
            // pace out; extents are the half-size the maths wants.
            e.x = ExtentSlider(y, w, x0, "width", e.x);
            y += 24f;
            e.y = ExtentSlider(y, w, x0, "height", e.y);
            y += 24f;
            e.z = ExtentSlider(y, w, x0, "depth", e.z);
            y += 26f;

            if (e != t.Extents) { t.Extents = e; Touch(); }
            return y;
        }

        private float ExtentSlider(float y, float w, float x0, string label, float value)
        {
            GUI.Label(new Rect(x0, y, 110f, 20), label + " " + (value * 2f).ToString("F1") + "m");

            float sliderX = x0 + 114f;
            return GUI.HorizontalSlider(new Rect(sliderX, y + 6f, w - sliderX - 6f, 18f),
                                        value, 0.5f, 30f);
        }

        // Item triggers search by NAME, because nobody knows item ids.
        private float ItemFields(float y, float w, float x0, ref Trigger t, int slot)
        {
            string current = Ctx.Inventory.NameForId(t.ItemId);

            GUI.Label(new Rect(x0, y, w - x0 - 6f, 20),
                      "item " + t.ItemId + (current != null ? "  -  " + current : ""));
            y += 24f;

            bool searching = _itemSearchTarget == slot;

            if (GUI.Button(new Rect(x0, y - 2f, 70f, 22f), searching ? "close" : "find"))
            {
                _itemSearchTarget = searching ? -1 : slot;
                _itemQuery = "";
                _itemResults.Clear();
            }

            if (GUI.Button(new Rect(x0 + 76f, y - 2f, 44f, 22f), Trigger.OpText(t.Compare)))
            {
                t.Compare = (Comparison)(((int)t.Compare + 1) % 3);
                Touch();
            }

            string amtText = GUI.TextField(new Rect(x0 + 126f, y - 2f, 50f, 22f), t.Amount.ToString());
            int parsedAmt;
            if (int.TryParse(amtText, out parsedAmt) && parsedAmt != t.Amount) { t.Amount = parsedAmt; Touch(); }

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
            }
            y += 26f;

            for (int i = 0; i < _itemResults.Count; i++)
            {
                if (GUI.Button(new Rect(x0 + 10f, y, w - x0 - 20f, 20f),
                               _itemResults[i].Name + "   (" + _itemResults[i].Id + ")", _rowStyle))
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
                GUI.Label(new Rect(x0 + 10f, y, w - x0 - 20f, 20f),
                          "no matches  (" + Ctx.Inventory.CatalogStatus + ")", _dimStyle);
                y += 21f;
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
            _selected = null;
            RebuildVisible();

            // Written straight through: a delete that only existed in memory
            // would reappear on reload and look like a bug.
            _library.SaveFile(file);
            _dirty = false;
            _status = "Deleted.";
        }

        private void Save()
        {
            if (_selected != null && !_library.IsIdAvailable(_selected.Id, _selected))
            {
                _status = "Cannot save: id '" + _selected.Id + "' is already used.";
                return;
            }

            if (_selected != null && !_selected.IsValid)
            {
                _status = "Cannot save: needs an id, and a spawn or a start and end.";
                return;
            }

            string file = _selected != null ? _selected.SourceFile : SegmentLibrary.UserFileName;

            if (_library.SaveFile(file))
            {
                _dirty = false;
                RebuildVisible();
                _status = "Saved to " + file;
            }
            else _status = "Save failed - see log.";
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

        private void Touch()
        {
            _dirty = true;

            // Anything holding this segment - a run armed against its
            // start zone - can see that it changed underneath them.
            if (_selected != null) _selected.Revision++;
        }

        // --- start state (a savestate restored on every restart) -----------
        private float DrawStartState(float y, float cw, Segment s)
        {
            if (_savestates == null) return y;
            if (!ReferenceEquals(_startStateFor, s) || _startStateForId != s.Id) RefreshStartStateLabel(s);

            // Buttons on the label's row, the description on a full-width
            // line of its own: squeezed beside the buttons it wrapped into
            // two half-visible lines (author, v0.21.0).
            GUI.Label(new Rect(0, y, 74, 20), "Start state");

            GUI.enabled = !_savestates.Busy && s.Id.Length > 0;
            if (GUI.Button(new Rect(80, y - 2, 110, 22), "Capture here")) CaptureStartState(s);
            GUI.enabled = !_savestates.Busy && _savestates.HasStartState(s);
            if (GUI.Button(new Rect(196, y - 2, 70, 22),
                           Time.unscaledTime <= _deleteStartArmedUntil ? "Sure?" : "Delete")) DeleteStartState(s);
            GUI.enabled = true;
            y += 26f;

            GUI.Label(new Rect(80, y, cw - 90, 20), _startStateLabel, _dimStyle);
            y += 22f;

            bool load = GUI.Toggle(new Rect(80, y, cw - 90, 20), s.StartRestoreWithLoad,
                                   " Restore with a load (slower, the game's full reset)");
            if (load != s.StartRestoreWithLoad) { s.StartRestoreWithLoad = load; Touch(); }
            y += 28f;
            return y;
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
            if (!_library.IsIdAvailable(s.Id, s)) { _status = "Pick a free id first - the start state is named after it."; return; }

            SetSpawnHere(s);

            // F7 restarts the CURRENT spot. Capturing on the editor's entry
            // left another spot current, so F7 teleported there as if no
            // start state existed (author, v0.21.0). Capturing is choosing.
            _current = s;
            _status = "Capturing the start state...";
            _savestates.CaptureStartState(s, delegate(string error)
            {
                RefreshStartStateLabel(s);
                _status = error == null
                    ? "Start state captured - F7 now restarts '" + s.Name + "'. Spawn moved here - Save to keep it."
                    : "Start state not captured: " + error;
            });
        }

        private void DeleteStartState(Segment s)
        {
            if (Time.unscaledTime > _deleteStartArmedUntil)
            {
                _deleteStartArmedUntil = Time.unscaledTime + 3f;
                _status = "Click Delete again within 3 s to delete the start state.";
                return;
            }

            _deleteStartArmedUntil = 0f;
            string error = _savestates.DeleteStartState(s);
            RefreshStartStateLabel(s);
            _status = error == null ? "Start state deleted - restarts now keep the game as it is." : "Delete failed: " + error;
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
