using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // In-game editor for practice segments.
    //
    // WHY THIS EXISTS
    // The segment format is text so that route sets can be shared and
    // diffed - not so that runners have to edit config files. Until this
    // panel existed the feature was untestable without a text editor,
    // which is not an acceptable interface for the people it is for.
    //
    // Everything is set from where you are STANDING: "here" buttons
    // capture the player's position rather than asking anyone to type
    // coordinates. Typing a zone centre by hand is the thing this is
    // meant to avoid.
    //
    // Edits live in memory until Save, so abandoning a half-made segment
    // costs nothing and a bad edit cannot corrupt a shared file.
    // ------------------------------------------------------------------
    public sealed class SegmentEditorModule : OverlayModule
    {
        private const float RowHeight = 22f;
        private const float DefaultRadius = 3f;

        public override string Id { get { return "segmenteditor"; } }
        public override string DisplayName { get { return "Segment editor"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Segments"; } }
        public override int TabOrder { get { return 20; } }
        public override bool IsPracticeOnly { get { return true; } }

        private SegmentLibrary _library;
        private Segment _selected;
        private bool _dirty;
        private string _status = "";

        private Rect _windowRect;
        private bool _windowPlaced;
        private Vector2 _listScroll;
        private Vector2 _editScroll;

        private GUIStyle _rowStyle;
        private GUIStyle _selectedRowStyle;
        private GUIStyle _headerStyle;

        // Label cache: Segment is pure data and carries no GUIContent, so
        // the panel that draws it owns the cache. Rebuilt only when the
        // library reloads.
        private readonly List<GUIContent> _rowLabels = new List<GUIContent>();

        public SegmentLibrary Library { get { return _library; } }

        // Live preview of the zones being edited, drawn in the world.
        private GameObject _previewHost;
        private ZonePreviewBehaviour _preview;
        private bool _showPreview = true;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _library = new SegmentLibrary(ctx.Log, ctx.ConfigDirectory);
            Reload();

            _previewHost = new GameObject("ForestOverlay_ZonePreview");
            _previewHost.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_previewHost);
            _preview = _previewHost.AddComponent<ZonePreviewBehaviour>();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.segments", KeyCode.None, "Open Segments tab", OpenMyTab);
        }

        private void Reload()
        {
            _library.Reload();
            _selected = null;
            _dirty = false;
            RebuildLabels();
        }

        private void RebuildLabels()
        {
            IList<Segment> all = _library.All;

            for (int i = 0; i < all.Count; i++)
            {
                string text = all[i].Name + "   [" + all[i].Id + "]";
                if (i < _rowLabels.Count) _rowLabels[i].text = text;
                else _rowLabels.Add(new GUIContent(text));
            }
        }

        // ------------------------------------------------------------------
        private float _tabW;
        private float _tabH;

        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;
            DrawContents(0);
        }

        private readonly GUIContent _title = new GUIContent("Segment editor");

        public override void Tick()
        {
            _title.text = "Segment editor  -  " + _library.Status + (_dirty ? "   *unsaved*" : "");
            UpdatePreview();
        }

        // Rebuilt every tick rather than only on edit: the values change
        // while a slider is dragged, and a stale preview would be worse
        // than none. It is a handful of spheres, so the cost is nothing.
        private void UpdatePreview()
        {
            if (_preview == null) return;

            if (!_showPreview || _selected == null)
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
            // Only zones have a position; item and event triggers have
            // nothing meaningful to draw.
            if (t.Kind != TriggerKind.Zone || t.Radius <= 0f) return n;

            PreviewZone z;
            z.Center = t.Position;
            z.Radius = t.Radius;
            z.Kind = kind;

            _preview.Zones[n] = z;
            return n + 1;
        }

        public override void Shutdown()
        {
            if (_previewHost != null) Object.Destroy(_previewHost);
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null) return;

            _rowStyle = new GUIStyle(GUI.skin.button);
            _rowStyle.alignment = TextAnchor.MiddleLeft;
            _rowStyle.padding = new RectOffset(8, 4, 0, 0);

            _selectedRowStyle = new GUIStyle(_rowStyle);
            _selectedRowStyle.fontStyle = FontStyle.Bold;

            _headerStyle = new GUIStyle(GUI.skin.box);
            _headerStyle.alignment = TextAnchor.MiddleLeft;
            _headerStyle.padding = new RectOffset(8, 4, 0, 0);
            _headerStyle.fontStyle = FontStyle.Bold;
        }

        private void DrawContents(int id)
        {
            EnsureStyles();

            float w = _tabW;
            float h = _tabH;
            const float listW = 250f;

            // --- toolbar ---------------------------------------------------
            if (GUI.Button(new Rect(10, 26, 90, 24), "New")) CreateNew();

            GUI.enabled = _selected != null;
            if (GUI.Button(new Rect(106, 26, 90, 24), "Duplicate")) Duplicate();
            if (GUI.Button(new Rect(202, 26, 80, 24), "Delete")) Delete();
            GUI.enabled = true;

            GUI.enabled = _dirty;
            if (GUI.Button(new Rect(w - 200, 26, 90, 24), "Save")) Save();
            GUI.enabled = true;

            if (GUI.Button(new Rect(w - 104, 26, 94, 24), "Reload")) Reload();

            bool preview = GUI.Toggle(new Rect(292, 28, 130, 20), _showPreview, " show zones");
            if (preview != _showPreview) _showPreview = preview;

            GUI.Label(new Rect(10, 54, w - 20, 20), _status);

            DrawList(new Rect(10, 78, listW, h - 90));
            DrawEditor(new Rect(listW + 20, 78, w - listW - 30, h - 90));

        }

        // ------------------------------------------------------------------
        private void DrawList(Rect area)
        {
            GUI.Box(area, GUIContent.none);

            IList<Segment> all = _library.All;
            Rect content = new Rect(0, 0, area.width - 20f, all.Count * RowHeight + 4f);

            _listScroll = GUI.BeginScrollView(area, _listScroll, content);

            for (int i = 0; i < all.Count; i++)
            {
                if (i >= _rowLabels.Count) break;

                Rect r = new Rect(2f, 2f + i * RowHeight, content.width - 4f, RowHeight - 2f);
                if (r.yMax < _listScroll.y || r.y > _listScroll.y + area.height) continue;

                bool isSelected = ReferenceEquals(all[i], _selected);

                if (GUI.Button(r, _rowLabels[i], isSelected ? _selectedRowStyle : _rowStyle))
                {
                    if (_dirty) _status = "Unsaved changes - Save or Reload first.";
                    else { _selected = all[i]; _status = ""; }
                }
            }

            GUI.EndScrollView();

            if (all.Count == 0)
            {
                GUI.Label(new Rect(area.x + 10, area.y + 10, area.width - 20, 60),
                          "No segments yet.\n\nPress New to make one\nfrom where you stand.");
            }
        }

        // ------------------------------------------------------------------
        private void DrawEditor(Rect area)
        {
            if (_selected == null)
            {
                GUI.Label(new Rect(area.x, area.y + 8, area.width, 40),
                          "Select a segment, or press New.");
                return;
            }

            Segment s = _selected;
            float w = area.width;

            Rect content = new Rect(0, 0, w - 20f, 470f + s.Checkpoints.Count * 78f);
            _editScroll = GUI.BeginScrollView(area, _editScroll, content);

            float y = 0f;

            y = Field(0, y, w, "Id", ref s.Id);

            // The id is the comparison key: renaming one orphans every time
            // recorded against it, so a clash has to be visible immediately.
            if (!_library.IsIdAvailable(s.Id, s))
            {
                GUI.Label(new Rect(90, y, w - 100, 18), "id already used - pick another");
                y += 18f;
            }

            y = Field(0, y, w, "Name", ref s.Name);
            y = Field(0, y, w, "Category", ref s.Category);
            y = Field(0, y, w, "Notes", ref s.Notes);

            y += 6f;

            // --- spawn -----------------------------------------------------
            GUI.Label(new Rect(0, y, 80, 20), "Spawn");
            GUI.Label(new Rect(90, y, w - 210, 20),
                      s.HasSpawn ? Coords(s.SpawnPosition) : "(none - cannot teleport to this segment)");

            if (GUI.Button(new Rect(w - 116, y - 2, 56, 22), "Here")) SetSpawnHere(s);
            GUI.enabled = s.HasSpawn;
            if (GUI.Button(new Rect(w - 56, y - 2, 46, 22), "Clr")) { s.HasSpawn = false; Touch(); }
            GUI.enabled = true;
            y += 26f;

            y += 6f;

            // --- triggers --------------------------------------------------
            y = DrawTrigger(y, w, "Start", ref s.Start);

            for (int i = 0; i < s.Checkpoints.Count; i++)
            {
                Trigger t = s.Checkpoints[i];
                float before = y;
                y = DrawTrigger(y, w, "Check " + (i + 1), ref t);
                s.Checkpoints[i] = t;

                if (GUI.Button(new Rect(w - 30f, before - 2f, 24f, 22f), "x"))
                {
                    s.Checkpoints.RemoveAt(i);
                    Touch();
                    break;
                }
            }

            if (GUI.Button(new Rect(0, y, 150, 22), "Add checkpoint here"))
            {
                s.Checkpoints.Add(ZoneHere());
                Touch();
            }
            y += 28f;

            y = DrawTrigger(y, w, "End", ref s.End);

            GUI.EndScrollView();
        }

        // One trigger: kind buttons, then the fields that kind needs.
        //
        // Laid out VERTICALLY rather than packed across one row. The first
        // version put the radius label and slider at fixed x offsets that
        // ran off the edge of a narrow pane, so half the controls were
        // invisible. Flowing downward cannot clip horizontally however
        // narrow the window gets.
        private float DrawTrigger(float y, float w, string label, ref Trigger t)
        {
            float labelW = 74f;
            float x0 = labelW + 6f;

            GUI.Label(new Rect(0, y, labelW, 20), label);

            float x = x0;
            x = KindButton(x, y, "zone", TriggerKind.Zone, ref t);
            x = KindButton(x, y, "item", TriggerKind.Item, ref t);
            x = KindButton(x, y, "event", TriggerKind.Event, ref t);
            x = KindButton(x, y, "manual", TriggerKind.Manual, ref t);

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

                        GUI.Label(new Rect(x0, y, 110f, 20), "radius " + t.Radius.ToString("F1") + "m");

                        float sliderX = x0 + 114f;
                        float r = GUI.HorizontalSlider(new Rect(sliderX, y + 6f, w - sliderX - 6f, 18f),
                                                       t.Radius <= 0f ? DefaultRadius : t.Radius,
                                                       0.5f, 25f);
                        if (!Mathf.Approximately(r, t.Radius)) { t.Radius = r; Touch(); }
                        y += 26f;
                        break;
                    }

                case TriggerKind.Item:
                    {
                        GUI.Label(new Rect(x0, y, 20f, 20), "id");

                        string idText = GUI.TextField(new Rect(x0 + 22f, y - 2f, 56f, 22f),
                                                      t.ItemId.ToString());
                        int parsedId;
                        if (int.TryParse(idText, out parsedId) && parsedId != t.ItemId)
                        {
                            t.ItemId = parsedId;
                            Touch();
                        }

                        if (GUI.Button(new Rect(x0 + 84f, y - 2f, 44f, 22f), Trigger.OpText(t.Compare)))
                        {
                            t.Compare = (Comparison)(((int)t.Compare + 1) % 3);
                            Touch();
                        }

                        string amtText = GUI.TextField(new Rect(x0 + 134f, y - 2f, 56f, 22f),
                                                       t.Amount.ToString());
                        int parsedAmt;
                        if (int.TryParse(amtText, out parsedAmt) && parsedAmt != t.Amount)
                        {
                            t.Amount = parsedAmt;
                            Touch();
                        }
                        y += 24f;

                        GUI.Label(new Rect(x0, y, w - x0 - 6f, 20),
                                  "fires when the held count crosses this");
                        y += 22f;
                        break;
                    }

                case TriggerKind.Event:
                    {
                        string name = GUI.TextField(new Rect(x0, y - 2f, w - x0 - 6f, 22f),
                                                    t.EventName ?? "");
                        if (name != t.EventName) { t.EventName = name; Touch(); }
                        y += 24f;

                        GUI.Label(new Rect(x0, y, w - x0 - 6f, 20), "a named game event");
                        y += 22f;
                        break;
                    }

                case TriggerKind.Manual:
                    GUI.Label(new Rect(x0, y, w - x0 - 6f, 20), "only fires on the hotkey");
                    y += 22f;
                    break;

                default:
                    GUI.Label(new Rect(x0, y, w - x0 - 6f, 20), "not set - pick a kind above");
                    y += 22f;
                    break;
            }

            return y + 6f;
        }

        private float KindButton(float x, float y, string text, TriggerKind kind, ref Trigger t)
        {
            bool on = t.Kind == kind;

            if (GUI.Button(new Rect(x, y - 2, 58, 22), text, on ? _selectedRowStyle : _rowStyle))
            {
                if (t.Kind != kind)
                {
                    t.Kind = kind;

                    // A freshly picked zone defaults to where you stand, so
                    // it is immediately meaningful rather than at the origin.
                    if (kind == TriggerKind.Zone)
                    {
                        Vector3 p;
                        if (TryPlayerPosition(out p)) t.Position = p;
                        if (t.Radius <= 0f) t.Radius = DefaultRadius;
                    }

                    Touch();
                }
            }

            return x + 62f;
        }

        // ------------------------------------------------------------------
        private void CreateNew()
        {
            Segment s = new Segment();
            s.Category = "My segments";
            s.Name = "New segment";
            s.Id = NextFreeId("my/segment");
            s.SourceFile = SegmentLibrary.UserFileName;

            Vector3 p;
            if (TryPlayerPosition(out p))
            {
                s.SpawnPosition = p;
                s.SpawnYaw = Ctx.Player.Transform.eulerAngles.y;
                s.SpawnPitch = Ctx.Bridge.GetLookPitch();
                s.HasSpawn = true;

                s.Start = ZoneHere();
                s.End = ZoneHere();
            }

            _library.Add(s);
            _selected = s;
            RebuildLabels();
            Touch();
            _status = "New segment - set the end zone where the run should finish.";
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
            s.Start = src.Start;
            s.End = src.End;
            s.Checkpoints.AddRange(src.Checkpoints);

            // Copies always land in the user's own file, never back into a
            // contributed set.
            s.SourceFile = SegmentLibrary.UserFileName;

            _library.Add(s);
            _selected = s;
            RebuildLabels();
            Touch();
        }

        private void Delete()
        {
            string file = _selected.SourceFile;

            _library.Remove(_selected);
            _selected = null;
            RebuildLabels();

            // Written straight through: a delete that only existed in
            // memory would reappear on the next reload and look like a bug.
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
                _status = "Cannot save: needs an id, a start and an end.";
                return;
            }

            string file = _selected != null ? _selected.SourceFile : SegmentLibrary.UserFileName;

            if (_library.SaveFile(file))
            {
                _dirty = false;
                RebuildLabels();
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
            return basis + "-" + Random.Range(1000, 9999);
        }

        private void Touch()
        {
            _dirty = true;
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

        private float Field(float x, float y, float w, string label, ref string value)
        {
            GUI.Label(new Rect(x, y, 80, 20), label);

            string edited = GUI.TextField(new Rect(x + 86, y - 2, w - 100, 22), value ?? "");
            if (edited != value) { value = edited; Touch(); }

            return y + 26f;
        }
    }
}
