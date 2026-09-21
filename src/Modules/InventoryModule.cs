using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Per-item inventory breakdown. INFO-ONLY.
    //
    // The HUD keeps only the total plus a short watch list, because the
    // full inventory is far too long to sit on screen during a run. The
    // watch list is the point: a runner cares about a handful of counts
    // (rope, cloth, sticks...) and wants them visible without opening
    // anything. The panel is for finding the exact names to watch.
    //
    // All display strings are rebuilt on a 4 Hz throttle rather than per
    // frame - reflection over the item list plus string building is far
    // too expensive to do at OnGUI rate.
    // ------------------------------------------------------------------
    public sealed class InventoryModule : OverlayModule
    {
        private const float RefreshInterval = 0.25f;
        private const float RowHeight = 19f;

        public override string Id { get { return "inventory"; } }
        public override string DisplayName { get { return "Inventory"; } }
        public override bool HasPanel { get { return true; } }

        private float _nextRefresh;
        private int _total = -1;

        // Item names the HUD should always show, lowercased for matching.
        private readonly List<string> _watch = new List<string>();
        private readonly List<string> _watchLines = new List<string>();

        private Rect _windowRect;
        private bool _windowPlaced;
        private Vector2 _scroll;
        private string _filter = "";

        private GUIStyle _rowStyle;

        // Row labels are cached and rebuilt only when the stack list
        // changes (4 Hz), never inside OnGUI. The pin marker is drawn as a
        // separate cached glyph so toggling a pin does not force the whole
        // label to be rebuilt.
        private readonly List<GUIContent> _rowLabels = new List<GUIContent>();
        private static readonly GUIContent PinMark = new GUIContent("*");

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add(KeyCode.F4, "inventory panel", Toggle);
        }

        private void Toggle()
        {
            TogglePanel();
        }

        public override void Tick()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            Ctx.Inventory.Resolve();
            _total = Ctx.Inventory.TotalCount();

            // Only pay for the full list when someone can see it.
            if (PanelOpen || _watch.Count > 0)
            {
                Ctx.Inventory.Refresh();
                RebuildRowLabels();
            }

            RebuildWatchLines();
        }

        private void RebuildRowLabels()
        {
            IList<ItemStack> stacks = Ctx.Inventory.Stacks;

            for (int i = 0; i < stacks.Count; i++)
            {
                string text = stacks[i].Name + "   x" + stacks[i].Amount +
                              "   (id " + stacks[i].Id + ")";

                if (i < _rowLabels.Count) _rowLabels[i].text = text;
                else _rowLabels.Add(new GUIContent(text));
            }
        }

        private void RebuildWatchLines()
        {
            _watchLines.Clear();
            if (_watch.Count == 0) return;

            IList<ItemStack> stacks = Ctx.Inventory.Stacks;

            for (int w = 0; w < _watch.Count; w++)
            {
                int amount = 0;
                string display = _watch[w];

                for (int i = 0; i < stacks.Count; i++)
                {
                    if (stacks[i].Name == null) continue;
                    if (stacks[i].Name.ToLowerInvariant() != _watch[w]) continue;
                    amount += stacks[i].Amount;
                    display = stacks[i].Name;
                }

                _watchLines.Add(display + " x" + amount);
            }
        }

        public override void ContributeHud(HudBuilder hud)
        {
            hud.Pair("Items", _total >= 0 ? _total.ToString() : "(inventory not resolved)");

            for (int i = 0; i < _watchLines.Count; i++)
                hud.Pair("", "  " + _watchLines[i]);
        }

        // ------------------------------------------------------------------
        public override void DrawPanel(int windowId)
        {
            if (!_windowPlaced)
            {
                _windowRect = new Rect(Screen.width - 430f, 60f, 400f, 460f);
                _windowPlaced = true;
            }

            _windowRect = GUI.Window(windowId, _windowRect, DrawContents,
                "Inventory  -  " + (_total >= 0 ? _total + " items" : "not resolved"));
        }

        private void DrawContents(int id)
        {
            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(GUI.skin.label);
                _rowStyle.alignment = TextAnchor.MiddleLeft;
                _rowStyle.padding = new RectOffset(4, 4, 0, 0);
            }

            GUI.Label(new Rect(10, 26, 46, 22), "Filter");
            _filter = GUI.TextField(new Rect(58, 26, 200, 22), _filter);

            if (GUI.Button(new Rect(266, 26, 56, 22), "Clear")) _filter = "";
            if (GUI.Button(new Rect(326, 26, 64, 22), "Refresh"))
            {
                Ctx.Inventory.Refresh();
                RebuildRowLabels();
            }

            GUI.Label(new Rect(10, 52, 380, 20),
                "Click an item to pin it to the HUD.  Pinned: " + _watch.Count);

            Rect listRect = new Rect(8, 76, _windowRect.width - 16, _windowRect.height - 86);
            DrawList(listRect);

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 22));
        }

        private void DrawList(Rect listRect)
        {
            IList<ItemStack> stacks = Ctx.Inventory.Stacks;
            string filter = _filter.Length > 0 ? _filter.ToLowerInvariant() : null;

            // Count matches first so the scroll view gets a correct height
            // without building a temporary list every pass.
            int matches = 0;
            for (int i = 0; i < stacks.Count; i++)
                if (Matches(stacks[i], filter)) matches++;

            Rect content = new Rect(0, 0, listRect.width - 20f, matches * RowHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, content);

            // Virtualised: only rows inside the viewport are drawn. Drawing
            // every row on every OnGUI pass is what caused the GC spike the
            // type explorer had to be rewritten to avoid.
            int first = Mathf.Max(0, (int)(_scroll.y / RowHeight) - 1);
            int visible = (int)(listRect.height / RowHeight) + 3;

            int row = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (!Matches(stacks[i], filter)) continue;

                int thisRow = row++;
                if (thisRow < first || thisRow > first + visible) continue;

                if (i >= _rowLabels.Count) continue;

                Rect r = new Rect(16f, thisRow * RowHeight, content.width - 16f, RowHeight);

                if (IsPinned(stacks[i].Name))
                    GUI.Label(new Rect(2f, thisRow * RowHeight, 14f, RowHeight), PinMark);

                if (GUI.Button(r, _rowLabels[i], _rowStyle))
                    TogglePin(stacks[i].Name);
            }

            GUI.EndScrollView();
        }

        private static bool Matches(ItemStack s, string lowerFilter)
        {
            if (lowerFilter == null) return true;
            if (s.Name == null) return false;
            return s.Name.ToLowerInvariant().Contains(lowerFilter);
        }

        private bool IsPinned(string name)
        {
            if (name == null) return false;
            for (int i = 0; i < _watch.Count; i++)
                if (string.Equals(_watch[i], name, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void TogglePin(string name)
        {
            string key = name.ToLowerInvariant();
            if (_watch.Contains(key)) _watch.Remove(key);
            else _watch.Add(key);
            RebuildWatchLines();
        }
    }
}
