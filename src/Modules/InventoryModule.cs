using System.Collections.Generic;
using BepInEx.Configuration;
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
    //
    // Also the home of "Logs in the inventory" (Game/LogStore), a
    // gameplay mod - off by default, marks practice when on.
    // ------------------------------------------------------------------
    public sealed class InventoryModule : OverlayModule
    {
        private const float RefreshInterval = 0.25f;
        private const float RowHeight = 19f;

        public override string Id { get { return "inventory"; } }
        public override string DisplayName { get { return "Inventory"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Inventory"; } }
        public override int TabOrder { get { return 40; } }

        private float _nextRefresh;
        private int _total = -1;
        private int _stackCount;

        // Item names the HUD should always show, lowercased for matching.
        private readonly List<string> _watch = new List<string>();
        private readonly List<string> _watchLines = new List<string>();

        private Vector2 _scroll;
        private string _filter = "";

        private GUIStyle _rowStyle;

        // Row labels are cached and rebuilt only when the stack list
        // changes (4 Hz), never inside OnGUI. The pin marker is drawn as a
        // separate cached glyph so toggling a pin does not force the whole
        // label to be rebuilt.
        private readonly List<GUIContent> _rowLabels = new List<GUIContent>();
        private static readonly GUIContent PinMark = new GUIContent("*");

        // Stack indices that pass the filter, rebuilt with the rows or when
        // the filter changes - lowercasing every name on every OnGUI pass
        // allocated for as long as the tab was open.
        private readonly List<int> _shown = new List<int>();
        private string _shownFilter = "";
        private bool _wasShowing;

        // Logs in the inventory (Game/LogStore).
        private ConfigEntry<bool> _logsCfg;
        private ConfigEntry<int> _logCapCfg;
        // Kept across launches (v0.24.191).
        private ConfigEntry<bool> _hidePhantomsCfg, _showZerosCfg;
        private string _capText;
        private bool _logsMarked;
        private string _logsHud;
        private readonly GUIContent _logsStatus = new GUIContent("");
        private static readonly GUIContent LogsText = new GUIContent(
            "Picked-up logs go into the inventory up to the cap instead of into your arms - hands stay free, " +
            "no energy cost. A full store leaves the log on the ground. Building, fires, the log sled, holders " +
            "and repairs take from the store; a log on a zipline still needs the arms (turn this off). " +
            "Turning it off puts up to 2 back in your arms and drops the rest. Changes gameplay: marks practice.");

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _logsCfg = Ctx.Config.Bind("Inventory", "LogsInInventory", false,
                "Gameplay mod: picked-up logs are stored with a counter up to LogsInInventoryCap instead of carried in the arms. Marks practice.");
            _hidePhantomsCfg = Ctx.Config.Bind("Inventory", "HidePhantoms", true, "Inventory tab: hide ids that are not real inventory contents.");
            _showZerosCfg = Ctx.Config.Bind("Inventory", "ShowZeroAmounts", true, "Inventory tab: list items held 0 times.");
            Ctx.Inventory.FilterPhantomItems = _hidePhantomsCfg.Value;
            Ctx.Inventory.ShowZeroAmounts = _showZerosCfg.Value;
            _logCapCfg = Ctx.Config.Bind("Inventory", "LogsInInventoryCap", 5,
                "How many logs the inventory holds with LogsInInventory on (1-99).");
            _capText = _logCapCfg.Value.ToString();
            LogStore.Init(ctx.Log, OverlayPlugin.PluginGuid);
            LogStore.Full = cap => Ctx.Notice.Show("Logs: the inventory holds " + cap + " - full", 3f);
        }

        public override void Shutdown()
        {
            LogStore.Full = null;
            LogStore.Shutdown();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.inventory", KeyCode.None, "Open Inventory tab", OpenMyTab);
        }

        public override void Tick()
        {
            // The first frame on the tab refreshes at once: it opened empty
            // until Refresh was clicked (author, v0.22.6) - rows were only
            // rebuilt behind PanelOpen, which a tab never sets.
            bool showing = TabShowing;
            if (showing && !_wasShowing) _nextRefresh = 0f;
            _wasShowing = showing;

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            TickLogs();
            Ctx.Inventory.Resolve();

            // Always refresh: the HUD total is derived from the live list
            // now, because _possessedItemsCount does not track reliably.
            Ctx.Inventory.Refresh();
            _total = Ctx.Inventory.TotalItems;
            _stackCount = Ctx.Inventory.TotalStacks;

            if (showing) RebuildRowLabels();

            RefreshTitle();
            RebuildWatchLines();
        }

        private void TickLogs()
        {
            LogStore.Cap = Mathf.Clamp(_logCapCfg.Value, 1, 99);
            LogStore.Maintain(_logsCfg.Value && !PlayerRef.AtTitleScreen);
            if (LogStore.Active && !_logsMarked)
            {
                _logsMarked = true;
                Ctx.Practice.Mark("logs in the inventory");
            }
            if (!_logsCfg.Value) _logsMarked = false;

            int stored = LogStore.Stored();
            string hud = stored >= 0 ? stored + " / " + LogStore.Cap : null;
            if (hud != _logsHud) _logsHud = hud;
            string status = !_logsCfg.Value ? "" :
                stored >= 0 ? "Stored: " + stored + " / " + LogStore.Cap + " logs" : "Logs in the inventory: " + LogStore.Status;
            if (status != _logsStatus.text) _logsStatus.text = status;
        }

        private void RebuildRowLabels()
        {
            IList<ItemStack> stacks = Ctx.Inventory.Stacks;

            for (int i = 0; i < stacks.Count; i++)
            {
                string text = stacks[i].Name + "   x" + stacks[i].Amount +
                              "   (id " + stacks[i].Id + ")" +
                              (stacks[i].Equipped ? "   [equipped]" : "");

                if (i < _rowLabels.Count) _rowLabels[i].text = text;
                else _rowLabels.Add(new GUIContent(text));
            }

            RebuildShown();
        }

        private void RebuildShown()
        {
            _shownFilter = _filter;
            string lower = _filter.Length > 0 ? _filter.ToLowerInvariant() : null;
            IList<ItemStack> stacks = Ctx.Inventory.Stacks;

            _shown.Clear();
            for (int i = 0; i < stacks.Count && i < _rowLabels.Count; i++)
                if (Matches(stacks[i], lower)) _shown.Add(i);
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
            if (Ctx.Inventory.Available)
                hud.Pair("Items", _total + "   (" + _stackCount + " stacks)");
            else
                hud.Pair("Items", "(inventory not resolved)");
            if (_logsHud != null) hud.Pair("Logs", _logsHud);

            for (int i = 0; i < _watchLines.Count; i++)
                hud.Pair("", "  " + _watchLines[i]);
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

        // Rebuilt on the throttle, not in OnGUI.
        private readonly GUIContent _summary = new GUIContent("");

        private void RefreshTitle()
        {
            _summary.text = (Ctx.Inventory.Available
                ? _total + " items in " + _stackCount + " stacks" +
                  (Ctx.Inventory.FilteredOut > 0 ? " (" + Ctx.Inventory.FilteredOut + " filtered)" : "")
                : "inventory not resolved - load a game") +
                (_watch.Count > 0 ? "   |   pinned: " + _watch.Count : "   |   click a row to pin it to the HUD");
        }

        private void DrawContents(int id)
        {
            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(GUI.skin.label);
                _rowStyle.alignment = TextAnchor.MiddleLeft;
                _rowStyle.padding = new RectOffset(4, 4, 0, 0);
            }

            float top = DrawLogs(4f);

            GUI.Label(new Rect(10, top, 46, 22), "Filter");
            _filter = GUI.TextField(new Rect(58, top, 200, 22), _filter);

            if (GUI.Button(new Rect(266, top, 56, 22), "Clear")) _filter = "";
            if (!ReferenceEquals(_filter, _shownFilter)) RebuildShown();
            if (GUI.Button(new Rect(326, top, 64, 22), "Refresh"))
            {
                Ctx.Inventory.Refresh();
                RebuildRowLabels();
            }

            // Exploration toggles: the filter hides ids the autosplitter
            // proved are not real inventory contents (dev id 302 and
            // anything outside 29-311), but seeing the raw list is exactly
            // the kind of thing this tool exists for.
            bool filter = GUI.Toggle(new Rect(10, top + 26, 130, 20),
                                     Ctx.Inventory.FilterPhantomItems, " hide phantoms");
            if (filter != Ctx.Inventory.FilterPhantomItems)
            {
                Ctx.Inventory.FilterPhantomItems = filter;
                _hidePhantomsCfg.Value = filter;
                Ctx.Inventory.Refresh();
                RebuildRowLabels();
            }

            bool zeros = GUI.Toggle(new Rect(146, top + 26, 120, 20),
                                    Ctx.Inventory.ShowZeroAmounts, " show x0");
            if (zeros != Ctx.Inventory.ShowZeroAmounts)
            {
                Ctx.Inventory.ShowZeroAmounts = zeros;
                _showZerosCfg.Value = zeros;
                Ctx.Inventory.Refresh();
                RebuildRowLabels();
            }

            float y = top + 50f + UiText.Draw(10, top + 50f, _tabW - 20, _summary);

            Rect listRect = new Rect(8, y, _tabW - 16, _tabH - y - 10);
            DrawList(listRect);

        }

        // The gameplay mod, above the list. Returns the y below it.
        private float DrawLogs(float y)
        {
            float w = _tabW - 20f;
            bool on = GUI.Toggle(new Rect(10, y, w, 22), _logsCfg.Value,
                                 " Logs in the inventory (gameplay mod, practice)");
            if (on != _logsCfg.Value) _logsCfg.Value = on;
            y += 24f;
            if (!_logsCfg.Value) return y + 4f;

            GUI.Label(new Rect(30, y, 110, 22), "Logs it holds");
            string t = GUI.TextField(new Rect(140, y, 50, 22), _capText);
            if (t != _capText)
            {
                _capText = t;
                int cap;
                if (int.TryParse(t, out cap) && cap >= 1 && cap <= 99) _logCapCfg.Value = cap;
            }
            y += 26f;
            y += UiText.Draw(30, y, w - 20f, _logsStatus) + 2f;
            y += UiText.DrawDim(30, y, w - 20f, LogsText) + 8f;
            return y;
        }

        private void DrawList(Rect listRect)
        {
            IList<ItemStack> stacks = Ctx.Inventory.Stacks;

            Rect content = new Rect(0, 0, listRect.width - 20f, _shown.Count * RowHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, content);

            // Virtualised: only rows inside the viewport are drawn. Drawing
            // every row on every OnGUI pass is what caused the GC spike the
            // type explorer had to be rewritten to avoid.
            int first = Mathf.Max(0, (int)(_scroll.y / RowHeight) - 1);
            int visible = (int)(listRect.height / RowHeight) + 3;

            int last = Mathf.Min(_shown.Count - 1, first + visible);
            for (int thisRow = first; thisRow <= last; thisRow++)
            {
                int i = _shown[thisRow];
                if (i >= stacks.Count || i >= _rowLabels.Count) continue;

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
            RefreshTitle();
        }
    }
}
