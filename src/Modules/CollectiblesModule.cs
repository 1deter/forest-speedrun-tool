using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // 100% tracking. INFO-ONLY.
    //
    // Tracked here:
    //   1. a unique-item collection (weapons, story documents, drawings,
    //      tapes, toy pieces, keycards...)
    //   2. the survival book's nature guide - animals, birds, fish,
    //      plants, grouped by the book page they are on
    //   3. the survival book's To Do List
    //
    // The collection list is DATA (config/ForestOverlay/collectibles), not
    // code, because what counts is an admin decision that will change
    // without the plugin changing.
    //
    // An earlier version showed the in-game bestiary instead. That was
    // wrong - it is not part of the requirement, and the game carries two
    // bestiary components so it also rendered twice.
    //
    // Collection state LATCHES: once an item has been seen in the
    // inventory it stays ticked. Story items persist, but latching means
    // the checklist cannot un-tick itself if something is dropped, and it
    // survives an item being consumed mid-run.
    //
    // Flower and plant COORDINATES are deliberately absent - the author
    // judged that over the line for the category.
    // ------------------------------------------------------------------
    public sealed class CollectiblesModule : OverlayModule
    {
        private const float RefreshInterval = 1f;
        private const float RowHeight = 19f;

        public override string Id { get { return "collectibles"; } }
        public override string DisplayName { get { return "100%"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "100%"; } }
        public override int TabOrder { get { return 45; } }

        private SurvivalBookReader _book;
        private NatureGuideReader _nature;
        private CollectionList _list;
        private float _nextRefresh;
        private bool _resolved;

        private float _tabW;
        private float _tabH;
        private Vector2 _scroll;

        private bool _showFound = true;
        private bool _showMissing = true;
        private bool _pinSummary;

        private GUIStyle _rowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _doneStyle;
        private GUIStyle _missingStyle;
        private GUIStyle _warnStyle;

        private readonly List<GUIContent> _labels = new List<GUIContent>();
        private readonly List<int> _kinds = new List<int>();   // 0 header, 1 done, 2 missing, 3 warn
        private int _rowCount;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _book = new SurvivalBookReader(ctx.Log);
            _nature = new NatureGuideReader(ctx.Log, ctx.Inventory.NameForId);
            _list = new CollectionList(ctx.Log, ctx.ConfigDirectory);
            _list.WriteReadmeIfMissing();
            _list.Reload();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.collectibles", KeyCode.None, "Open 100% tab", OpenMyTab);
        }

        public override void Tick()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            _book.Refresh();
            _nature.Refresh();

            // Names resolve once the item catalogue exists, which needs the
            // game loaded - so keep trying until it takes.
            if (!_resolved)
            {
                Ctx.Inventory.BuildCatalog();
                if (Ctx.Inventory.Catalog.Count > 0)
                {
                    _list.Resolve(FindItemId);
                    _resolved = true;
                }
            }

            UpdateSeen();
            RebuildRows();
        }

        /// Exact name match first, then a unique substring. Deliberately
        /// refuses an ambiguous match rather than guessing, so a wrong tick
        /// never appears.
        private int FindItemId(string name)
        {
            IList<ItemInfo> catalog = Ctx.Inventory.Catalog;
            string lower = name.ToLowerInvariant();

            for (int i = 0; i < catalog.Count; i++)
                if (catalog[i].Name.ToLowerInvariant() == lower) return catalog[i].Id;

            int found = -1;

            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Name.ToLowerInvariant().IndexOf(lower, System.StringComparison.Ordinal) < 0)
                    continue;

                if (found >= 0) return -1;   // ambiguous
                found = catalog[i].Id;
            }

            return found;
        }

        private void UpdateSeen()
        {
            Ctx.Inventory.Resolve();
            Ctx.Inventory.Refresh();

            IList<ItemStack> stacks = Ctx.Inventory.Stacks;
            IList<CollectionEntry> entries = _list.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                CollectionEntry e = entries[i];
                if (e.ItemId < 0) continue;

                int held = 0;
                for (int s = 0; s < stacks.Count; s++)
                    if (stacks[s].Id == e.ItemId) held += stacks[s].Amount;

                // Latched: the highest count seen, so the checklist
                // cannot un-tick itself when something is used up.
                if (held > e.Held) e.Held = held;
                if (e.Held >= e.Required) e.Seen = true;
            }
        }

        public override void ContributeHud(HudBuilder hud)
        {
            if (!_pinSummary) return;

            if (_list.Total > 0) hud.Pair("Items", _list.SeenCount + "/" + _list.Total);
            if (_nature.Entries.Count > 0) hud.Pair("Nature", _nature.TickedCount + "/" + _nature.Entries.Count);
            if (_book.Todo.Count > 0) hud.Pair("Tasks", _book.TodoDone + "/" + _book.Todo.Count);
        }

        // ------------------------------------------------------------------
        private void RebuildRows()
        {
            int n = 0;

            // --- collection ------------------------------------------------
            n = Add(n, "UNIQUE COLLECTION   " + _list.SeenCount + "/" + _list.Total, 0);

            if (_list.Unresolved > 0)
            {
                n = Add(n, "  " + _list.Unresolved +
                           " name(s) did not match an item - shown below as (?)", 3);
            }

            // An empty list once rendered as a bare "0/0" header, which a
            // runner reported as "everything is missing".
            if (_list.Total == 0)
            {
                n = Add(n, "  No checklist loaded - expected a .txt file in", 3);
                n = Add(n, "  " + _list.Folder, 3);
            }

            IList<string> categories = _list.Categories;
            IList<CollectionEntry> entries = _list.Entries;

            for (int c = 0; c < categories.Count; c++)
            {
                string category = categories[c];
                n = Add(n, "  " + category + "   " + _list.SeenIn(category) + "/" + _list.TotalIn(category), 0);

                for (int i = 0; i < entries.Count; i++)
                {
                    CollectionEntry e = entries[i];
                    if (e.Category != category) continue;

                    if (e.Seen && !_showFound) continue;
                    if (!e.Seen && !_showMissing) continue;

                    if (e.ItemId < 0)
                    {
                        n = Add(n, "      " + e.Name + "   -   (?) not recognised", 3);
                        continue;
                    }

                    string state = e.Seen ? "collected" : "missing";
                    if (e.Required > 1) state += "  " + e.Held + "/" + e.Required;

                    n = Add(n, "      " + e.Name + "   -   " + state, e.Seen ? 1 : 2);
                }
            }

            // --- nature guide ----------------------------------------------
            n = Add(n, "", 0);
            n = Add(n, "NATURE GUIDE   " + _nature.TickedCount + "/" + _nature.Entries.Count, 0);

            if (_nature.Entries.Count == 0)
                n = Add(n, "  " + _nature.Status, 3);

            IList<NaturePage> pages = _nature.Pages;
            IList<NatureEntry> nature = _nature.Entries;

            for (int p = 0; p < pages.Count; p++)
            {
                n = Add(n, "  " + pages[p].Name + "   " + pages[p].Ticked + "/" + pages[p].Total, 0);

                for (int i = 0; i < nature.Count; i++)
                {
                    NatureEntry e = nature[i];
                    if (e.PageIndex != p) continue;

                    if (e.Ticked && !_showFound) continue;
                    if (!e.Ticked && !_showMissing) continue;

                    n = Add(n, "      " + e.Name + "   -   " + (e.Ticked ? "found" : "not found"),
                            e.Ticked ? 1 : 2);
                }
            }

            // --- todo ------------------------------------------------------
            if (_book.Todo.Count > 0)
            {
                n = Add(n, "", 0);
                n = Add(n, "TO DO LIST   " + _book.TodoDone + "/" + _book.Todo.Count, 0);

                for (int i = 0; i < _book.Todo.Count; i++)
                {
                    BookEntry task = _book.Todo[i];

                    if (task.Done && !_showFound) continue;
                    if (!task.Done && !_showMissing) continue;

                    n = Add(n, "      " + task.Name + "   -   " + (task.Done ? "done" : "to do"),
                            task.Done ? 1 : 2);
                }
            }

            _rowCount = n;
        }

        private int Add(int index, string text, int kind)
        {
            if (index < _labels.Count)
            {
                _labels[index].text = text;
                _kinds[index] = kind;
            }
            else
            {
                _labels.Add(new GUIContent(text));
                _kinds.Add(kind);
            }

            return index + 1;
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;
            EnsureStyles();

            float w = _tabW;

            bool found = GUI.Toggle(new Rect(0, 2, 110, 20), _showFound, " collected");
            if (found != _showFound) { _showFound = found; RebuildRows(); }

            bool missing = GUI.Toggle(new Rect(116, 2, 110, 20), _showMissing, " missing");
            if (missing != _showMissing) { _showMissing = missing; RebuildRows(); }

            bool pin = GUI.Toggle(new Rect(w - 190, 2, 190, 20), _pinSummary, " show totals on the HUD");
            if (pin != _pinSummary) _pinSummary = pin;

            if (GUI.Button(new Rect(w - 300, 26, 106, 22), "Write dumps"))
                DumpItems();

            if (GUI.Button(new Rect(w - 190, 26, 90, 22), "Reload list"))
            {
                _list.Reload();
                _resolved = false;
            }

            if (GUI.Button(new Rect(w - 90, 26, 90, 22), "Refresh"))
            {
                _book.Refresh();
                UpdateSeen();
                RebuildRows();
            }

            // Under the buttons that produce them: the dump result, then
            // where the list and the book came from.
            RefreshTabText();
            float y = 52f;
            y += UiText.Draw(0, y, w, _dumpText);
            y += UiText.DrawDim(0, y, w, _sourceText);

            DrawList(new Rect(0, y + 2f, w, _tabH - y - 6f));
        }

        /// Writes every id and name the game knows, so a checklist can
        /// be authored against the real names rather than guessed ones.
        private void DumpItems()
        {
            Ctx.Inventory.BuildCatalog();

            if (Ctx.Inventory.Catalog.Count == 0)
            {
                _dumpStatus = "no items yet - " + Ctx.Inventory.CatalogStatus;
                return;
            }

            try
            {
                string path = GameDumper.WriteItemCatalogue(Ctx.Log, Ctx.Inventory.Catalog);
                _dumpStatus = "wrote " + Ctx.Inventory.Catalog.Count + " items";

                // The nature guide only exists in a loaded save.
                if (_nature.Entries.Count > 0)
                {
                    _nature.WriteDump();
                    _dumpStatus += " + nature guide";
                }

                _dumpStatus += " -> " + System.IO.Path.GetDirectoryName(path);
            }
            catch (System.Exception ex)
            {
                _dumpStatus = "dump failed - see log";
                Ctx.Log.LogError("Item dump failed: " + ex);
            }
        }

        private string _dumpStatus = "";

        // Tab text, rebuilt only when a source string changes (a reference
        // compare per pass, no allocation otherwise).
        private readonly GUIContent _dumpText = new GUIContent("");
        private readonly GUIContent _sourceText = new GUIContent("");
        private readonly GUIContent _emptyText = new GUIContent("");
        private string _dumpShown, _listShown, _bookShown;

        private void RefreshTabText()
        {
            if (!ReferenceEquals(_dumpStatus, _dumpShown)) { _dumpShown = _dumpStatus; _dumpText.text = _dumpStatus; }

            string list = _list.Status, book = _book.Status;
            if (ReferenceEquals(list, _listShown) && ReferenceEquals(book, _bookShown)) return;
            _listShown = list;
            _bookShown = book;
            _sourceText.text = list + "   |   " + book;
            _emptyText.text = "Nothing to show yet.\n\n" + list;
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null) return;

            _rowStyle = new GUIStyle(GUI.skin.label);
            _rowStyle.alignment = TextAnchor.MiddleLeft;
            _rowStyle.padding = new RectOffset(2, 2, 0, 0);

            _headerStyle = new GUIStyle(_rowStyle);
            _headerStyle.fontStyle = FontStyle.Bold;

            // Muted rather than saturated, for the same reason the zone
            // colours were toned down.
            _doneStyle = new GUIStyle(_rowStyle);
            _doneStyle.normal.textColor = new Color(0.45f, 0.82f, 0.50f);

            _missingStyle = new GUIStyle(_rowStyle);
            _missingStyle.normal.textColor = new Color(0.92f, 0.72f, 0.32f);

            _warnStyle = new GUIStyle(_rowStyle);
            _warnStyle.normal.textColor = new Color(0.90f, 0.45f, 0.45f);
        }

        private void DrawList(Rect area)
        {
            GUI.Box(area, GUIContent.none);

            Rect content = new Rect(0, 0, area.width - 20f, _rowCount * RowHeight + 4f);
            _scroll = GUI.BeginScrollView(area, _scroll, content);

            for (int i = 0; i < _rowCount && i < _labels.Count; i++)
            {
                float y = 2f + i * RowHeight;
                if (y + RowHeight < _scroll.y || y > _scroll.y + area.height) continue;

                GUIStyle style;
                switch (_kinds[i])
                {
                    case 0: style = _headerStyle; break;
                    case 1: style = _doneStyle; break;
                    case 3: style = _warnStyle; break;
                    default: style = _missingStyle; break;
                }

                GUI.Label(new Rect(4f, y, content.width - 8f, RowHeight), _labels[i], style);
            }

            GUI.EndScrollView();

            if (_rowCount == 0)
            {
                UiText.Draw(area.x + 8, area.y + 8, area.width - 16, _emptyText);
            }
        }
    }
}
