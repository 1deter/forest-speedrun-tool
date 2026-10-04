using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The Map tab: the island from above with every Practice spot on it
    // (author's backlog: "an in-game 3D map of saved spots and routes
    // (caves too)" - this is the 2D half; 3D is for later).
    //
    //   - the picture: a shaded relief made once from the live terrain
    //     and cached (Game/MapRelief), no hitch;
    //   - spots: a dot per entry with a spawn, coloured by category;
    //     hollow = a community spot, a triangle = underground (a cave set
    //     on the spot, or well below the terrain there); "Only cave spots";
    //   - the selected entry's zones (start / checkpoints / end: spheres,
    //     turned boxes, polygons), the timed segment's too, or every
    //     segment's with "Every segment's zones";
    //   - the comparison run's line and its ghost (Runs tab's Compare to),
    //     and the player with their facing;
    //   - wheel / buttons zoom at the pointer, drag pans, hover names a
    //     spot, a click selects it in Practice; Go is Practice's Go (run
    //     mode refuses it the same way).
    //
    // Allocation-free per frame: spot positions, colours and texts are
    // cached when the library changes (checked twice a second while the
    // tab shows); the overlay is one GL triangle batch in Repaint, clipped
    // by hand (GL ignores the window's clip) - Data/MapGeometry.
    // ------------------------------------------------------------------
    public sealed class MapModule : OverlayModule
    {
        public override string Id { get { return "map"; } }
        public override string DisplayName { get { return "Map"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Map"; } }
        public override int TabOrder { get { return 15; } }
        // A view: its Go is Practice's (which marks practice itself).

        private const float PickRadius = 9f;
        private const float DotSize = 4f;
        private const int LineCapacity = 3000;
        private const int MaxLegend = 8;

        private static readonly Color[] Palette =
        {
            new Color(0.30f, 0.65f, 1.00f), new Color(1.00f, 0.55f, 0.20f),
            new Color(0.55f, 0.90f, 0.35f), new Color(0.95f, 0.35f, 0.75f),
            new Color(0.25f, 0.90f, 0.85f), new Color(1.00f, 0.88f, 0.25f),
            new Color(0.70f, 0.50f, 1.00f), new Color(1.00f, 0.40f, 0.40f),
        };
        private static readonly Color StartColour = new Color(0.38f, 0.85f, 0.45f, 0.95f);
        private static readonly Color CheckColour = new Color(0.98f, 0.75f, 0.26f, 0.95f);
        private static readonly Color EndColour = new Color(0.95f, 0.36f, 0.36f, 0.95f);
        private static readonly Color LineColour = new Color(0.35f, 0.95f, 1.00f, 0.9f);
        private static readonly Color GhostColour = new Color(0.85f, 0.45f, 1.00f, 1f);
        private static readonly Color Outline = new Color(0.05f, 0.05f, 0.05f, 0.9f);
        private static readonly Color SelectRing = new Color(1f, 1f, 1f, 1f);
        private static readonly Color MapBackground = new Color(0.07f, 0.16f, 0.24f, 1f);

        private MapView _view;
        private MapRelief _relief;
        private PracticeModule _practice;
        private PracticeRunModule _runs;
        private Material _material;
        private Texture2D _white;

        private ConfigEntry<bool> _onlyCavesCfg, _allZonesCfg, _lineCfg;
        private bool _onlyCaves, _allZones, _showLine;

        // --- spot cache (rebuilt when the library changes) ---------------
        private Segment[] _spots = new Segment[0];
        private float[] _sx = new float[0], _sz = new float[0];
        private int[] _colour = new int[0];
        private bool[] _community = new bool[0], _under = new bool[0];
        private int[] _revision = new int[0];
        private Vector3[] _spawn = new Vector3[0];
        private int _underCount;
        private bool _underNeedsTerrain;
        private float _nextCheck;

        // Where each drawn spot landed in the last Repaint (picking).
        private float[] _vx = new float[0], _vy = new float[0];
        private int[] _vIndex = new int[0];
        private int _drawnCount;
        private int _hover = -1;

        // --- comparison line ----------------------------------------------
        private readonly float[] _lineX = new float[LineCapacity], _lineZ = new float[LineCapacity];
        private int _lineCount;
        private Attempt _lineFor;
        private int _lineForSamples;

        // --- interaction -----------------------------------------------------
        private bool _dragging, _dragMoved;
        private Vector2 _dragFrom;
        private bool _fitted;

        // --- texts (built in Tick on change, never in DrawTab) -------------
        private readonly GUIContent _hoverLabel = new GUIContent("");
        private readonly GUIContent _selectedInfo = new GUIContent("");
        private readonly GUIContent _status = new GUIContent("");
        private readonly GUIContent _goStatus = new GUIContent("");
        private readonly GUIContent _shapes = new GUIContent(
            "Square = a spot, hollow = community, triangle = underground. Zones: green start, yellow checkpoints, red end. " +
            "Cyan = the comparison run, purple = its ghost, white arrow = you. Wheel / + - zoom, drag to pan, click a spot to select it.");
        private readonly GUIContent _onlyCavesLabel = new GUIContent("Only cave spots");
        private readonly GUIContent _allZonesLabel = new GUIContent("Every segment's zones");
        private readonly GUIContent _lineLabel = new GUIContent("Comparison line + ghost");
        private GUIContent[] _legend = new GUIContent[0];
        private int[] _legendColour = new int[0];
        private Segment _selectedFor;
        private int _selectedRevision = -1;
        private int _statusVersion = -1;
        private int _statusSpots = -1, _statusUnder = -1;
        private float _statusProgressAt = -1f;
        private GUIStyle _labelBox;

        // GL scratch (no allocation in Repaint).
        private readonly float[] _cx = new float[4], _cz = new float[4];
        private float _ox, _oy, _screenH;
        private float _clipW, _clipH;
        private static readonly float[] CircleCos = BuildCircle(true), CircleSin = BuildCircle(false);

        private static float[] BuildCircle(bool cos)
        {
            float[] a = new float[33];
            for (int i = 0; i <= 32; i++)
            {
                double t = i * Math.PI * 2.0 / 32.0;
                a[i] = (float)(cos ? Math.Cos(t) : Math.Sin(t));
            }
            return a;
        }

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _view = new MapView();
            _relief = new MapRelief(ctx.Log, ctx.ConfigDirectory);
            _view.SetBounds(_relief.MinX, _relief.MinZ, _relief.SizeX, _relief.SizeZ);
            _practice = Host.Find<PracticeModule>();
            _runs = Host.Find<PracticeRunModule>();

            _onlyCavesCfg = ctx.Config.Bind("Map", "OnlyCaveSpots", false, "Map tab: show only underground spots.");
            _allZonesCfg = ctx.Config.Bind("Map", "EverySegmentsZones", false, "Map tab: draw every timed segment's zones, not only the selected one's.");
            _lineCfg = ctx.Config.Bind("Map", "ComparisonLine", true, "Map tab: draw the comparison run's line and ghost.");
            _onlyCaves = _onlyCavesCfg.Value;
            _allZones = _allZonesCfg.Value;
            _showLine = _lineCfg.Value;
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.map", KeyCode.None, "Open Map tab", OpenMyTab);
        }

        public override void Shutdown()
        {
            if (_relief != null) _relief.Shutdown();
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_white != null) UnityEngine.Object.Destroy(_white);
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            _relief.Tick();
            if (!TabShowing) return;

            _relief.Start();
            if (_relief.Ready && (_view.MinX != _relief.MinX || _view.SizeX != _relief.SizeX ||
                                  _view.MinZ != _relief.MinZ || _view.SizeZ != _relief.SizeZ))
            {
                _view.SetBounds(_relief.MinX, _relief.MinZ, _relief.SizeX, _relief.SizeZ);
                _view.Fit();
            }

            float now = Time.realtimeSinceStartup;
            if (now >= _nextCheck)
            {
                _nextCheck = now + 0.5f;
                if (LibraryChanged()) RebuildSpots();
                else if (_underNeedsTerrain && Terrain.activeTerrain != null) RebuildSpots();
            }

            UpdateLine();
            UpdateSelected();
            UpdateStatus(now);
        }

        private bool LibraryChanged()
        {
            if (_practice == null || _practice.Library == null) return _spots.Length != 0;
            IList<Segment> all = _practice.Library.All;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Segment s = all[i];
                if (!s.HasSpawn) continue;
                if (n >= _spots.Length || !ReferenceEquals(_spots[n], s) || _revision[n] != s.Revision ||
                    _spawn[n] != s.SpawnPosition) return true;
                n++;
            }
            return n != _spots.Length;
        }

        private void RebuildSpots()
        {
            List<Segment> list = new List<Segment>();
            if (_practice != null && _practice.Library != null)
            {
                IList<Segment> all = _practice.Library.All;
                for (int i = 0; i < all.Count; i++) if (all[i].HasSpawn) list.Add(all[i]);
            }

            int n = list.Count;
            _spots = list.ToArray();
            _sx = new float[n]; _sz = new float[n];
            _colour = new int[n]; _community = new bool[n]; _under = new bool[n];
            _revision = new int[n]; _spawn = new Vector3[n];
            _vx = new float[n]; _vy = new float[n]; _vIndex = new int[n];
            _drawnCount = 0;
            _hover = -1;
            _underCount = 0;

            bool terrain = Terrain.activeTerrain != null;
            _underNeedsTerrain = !terrain;
            List<string> categories = new List<string>();
            for (int i = 0; i < n; i++)
            {
                Segment s = _spots[i];
                Vector3 p = s.SpawnPosition;
                _sx[i] = p.x; _sz[i] = p.z; _spawn[i] = p; _revision[i] = s.Revision;
                _colour[i] = MapGeometry.PaletteIndex(s.Category, Palette.Length);
                _community[i] = SegmentLibrary.IsCommunity(s);
                _under[i] = MapGeometry.IsUnderground(p.y, terrain ? MapRelief.TerrainY(p.x, p.z) : float.NaN, s.Cave);
                if (_under[i]) _underCount++;

                bool seen = false;
                for (int c = 0; c < categories.Count && !seen; c++)
                    seen = SegmentLibrary.SameCategory(categories[c], s.Category);
                if (!seen) categories.Add(s.Category);
            }

            categories.Sort(StringComparer.OrdinalIgnoreCase);
            int legend = Math.Min(categories.Count, MaxLegend);
            bool more = categories.Count > MaxLegend;
            _legend = new GUIContent[legend + (more ? 1 : 0)];
            _legendColour = new int[_legend.Length];
            for (int i = 0; i < legend; i++)
            {
                _legend[i] = new GUIContent(categories[i].Length > 0 ? categories[i] : "(no category)");
                _legendColour[i] = MapGeometry.PaletteIndex(categories[i], Palette.Length);
            }
            if (more)
            {
                _legend[legend] = new GUIContent("+" + (categories.Count - MaxLegend) + " more");
                _legendColour[legend] = -1;
            }
            _statusSpots = -1;
            _selectedRevision = int.MinValue;   // its "underground" may have changed
        }

        private void UpdateLine()
        {
            Attempt a = _runs != null && _showLine ? _runs.ComparisonRun : null;
            int count = a != null ? a.Samples.Count : 0;
            if (ReferenceEquals(a, _lineFor) && count == _lineForSamples) return;
            _lineFor = a;
            _lineForSamples = count;
            // ~1 m apart, coarser for a long run so it fits the arrays.
            float step = 1f;
            if (a != null && a.PathLength > LineCapacity) step = a.PathLength / (LineCapacity - 1);
            _lineCount = a != null ? MapGeometry.Thin(a.Samples, step, _lineX, _lineZ) : 0;
        }

        private void UpdateSelected()
        {
            Segment s = _practice != null ? _practice.SelectedSegment : null;
            int rev = s != null ? s.Revision : -1;
            if (ReferenceEquals(s, _selectedFor) && rev == _selectedRevision) return;
            if (!ReferenceEquals(s, _selectedFor)) _goStatus.text = "";
            _selectedFor = s;
            _selectedRevision = rev;

            if (s == null) { _selectedInfo.text = "Nothing selected - click a spot on the map."; return; }

            string where = "";
            int i = Array.IndexOf(_spots, s);
            if (!s.HasSpawn) where = "no spawn point (not on the map)";
            else if (i >= 0 && _under[i]) where = s.Cave.Length > 0 ? "underground (" + s.Cave + ")" : "underground";
            else where = "on the surface";

            _selectedInfo.text = s.Name + "  -  " + (s.Category.Length > 0 ? s.Category : "(no category)") +
                                 (SegmentLibrary.IsCommunity(s) ? ", community" : "") + ", " + where +
                                 (s.IsTimed ? ", timed (" + s.Checkpoints.Count + " checkpoint" +
                                              (s.Checkpoints.Count == 1 ? "" : "s") + ")" : "") +
                                 (s.HasSpawn ? "  at " + Mathf.RoundToInt(s.SpawnPosition.x) + ", " +
                                               Mathf.RoundToInt(s.SpawnPosition.y) + ", " +
                                               Mathf.RoundToInt(s.SpawnPosition.z) : "");
        }

        private void UpdateStatus(float now)
        {
            bool building = _relief.Building;
            if (_statusVersion == _relief.Version && _statusSpots == _spots.Length &&
                _statusUnder == _underCount && !(building && now - _statusProgressAt > 0.25f)) return;
            _statusVersion = _relief.Version;
            _statusSpots = _spots.Length;
            _statusUnder = _underCount;
            _statusProgressAt = now;

            string relief = _relief.Status;
            if (building && _relief.Progress < 1f)
                relief += " " + Mathf.RoundToInt(_relief.Progress * 100f) + "%";
            string spots = _spots.Length + " spot" + (_spots.Length == 1 ? "" : "s") + " on the map, " +
                           _underCount + " underground" +
                           (_underNeedsTerrain ? " (by their cave only until a game is loaded)" : "") + ".";
            _status.text = relief.Length > 0 ? relief + " " + spots : spots;
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            float w = area.width;
            float y = 0f;

            // Buttons: short constant labels, one row.
            if (GUI.Button(new Rect(0f, y, 32f, 22f), "-")) _view.ZoomAt(1f / 1.6f, _view.ViewW * 0.5f, _view.ViewH * 0.5f);
            if (GUI.Button(new Rect(36f, y, 32f, 22f), "+")) _view.ZoomAt(1.6f, _view.ViewW * 0.5f, _view.ViewH * 0.5f);
            if (GUI.Button(new Rect(72f, y, 50f, 22f), "Fit")) _view.Fit();
            GUI.enabled = Ctx.Player.Found;
            if (GUI.Button(new Rect(126f, y, 70f, 22f), "On me"))
            {
                Vector3 p = Ctx.Player.Transform.position;
                if (_view.AtFit) _view.ZoomAt(4f, _view.ViewW * 0.5f, _view.ViewH * 0.5f);
                _view.CentreOn(p.x, p.z);
            }
            GUI.enabled = true;
            y += 26f;

            // Toggles flow onto as many rows as the width needs.
            float x = 0f;
            bool v;
            v = FlowToggle(ref x, ref y, w, _onlyCaves, _onlyCavesLabel);
            if (v != _onlyCaves) { _onlyCaves = v; _onlyCavesCfg.Value = v; _hover = -1; }
            v = FlowToggle(ref x, ref y, w, _allZones, _allZonesLabel);
            if (v != _allZones) { _allZones = v; _allZonesCfg.Value = v; }
            v = FlowToggle(ref x, ref y, w, _showLine, _lineLabel);
            if (v != _showLine) { _showLine = v; _lineCfg.Value = v; }
            y += 24f;

            // The bottom panel's height, measured before the map takes the rest.
            float infoH = UiText.Height(w, _selectedInfo, UiText.Plain) + 26f +
                          UiText.Height(w, _goStatus, UiText.Plain) +
                          UiText.Height(w, _status, UiText.Dim) +
                          UiText.Height(w, _shapes, UiText.Dim);
            float mapH = Mathf.Max(140f, area.height - y - infoH - 6f);
            Rect map = new Rect(0f, y, w, mapH);
            DrawMap(map);
            y += mapH + 4f;

            y += UiText.Draw(0f, y, w, _selectedInfo);
            Segment sel = _practice != null ? _practice.SelectedSegment : null;
            GUI.enabled = sel != null && sel.HasSpawn;
            if (GUI.Button(new Rect(0f, y, 60f, 22f), "Go"))
            {
                string r = _practice.GoFromMap(sel);
                _goStatus.text = r.Length > 0 ? r : "Moved to '" + sel.Name + "'.";
            }
            if (GUI.Button(new Rect(64f, y, 70f, 22f), "Centre"))
            {
                if (_view.AtFit) _view.ZoomAt(4f, _view.ViewW * 0.5f, _view.ViewH * 0.5f);
                _view.CentreOn(sel.SpawnPosition.x, sel.SpawnPosition.z);
            }
            GUI.enabled = sel != null;
            if (GUI.Button(new Rect(138f, y, 120f, 22f), "Edit in Practice")) _practice.ShowTab();
            GUI.enabled = true;
            y += 26f;
            y += UiText.Draw(0f, y, w, _goStatus);
            y += UiText.DrawDim(0f, y, w, _status);
            UiText.DrawDim(0f, y, w, _shapes);
        }

        private static bool FlowToggle(ref float x, ref float y, float width, bool value, GUIContent label)
        {
            float tw = GUI.skin.toggle.CalcSize(label).x + 6f;
            if (x > 0f && x + tw > width) { x = 0f; y += 22f; }
            bool r = GUI.Toggle(new Rect(x, y, tw, 20f), value, label);
            x += tw + 10f;
            return r;
        }

        // ------------------------------------------------------------------
        private void DrawMap(Rect map)
        {
            _view.SetViewport(map.width, map.height);
            if (!_fitted) { _view.Fit(); _fitted = true; }

            Event e = Event.current;
            Vector2 mouse = e.mousePosition;
            bool inside = map.Contains(mouse);
            float mx = mouse.x - map.x, my = mouse.y - map.y;
            HandleInput(e, inside, mx, my);

            if (e.type != EventType.Repaint) return;

            EnsureDrawing();
            Color old = GUI.color;
            GUI.color = MapBackground;
            GUI.DrawTexture(map, _white);
            GUI.color = old;

            MapBox view, uv;
            if (_relief.Ready && _view.TextureWindow(out view, out uv))
                GUI.DrawTextureWithTexCoords(
                    new Rect(map.x + view.X0, map.y + view.Y0, view.Width, view.Height), _relief.Texture,
                    new Rect(uv.X0, uv.Y0, uv.Width, uv.Height));

            ProjectSpots();
            _hover = inside && !_dragging ? PickSpot(mx, my) : -1;
            DrawOverlay(map);
            DrawLegend(map);
            if (_hover >= 0) DrawHoverLabel(map, mx, my);
        }

        private void HandleInput(Event e, bool inside, float mx, float my)
        {
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (!inside) return;
                    _view.ZoomAt(e.delta.y > 0f ? 1f / 1.25f : 1.25f, mx, my);
                    e.Use();
                    break;

                case EventType.MouseDown:
                    if (!inside || e.button != 0) return;
                    _dragging = true;
                    _dragMoved = false;
                    _dragFrom = e.mousePosition;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_dragging) return;
                    if (!_dragMoved && (e.mousePosition - _dragFrom).sqrMagnitude > 16f) _dragMoved = true;
                    if (_dragMoved) _view.Pan(e.delta.x, e.delta.y);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (!_dragging || e.button != 0) return;
                    _dragging = false;
                    if (!_dragMoved && inside)
                    {
                        int hit = PickSpot(mx, my);
                        if (hit >= 0 && _practice != null) _practice.SelectFromMap(_spots[hit]);
                    }
                    e.Use();
                    break;
            }
        }

        /// The spot under (mx, my) from the last projection, or -1.
        private int PickSpot(float mx, float my)
        {
            int i = MapGeometry.Nearest(_vx, _vy, _drawnCount, mx, my, PickRadius);
            return i >= 0 ? _vIndex[i] : -1;
        }

        private bool SpotShown(int i)
        {
            return !_onlyCaves || _under[i];
        }

        private void ProjectSpots()
        {
            _drawnCount = 0;
            for (int i = 0; i < _spots.Length; i++)
            {
                if (!SpotShown(i)) continue;
                Vector2 p = _view.WorldToView(_sx[i], _sz[i]);
                if (p.x < DotSize || p.y < DotSize || p.x > _view.ViewW - DotSize || p.y > _view.ViewH - DotSize) continue;
                _vx[_drawnCount] = p.x;
                _vy[_drawnCount] = p.y;
                _vIndex[_drawnCount] = i;
                _drawnCount++;
            }
        }

        // ------------------------------------------------------------------
        // GL, in map-local pixels (Y down), clipped to the map by hand.
        // ------------------------------------------------------------------
        private void EnsureDrawing()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.hideFlags = HideFlags.HideAndDontSave;
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            if (_labelBox == null)
            {
                _labelBox = new GUIStyle(GUI.skin.box);
                _labelBox.alignment = TextAnchor.MiddleLeft;
                _labelBox.wordWrap = false;
            }
            if (_material != null) return;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;
            _material = new Material(shader);
            _material.hideFlags = HideFlags.HideAndDontSave;
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void DrawOverlay(Rect map)
        {
            if (_material == null) return;
            Vector2 o = GUIUtility.GUIToScreenPoint(new Vector2(map.x, map.y));
            _ox = o.x; _oy = o.y; _screenH = Screen.height;
            _clipW = map.width; _clipH = map.height;

            GL.PushMatrix();
            _material.SetPass(0);
            GL.LoadPixelMatrix();
            GL.Begin(GL.TRIANGLES);

            DrawZones();
            DrawRunLine();
            DrawSpots();
            DrawPlayer();

            GL.End();
            GL.PopMatrix();
        }

        private void DrawZones()
        {
            Segment sel = _practice != null ? _practice.SelectedSegment : null;
            Segment timed = _runs != null ? _runs.TimedSegment : null;
            if (_allZones)
            {
                for (int i = 0; i < _spots.Length; i++)
                    if (_spots[i].IsTimed && SpotShown(i)) DrawSegmentZones(_spots[i]);
            }
            if (sel != null && sel.IsTimed) DrawSegmentZones(sel);
            if (timed != null && timed.IsTimed && !ReferenceEquals(timed, sel)) DrawSegmentZones(timed);
        }

        private void DrawSegmentZones(Segment s)
        {
            DrawZone(s.Start, StartColour);
            for (int i = 0; i < s.Checkpoints.Count; i++)
                if (!s.IsCheckpointHidden(i)) DrawZone(s.Checkpoints[i], CheckColour);
            DrawZone(s.End, EndColour);
        }

        private void DrawZone(Trigger t, Color c)
        {
            if (t.Kind != TriggerKind.Zone) return;
            float s = _view.Scale;
            if (t.Shape == ZoneShape.Polygon && t.Points != null && t.Points.Length >= 3)
            {
                Vector2[] pts = t.Points;
                Vector2 prev = _view.WorldToView(pts[pts.Length - 1].x, pts[pts.Length - 1].y);
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector2 p = _view.WorldToView(pts[i].x, pts[i].y);
                    Line(prev.x, prev.y, p.x, p.y, 2f, c);
                    prev = p;
                }
                return;
            }

            Vector2 centre = _view.WorldToView(t.Position.x, t.Position.z);
            if (t.Shape == ZoneShape.Box)
            {
                MapGeometry.BoxCorners(t.Position.x, t.Position.z, t.Extents.x, t.Extents.z, t.Yaw, _cx, _cz);
                if (t.Extents.x * s < 2f && t.Extents.z * s < 2f) { Dot(centre.x, centre.y, 3f, c); return; }
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) & 3;
                    Vector2 a = _view.WorldToView(_cx[i], _cz[i]);
                    Vector2 b = _view.WorldToView(_cx[j], _cz[j]);
                    Line(a.x, a.y, b.x, b.y, 2f, c);
                }
                return;
            }

            float r = t.Radius * s;
            if (r < 3f) { Dot(centre.x, centre.y, 3f, c); return; }
            for (int i = 0; i < 32; i++)
                Line(centre.x + CircleCos[i] * r, centre.y + CircleSin[i] * r,
                     centre.x + CircleCos[i + 1] * r, centre.y + CircleSin[i + 1] * r, 2f, c);
        }

        private void DrawRunLine()
        {
            if (!_showLine) return;
            if (_lineCount > 1)
            {
                Vector2 prev = _view.WorldToView(_lineX[0], _lineZ[0]);
                for (int i = 1; i < _lineCount; i++)
                {
                    Vector2 p = _view.WorldToView(_lineX[i], _lineZ[i]);
                    Line(prev.x, prev.y, p.x, p.y, 2f, LineColour);
                    prev = p;
                }
            }
            Vector3 ghost;
            if (_runs != null && _runs.GhostOnMap(out ghost))
            {
                Vector2 g = _view.WorldToView(ghost.x, ghost.z);
                if (InsideBy(g, 6f)) { Dot(g.x, g.y, 6f, Outline); Dot(g.x, g.y, 4.5f, GhostColour); }
            }
        }

        private void DrawSpots()
        {
            Segment sel = _practice != null ? _practice.SelectedSegment : null;
            for (int k = 0; k < _drawnCount; k++)
            {
                int i = _vIndex[k];
                float x = _vx[k], y = _vy[k];
                Color c = Palette[_colour[i]];
                bool big = i == _hover || ReferenceEquals(_spots[i], sel);
                float r = big ? DotSize + 1.5f : DotSize;
                if (ReferenceEquals(_spots[i], sel)) Shape(x, y, r + 3f, _under[i], SelectRing);
                Shape(x, y, r + 1.2f, _under[i], Outline);
                if (_community[i])
                {
                    Shape(x, y, r, _under[i], c);
                    Shape(x, y, r - 2f, _under[i], Outline);
                }
                else Shape(x, y, r, _under[i], c);
            }
        }

        /// A square (surface) or a down-pointing triangle (underground).
        private void Shape(float x, float y, float r, bool under, Color c)
        {
            if (r <= 0f) return;
            if (!under) { Dot(x, y, r, c); return; }
            GL.Color(c);
            float t = r * 1.3f;
            Vertex(x - t, y - t * 0.75f); Vertex(x + t, y - t * 0.75f); Vertex(x, y + t);
        }

        private void DrawPlayer()
        {
            if (!Ctx.Player.Found) return;
            Transform tr = Ctx.Player.Transform;
            Vector3 p = tr.position;
            Vector2 c = _view.WorldToView(p.x, p.z);
            if (!InsideBy(c, 12f)) return;

            float yaw = tr.eulerAngles.y * Mathf.Deg2Rad;
            float fx = Mathf.Sin(yaw), fy = -Mathf.Cos(yaw);   // view Y is south
            float px = -fy, py = fx;
            Arrow(c, fx, fy, px, py, 12f, Outline);
            Arrow(c, fx, fy, px, py, 9f, Color.white);
        }

        private void Arrow(Vector2 c, float fx, float fy, float px, float py, float size, Color col)
        {
            GL.Color(col);
            float tipX = c.x + fx * size, tipY = c.y + fy * size;
            float bx = c.x - fx * size * 0.6f, by = c.y - fy * size * 0.6f;
            float w = size * 0.7f;
            Vertex(tipX, tipY); Vertex(bx + px * w, by + py * w); Vertex(c.x, c.y);
            Vertex(tipX, tipY); Vertex(c.x, c.y); Vertex(bx - px * w, by - py * w);
        }

        private bool InsideBy(Vector2 p, float margin)
        {
            return p.x >= margin && p.y >= margin && p.x <= _clipW - margin && p.y <= _clipH - margin;
        }

        private void Dot(float x, float y, float r, Color c)
        {
            float x0 = Mathf.Max(0f, x - r), y0 = Mathf.Max(0f, y - r);
            float x1 = Mathf.Min(_clipW, x + r), y1 = Mathf.Min(_clipH, y + r);
            if (x1 <= x0 || y1 <= y0) return;
            GL.Color(c);
            Vertex(x0, y0); Vertex(x1, y0); Vertex(x1, y1);
            Vertex(x0, y0); Vertex(x1, y1); Vertex(x0, y1);
        }

        /// A line `w` px wide as two triangles, clipped to the map.
        private void Line(float ax, float ay, float bx, float by, float w, Color c)
        {
            if (!MapGeometry.ClipSegment(ref ax, ref ay, ref bx, ref by, 0f, 0f, _clipW, _clipH)) return;
            float dx = bx - ax, dy = by - ay;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            float nx = -dy / len * w * 0.5f, ny = dx / len * w * 0.5f;
            GL.Color(c);
            Vertex(ax + nx, ay + ny); Vertex(bx + nx, by + ny); Vertex(bx - nx, by - ny);
            Vertex(ax + nx, ay + ny); Vertex(bx - nx, by - ny); Vertex(ax - nx, ay - ny);
        }

        private void Vertex(float x, float y)
        {
            GL.Vertex3(_ox + x, _screenH - (_oy + y), 0f);
        }

        // ------------------------------------------------------------------
        private void DrawLegend(Rect map)
        {
            if (_legend.Length == 0) return;
            float x = map.x + 6f, y = map.y + 6f;
            Color old = GUI.color;
            for (int i = 0; i < _legend.Length; i++)
            {
                if (y + 18f > map.yMax) break;
                float tw = Mathf.Min(map.width - 30f, GUI.skin.label.CalcSize(_legend[i]).x + 4f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(x - 2f, y - 1f, tw + 18f, 18f), _white);
                if (_legendColour[i] >= 0)
                {
                    GUI.color = Palette[_legendColour[i]];
                    GUI.DrawTexture(new Rect(x, y + 4f, 9f, 9f), _white);
                }
                GUI.color = old;
                GUI.Label(new Rect(x + 14f, y - 2f, tw, 20f), _legend[i]);
                y += 18f;
            }
            GUI.color = old;
        }

        private void DrawHoverLabel(Rect map, float mx, float my)
        {
            Segment s = _spots[_hover];
            _hoverLabel.text = s.Name;   // a field read: no allocation
            Vector2 size = _labelBox.CalcSize(_hoverLabel);
            float w = Mathf.Min(size.x + 4f, map.width - 4f);
            float x = map.x + mx + 14f, y = map.y + my - 26f;
            if (x + w > map.xMax) x = map.x + mx - 14f - w;
            if (x < map.x) x = map.x + 2f;
            if (y < map.y) y = map.y + my + 14f;
            GUI.Box(new Rect(x, y, w, 22f), _hoverLabel, _labelBox);
        }

        public override void DrawPanel(int windowId) { }
    }
}
