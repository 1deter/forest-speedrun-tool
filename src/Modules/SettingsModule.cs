using System.Collections.Generic;
using BepInEx.Configuration;
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
        private static readonly GUIContent TextHud = new GUIContent("HUD");
        private static readonly GUIContent TipHud = new GUIContent(
            "The values on screen: the HUD layout editor, the whole HUD's look and which values show.");
        private static readonly GUIContent TextTheme = new GUIContent("Theme");
        private static readonly GUIContent TipTheme = new GUIContent(
            "The window's colours. Changes at once.");
        private GUIContent[] _themeNames, _themeTips;
        private string[] _themeLog;
        private ConfigEntry<string> _theme;
        private static readonly GUIContent TextPerf = new GUIContent("Performance");
        private static readonly GUIContent TipPerf = new GUIContent(
            "Patches that make the game do less work each frame without changing what it does. On by default.");
        private static readonly GUIContent TextLoads = new GUIContent("Loads and savestates");
        private static readonly GUIContent TipLoads = new GUIContent(
            "What a restore puts back, and two fixes for what the game leaves behind after a load.");
        private GUIContent[] _hudNames;
        private GUIContent[] _hudDescriptions;
        private static readonly GUIContent HudIntro = new GUIContent(
            "Tick the values you want on screen. Hover one for what it shows.");
        private static readonly GUIContent HudWideText = new GUIContent("The whole HUD");
        private static readonly GUIContent ValuesText = new GUIContent("Values");
        private static readonly GUIContent SizeTip = new GUIContent(
            "The column of values and the practice / ON NOW warnings. A value placed on its own is sized in Edit HUD layout.");
        private static readonly GUIContent PlaceTip = new GUIContent(
            "Where the column of values sits (top left by default). Drag it while this window is open.");
        private static readonly GUIContent ResetValuesText = new GUIContent("Reset the values");
        private static readonly GUIContent ResetValuesTip = new GUIContent(
            "Every value back to its default on / off. Look, position and widgets stay.");
        private static readonly GUIContent EditLayoutText = new GUIContent("Edit HUD layout");
        private static readonly GUIContent EditLayoutTip = new GUIContent(
            "Show / hide each value, add your own text around it, drag it out of the column to place it anywhere " +
            "and keep layouts as named profiles (files in config/ForestOverlay/hud).");
        private static readonly GUIContent CompactText = new GUIContent(" Compact: fewer words");
        private static readonly GUIContent CompactNote = new GUIContent(
            "Shorter values (no stack count, no units) and the short title.");
        private static readonly GUIContent LockedNote = new GUIContent("always shown");
        private readonly GUIContent _sizeText = new GUIContent("");
        private readonly GUIContent _placeText = new GUIContent("");
        private int _sizeShown = -1;
        private float _placeShownX = float.NaN, _placeShownY = float.NaN;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            // T-0258 (author, 2026-10-11): preset themes, the runner picks.
            _theme = ctx.Config.Bind("Window", "Theme", UiPalette.Default.Id,
                "The window's colours: site, black, warm, translucent, bluegrey, catppuccin-mocha, catppuccin-macchiato or catppuccin-frappe.");
            UiKit.Apply(UiPalette.Find(_theme.Value));
            int n = UiPalette.All.Length;
            _themeNames = new GUIContent[n];
            _themeTips = new GUIContent[n];
            _themeLog = new string[n];
            for (int i = 0; i < n; i++)
            {
                _themeNames[i] = new GUIContent(" " + UiPalette.All[i].Name);
                _themeTips[i] = new GUIContent(UiPalette.All[i].Description);
                _themeLog[i] = "Theme: " + UiPalette.All[i].Id;
            }
        }

        private float DrawTheme(float y, float w)
        {
            for (int i = 0; i < UiPalette.All.Length; i++)
            {
                UiPalette p = UiPalette.All[i];
                bool on = UiKit.Toggle(new Rect(12, y, w - 24, 22), UiKit.Palette == p, _themeNames[i]);
                if (on && UiKit.Palette != p)
                {
                    _theme.Value = p.Id;
                    UiKit.Apply(p);
                    Ctx.Log.LogInfo(_themeLog[i]);   // log: Theme
                }
                y += 22f;
                y += UiText.DrawDim(34, y, w - 46, _themeTips[i]) + 4f;
            }
            return y + 4f;
        }

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

            _labelStyle = UiKit.Style(GUI.skin.label);
            _labelStyle.alignment = TextAnchor.MiddleLeft;

            _warnStyle = UiKit.Style(_labelStyle);
            _warnStyle.normal.textColor = new Color(1f, 0.55f, 0.2f);

            // The prompt is long and was clipping to one line. Wrapping it
            // and measuring the height keeps it readable at any width.
            _promptStyle = UiKit.Style(_warnStyle);
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
            bool lockPlayer = UiKit.Toggle(new Rect(12, y, w - 24, 22),
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

            if (UiKit.Section(0f, ref y, w, "settings.theme", TextTheme, null, TipTheme, false))
                y = DrawTheme(y, w);

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

        // --- the HUD values -------------------------------------------
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

            Rect editR = new Rect(4, y, 170, 28);
            if (UiKit.PrimaryButton(editR, EditLayoutText))
            {
                MainWindowModule main = Host.Find<MainWindowModule>();
                if (main != null) main.BeginHudEdit();
            }
            UiKit.Hint(editR, EditLayoutTip);
            y += 34f;

            // HUD-wide options first, then the values (T-0253,
            // decisions.md *Easy to learn*): compact, size and position act
            // with the box off too (widgets, the practice / ON NOW warnings),
            // so none of them sits under the box.
            GUI.Label(new Rect(4, y, cw - 8, 22), HudWideText, _labelStyle);
            y += 22f;
            Rect compactR = new Rect(4, y, cw - 8, 22);
            bool compact = UiKit.Toggle(compactR, s.Compact, CompactText);
            if (compact != s.Compact) s.Compact = compact;
            UiKit.Hint(compactR, CompactNote);
            y += 24f;

            GUI.Label(new Rect(4, y, 160, 22), _sizeText, _labelStyle);
            UiKit.Hint(new Rect(4, y, 160, 22), SizeTip);
            if (GUI.Button(new Rect(168, y, 30, 22), "-")) s.TextSize = HudLines.StepTextSize(s.TextSize, -1);
            if (GUI.Button(new Rect(202, y, 30, 22), "+")) s.TextSize = HudLines.StepTextSize(s.TextSize, +1);
            if (GUI.Button(new Rect(236, y, 70, 22), "default")) s.TextSize = 0;
            y += 26f;

            float placeH = Mathf.Max(22f, UiText.Draw(4, y, cw - 8, _placeText));
            UiKit.Hint(new Rect(4, y, cw - 8, placeH), PlaceTip);
            y += placeH;
            if (GUI.Button(new Rect(4, y, 130, 22), "Reset position")) s.SetPosition(HudSettings.DefaultX, HudSettings.DefaultY);
            y += 30f;

            GUI.Label(new Rect(4, y, cw - 8, 22), ValuesText, _labelStyle);
            UiKit.Hint(new Rect(4, y, cw - 8, 22), HudIntro);
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
                    bool on = s.ExternalShows;
                    bool now = UiKit.Toggle(new Rect(4, y, cw - 8, 22), on, _hudNames[i]);
                    if (now != on) s.SetExternalShows(now);
                }
                else
                {
                    bool on = s.Shows(i);
                    Rect row = new Rect(4, y, cw - 8, 22);
                    bool now = UiKit.Toggle(row, on, _hudNames[i]);
                    if (now != on) s.SetShows(i, now);
                    UiKit.Hint(row, _hudDescriptions[i]);
                }
                y += 24f;
            }

            Rect resetR = new Rect(4, y + 2f, 150, 22);
            if (GUI.Button(resetR, ResetValuesText))
            {
                s.ResetValues();
                s.SetExternalShows(false);
            }
            UiKit.Hint(resetR, ResetValuesTip);
            y += 30f;

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

                GUI.Label(new Rect(12, y, content.width - 252f, RowHeight), b.Description, _labelStyle);

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
