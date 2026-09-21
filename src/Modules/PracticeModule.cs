using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Practice tools: position save/restore and the teleport library.
    //
    // STATE-ALTERING - PRACTICE ONLY. Everything here writes to the game,
    // so every entry point marks PracticeState, which puts a sticky
    // warning on the HUD. That marker is the whole reason this module is
    // kept separate from the info-only ones: a runner must never be able
    // to use a teleport and then forget it happened while recording.
    //
    // The location list is data, not code (see LocationLibrary). Adding
    // spots is dropping a text file in the locations folder - no rebuild,
    // no registration - which is what lets a community-contributed set
    // grow the panel on its own.
    // ------------------------------------------------------------------
    public sealed class PracticeModule : OverlayModule
    {
        private const float RowHeight = 21f;
        // Char code rather than an escape so the literal survives tooling
        // that rewrites this file.
        private static readonly string NL = ((char)10).ToString();
        private const float HeaderHeight = 22f;

        public override string Id { get { return "practice"; } }
        public override string DisplayName { get { return "Practice"; } }
        public override bool HasPanel { get { return true; } }
        public override bool WantsPlayerLock { get { return true; } }
        public override bool IsPracticeOnly { get { return true; } }

        private LocationLibrary _library;

        private bool _hasSaved;
        private Vector3 _savedPosition;
        private Quaternion _savedRotation;
        private string _status = "";

        private Rect _windowRect;
        private bool _windowPlaced;
        private Vector2 _scroll;
        private string _filter = "";

        // Category name -> collapsed. Collapsing matters once a contributed
        // set pushes the list past a screenful.
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
            map.Add(KeyCode.F6, "save position", SavePosition);
            map.Add(KeyCode.F7, "restore position", RestorePosition);
            map.Add(KeyCode.F3, "practice panel", TogglePanel);
        }

        // ------------------------------------------------------------------
        private void SavePosition()
        {
            if (!Ctx.Player.Found) { _status = "No player ref."; return; }

            _savedPosition = Ctx.Player.Transform.position;
            _savedRotation = Ctx.Player.Transform.rotation;
            _hasSaved = true;
            _status = "Saved.";
        }

        private void RestorePosition()
        {
            if (!_hasSaved) { _status = "Nothing saved yet."; return; }

            if (Ctx.Player.MoveTo(_savedPosition, _savedRotation))
            {
                Ctx.Practice.Mark("position restore");
                _status = "Restored.";
            }
            else _status = "No player ref.";
        }

        private void TeleportTo(Location loc)
        {
            Quaternion rot = Quaternion.Euler(0f, loc.Yaw, 0f);
            if (Ctx.Player.MoveTo(loc.Position, rot))
            {
                Ctx.Practice.Mark("teleport: " + loc.Name);
                _status = "-> " + loc.Name;
            }
            else _status = "No player ref.";
        }

        private void CaptureHere()
        {
            if (!Ctx.Player.Found) { _status = "No player ref."; return; }

            Vector3 p = Ctx.Player.Transform.position;
            float yaw = Ctx.Player.Transform.eulerAngles.y;

            if (_library.Append(_captureCategory, _captureName, p, yaw, ""))
            {
                _library.Reload();
                _status = "Captured '" + _captureName + "'";
            }
            else _status = "Capture failed - see log.";
        }

        // ------------------------------------------------------------------
        public override void ContributeHud(HudBuilder hud)
        {
            if (_status.Length > 0) hud.Pair("Prac", _status);
        }

        public override void DrawPanel(int windowId)
        {
            if (!_windowPlaced)
            {
                _windowRect = new Rect(30f, 200f, 420f, 500f);
                _windowPlaced = true;
            }

            _windowRect = GUI.Window(windowId, _windowRect, DrawContents,
                "Practice  -  " + _library.Status);
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

            // --- position save/restore -------------------------------------
            if (GUI.Button(new Rect(10, 26, 120, 24), "Save pos  (F6)")) SavePosition();

            GUI.enabled = _hasSaved;
            if (GUI.Button(new Rect(136, 26, 130, 24), "Restore pos  (F7)")) RestorePosition();
            GUI.enabled = true;

            if (GUI.Button(new Rect(272, 26, w - 282, 24), "Reload files")) _library.Reload();

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
        // be measured rather than assumed. A fixed 60px box clipped the last
        // line. GUIContent and style are cached because this runs in OnGUI.
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
                    "then press \"Add here\". It is appended to " +
                    Data.LocationLibrary.UserFileName + " and shows up in this list." + NL + NL +
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
