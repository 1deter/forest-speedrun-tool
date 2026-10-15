using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The always-on lines (ON NOW, the practice marker, run mode's line)
    // drawn as the game's own bottom-left messages (author, T-0325;
    // decisions.md *Plugin*).
    //
    // HOW (docs/game-notes.md *The bottom-left HUD messages*): the game's
    // lines are copies of a template under one NGUI grid, HudGui.Grid; a
    // todo message is HudGui._todoListMessageView (a UILabel "Name" and a
    // tick sprite). Each line here is OUR OWN copy of that template
    // (Instantiate, never the "misc" pool: pooled copies are reused by the
    // game's own messages, so a restyle would leak into them - bridge,
    // 2026-10-15). The copies sit in no HudGui timed dictionary, so the
    // game never expires them, and they are kept the grid's last children:
    // the lowest lines, the game's passing messages above them. Higher
    // contrast than the game's plain yellow (a black outline), no
    // translation, and the game's own icons in the template's icon slot
    // (its "Book") so they stand apart from the game's messages (author,
    // 2026-10-15): "cog" on an ON NOW flag, "Rewind" on the practice
    // marker (sprite names from the HUD atlas, bridge).
    //
    // Where the grid is not on screen (inventory open, the game's HUD off,
    // the HUD camera off in a cutscene / sleep, the title) Mode is Overlay
    // and the plugin draws the same lines at Anchor - the same spot - so a
    // recording always shows them. Members missing (a game update): one
    // warning, the overlay text for good.
    //
    // Runs from Update; text is applied only when it changes.
    // ------------------------------------------------------------------
    public sealed class GameMessageLines
    {
        private const string LineName = "ForestOverlay line";
        // The game's label colour (0.897, 0.798, 0) lifted a little, with a
        // black outline: readable on snow and sky too.
        private static readonly Color TextColour = new Color(1f, 0.88f, 0.15f, 1f);
        private static readonly Color OutlineColour = new Color(0f, 0f, 0f, 1f);
        // The marker's warning (run mode's not-valid line): the overlay
        // fallback's orange, so both modes say it the same way.
        private static readonly Color WarnColour = new Color(1f, 0.55f, 0.2f, 1f);
        private const string FlagIcon = "cog", MarkerIcon = "Rewind";

        private readonly ManualLogSource _log;
        private readonly GameLinesState _state = new GameLinesState();
        private readonly List<GameObject> _copies = new List<GameObject>();
        private readonly List<Component> _labels = new List<Component>();
        private readonly List<Component> _icons = new List<Component>();
        private readonly List<string> _iconNames = new List<string>();

        private bool _resolved, _missing;
        private FieldInfo _gridF, _templateF, _camF;
        private PropertyInfo _textP, _colourP, _effectP, _effectColourP, _spriteP;
        private object _outline;
        private MethodInfo _reposition;

        private object _hud;
        private Component _grid;
        private GameObject _template;
        private Camera _cam;
        private int _applied = -1;
        private Vector2 _anchor;
        private bool _anchorMeasured;

        public GameMessageLines(ManualLogSource log)
        {
            _log = log;
        }

        public GameLinesMode Mode { get; private set; }

        /// The lines, top to bottom (the overlay fallback draws these).
        public List<string> Lines { get { return _state.Lines; } }

        /// Lines from here on are the practice marker's.
        public int MarkerStart { get { return _state.MarkerStart; } }

        /// Screen pixels, GUI space (y down): the left end and vertical
        /// middle of the lowest line.
        public Vector2 Anchor
        {
            get
            {
                if (_anchorMeasured) return _anchor;
                // Before a copy was ever shown: where the game's lowest line
                // sits (bridge shot, 1366x768: x 0.073, y 0.928).
                return new Vector2(Screen.width * 0.073f, Screen.height * 0.928f);
            }
        }

        /// Each frame. `visible`: the HUD would show these lines now;
        /// `warn`: the marker in its warning colour.
        public void Update(string onText, string markerText, bool warn, bool visible)
        {
            _state.SetText(visible ? onText : null, visible ? markerText : null, warn);
            Mode = GameLinesMode.Overlay;
            if (_missing) return;
            if (!_resolved && !Resolve()) return;

            object hud = GameBridge.ReadStaticField("TheForest.Utils.Scene", "HudGui");
            if (hud == null || !(hud is UnityEngine.Object) || (UnityEngine.Object)hud == null) { Forget(); return; }

            try
            {
                // Inside the try: a half-destroyed HudGui (a scene change)
                // takes the rebind path too.
                if (!ReferenceEquals(hud, _hud) || _grid == null || _template == null) Bind(hud);
                if (_grid == null || _template == null) return;

                bool onScreen = _grid.gameObject.activeInHierarchy && _cam != null && _cam.enabled && _cam.gameObject.activeInHierarchy;
                Mode = GameLinesState.Decide(true, onScreen);

                bool moved = false;
                if (_applied != _state.Version) { Apply(); moved = true; }
                moved |= KeepLowest();
                if (moved) Reposition();
                Measure();
            }
            catch (Exception ex)
            {
                Mode = GameLinesMode.Overlay;
                if (_state.GiveUpAfterFailure(Time.realtimeSinceStartup))
                    Fail("GameMessageLines: " + ex.Message + " (" + _state.Failures + " times) - the always-on lines are drawn by the overlay.");
                else
                {
                    _log.LogWarning("GameMessageLines: " + ex.Message + " - rebinding to the game's grid.");
                    Forget();
                }
            }
        }

        private bool Resolve()
        {
            _resolved = true;
            Type hudT = GameBridge.FindGameType("HudGui");
            Type labelT = GameBridge.FindGameType("UILabel");
            Type gridT = GameBridge.FindGameType("UIGrid");
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (hudT != null)
            {
                _gridF = hudT.GetField("Grid", F);
                _templateF = hudT.GetField("_todoListMessageView", F);
                _camF = hudT.GetField("GuiCamC", F);
            }
            if (labelT != null)
            {
                _textP = labelT.GetProperty("text", F);
                _colourP = labelT.GetProperty("color", F);
                _effectP = labelT.GetProperty("effectStyle", F);
                _effectColourP = labelT.GetProperty("effectColor", F);
            }
            Type spriteT = GameBridge.FindGameType("UISprite");
            if (spriteT != null) _spriteP = spriteT.GetProperty("spriteName", F);
            if (gridT != null) _reposition = gridT.GetMethod("Reposition", F, null, Type.EmptyTypes, null);
            if (_effectP != null)
            {
                try { _outline = Enum.Parse(_effectP.PropertyType, "Outline8"); }
                catch (Exception)
                {
                    try { _outline = Enum.Parse(_effectP.PropertyType, "Outline"); }
                    catch (Exception) { _outline = null; }
                }
            }
            string missing = GameLinesState.MissingMembers(
                new[] { "HudGui.Grid", "HudGui._todoListMessageView", "HudGui.GuiCamC", "UILabel.text", "UIGrid.Reposition" },
                new[] { _gridF != null, _templateF != null, _camF != null, _textP != null, _reposition != null });
            if (missing.Length > 0)
            {
                Fail("GameMessageLines: " + missing + " not found - the always-on lines are drawn by the overlay.");
                return false;
            }
            return true;
        }

        private void Fail(string message)
        {
            _missing = true;
            Mode = GameLinesMode.Overlay;
            if (_state.WarnMissing()) _log.LogWarning(message);   // log: GameMessageLines
            Forget();
        }

        // A new HudGui (a new scene): the old copies went with the old grid.
        private void Bind(object hud)
        {
            Forget();
            _hud = hud;
            _grid = _gridF.GetValue(hud) as Component;
            _template = _templateF.GetValue(hud) as GameObject;
            _cam = _camF.GetValue(hud) as Camera;
        }

        private void Forget()
        {
            for (int i = 0; i < _copies.Count; i++)
                if (_copies[i] != null) UnityEngine.Object.Destroy(_copies[i]);
            _copies.Clear();
            _labels.Clear();
            _icons.Clear();
            _iconNames.Clear();
            _hud = null;
            _grid = null;
            _template = null;
            _cam = null;
            _applied = -1;
        }

        private void Apply()
        {
            _applied = _state.Version;
            List<string> lines = _state.Lines;
            while (_copies.Count < lines.Count) MakeCopy();
            for (int i = 0; i < _copies.Count; i++)
            {
                bool used = i < lines.Count;
                if (used && _labels[i] != null)
                {
                    _textP.SetValue(_labels[i], lines[i], null);
                    if (_colourP != null) _colourP.SetValue(_labels[i], _state.LineWarns(i) ? WarnColour : TextColour, null);
                }
                if (used) SetIcon(i, i >= _state.MarkerStart ? MarkerIcon : FlagIcon);
                if (_copies[i] != null && _copies[i].activeSelf != used) _copies[i].SetActive(used);
            }
        }

        private void MakeCopy()
        {
            GameObject go = UnityEngine.Object.Instantiate(_template, _grid.transform, false);
            go.name = LineName;
            // The game's caps / translation would overwrite our text on enable.
            Behaviour[] behaviours = go.GetComponentsInChildren<Behaviour>(true);
            Component label = null;
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour b = behaviours[i];
                if (b == null) continue;
                string n = b.GetType().Name;
                if (n == "UiTranslationLabel") b.enabled = false;
                else if (n == "UILabel" && label == null) label = b;
            }
            // The icon slot (the game's "Book"): ours when UISprite resolved,
            // else hidden - the book says the survival book changed.
            Component icon = null;
            Transform slot = go.transform.Find("Tut ShowContainer/Texture tut");
            if (slot != null)
            {
                if (_spriteP != null)
                {
                    Component[] cs = slot.GetComponents<Component>();
                    for (int i = 0; i < cs.Length; i++)
                        if (cs[i] != null && cs[i].GetType().Name == "UISprite") { icon = cs[i]; break; }
                }
                slot.gameObject.SetActive(icon != null);
            }
            if (label != null)
            {
                if (_colourP != null) _colourP.SetValue(label, TextColour, null);
                if (_effectP != null && _outline != null) _effectP.SetValue(label, _outline, null);
                if (_effectColourP != null) _effectColourP.SetValue(label, OutlineColour, null);
            }
            _copies.Add(go);
            _labels.Add(label);
            _icons.Add(icon);
            _iconNames.Add(null);
        }

        private void SetIcon(int i, string name)
        {
            if (_icons[i] == null || _iconNames[i] == name) return;
            _spriteP.SetValue(_icons[i], name, null);
            _iconNames[i] = name;
        }

        // Ours lowest: the grid's last children, in order (author, 2026-10-15).
        private bool KeepLowest()
        {
            Transform grid = _grid.transform;
            int n = grid.childCount;
            int used = Math.Min(_state.Lines.Count, _copies.Count);
            bool ok = true;
            for (int i = 0; i < used && ok; i++)
                if (_copies[i] == null || _copies[i].transform.GetSiblingIndex() != n - used + i) ok = false;
            if (ok) return false;
            for (int i = 0; i < used; i++)
                if (_copies[i] != null) _copies[i].transform.SetAsLastSibling();
            return true;
        }

        private void Reposition()
        {
            if (_grid.gameObject.activeInHierarchy) _reposition.Invoke(_grid, null);
        }

        private void Measure()
        {
            if (Mode != GameLinesMode.Game || _cam == null) return;
            int last = Math.Min(_state.Lines.Count, _labels.Count) - 1;
            if (last < 0 || _labels[last] == null) return;
            Vector3 p = _cam.WorldToScreenPoint(_labels[last].transform.position);
            _anchor = new Vector2(p.x, Screen.height - p.y);
            _anchorMeasured = true;
        }
    }
}
