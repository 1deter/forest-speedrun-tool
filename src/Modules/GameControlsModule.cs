using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Extra game bindings (T-0343, author 2026-10-19): any of the game's
    // actions on one more key, mouse button or wheel direction, beside the
    // game's own bindings (e.g. the wheel to jump). Set in Settings > Game
    // controls (drawn there through Host.Find, like Paint), saved in
    // `[Game controls] Bindings` (Data/ExtraBindings), put into the game's
    // own Rewired maps by Game/ExtraBindingMaps.
    //
    // Not practice-only and never refused in run mode, with no flag: the
    // author counts it the same as macro software (2026-10-19).
    // ------------------------------------------------------------------
    public sealed class GameControlsModule : OverlayModule
    {
        public override string Id { get { return "gamecontrols"; } }
        public override string DisplayName { get { return "Game controls"; } }

        private const float RowH = 22f;
        private const int PickRows = 8;

        private sealed class Row
        {
            public ExtraBinding B;
            public KeyCode Key;              // the input as a KeyCode (None for the wheel): the hotkey clash
            public HotkeyMap.Binding Clash;  // the overlay hotkey on it when the note was built
            public readonly GUIContent Text = new GUIContent("");
            public readonly GUIContent Note = new GUIContent("");
        }

        private sealed class Pick
        {
            public ExtraBindingMaps.ActionInfo A;
            public bool Negative;
            public GUIContent Label;
            public string Search;   // lower case: label and name
        }

        private ConfigEntry<string> _cfg;
        private List<ExtraBinding> _list = new List<ExtraBinding>();
        private readonly List<Row> _rows = new List<Row>();
        private bool _rowsDirty = true;
        private int _rowsVersion = -1;
        private bool _writing;

        // Add: pick an action, then capture its input.
        private readonly List<Pick> _picks = new List<Pick>();
        private readonly List<int> _shown = new List<int>();
        private int _picksFor = -1;
        private bool _picking;
        private string _filter = "", _filterShown;
        private Vector2 _pickScroll;
        private Pick _armed;
        private int _armedFrame, _eatFrame = -10, _drawnFrame = -10;
        private bool _eatClick;
        private readonly GUIContent _prompt = new GUIContent("");
        private readonly GUIContent _status = new GUIContent("");
        private readonly GUIContent _waiting = new GUIContent("");

        private static KeyCode[] _keys;   // every keyboard KeyCode, polled while capturing

        private static readonly GUIContent Intro = new GUIContent(
            "Put one of the game's actions on one more key, mouse button or wheel direction, e.g. jump on the wheel. " +
            "The game's own keys keep working and its Controls menu does not change. Allowed in runs.");
        private static readonly GUIContent NoneText = new GUIContent("No extra bindings yet.");
        private static readonly GUIContent AddText = new GUIContent("Add a binding");
        private static readonly GUIContent RemoveText = new GUIContent("Remove");
        private static readonly GUIContent CancelText = new GUIContent("Cancel");
        private static readonly GUIContent PickText = new GUIContent("Pick the game action (type to narrow the list):");
        private static readonly GUIContent NoMatchText = new GUIContent("No action matches.");

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _cfg = ctx.Config.Bind("Game controls", "Bindings", "",
                "Extra game bindings, beside the game's own: Action=Input, separated by commas, e.g. " +
                "Jump=WheelUp, Crouch=Mouse3, Vertical-=K. Action is the game's action name (a - picks an axis's " +
                "negative side); Input is a key name, Mouse0..Mouse6, WheelUp or WheelDown. Set them in Settings > Game controls.");
            Load();
            _cfg.SettingChanged += OnConfigChanged;
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            if (_writing) return;
            Load();
        }

        private void Load()
        {
            List<string> skipped = new List<string>();
            _list = ExtraBindings.Parse(_cfg.Value, KeyName, skipped);
            for (int i = 0; i < skipped.Count; i++)
                Ctx.Log.LogWarning("Game controls: skipped '" + skipped[i] + "' in [Game controls] Bindings - not Action=Input with a key, Mouse0..Mouse6, WheelUp or WheelDown.");
            ExtraBindingMaps.SetBindings(_list, Ctx.Log, OverlayPlugin.PluginGuid);
            _rowsDirty = true;
        }

        private void Save()
        {
            _writing = true;
            try { _cfg.Value = ExtraBindings.Format(_list); }
            finally { _writing = false; }
        }

        /// A keyboard key's KeyCode name, or null (mouse, joystick, None, junk).
        private static string KeyName(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsDigit(s[0]) || s[0] == '-') return null;
            try
            {
                KeyCode k = (KeyCode)Enum.Parse(typeof(KeyCode), s, true);
                if (k == KeyCode.None || k >= KeyCode.Mouse0 || !Enum.IsDefined(typeof(KeyCode), k)) return null;
                return k.ToString();
            }
            catch (Exception) { return null; }
        }

        private static KeyCode InputKey(ExtraBinding b)
        {
            if (b.Kind == BindInput.Wheel) return KeyCode.None;
            if (b.Kind == BindInput.MouseButton) return KeyCode.Mouse0 + (b.Element - ExtraBindings.FirstButtonElement);
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), b.Input); }
            catch (Exception) { return KeyCode.None; }
        }

        // --- each frame --------------------------------------------------------------------------

        public override void Tick()
        {
            ExtraBindingMaps.Tick(Ctx.Log, OverlayPlugin.PluginGuid);

            bool shown = Time.frameCount - _drawnFrame <= 2;
            if (_armed != null)
            {
                // Never left armed with its fold off screen: it would eat the next press.
                if (!shown) Disarm();
                else
                {
                    Host.Hotkeys.HoldCapture();
                    PollCapture();
                }
            }
            if (_eatClick && Time.frameCount > _eatFrame + 1 && !Input.GetKey(KeyCode.Mouse0) && !Input.GetKey(KeyCode.Mouse1))
                _eatClick = false;

            if (!shown) return;
            if (_rowsVersion != ExtraBindingMaps.Version) _rowsDirty = true;
            if (!_rowsDirty && Time.frameCount % 30 == 0)
            {
                // An overlay hotkey moved onto (or off) a bound input: no allocation to check.
                for (int i = 0; i < _rows.Count; i++)
                    if (Host.Hotkeys.Conflict(_rows[i].Key, null) != _rows[i].Clash) { _rowsDirty = true; break; }
            }
            if (_rowsDirty) RebuildRows();
            if (_picksFor != ExtraBindingMaps.Actions.Count) BuildPicks();
            if (_picking && !string.Equals(_filter, _filterShown, StringComparison.Ordinal)) Narrow();
        }

        private void PollCapture()
        {
            float wheel = Input.mouseScrollDelta.y;   // up is positive
            if (wheel != 0f)
            {
                Bind(wheel > 0f ? ExtraBindings.WheelUp : ExtraBindings.WheelDown);
                return;
            }
            if (!Input.anyKeyDown) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Host.Hotkeys.Swallow(KeyCode.Escape);
                Disarm();
                _status.text = "Cancelled.";
                return;
            }
            for (KeyCode m = KeyCode.Mouse0; m <= KeyCode.Mouse6; m++)
            {
                if (!Input.GetKeyDown(m)) continue;
                // The click that armed the capture must not bind itself.
                if ((m == KeyCode.Mouse0 || m == KeyCode.Mouse1) && Time.frameCount <= _armedFrame) continue;
                if (m <= KeyCode.Mouse1) _eatClick = true;   // nor click the window under it
                Host.Hotkeys.Swallow(m);
                Bind("Mouse" + (int)(m - KeyCode.Mouse0));
                return;
            }
            if (_keys == null) _keys = KeyboardKeys();
            for (int i = 0; i < _keys.Length; i++)
            {
                if (!Input.GetKeyDown(_keys[i])) continue;
                Host.Hotkeys.Swallow(_keys[i]);
                Bind(_keys[i].ToString());
                return;
            }
        }

        private static KeyCode[] KeyboardKeys()
        {
            List<KeyCode> keys = new List<KeyCode>();
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                if (k != KeyCode.None && k < KeyCode.Mouse0 && !keys.Contains(k)) keys.Add(k);
            return keys.ToArray();
        }

        /// Called by Settings before it draws its page: the press that binds
        /// must not also scroll the page, type into a field or click a button.
        public void CaptureEvents()
        {
            Event e = Event.current;
            if (e == null) return;
            bool capturing = _armed != null || Time.frameCount == _eatFrame;
            if (capturing && (e.type == EventType.ScrollWheel || e.type == EventType.KeyDown)) e.Use();
            if (_eatClick && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.MouseDrag))
            {
                if (e.type == EventType.MouseUp) _eatClick = false;
                e.Use();
            }
        }

        private void Arm(Pick p)
        {
            _armed = p;
            _armedFrame = Time.frameCount;
            _picking = false;
            GUIUtility.keyboardControl = 0;   // the filter field gives the keys back
            _prompt.text = p.A.LabelFor(p.Negative) + ": press a key, click a mouse button or scroll the wheel. Esc cancels.";
            _status.text = "";
        }

        private void Disarm()
        {
            _armed = null;
            Host.Hotkeys.ReleaseCapture();
        }

        private void Bind(string input)
        {
            Pick p = _armed;
            Disarm();
            _eatFrame = Time.frameCount;
            if (p == null) return;
            ExtraBinding b = ExtraBindings.Make(p.A.Name, p.Negative, input, KeyName);
            string text = p.A.LabelFor(p.Negative) + " = " + ExtraBindings.InputLabel(input);
            if (b == null) { _status.text = input + " cannot be bound."; return; }
            if (!ExtraBindings.AddUnique(_list, b)) { _status.text = text + " is there already."; return; }
            Save();
            ExtraBindingMaps.SetBindings(_list, Ctx.Log, OverlayPlugin.PluginGuid);
            string where = ExtraBindingMaps.Where(b) ?? "applied when the game's controls are ready";
            Ctx.Log.LogInfo("Game controls: added " + text + " [" + b.Token + "] (" + where + ").");
            string clash = ClashNote(b, InputKey(b));
            _status.text = "Added: " + text + "." + (clash.Length > 0 ? " " + clash + "." : "");
            _rowsDirty = true;
        }

        private void Remove(int i)
        {
            if (i < 0 || i >= _list.Count) return;
            ExtraBinding b = _list[i];
            string text = _rows.Count > i ? _rows[i].Text.text : b.Token;
            _list.RemoveAt(i);
            Save();
            ExtraBindingMaps.SetBindings(_list, Ctx.Log, OverlayPlugin.PluginGuid);
            Ctx.Log.LogInfo("Game controls: removed " + text + " [" + b.Token + "].");
            _status.text = "Removed: " + text + ".";
            _rowsDirty = true;
        }

        // --- texts, built when they change --------------------------------------------------------

        private string ClashNote(ExtraBinding b, KeyCode key)
        {
            string note = "";
            string game = ExtraBindingMaps.GameUses(b);
            if (game.Length > 0) note = "also " + game + " in the game's controls";
            HotkeyMap.Binding hot = Host.Hotkeys.Conflict(key, null);
            if (hot != null) note += (note.Length > 0 ? "; " : "") + "also the overlay's '" + hot.Description + "' key";
            return note;
        }

        private void RebuildRows()
        {
            _rowsDirty = false;
            _rowsVersion = ExtraBindingMaps.Version;
            while (_rows.Count < _list.Count) _rows.Add(new Row());
            if (_rows.Count > _list.Count) _rows.RemoveRange(_list.Count, _rows.Count - _list.Count);
            for (int i = 0; i < _list.Count; i++)
            {
                ExtraBinding b = _list[i];
                Row r = _rows[i];
                r.B = b;
                r.Key = InputKey(b);
                r.Clash = Host.Hotkeys.Conflict(r.Key, null);
                ExtraBindingMaps.ActionInfo a = ExtraBindingMaps.Find(b.Action);
                string action = a != null ? a.LabelFor(b.Negative) : b.Action + (b.Negative ? " -" : "");
                r.Text.text = action + " = " + ExtraBindings.InputLabel(b.Input);
                string where = ExtraBindingMaps.Where(b);
                string note = where != null && where.StartsWith("not ", StringComparison.Ordinal) ? where : "";
                string clash = ClashNote(b, r.Key);
                if (clash.Length > 0) note += (note.Length > 0 ? "; " : "") + clash;
                r.Note.text = note.Length > 0 ? char.ToUpper(note[0]) + note.Substring(1) + "." : "";
            }
            _waiting.text = ExtraBindingMaps.Ready ? "" : "The game's controls: " + ExtraBindingMaps.Status + ".";
        }

        private void BuildPicks()
        {
            _picksFor = ExtraBindingMaps.Actions.Count;
            _picks.Clear();
            for (int i = 0; i < ExtraBindingMaps.Actions.Count; i++)
            {
                ExtraBindingMaps.ActionInfo a = ExtraBindingMaps.Actions[i];
                AddPick(a, false);
                if (a.Axis) AddPick(a, true);
            }
            _filterShown = null;
            _rowsDirty = true;
        }

        private void AddPick(ExtraBindingMaps.ActionInfo a, bool negative)
        {
            Pick p = new Pick();
            p.A = a;
            p.Negative = negative;
            string label = a.LabelFor(negative);
            string shown = string.Equals(label, a.Name, StringComparison.OrdinalIgnoreCase)
                ? label : label + "  (" + a.Name + (a.Axis ? (negative ? " -" : " +") : "") + ")";
            p.Label = new GUIContent(shown);
            p.Search = shown.ToLowerInvariant();
            _picks.Add(p);
        }

        private void Narrow()
        {
            _filterShown = _filter;
            _shown.Clear();
            string f = (_filter ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < _picks.Count; i++)
                if (f.Length == 0 || _picks[i].Search.IndexOf(f, StringComparison.Ordinal) >= 0) _shown.Add(i);
            _pickScroll = Vector2.zero;
        }

        // --- Settings > Game controls -------------------------------------------------------------

        /// The fold's contents at (x, y), width w; returns the y under them.
        public float DrawSettings(float x, float y, float w)
        {
            _drawnFrame = Time.frameCount;
            y += UiText.DrawDim(x, y, w, Intro) + 6f;
            if (_waiting.text.Length > 0) y += UiText.Draw(x, y, w, _waiting) + 4f;

            int remove = -1;
            int n = Mathf.Min(_rows.Count, _list.Count);
            if (n == 0) y += UiText.DrawDim(x, y, w, NoneText) + 4f;
            for (int i = 0; i < n; i++)
            {
                Row r = _rows[i];
                float h = Mathf.Max(RowH, UiText.Draw(x, y + 1f, w - 90f, r.Text));
                if (WinPx.Button(new Rect(x + w - 80f, y, 80f, RowH), RemoveText)) remove = i;
                y += h + 2f;
                if (r.Note.text.Length > 0) y += UiText.DrawDim(x + 12f, y, w - 12f, r.Note) + 2f;
            }
            if (remove >= 0) Remove(remove);
            y += 6f;

            if (_armed != null)
            {
                y += UiText.Draw(x, y, w, _prompt, UiKit.HintStyle) + 4f;
            }
            else if (!_picking)
            {
                if (WinPx.Button(new Rect(x, y, 140f, 24f), AddText))
                {
                    _picking = true;
                    _filter = "";
                    _filterShown = null;
                    _status.text = "";
                }
                y += 28f;
            }
            else y = DrawPicker(x, y, w);

            if (_status.text.Length > 0) y += UiText.Draw(x, y, w, _status) + 4f;
            return y;
        }

        private float DrawPicker(float x, float y, float w)
        {
            y += UiText.Draw(x, y, w, PickText) + 2f;
            _filter = WinPx.TextField(new Rect(x, y, Mathf.Min(w - 90f, 260f), 22f), _filter ?? "");
            if (WinPx.Button(new Rect(x + w - 80f, y, 80f, 22f), CancelText)) { _picking = false; return y + 28f; }
            y += 28f;

            if (_picks.Count == 0) return y + UiText.Draw(x, y, w, _waiting.text.Length > 0 ? _waiting : NoMatchText) + 4f;
            if (_filterShown == null) return y + PickRows * RowH + 6f;   // the list is built on the next Tick
            if (_shown.Count == 0) return y + UiText.DrawDim(x, y, w, NoMatchText) + 4f;

            // Virtualised: only the rows in view are drawn.
            float listH = Mathf.Min(PickRows, _shown.Count) * RowH;
            Rect view = new Rect(x, y, w, listH);
            Rect content = new Rect(0f, 0f, w - 20f, _shown.Count * RowH);
            _pickScroll = WinPx.BeginScrollView(view, _pickScroll, content);
            int first = Mathf.Max(0, (int)(_pickScroll.y / RowH) - 1);
            int last = Mathf.Min(_shown.Count - 1, first + PickRows + 2);
            Pick chosen = null;
            for (int i = first; i <= last; i++)
            {
                Pick p = _picks[_shown[i]];
                if (WinPx.Button(new Rect(0f, i * RowH, content.width, RowH - 2f), p.Label)) chosen = p;
            }
            WinPx.EndScrollView();
            if (chosen != null) Arm(chosen);
            return y + listH + 6f;
        }
    }
}
