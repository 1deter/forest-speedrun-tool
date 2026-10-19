using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The runner's extra game bindings (T-0343): one game action on one more
    // key, mouse button or wheel direction, beside the game's own bindings.
    // Saved as `[Game controls] Bindings`, entries separated by ", ":
    //
    //     Jump=WheelUp, Crouch=Mouse3, Vertical-=K
    //
    // - Action: the Rewired action's `name`; a trailing `-` is the negative
    //   side of an axis action (`+` is the default and is not written).
    // - Input: a UnityEngine.KeyCode name, Mouse0..Mouse6, WheelUp, WheelDown.
    //   Mouse0..Mouse6 are Rewired's mouse elements 3..9 (Mouse0 left, Mouse1
    //   right, Mouse2 middle, Mouse3 / Mouse4 the side buttons); the wheel is
    //   element 2, an axis, its positive (up) or negative (down) range
    //   (docs/game-notes.md *Adding a binding at runtime*).
    //
    // Pure: linked into the tests. Key names are checked by the caller's
    // `keyName` (KeyCode lives in UnityEngine), which returns the canonical
    // name or null.
    // ------------------------------------------------------------------
    public enum BindInput { Key, MouseButton, Wheel }

    public sealed class ExtraBinding
    {
        public string Action;
        public bool Negative;
        public string Input;      // canonical: a KeyCode name, Mouse0..Mouse6, WheelUp, WheelDown
        public BindInput Kind;
        public int Element = -1;  // the Rewired mouse element (2 the wheel, 3..9 buttons); -1 for a key

        public bool IsMouse { get { return Kind != BindInput.Key; } }
        public bool WheelDown { get { return Kind == BindInput.Wheel && Input == ExtraBindings.WheelDown; } }

        /// The entry as saved: `Action[-]=Input`.
        public string Token { get { return Action + (Negative ? "-" : "") + "=" + Input; } }

        public bool Same(ExtraBinding o)
        {
            return o != null && o.Negative == Negative
                && string.Equals(o.Action, Action, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.Input, Input, StringComparison.Ordinal);
        }
    }

    public static class ExtraBindings
    {
        public const string WheelUp = "WheelUp";
        public const string WheelDown = "WheelDown";
        public const int WheelElement = 2;
        public const int FirstButtonElement = 3;   // Mouse0
        public const int MouseButtons = 7;         // Mouse0..Mouse6

        /// One binding, or null when the action is empty or the input is not
        /// a key, mouse button or wheel direction.
        public static ExtraBinding Make(string action, bool negative, string input, Func<string, string> keyName)
        {
            if (action == null || input == null) return null;
            action = action.Trim();
            input = input.Trim();
            if (action.Length == 0 || input.Length == 0) return null;

            ExtraBinding b = new ExtraBinding();
            b.Action = action;
            b.Negative = negative;
            if (string.Equals(input, WheelUp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, WheelDown, StringComparison.OrdinalIgnoreCase))
            {
                b.Kind = BindInput.Wheel;
                b.Input = string.Equals(input, WheelUp, StringComparison.OrdinalIgnoreCase) ? WheelUp : WheelDown;
                b.Element = WheelElement;
                return b;
            }
            int button = MouseIndex(input);
            if (button >= 0)
            {
                b.Kind = BindInput.MouseButton;
                b.Input = "Mouse" + button;
                b.Element = FirstButtonElement + button;
                return b;
            }
            if (input.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase)) return null;   // Mouse7 and up: no such element
            string key = keyName != null ? keyName(input) : null;
            if (string.IsNullOrEmpty(key)) return null;
            b.Kind = BindInput.Key;
            b.Input = key;
            return b;
        }

        /// "Mouse0".."Mouse6" (any case) -> 0..6, else -1.
        private static int MouseIndex(string input)
        {
            if (input.Length != 6 || !input.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase)) return -1;
            int d = input[5] - '0';
            return d >= 0 && d < MouseButtons ? d : -1;
        }

        /// The saved text as a list: duplicates dropped, each entry that is
        /// not `Action[-]=Input` skipped and added to `skipped` (may be null).
        /// Never throws.
        public static List<ExtraBinding> Parse(string text, Func<string, string> keyName, List<string> skipped)
        {
            List<ExtraBinding> list = new List<ExtraBinding>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] parts = text.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string entry = parts[i].Trim();
                if (entry.Length == 0) continue;
                ExtraBinding b = null;
                int eq = entry.IndexOf('=');
                if (eq > 0)
                {
                    string action = entry.Substring(0, eq).Trim();
                    bool negative = false;
                    if (action.EndsWith("-", StringComparison.Ordinal) || action.EndsWith("+", StringComparison.Ordinal))
                    {
                        negative = action[action.Length - 1] == '-';
                        action = action.Substring(0, action.Length - 1);
                    }
                    b = Make(action, negative, entry.Substring(eq + 1), keyName);
                }
                if (b == null)
                {
                    if (skipped != null) skipped.Add(entry);
                    continue;
                }
                AddUnique(list, b);
            }
            return list;
        }

        /// The list as saved; "" when empty.
        public static string Format(IList<ExtraBinding> list)
        {
            if (list == null || list.Count == 0) return "";
            string s = "";
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) s += ", ";
                s += list[i].Token;
            }
            return s;
        }

        /// Adds `b` unless the list has the same binding; false when it was there.
        public static bool AddUnique(List<ExtraBinding> list, ExtraBinding b)
        {
            if (list == null || b == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Same(b)) return false;
            list.Add(b);
            return true;
        }

        /// An input's name for a runner: the mouse buttons as people call
        /// them (Mouse3 is the 4th button, the back side button), as the
        /// overlay's own key list names them.
        public static string InputLabel(string input)
        {
            if (input == null) return "";
            switch (input)
            {
                case WheelUp: return "Mouse wheel up";
                case WheelDown: return "Mouse wheel down";
                case "Mouse0": return "Left mouse";
                case "Mouse1": return "Right mouse";
                case "Mouse2": return "Middle mouse";
                case "Mouse3": return "Mouse 4";
                case "Mouse4": return "Mouse 5";
                case "Mouse5": return "Mouse 6";
                case "Mouse6": return "Mouse 7";
                default: return input;
            }
        }
    }
}
