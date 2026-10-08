using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Keybinds and global toggles.
    //
    // Rebinding in game rather than only through the .cfg, because the
    // key you want to change is usually the one you just discovered
    // clashes with something - and at that point you are in the game, not
    // in a text editor. Writes go to the same BepInEx ConfigEntry the file
    // exposes, so both routes stay in sync.
    //
    // Capture uses Event.current during OnGUI rather than polling Input,
    // because Input.GetKeyDown cannot see which key was pressed without
    // testing every KeyCode, and Event gives it directly.
    // ------------------------------------------------------------------
    public sealed class SettingsModule : OverlayModule
    {
        private const float RowHeight = 26f;

        public override string Id { get { return "settings"; } }
        public override string DisplayName { get { return "Settings"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Settings"; } }
        public override int TabOrder { get { return 60; } }


        // Key names: KeyCode.ToString() allocates, and the list asked for
        // one per row on every OnGUI pass.
        private readonly Dictionary<KeyCode, GUIContent> _keyLabels = new Dictionary<KeyCode, GUIContent>();
        private static readonly GUIContent PressText = new GUIContent("press...");
        private static readonly GUIContent UnboundText = new GUIContent("unbound");
        private HotkeyMap.Binding _promptFor;

        private GUIStyle _labelStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _promptStyle;
        private readonly GUIContent _prompt = new GUIContent();
        private string _message = "";

        // The page: folds, one scroll (T-0226).
        private Vector2 _pageScroll;
        private float _pageH = 900f;   // measured on the last pass
        private DebugViewModule _views;
        private SavestateModule _savestates;
        private static readonly GUIContent TextKeys = new GUIContent("Keys");
        private static readonly GUIContent TipKeys = new GUIContent(
            "Click a key to rebind it (Esc cancels, Backspace unbinds); def puts the default back.");
        private static readonly GUIContent TextHud = new GUIContent("Info box (HUD)");
        private static readonly GUIContent TipHud = new GUIContent(
            "The values on screen: which lines show, their size and place, and the HUD layout editor.");
        private static readonly GUIContent TextPerf = new GUIContent("Performance");
        private static readonly GUIContent TipPerf = new GUIContent(
            "Patches that make the game do less work each frame without changing what it does. On by default.");
        private static readonly GUIContent TextLoads = new GUIContent("Loads and savestates");
        private static readonly GUIContent TipLoads = new GUIContent(
            "What a restore puts back, and two fixes for what the game leaves behind after a load.");
        private GUIContent[] _hudNames;
        private GUIContent[] _hudDescriptions;
        private static readonly GUIContent HudIntro = new GUIContent(
            "Tick the lines you want. Hover one for what it shows.");
        private static readonly GUIContent EditLayoutText = new GUIContent("Edit HUD layout");
        private static readonly GUIContent EditLayoutTip = new GUIContent(
            "Move, resize and show / hide each value; drag a value out of the box to make it a widget of its own " +
            "(layout file: config/ForestOverlay/hud-layout.txt).");
        private static readonly GUIContent CompactText = new GUIContent(" Compact: fewer words");
        private static readonly GUIContent InfoBoxText = new GUIContent(" Info box");
        private static readonly GUIContent InfoBoxTip = new GUIContent(
            "The old box of values at the top left. Off, values you want on screen are HUD widgets (Edit HUD layout); practice and ON NOW warnings always show.");
        private static readonly GUIContent CompactNote = new GUIContent(
            "Shorter values (no stack count, no units), labels without column padding, a short title.");
        private static readonly GUIContent LockedNote = new GUIContent("always shown");
        private readonly GUIContent _sizeText = new GUIContent("");
        private readonly GUIContent _placeText = new GUIContent("");
        private int _sizeShown = -1;
        private float _placeShownX = float.NaN, _placeShownY = float.NaN;

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.settings", KeyCode.None, "Open Settings tab", OpenMyTab);
        }

        public override void Tick()
        {
            // Never leave a capture armed once the tab is gone; it would eat
            // the next key press the moment the window reopened. (This was
            // OnPanelToggled, which never fires for a tab.)
            if (Host.Hotkeys.AwaitingRebind != null && !TabShowing) Host.Hotkeys.AwaitingRebind = null;

            // The HUD view's changing text, built here (not in OnGUI) and
            // only when it changed.
            if (!TabShowing) return;
            HudSettings s = Host.Hud.Settings;
            if (s.TextSize != _sizeShown)
            {
                _sizeShown = s.TextSize;
                _sizeText.text = "Text size: " + HudLines.SizeText(_sizeShown);
            }
            if (s.X != _placeShownX || s.Y != _placeShownY)
            {
                _placeShownX = s.X;
                _placeShownY = s.Y;
                _placeText.text = "Position: " + Mathf.RoundToInt(s.X) + ", " + Mathf.RoundToInt(s.Y) + " (from the top left)";
            }
        }

        private float _tabW;
        private float _tabH;

        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;
            if (_views == null) _views = Host.Find<DebugViewModule>();
            if (_savestates == null) _savestates = Host.Find<SavestateModule>();
            DrawContents();
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.alignment = TextAnchor.MiddleLeft;

            _warnStyle = new GUIStyle(_labelStyle);
            _warnStyle.normal.textColor = new Color(1f, 0.55f, 0.2f);

            // The prompt is long and was clipping to one line. Wrapping it
            // and measuring the height keeps it readable at any width.
            _promptStyle = new GUIStyle(_warnStyle);
            _promptStyle.wordWrap = true;
            _promptStyle.alignment = TextAnchor.UpperLeft;
        }

        private void DrawContents()
        {
            EnsureStyles();
            HotkeyMap map = Host.Hotkeys;

            // One page of folds (T-0226, author 2026-10-08: "a settings tab
            // with folds ... clear and easy to navigate").
            Rect area = new Rect(0, 0, _tabW, _tabH);
            bool scrolls = _pageH > _tabH;
            float w = scrolls ? _tabW - 20f : _tabW;
            _pageScroll = GUI.BeginScrollView(area, _pageScroll, new Rect(0, 0, w, Mathf.Max(_pageH, _tabH)));

            float y = 4f;
            bool lockPlayer = GUI.Toggle(new Rect(12, y, w - 24, 22),
                                         Host.LockPlayerWhilePanelOpen,
                                         " Hold player and block game input while open");
            if (lockPlayer != Host.LockPlayerWhilePanelOpen) Host.SetLockPlayer(lockPlayer);
            y += 28f;

            if (UiKit.Section(0f, ref y, w, "settings.keys", TextKeys, null, TipKeys, true))
                y = DrawKeys(y, w, map);
            else if (map.AwaitingRebind != null)
                map.AwaitingRebind = null;   // a closed fold never keeps a capture armed

            if (UiKit.Section(0f, ref y, w, "settings.hud", TextHud, null, TipHud, false))
                y = DrawHudSettings(y, w);

            if (_views != null && UiKit.Section(0f, ref y, w, "settings.perf", TextPerf, null, TipPerf, false))
                y = _views.DrawPerformance(y, w) + 6f;

            if (_savestates != null && UiKit.Section(0f, ref y, w, "settings.loads", TextLoads, null, TipLoads, false))
                y = _savestates.DrawOptions(12f, y, w - 24f) + 6f;

            _pageH = y + 4f;
            GUI.EndScrollView();

            // Capture has to run before DragWindow, or dragging swallows
            // the key event we are waiting for.
            if (map.AwaitingRebind != null) CaptureKey(map);
        }

        private float DrawKeys(float y, float w, HotkeyMap map)
        {
            bool rebinding = map.AwaitingRebind != null;
            if (rebinding && !ReferenceEquals(_promptFor, map.AwaitingRebind))
            {
                _promptFor = map.AwaitingRebind;
                _prompt.text = "Press a key for: " + _promptFor.Description + "      Esc cancels, Backspace unbinds";
            }
            else if (!rebinding)
            {
                _promptFor = null;
                _prompt.text = _message.Length > 0 ? _message : StatusLine();
            }

            if (GUI.Button(new Rect(12, y, 118, 22), "Reset all keys"))
            {
                map.ResetToDefaults();
                _message = "Keys reset to defaults.";
            }
            y += 26f;

            // Where the click was: the prompt / result under the button.
            GUIStyle promptStyle = rebinding ? _promptStyle : _labelStyle;
            float promptW = w - 24f;
            float promptH = _prompt.text.Length == 0
                ? 0f
                : Mathf.Max(20f, promptStyle.CalcHeight(_prompt, promptW));
            GUI.Label(new Rect(12, y, promptW, promptH), _prompt, promptStyle);
            y += promptH + 4f;

            return DrawBindList(y, w, map) + 6f;
        }

        // --- the info box (HUD) -------------------------------------------
        // Every line with a tick box and what it shows (Data/HudLines);
        // the honest-labelling lines are listed as "always shown". Each
        // click writes the config once (gotcha 60).
        private float DrawHudSettings(float y, float cw)
        {
            HudSettings s = Host.Hud.Settings;
            if (_hudNames == null)
            {
                _hudNames = new GUIContent[HudLines.All.Length];
                _hudDescriptions = new GUIContent[HudLines.All.Length];
                for (int i = 0; i < HudLines.All.Length; i++)
                {
                    _hudNames[i] = new GUIContent(" " + HudLines.All[i].Name);
                    _hudDescriptions[i] = new GUIContent(HudLines.All[i].Description);
                }
            }

            Rect editR = new Rect(12, y, 170, 28);
            if (UiKit.PrimaryButton(editR, EditLayoutText))
            {
                MainWindowModule main = Host.Find<MainWindowModule>();
                if (main != null) main.BeginHudEdit();
            }
            UiKit.Hint(editR, EditLayoutTip);
            y += 34f;
            y += UiText.Note(4, y, cw - 8, HudIntro) + 4f;

            bool box = GUI.Toggle(new Rect(4, y, cw - 8, 22), s.InfoBox, InfoBoxText);
            if (box != s.InfoBox) s.InfoBox = box;
            UiKit.Hint(new Rect(4, y, cw - 8, 22), InfoBoxTip);
            y += 24f;
            bool compact = GUI.Toggle(new Rect(4, y, cw - 8, 22), s.Compact, CompactText);
            if (compact != s.Compact) s.Compact = compact;
            y += 22f;
            y += UiText.Note(28, y, cw - 32, CompactNote) + 4f;

            GUI.Label(new Rect(4, y, 160, 22), _sizeText, _labelStyle);
            if (GUI.Button(new Rect(168, y, 30, 22), "-")) s.TextSize = HudLines.StepTextSize(s.TextSize, -1);
            if (GUI.Button(new Rect(202, y, 30, 22), "+")) s.TextSize = HudLines.StepTextSize(s.TextSize, +1);
            if (GUI.Button(new Rect(236, y, 70, 22), "default")) s.TextSize = 0;
            y += 26f;

            y += Mathf.Max(22f, UiText.Draw(4, y, cw - 8, _placeText));
            if (GUI.Button(new Rect(4, y, 130, 22), "Reset position")) s.SetPosition(HudSettings.DefaultX, HudSettings.DefaultY);
            if (GUI.Button(new Rect(140, y, 150, 22), "Reset the info box"))
            {
                s.ResetLook();
                CollectiblesModule totals = Host.Find<CollectiblesModule>();
                if (totals != null) totals.TotalsOnHud = false;
            }
            y += 30f;

            GUI.Label(new Rect(4, y, cw - 8, 22), "Lines", _labelStyle);
            y += 22f;
            for (int i = 0; i < HudLines.All.Length; i++)
            {
                HudLine l = HudLines.All[i];
                if (l.Locked)
                {
                    GUI.Label(new Rect(8, y, 200, 22), _hudNames[i], _labelStyle);
                    GUI.Label(new Rect(212, y, cw - 216, 22), LockedNote, _warnStyle);
                }
                else if (l.External)
                {
                    CollectiblesModule totals = Host.Find<CollectiblesModule>();
                    bool on = totals != null && totals.TotalsOnHud;
                    bool now = GUI.Toggle(new Rect(4, y, cw - 8, 22), on, _hudNames[i]);
                    if (now != on && totals != null) totals.TotalsOnHud = now;
                }
                else
                {
                    bool on = s.Shows(i);
                    Rect row = new Rect(4, y, cw - 8, 22);
                    bool now = GUI.Toggle(row, on, _hudNames[i]);
                    if (now != on) s.SetShows(i, now);
                    UiKit.Hint(row, _hudDescriptions[i]);
                }
                y += 24f;
            }

            return y + 6f;
        }

        // Rebuilt only when the status text changes, not on every OnGUI pass.
        private string _statusSource;
        private string _statusLine = "";

        private string StatusLine()
        {
            string s = Host.InputStatus;
            if (!ReferenceEquals(s, _statusSource))
            {
                _statusSource = s;
                _statusLine = "Game input: " + s;
            }
            return _statusLine;
        }

        // The rows, inline in the page (the page scrolls). Returns the y under them.
        private float DrawBindList(float top, float w, HotkeyMap map)
        {
            IList<HotkeyMap.Binding> binds = map.Bindings;

            Rect content = new Rect(8, top, w - 16f, binds.Count * RowHeight);

            for (int i = 0; i < binds.Count; i++)
            {
                HotkeyMap.Binding b = binds[i];
                float y = top + i * RowHeight;

                GUI.Label(new Rect(4, y, content.width - 244f, RowHeight), b.Description, _labelStyle);

                HotkeyMap.Binding clash = map.Conflict(b.Key, b);
                if (clash != null)
                {
                    GUI.Label(new Rect(content.width - 236f, y, 60f, RowHeight), "clash", _warnStyle);
                }

                bool waiting = map.AwaitingRebind == b;
                GUIContent label = waiting ? PressText : (b.Key == KeyCode.None ? UnboundText : KeyLabel(b.Key));

                // Wide enough for the longest key name (KeypadMultiply was cut off).
                if (GUI.Button(new Rect(content.width - 172f, y + 2f, 124f, RowHeight - 6f), label))
                    map.AwaitingRebind = waiting ? null : b;

                if (GUI.Button(new Rect(content.width - 44f, y + 2f, 40f, RowHeight - 6f), "def"))
                {
                    b.Key = b.Default;
                    _message = b.Description + " reset to " + b.Default + ".";
                }
            }

            return top + binds.Count * RowHeight;
        }

        private GUIContent KeyLabel(KeyCode key)
        {
            GUIContent c;
            if (!_keyLabels.TryGetValue(key, out c)) _keyLabels[key] = c = new GUIContent(key.ToString());
            return c;
        }

        private void CaptureKey(HotkeyMap map)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            HotkeyMap.Binding target = map.AwaitingRebind;

            if (e.keyCode == KeyCode.Escape)
            {
                map.AwaitingRebind = null;
                _message = "Rebind cancelled.";
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Backspace)
            {
                target.Key = KeyCode.None;
                map.AwaitingRebind = null;
                map.Swallow(KeyCode.Backspace);
                _message = target.Description + " unbound.";
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.None) return;

            HotkeyMap.Binding clash = map.Conflict(e.keyCode, target);

            target.Key = e.keyCode;
            map.AwaitingRebind = null;

            // Stop this same press from also firing the action we just
            // bound it to.
            map.Swallow(e.keyCode);

            // Bind it anyway and say so, rather than refusing. Two actions
            // on one key is occasionally deliberate, and silently dropping
            // the press would be worse than a warning.
            _message = clash == null
                ? target.Description + " -> " + e.keyCode
                : target.Description + " -> " + e.keyCode + "  (also used by '" + clash.Description + "')";

            e.Use();
        }
    }
}
