using System;
using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Practice spots: teleport somewhere and practise from it.
    //
    // THERE IS NO SEPARATE "ANCHOR".
    // An earlier version had two competing ideas - a manually set anchor
    // and a teleport library - and they overlapped confusingly: if you can
    // save a spot, setting a nameless anchor as well is redundant. So the
    // spot you last went to IS where the next attempt starts from, and
    // "save spot here" is the only way to make a new one.
    //
    // A spot is also what a segment grows out of: attach start/end
    // triggers to one later and it becomes a timed, splittable segment
    // (see Data/Segments.cs). Same position, more configuration.
    //
    // PracticeRunModule watches CurrentSpot to time attempts.
    //
    // The list is data, not code (see LocationLibrary): every .txt in the
    // locations folder is merged, so a shared set is a file you drop in.
    // ------------------------------------------------------------------
    public sealed class PracticeModule : OverlayModule
    {
        private const float RowHeight = 21f;
        private const float HeaderHeight = 22f;

        // Char code rather than an escape so the literal survives tooling
        // that rewrites this file.
        private static readonly string NL = ((char)10).ToString();

        public override string Id { get { return "practice"; } }
        public override string DisplayName { get { return "Practice"; } }
        public override bool HasPanel { get { return true; } }
        public override bool IsPracticeOnly { get { return true; } }

        private LocationLibrary _library;

        // --- current spot -------------------------------------------------
        private bool _hasSpot;
        private Vector3 _spotPosition;
        private float _spotYaw;
        private float _spotPitch;
        private string _spotLabel = "";

        public bool HasSpot { get { return _hasSpot; } }
        public Vector3 SpotPosition { get { return _spotPosition; } }
        public string SpotLabel { get { return _spotLabel; } }

        /// Raised whenever the player is placed at the current spot, so a
        /// practice attempt can be armed without this module knowing the
        /// timer exists.
        public System.Action OnPlacedAtSpot;

        private string _status = "";

        private Rect _windowRect;
        private bool _windowPlaced;
        private Vector2 _scroll;
        private string _filter = "";

        // Category name -> collapsed. Matters once a contributed set
        // pushes the list past a screenful.
        private readonly Dictionary<string, bool> _collapsed = new Dictionary<string, bool>();

        private string _captureName = "new spot";
        private string _captureCategory = "My spots";

        private GUIStyle _rowStyle;
        private GUIStyle _headerStyle;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _library = new LocationLibrary(ctx.Log, ctx.ConfigDirectory);
            _library.Reload();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("practice.saveSpot", KeyCode.F6, "Save spot here", QuickSaveSpot);
            map.Add("practice.toSpot", KeyCode.F7, "Return to current spot", ReturnToSpot);
            map.Add("panel.practice", KeyCode.F3, "Practice panel", TogglePanel);
        }

        // ------------------------------------------------------------------
        private void SetSpot(Vector3 pos, float yaw, float pitch, string label)
        {
            _spotPosition = pos;
            _spotYaw = yaw;
            _spotPitch = pitch;
            _spotLabel = label;
            _hasSpot = true;
        }

        /// Saves where you are standing as a real, named spot and makes it
        /// current. There is no unnamed "just remember this" state any
        /// more: a saved spot is the only kind, so it survives a restart
        /// and can be shared or promoted to a segment later.
        private void QuickSaveSpot()
        {
            if (!Ctx.Player.Found) { _status = "No player ref."; return; }

            Vector3 p = Ctx.Player.Transform.position;
            float yaw = Ctx.Player.Transform.eulerAngles.y;
            float pitch = Ctx.Bridge.GetLookPitch();

            string name = _captureName;
            if (name.Length == 0 || name == "new spot")
                name = "spot " + DateTime.Now.ToString("HH:mm:ss");

            if (_library.Append(_captureCategory, name, p, yaw, "", pitch))
            {
                _library.Reload();
                SetSpot(p, yaw, pitch, name);
                _status = "Saved and selected '" + name + "'";
            }
            else _status = "Save failed - see log.";
        }

        public void ReturnToSpot()
        {
            if (!_hasSpot) { _status = "No spot selected - click one below."; return; }

            if (Ctx.Player.MoveTo(_spotPosition, Quaternion.Euler(0f, _spotYaw, 0f)))
            {
                Ctx.Bridge.ApplyLook(Ctx.Player.Transform, _spotYaw, _spotPitch);
                Ctx.Practice.Mark("return to spot");
                _status = "-> " + _spotLabel;
                if (OnPlacedAtSpot != null) OnPlacedAtSpot();
            }
            else _status = "No player ref.";
        }

        // Teleporting somewhere makes that the new anchor: it is where the
        // next attempt starts from.
        private void TeleportTo(Location loc)
        {
            Quaternion rot = Quaternion.Euler(0f, loc.Yaw, 0f);

            if (Ctx.Player.MoveTo(loc.Position, rot))
            {
                Ctx.Bridge.ApplyLook(Ctx.Player.Transform, loc.Yaw, loc.Pitch);
                SetSpot(loc.Position, loc.Yaw, loc.Pitch, loc.Name);
                Ctx.Practice.Mark("teleport: " + loc.Name);
                _status = "-> " + loc.Name;
                if (OnPlacedAtSpot != null) OnPlacedAtSpot();
            }
            else _status = "No player ref.";
        }

        private void CaptureHere()
        {
            if (!Ctx.Player.Found) { _status = "No player ref."; return; }

            Vector3 p = Ctx.Player.Transform.position;
            float yaw = Ctx.Player.Transform.eulerAngles.y;

            if (_library.Append(_captureCategory, _captureName, p, yaw, "", Ctx.Bridge.GetLookPitch()))
            {
                _library.Reload();
                _status = "Captured '" + _captureName + "'";
            }
            else _status = "Capture failed - see log.";
        }

        // ------------------------------------------------------------------
        public override void ContributeHud(HudBuilder hud)
        {
            if (_hasSpot) hud.Pair("Spot", _spotLabel);
            if (_status.Length > 0) hud.Pair("Prac", _status);
        }

        public override void DrawPanel(int windowId)
        {
            if (!_windowPlaced)
            {
                _windowRect = new Rect(30f, 200f, 420f, 520f);
                _windowPlaced = true;
            }

            _windowRect = GUI.Window(windowId, _windowRect, DrawContents, _title);
        }

        private readonly GUIContent _title = new GUIContent("Practice");

        public override void Tick()
        {
            _title.text = "Practice  -  " + _library.Status +
                          (_hasSpot ? "  |  spot: " + _spotLabel : "  |  no spot selected");
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null) return;

            _rowStyle = new GUIStyle(GUI.skin.button);
            _rowStyle.alignment = TextAnchor.MiddleLeft;
            _rowStyle.padding = new RectOffset(8, 4, 0, 0);

            _headerStyle = new GUIStyle(GUI.skin.box);
            _headerStyle.alignment = TextAnchor.MiddleLeft;
            _headerStyle.padding = new RectOffset(8, 4, 0, 0);
            _headerStyle.fontStyle = FontStyle.Bold;
        }

        private void DrawContents(int id)
        {
            EnsureStyles();

            float w = _windowRect.width;

            // --- anchor ----------------------------------------------------
            if (GUI.Button(new Rect(10, 26, 130, 24), "Save spot here")) QuickSaveSpot();

            GUI.enabled = _hasSpot;
            if (GUI.Button(new Rect(146, 26, 130, 24), "Return to spot")) ReturnToSpot();
            GUI.enabled = true;

            if (GUI.Button(new Rect(282, 26, w - 292, 24), "Reload files")) _library.Reload();

            // --- capture ---------------------------------------------------
            GUI.Label(new Rect(10, 58, 60, 22), "Capture");
            _captureCategory = GUI.TextField(new Rect(72, 58, 110, 22), _captureCategory);
            _captureName = GUI.TextField(new Rect(188, 58, 130, 22), _captureName);
            if (GUI.Button(new Rect(324, 58, w - 334, 22), "Add here")) CaptureHere();

            // --- filter ----------------------------------------------------
            GUI.Label(new Rect(10, 86, 40, 22), "Find");
            _filter = GUI.TextField(new Rect(52, 86, 200, 22), _filter);
            if (GUI.Button(new Rect(258, 86, 56, 22), "Clear")) _filter = "";

            GUI.Label(new Rect(10, 112, w - 20, 20), _status);

            DrawLocationList(new Rect(8, 134, w - 16, _windowRect.height - 144));

            GUI.DragWindow(new Rect(0, 0, w, 22));
        }

        // Grouped by category, collapsible, virtualised the same way the
        // type explorer is - a contributed set could be thousands of rows
        // and OnGUI runs several times a frame.
        private void DrawLocationList(Rect listRect)
        {
            IList<Location> all = _library.All;
            string filter = _filter.Length > 0 ? _filter.ToLowerInvariant() : null;

            float y = 0f;
            float contentHeight = MeasureContent(all, filter);

            Rect content = new Rect(0, 0, listRect.width - 20f, contentHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, content);

            string currentCategory = null;
            bool categoryCollapsed = false;

            for (int i = 0; i < all.Count; i++)
            {
                Location loc = all[i];
                if (!MatchesFilter(loc, filter)) continue;

                if (loc.Category != currentCategory)
                {
                    currentCategory = loc.Category;
                    categoryCollapsed = IsCollapsed(currentCategory);

                    Rect hr = new Rect(0, y, content.width, HeaderHeight);
                    if (IsVisible(hr, listRect))
                    {
                        if (GUI.Button(hr, (categoryCollapsed ? "+ " : "- ") + currentCategory, _headerStyle))
                            _collapsed[currentCategory] = !categoryCollapsed;
                    }
                    y += HeaderHeight;
                }

                if (categoryCollapsed) continue;

                Rect r = new Rect(12f, y, content.width - 12f, RowHeight);
                if (IsVisible(r, listRect))
                {
                    if (GUI.Button(r, loc.Label, _rowStyle)) TeleportTo(loc);
                }
                y += RowHeight;
            }

            GUI.EndScrollView();

            if (all.Count == 0) DrawEmptyState(listRect);
        }

        // The help text wraps - the config path is long - so its height must
        // be measured rather than assumed. A fixed box clipped the last line.
        private GUIContent _emptyHelp;
        private GUIStyle _wrapStyle;

        private void DrawEmptyState(Rect listRect)
        {
            if (_wrapStyle == null)
            {
                _wrapStyle = new GUIStyle(GUI.skin.label);
                _wrapStyle.wordWrap = true;
                _wrapStyle.alignment = TextAnchor.UpperLeft;
            }

            if (_emptyHelp == null)
            {
                _emptyHelp = new GUIContent(
                    "No locations yet." + NL + NL +
                    "Stand where you want a spot, set a category and name above, " +
                    "then press Add here. It is appended to " +
                    LocationLibrary.UserFileName + " and shows up in this list." + NL + NL +
                    "Loaded from:" + NL + _library.Folder + NL + NL +
                    "Any .txt file in that folder is merged in, so a shared set can " +
                    "be dropped straight in.");
            }

            float w = listRect.width - 20f;
            float h = _wrapStyle.CalcHeight(_emptyHelp, w);

            GUI.Label(new Rect(listRect.x + 8f, listRect.y + 6f, w, h), _emptyHelp, _wrapStyle);
        }

        private float MeasureContent(IList<Location> all, string filter)
        {
            float h = 0f;
            string current = null;
            bool collapsed = false;

            for (int i = 0; i < all.Count; i++)
            {
                if (!MatchesFilter(all[i], filter)) continue;

                if (all[i].Category != current)
                {
                    current = all[i].Category;
                    collapsed = IsCollapsed(current);
                    h += HeaderHeight;
                }

                if (!collapsed) h += RowHeight;
            }
            return h;
        }

        private bool IsCollapsed(string category)
        {
            bool v;
            return _collapsed.TryGetValue(category, out v) && v;
        }

        private bool IsVisible(Rect row, Rect viewport)
        {
            return row.yMax >= _scroll.y - RowHeight &&
                   row.y <= _scroll.y + viewport.height + RowHeight;
        }

        private static bool MatchesFilter(Location loc, string lowerFilter)
        {
            if (lowerFilter == null) return true;
            return loc.Name.ToLowerInvariant().Contains(lowerFilter) ||
                   loc.Category.ToLowerInvariant().Contains(lowerFilter);
        }
    }
}
