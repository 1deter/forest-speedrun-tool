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

        private Vector2 _scroll;

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

        // Keys or the info box (HUD) below the global toggles.
        private bool _hudView;
        private bool _paintView;
        private Vector2 _hudScroll;
        private float _hudContentH = 900f;   // measured on the last pass
        private GUIContent[] _hudNames;
        private GUIContent[] _hudDescriptions;
        private static readonly GUIContent HudIntro = new GUIContent(
            "The info box in the top left. Untick a line to hide it. To move the box, drag it with the mouse " +
            "while this window is open.");
        private static readonly GUIContent CompactText = new GUIContent(" Compact: fewer words");
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
            if (!TabShowing || !_hudView) return;
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

            float w = _tabW;

            // --- global toggles --------------------------------------------
            bool lockPlayer = GUI.Toggle(new Rect(12, 28, w - 160, 22),
                                         Host.LockPlayerWhilePanelOpen,
                                         " Hold player and block game input while open");
            if (lockPlayer != Host.LockPlayerWhilePanelOpen) Host.SetLockPlayer(lockPlayer);

            if (GUI.Button(new Rect(w - 130, 28, 118, 22), "Reset all keys"))
            {
                map.ResetToDefaults();
                _message = "Keys reset to defaults.";
            }

            // The live test bridge (developer tool): toggle and what it is doing.
            float y = 54f;
            BridgeModule bridge = Host.Find<BridgeModule>();
            if (bridge != null)
            {
                bool on = GUI.Toggle(new Rect(12, y, w - 24, 22), bridge.Enabled,
                                     " Test bridge (developer tool): run commands from bridge/in.txt");
                if (on != bridge.Enabled) bridge.Enabled = on;
                y += 24f;
                y += UiText.DrawDim(12, y, w - 24, bridge.StatusText);
            }

            // Keys | Info box (HUD) | Paint
            bool keysView = GUI.Toggle(new Rect(12, y, 120, 22), !_hudView && !_paintView, "Keys", GUI.skin.button);
            bool hudView = GUI.Toggle(new Rect(136, y, 160, 22), _hudView, "Info box (HUD)", GUI.skin.button);
            bool paintView = GUI.Toggle(new Rect(300, y, 90, 22), _paintView, "Paint", GUI.skin.button);
            if (keysView && (_hudView || _paintView)) { _hudView = false; _paintView = false; }
            else if (hudView && !_hudView) { _hudView = true; _paintView = false; map.AwaitingRebind = null; }
            else if (paintView && !_paintView) { _paintView = true; _hudView = false; map.AwaitingRebind = null; }
            y += 28f;

            if (_hudView)
            {
                DrawHudSettings(new Rect(8, y, w - 16, _tabH - y - 10f));
                return;
            }

            if (_paintView)
            {
                PaintModule paint = Host.Find<PaintModule>();
                if (paint != null) paint.DrawSettings(new Rect(8, y, w - 16, _tabH - y - 10f));
                return;
            }

            bool rebinding = map.AwaitingRebind != null;
            if (rebinding && !ReferenceEquals(_promptFor, map.AwaitingRebind))
            {
                _promptFor = map.AwaitingRebind;
                _prompt.text = "Press a key or mouse button for: " + _promptFor.Description + "      Esc cancels, Backspace unbinds";
            }
            else if (!rebinding)
            {
                _promptFor = null;
                _prompt.text = _message.Length > 0 ? _message : StatusLine();
            }

            GUIStyle promptStyle = rebinding ? _promptStyle : _labelStyle;
            float promptW = w - 24f;
            float promptH = _prompt.text.Length == 0
                ? 0f
                : Mathf.Max(20f, promptStyle.CalcHeight(_prompt, promptW));

            GUI.Label(new Rect(12, y, promptW, promptH), _prompt, promptStyle);

            float listY = y + 4f + promptH;
            DrawBindList(new Rect(8, listY, w - 16, _tabH - listY - 10f), map);

            // Capture has to run before DragWindow, or dragging swallows
            // the key event we are waiting for.
            if (map.AwaitingRebind != null) CaptureKey(map);

        }

        // --- the info box (HUD) -------------------------------------------
        // Every line with a tick box and what it shows (Data/HudLines);
        // the honest-labelling lines are listed as "always shown". Each
        // click writes the config once (gotcha 60).
        private void DrawHudSettings(Rect area)
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

            float cw = area.width - 20f;
            _hudScroll = GUI.BeginScrollView(area, _hudScroll, new Rect(0, 0, cw, _hudContentH));
            float y = 0f;
            y += UiText.Draw(4, y, cw - 8, HudIntro) + 4f;

            bool compact = GUI.Toggle(new Rect(4, y, cw - 8, 22), s.Compact, CompactText);
            if (compact != s.Compact) s.Compact = compact;
            y += 22f;
            y += UiText.DrawDim(28, y, cw - 32, CompactNote) + 4f;

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
                    bool now = GUI.Toggle(new Rect(4, y, cw - 8, 22), on, _hudNames[i]);
                    if (now != on) s.SetShows(i, now);
                }
                y += 22f;
                y += UiText.DrawDim(28, y, cw - 32, _hudDescriptions[i]) + 2f;
            }

            _hudContentH = y + 8f;
            GUI.EndScrollView();
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

        private void DrawBindList(Rect listRect, HotkeyMap map)
        {
            IList<HotkeyMap.Binding> binds = map.Bindings;

            Rect content = new Rect(0, 0, listRect.width - 20f, binds.Count * RowHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, content);

            for (int i = 0; i < binds.Count; i++)
            {
                HotkeyMap.Binding b = binds[i];
                float y = i * RowHeight;

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
                    _message = b.Description + " reset to " + HotkeyMap.KeyName(b.Default) + ".";
                }
            }

            GUI.EndScrollView();
        }

        private GUIContent KeyLabel(KeyCode key)
        {
            GUIContent c;
            if (!_keyLabels.TryGetValue(key, out c)) _keyLabels[key] = c = new GUIContent(HotkeyMap.KeyName(key));
            return c;
        }

        private void CaptureKey(HotkeyMap map)
        {
            HotkeyMap.Binding target = map.AwaitingRebind;

            // Mouse buttons (middle and the side ones; left / right click the
            // window) never come as key events: polled.
            for (KeyCode m = KeyCode.Mouse2; m <= KeyCode.Mouse6; m++)
            {
                if (!Input.GetKeyDown(m)) continue;
                Bind(map, target, m);
                return;
            }

            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

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

            Bind(map, target, e.keyCode);
            e.Use();
        }

        private void Bind(HotkeyMap map, HotkeyMap.Binding target, KeyCode key)
        {
            HotkeyMap.Binding clash = map.Conflict(key, target);

            target.Key = key;
            map.AwaitingRebind = null;

            // Stop this same press from also firing the action we just
            // bound it to.
            map.Swallow(key);

            // Bind it anyway and say so, rather than refusing. Two actions
            // on one key is occasionally deliberate, and silently dropping
            // the press would be worse than a warning.
            string name = HotkeyMap.KeyName(key);
            _message = clash == null
                ? target.Description + " -> " + name
                : target.Description + " -> " + name + "  (also used by '" + clash.Description + "')";
        }
    }
}
