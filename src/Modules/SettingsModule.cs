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
        private GUIStyle _lockedNameStyle;
        private GUIStyle _lockedStyle;
        private readonly GUIContent _prompt = new GUIContent();
        private readonly GUIContent _devPrompt = new GUIContent();
        private string _promptText = "";
        private bool _messageDev;   // the message is about a developer key (shown in the Developer tab)

        // The key list in groups (T-0263, Data/KeyGroups): each binding's
        // group, worked out once; the developer group is drawn by the
        // Developer tab (DrawDeveloperKeys).
        private int[] _groupOf = new int[0];
        private readonly int[] _groupCount = new int[KeyGroups.Headings.Length];
        private GUIContent[] _groupHeadings;
        private GUIContent[] _hudHeadings;
        private GUIStyle _groupStyle;
        // The frame each list's rows were last drawn (0 Settings, 1 Developer).
        private readonly int[] _rowsDrawnFrame = new int[2];
        // A rebind / reset result, shown in place of the Game input status
        // for a few seconds, then the status comes back (T-0261).
        private const float MessageSeconds = 5f;
        private string _message = "";
        private float _messageUntil;

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
        private static readonly GUIContent HudIntro = new GUIContent(
            "Tick the values you want on screen. Hover one for what it shows.");
        private static readonly GUIContent HudWideText = new GUIContent("The whole HUD");
        private static readonly GUIContent ValuesText = new GUIContent("Values");
        private static readonly GUIContent SizeTip = new GUIContent(
            "The column of values and the practice / ON NOW warnings. A value placed on its own is sized in Edit HUD layout.");
        private static readonly GUIContent PlaceTip = new GUIContent(
            "Where the column of values sits (top left by default). Drag it with Edit HUD on.");
        private static readonly GUIContent ResetValuesText = new GUIContent("Reset the values");
        private static readonly GUIContent ResetKeysText = new GUIContent("Reset all keys");
        private static readonly GUIContent ResetKeysTip = new GUIContent("Every key back to its default.");
        private static readonly GUIContent SureText = new GUIContent("Sure?");
        private static readonly GUIContent ValuesResetText = new GUIContent("Values reset to defaults.");
        // The resets ask once (T-0260): the first click arms "Sure?" for a
        // few seconds, a second click resets - as Edit HUD's profile Delete.
        private const float ConfirmSeconds = 3f;
        private float _resetKeysArmedUntil = -1f;
        private float _resetValuesArmedUntil = -1f;
        private float _valuesResetUntil = -1f;
        private static readonly GUIContent ResetValuesTip = new GUIContent(
            "Every value back to its default on / off. Look, position and widgets stay.");
        private static readonly GUIContent EditLayoutText = new GUIContent("Edit HUD layout");
        private static readonly GUIContent EditLayoutTip = new GUIContent(
            "Show / hide each value, add your own text around it, drag it out of the column to place it anywhere " +
            "and keep layouts as named profiles (files in config/ForestOverlay/hud).");
        private static readonly GUIContent CompactText = new GUIContent(" Compact: fewer words");
        private static readonly GUIContent CompactNote = new GUIContent(
            "Shorter values (no stack count, no units) and the short title.");
        // T-0318. Words proposed, not yet approved by the author.
        private static readonly GUIContent SizesFollowText = new GUIContent(" Sizes follow the screen");
        private static readonly GUIContent SizesFollowNote = new GUIContent(
            "On: the HUD, splits, results and run code keep the same look at every resolution. " +
            "Off: their sizes stay in pixels. Their places always stay put relative to the screen.");
        private static readonly GUIContent LockedNote = new GUIContent("always shown");
        // The trend colours (T-0023). Words proposed, not yet approved by the author.
        private static readonly GUIContent TrendTitle = new GUIContent("Trend colours");
        private static readonly GUIContent TrendTip = new GUIContent(
            "For the values with Trend on in Edit HUD layout: their colour while gaining or losing (steady uses the theme's text colour). Click a swatch or type a colour as #RRGGBB.");
        private static readonly GUIContent TrendBad = new GUIContent("Not a colour: type #RRGGBB, e.g. #33DD55.");
        private static readonly TrendState[] TrendRows = { TrendState.Gaining, TrendState.Losing };
        private static readonly GUIContent[] TrendNames =
        {
            new GUIContent("Gaining"), new GUIContent("Losing"),
        };
        private readonly string[] _trendEdit = new string[3];   // the field's text, by row
        private readonly string[] _trendFrom = new string[3];   // the stored text it was taken from
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
            // Never leave a capture armed once its row is off screen (the
            // tab, the fold or the window gone); it would eat the next key
            // press the moment the row came back. (This was OnPanelToggled,
            // which never fires for a tab.) The row can be in Settings or in
            // the Developer tab (T-0263).
            HotkeyMap.Binding awaiting = Host.Hotkeys.AwaitingRebind;
            if (awaiting != null && Time.frameCount - _rowsDrawnFrame[KeyGroups.IsDeveloper(awaiting.Id) ? 1 : 0] > 2)
                Host.Hotkeys.AwaitingRebind = null;

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

            // The HUD's always-shown values (T-0264): the name where a tick
            // box's label starts (the toggle style, which draws no box), and
            // "always shown" dim - locked, not a warning (author: dim grey,
            // tentative).
            _lockedNameStyle = UiKit.Style(GUI.skin.toggle);

            // Group headings in both lists (T-0263).
            _groupStyle = UiKit.Style(_labelStyle);
            _groupStyle.fontStyle = FontStyle.Bold;
            _groupStyle.normal.textColor = UiKit.SoftColour;
            _lockedStyle = UiKit.Style(_labelStyle);
            _lockedStyle.normal.textColor = UiKit.DimColour;
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
            else if (map.AwaitingRebind != null && !KeyGroups.IsDeveloper(map.AwaitingRebind.Id))
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
            if (map.AwaitingRebind != null) CaptureKey(map, false);
        }

        private float DrawKeys(float y, float w, HotkeyMap map)
        {
            EnsureGroups(map);
            Rect resetR = new Rect(12, y, 118, 22);
            bool armed = Time.unscaledTime <= _resetKeysArmedUntil;
            if (GUI.Button(resetR, armed ? SureText : ResetKeysText))
            {
                if (!armed) _resetKeysArmedUntil = Time.unscaledTime + ConfirmSeconds;
                else
                {
                    _resetKeysArmedUntil = -1f;
                    map.ResetToDefaults();
                    Say("Keys reset to defaults.");
                    Ctx.Log.LogInfo("Settings: all keys reset to their defaults.");
                }
            }
            UiKit.Hint(resetR, ResetKeysTip);
            y += 26f;

            // Where the click was: the prompt / result under the button.
            y = DrawPrompt(y, w, map, false, _prompt);

            // The runner's keys under short headings (T-0263); the
            // developer keys are in the Developer tab.
            for (int o = 0; o < KeyGroups.Order.Length; o++)
            {
                int g = KeyGroups.Order[o];
                if (_groupCount[g] == 0) continue;
                GUI.Label(new Rect(12, y, w - 24f, 22f), _groupHeadings[g], _groupStyle);
                y += 22f;
                y = DrawBindRows(y, w, map, g) + 4f;
            }
            return y + 2f;
        }

        /// The developer keys (dumps, type explorer, trajectory preview, TAS, QA) for the
        /// Developer tab's Keys fold (T-0263): the same rows, rebinding and
        /// messages as the runner's keys here. Returns the y under them.
        public float DrawDeveloperKeys(float y, float w)
        {
            EnsureStyles();
            HotkeyMap map = Host.Hotkeys;
            EnsureGroups(map);
            y = DrawPrompt(y, w, map, true, _devPrompt);
            y = DrawBindRows(y, w, map, KeyGroups.Developer);
            if (map.AwaitingRebind != null) CaptureKey(map, true);
            return y + 6f;
        }

        private void EnsureGroups(HotkeyMap map)
        {
            IList<HotkeyMap.Binding> binds = map.Bindings;
            if (_groupHeadings == null)
            {
                _groupHeadings = new GUIContent[KeyGroups.Headings.Length];
                for (int g = 0; g < _groupHeadings.Length; g++) _groupHeadings[g] = new GUIContent(KeyGroups.Headings[g]);
            }
            if (_groupOf.Length == binds.Count) return;
            _groupOf = new int[binds.Count];
            for (int g = 0; g < _groupCount.Length; g++) _groupCount[g] = 0;
            for (int i = 0; i < binds.Count; i++)
            {
                _groupOf[i] = KeyGroups.Of(binds[i].Id);
                _groupCount[_groupOf[i]]++;
            }
        }

        /// The capture prompt while one of this list's keys waits for a
        /// press, else the last message about this list; Settings shows the
        /// Game input status when there is none. Returns the y under it.
        private float DrawPrompt(float y, float w, HotkeyMap map, bool dev, GUIContent prompt)
        {
            HotkeyMap.Binding awaiting = map.AwaitingRebind;
            bool rebinding = awaiting != null && KeyGroups.IsDeveloper(awaiting.Id) == dev;
            if (rebinding)
            {
                if (!ReferenceEquals(_promptFor, awaiting))
                {
                    _promptFor = awaiting;
                    _promptText = "Press a key for: " + awaiting.Description + "      Esc cancels, Backspace unbinds";
                }
                prompt.text = _promptText;
            }
            else
            {
                if (_message.Length > 0 && Time.unscaledTime > _messageUntil) _message = "";
                prompt.text = _message.Length > 0 && _messageDev == dev ? _message : (dev ? "" : StatusLine());
            }

            if (prompt.text.Length == 0) return y;
            GUIStyle promptStyle = rebinding ? _promptStyle : _labelStyle;
            float promptW = w - 24f;
            float promptH = Mathf.Max(20f, promptStyle.CalcHeight(prompt, promptW));
            GUI.Label(new Rect(12, y, promptW, promptH), prompt, promptStyle);
            return y + promptH + 4f;
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
                _hudHeadings = new GUIContent[HudLines.All.Length];
                for (int i = 0; i < HudLines.All.Length; i++)
                {
                    _hudNames[i] = new GUIContent(" " + HudLines.All[i].Name);
                    string heading = HudLines.HeadingAt(i);
                    if (heading != null) _hudHeadings[i] = new GUIContent(heading);
                }
            }
            // The tips name the runner's keys, not the defaults (T-0262).
            GUIContent[] tips = HudLineTips.Get(Host.Hotkeys);

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

            Rect followR = new Rect(4, y, cw - 8, 22);
            bool follow = UiKit.Toggle(followR, s.SizesFollow, SizesFollowText);
            if (follow != s.SizesFollow)
            {
                s.SizesFollow = follow;
                Ctx.Log.LogInfo("HUD sizes " + (follow ? "follow the screen." : "stay in pixels."));
            }
            UiKit.Hint(followR, SizesFollowNote);
            y += 28f;

            y = DrawTrendColours(y, cw, s);

            GUI.Label(new Rect(4, y, cw - 8, 22), ValuesText, _labelStyle);
            UiKit.Hint(new Rect(4, y, cw - 8, 22), HudIntro);
            y += 22f;
            for (int i = 0; i < HudLines.All.Length; i++)
            {
                HudLine l = HudLines.All[i];
                // The values under short headings (T-0263).
                if (_hudHeadings[i] != null)
                {
                    GUI.Label(new Rect(4, y + 2f, cw - 8, 22), _hudHeadings[i], _groupStyle);
                    y += 24f;
                }
                // Every row's description is its hover tip (T-0259).
                Rect row = new Rect(4, y, cw - 8, 22);
                if (l.Locked)
                {
                    GUI.Label(new Rect(4, y, 208, 22), _hudNames[i], _lockedNameStyle);
                    GUI.Label(new Rect(212, y, cw - 216, 22), LockedNote, _lockedStyle);
                }
                else if (l.External)
                {
                    bool on = s.ExternalShows;
                    bool now = UiKit.Toggle(row, on, _hudNames[i]);
                    if (now != on) s.SetExternalShows(now);
                }
                else
                {
                    bool on = s.Shows(i);
                    bool now = UiKit.Toggle(row, on, _hudNames[i]);
                    if (now != on) s.SetShows(i, now);
                }
                UiKit.Hint(row, tips[i]);
                y += 24f;
            }

            Rect resetR = new Rect(4, y + 2f, 150, 22);
            bool armed = Time.unscaledTime <= _resetValuesArmedUntil;
            if (GUI.Button(resetR, armed ? SureText : ResetValuesText))
            {
                if (!armed) _resetValuesArmedUntil = Time.unscaledTime + ConfirmSeconds;
                else
                {
                    _resetValuesArmedUntil = -1f;
                    s.ResetValues();
                    s.SetExternalShows(false);
                    Ctx.Log.LogInfo("Settings: HUD values reset to their defaults.");
                    _valuesResetUntil = Time.unscaledTime + MessageSeconds;
                }
            }
            UiKit.Hint(resetR, ResetValuesTip);
            y += 30f;
            // What it did, under the click.
            if (Time.unscaledTime <= _valuesResetUntil)
                y += UiText.Draw(4, y, cw - 8, ValuesResetText) + 4f;

            return y + 6f;
        }

        // The three trend colours: a swatch, the #RRGGBB field (a valid colour
        // is written as it is typed - one write per valid edit, not per key)
        // and its default. A field that is not a colour says so under it.
        private float DrawTrendColours(float y, float cw, HudSettings s)
        {
            GUI.Label(new Rect(4, y, cw - 8, 22), TrendTitle, _labelStyle);
            UiKit.Hint(new Rect(4, y, cw - 8, 22), TrendTip);
            y += 22f;
            for (int r = 0; r < TrendRows.Length; r++)
            {
                TrendState state = TrendRows[r];
                string stored = s.TrendText(state);
                if (!ReferenceEquals(stored, _trendFrom[r]))
                {
                    // A profile switch or a default: the field follows.
                    _trendFrom[r] = stored;
                    _trendEdit[r] = stored ?? "";
                }

                GUI.Label(new Rect(8, y, 80, 22), TrendNames[r], _labelStyle);
                Color old = GUI.color;
                GUI.color = s.TrendColour(state);
                GUI.DrawTexture(new Rect(92, y + 3, 16, 16), Texture2D.whiteTexture);
                GUI.color = old;
                string typed = GUI.TextField(new Rect(114, y, 90, 22), _trendEdit[r], 9);
                if (!string.Equals(typed, _trendEdit[r]))
                {
                    _trendEdit[r] = typed;
                    if (s.SetTrendText(state, typed)) _trendFrom[r] = s.TrendText(state);   // keep the text as typed
                }
                if (GUI.Button(new Rect(210, y, 70, 22), "default"))
                {
                    // The field follows even when the stored colour already is the
                    // default (bad text typed over it changes nothing stored).
                    s.SetTrendText(state, HudTrend.Format(HudSettings.TrendDefaults[(int)state]));
                    _trendFrom[r] = s.TrendText(state);
                    _trendEdit[r] = _trendFrom[r] ?? "";
                }
                y += 24f;
                // The presets: one click sets the colour (the field follows).
                for (int k = 0; k < HudTrend.Presets.Length; k++)
                {
                    Rect sw = new Rect(114 + k * 22, y + 1, 18, 18);
                    uint pc = HudTrend.Presets[k];
                    Color keep = GUI.color;
                    GUI.color = new Color(UiPalette.R(pc), UiPalette.G(pc), UiPalette.B(pc), 1f);
                    GUI.DrawTexture(sw, Texture2D.whiteTexture);
                    GUI.color = keep;
                    if (GUI.Button(sw, GUIContent.none, GUIStyle.none))
                    {
                        s.SetTrendText(state, HudTrend.Format(pc));
                        _trendFrom[r] = s.TrendText(state);
                        _trendEdit[r] = _trendFrom[r] ?? "";
                    }
                }
                y += 22f;
                uint parsed;
                if (!HudTrend.TryParse(_trendEdit[r], out parsed))
                    y += UiText.DrawDim(114, y, cw - 118, TrendBad) + 2f;
            }
            return y + 6f;
        }

        private void Say(string message)
        {
            Say(message, false);
        }

        private void Say(string message, bool dev)
        {
            _message = message;
            _messageDev = dev;
            _messageUntil = Time.unscaledTime + MessageSeconds;
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

        // One group's rows, inline in the page (the page scrolls). Returns
        // the y under them.
        private float DrawBindRows(float top, float w, HotkeyMap map, int group)
        {
            IList<HotkeyMap.Binding> binds = map.Bindings;
            bool dev = group == KeyGroups.Developer;
            _rowsDrawnFrame[dev ? 1 : 0] = Time.frameCount;

            float contentW = w - 16f;
            float y = top;

            for (int i = 0; i < binds.Count; i++)
            {
                if (_groupOf[i] != group) continue;
                HotkeyMap.Binding b = binds[i];

                GUI.Label(new Rect(12, y, contentW - 252f, RowHeight), b.Description, _labelStyle);

                HotkeyMap.Binding clash = map.Conflict(b.Key, b);
                if (clash != null)
                {
                    GUI.Label(new Rect(contentW - 236f, y, 60f, RowHeight), "clash", _warnStyle);
                }

                bool waiting = map.AwaitingRebind == b;
                GUIContent label = waiting ? PressText : (b.Key == KeyCode.None ? UnboundText : KeyLabel(b.Key));

                // Wide enough for the longest key name (KeypadMultiply was cut off).
                if (GUI.Button(new Rect(contentW - 172f, y + 2f, 124f, RowHeight - 6f), label))
                    map.AwaitingRebind = waiting ? null : b;

                if (GUI.Button(new Rect(contentW - 44f, y + 2f, 40f, RowHeight - 6f), "def"))
                {
                    b.Key = b.Default;
                    Say(b.Description + " reset to " + b.Default + ".", dev);
                    Ctx.Log.LogInfo("Settings: key '" + b.Description + "' reset to " + b.Default + ".");
                }
                y += RowHeight;
            }

            return y;
        }

        private GUIContent KeyLabel(KeyCode key)
        {
            GUIContent c;
            if (!_keyLabels.TryGetValue(key, out c)) _keyLabels[key] = c = new GUIContent(key.ToString());
            return c;
        }

        // `dev`: the list drawing this pass; a capture belongs to its row's list.
        private void CaptureKey(HotkeyMap map, bool dev)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            HotkeyMap.Binding target = map.AwaitingRebind;
            if (KeyGroups.IsDeveloper(target.Id) != dev) return;

            if (e.keyCode == KeyCode.Escape)
            {
                map.AwaitingRebind = null;
                Say("Rebind cancelled.", dev);
                Ctx.Log.LogInfo("Settings: rebind of '" + target.Description + "' cancelled.");
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Backspace)
            {
                target.Key = KeyCode.None;
                map.AwaitingRebind = null;
                map.Swallow(KeyCode.Backspace);
                Say(target.Description + " unbound.", dev);
                Ctx.Log.LogInfo("Settings: key '" + target.Description + "' unbound.");
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
            Ctx.Log.LogInfo("Settings: key '" + target.Description + "' rebound to " + e.keyCode
                + (clash == null ? "." : " (also used by '" + clash.Description + "')."));
            Say(clash == null
                ? target.Description + " -> " + e.keyCode
                : target.Description + " -> " + e.keyCode + "  (also used by '" + clash.Description + "')", dev);

            e.Use();
        }
    }
}
