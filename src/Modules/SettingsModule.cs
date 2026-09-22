using System.Collections.Generic;
using ForestOverlay.Core;
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

        private Rect _windowRect;
        private bool _windowPlaced;
        private Vector2 _scroll;

        private GUIStyle _labelStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _promptStyle;
        private readonly GUIContent _prompt = new GUIContent();
        private string _message = "";

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.settings", KeyCode.None, "Open Settings tab", OpenMyTab);
        }

        public override void OnPanelToggled(bool open)
        {
            // Never leave a capture armed across a close; it would eat the
            // next key press the moment the panel reopened.
            if (!open && Host != null) Host.Hotkeys.AwaitingRebind = null;
        }

        private float _tabW;
        private float _tabH;

        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;
            DrawContents(0);
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

        private void DrawContents(int id)
        {
            EnsureStyles();
            HotkeyMap map = Host.Hotkeys;

            float w = _tabW;

            // --- global toggles --------------------------------------------
            bool lockPlayer = GUI.Toggle(new Rect(12, 28, 220, 22),
                                         Host.LockPlayerWhilePanelOpen,
                                         " Hold player while a panel is open");
            if (lockPlayer != Host.LockPlayerWhilePanelOpen) Host.SetLockPlayer(lockPlayer);

            if (GUI.Button(new Rect(w - 130, 28, 118, 22), "Reset all keys"))
            {
                map.ResetToDefaults();
                _message = "Keys reset to defaults.";
            }

            bool rebinding = map.AwaitingRebind != null;
            _prompt.text = rebinding
                ? "Press a key for: " + map.AwaitingRebind.Description +
                  "      Esc cancels, Backspace unbinds"
                : _message;

            GUIStyle promptStyle = rebinding ? _promptStyle : _labelStyle;
            float promptW = w - 24f;
            float promptH = _prompt.text.Length == 0
                ? 0f
                : Mathf.Max(20f, promptStyle.CalcHeight(_prompt, promptW));

            GUI.Label(new Rect(12, 54, promptW, promptH), _prompt, promptStyle);

            float listY = 58f + promptH;
            DrawBindList(new Rect(8, listY, w - 16, _tabH - listY - 10f), map);

            // Capture has to run before DragWindow, or dragging swallows
            // the key event we are waiting for.
            if (map.AwaitingRebind != null) CaptureKey(map);

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

                GUI.Label(new Rect(4, y, content.width - 200f, RowHeight), b.Description, _labelStyle);

                HotkeyMap.Binding clash = map.Conflict(b.Key, b);
                if (clash != null)
                {
                    GUI.Label(new Rect(content.width - 196f, y, 60f, RowHeight), "clash", _warnStyle);
                }

                bool waiting = map.AwaitingRebind == b;
                string label = waiting ? "press..." : (b.Key == KeyCode.None ? "unbound" : b.Key.ToString());

                if (GUI.Button(new Rect(content.width - 132f, y + 2f, 84f, RowHeight - 6f), label))
                    map.AwaitingRebind = waiting ? null : b;

                if (GUI.Button(new Rect(content.width - 44f, y + 2f, 40f, RowHeight - 6f), "def"))
                {
                    b.Key = b.Default;
                    _message = b.Description + " reset to " + b.Default + ".";
                }
            }

            GUI.EndScrollView();
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
