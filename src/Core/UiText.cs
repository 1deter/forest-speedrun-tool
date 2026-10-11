using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The one way to put variable text in a panel.
    //
    // A fixed 20 px label clipped every message longer than its panel -
    // cut off on the right when wrapping was off, cut in half top and
    // bottom when it was on (author, v0.21-0.22, reported three times).
    // So: status lines, errors and descriptions wrap to the width they are
    // given and take the height they need, and the caller moves on by the
    // height returned. Fixed one-line labels are for short constant text
    // and list rows only.
    //
    // No allocation: CalcHeight measures a GUIContent the caller keeps, or
    // the shared scratch one for a string.
    // ------------------------------------------------------------------
    public static class UiText
    {
        private const float LineHeight = 20f;
        private const float Gap = 2f;

        private static GUIStyle _plain;
        private static GUIStyle _dim;
        private static GUIStyle _box;
        private static readonly GUIContent Scratch = new GUIContent("");

        /// Wrapped label; returns the height used, gap included - 0 for no text.
        public static float Draw(float x, float y, float width, GUIContent content)
        {
            return Draw(x, y, width, content, Plain);
        }

        public static float Draw(float x, float y, float width, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            Scratch.text = text;
            return Draw(x, y, width, Scratch, Plain);
        }

        /// An explanation of the control just above (redesign, author 2026-10-05:
        /// "a feature, its name and a toggle - hover for a brief description"):
        /// shown as that row's tooltip, takes no space.
        public static float Note(float x, float y, float width, GUIContent content)
        {
            if (content == null || string.IsNullOrEmpty(content.text)) return 0f;
            UiKit.Hint(new Rect(0f, y - 24f, x + width, 24f), content);
            return 0f;
        }

        private static readonly GUIContent NoteCache = new GUIContent("");

        public static float Note(float x, float y, float width, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            // One shared cache: set only for the row under the mouse.
            Rect r = new Rect(0f, y - 24f, x + width, 24f);
            Event e = Event.current;
            if (e == null || !r.Contains(e.mousePosition)) return 0f;
            UiKit.Hint(r, text, NoteCache);
            return 0f;
        }

        public static float DrawDim(float x, float y, float width, GUIContent content)
        {
            return Draw(x, y, width, content, Dim);
        }

        public static float Draw(float x, float y, float width, GUIContent content, GUIStyle wrapping)
        {
            if (content == null || string.IsNullOrEmpty(content.text) || width <= 0f) return 0f;
            float h = Mathf.Max(LineHeight, wrapping.CalcHeight(content, width));
            GUI.Label(new Rect(x, y, width, h), content, wrapping);
            return h + Gap;
        }

        /// The height Draw would return, without drawing (a panel sizes its
        /// background before its text goes on it).
        public static float Height(float width, GUIContent content, GUIStyle wrapping)
        {
            if (content == null || string.IsNullOrEmpty(content.text) || width <= 0f) return 0f;
            return Mathf.Max(LineHeight, wrapping.CalcHeight(content, width)) + Gap;
        }

        /// Height a wrapped text box needs for this text at this width (one line at least).
        public static float BoxHeight(string text, float width)
        {
            Scratch.text = string.IsNullOrEmpty(text) ? " " : text;
            return Mathf.Max(22f, Box.CalcHeight(Scratch, width));
        }

        /// A text box that wraps and grows downwards instead of scrolling
        /// sideways (QA notes, v0.24.103). Enter is not a line break: notes
        /// go into one-line log and answer formats, so a newline is a space.
        public static string TextBox(float x, float y, float width, float height, string value)
        {
            string v = GUI.TextArea(new Rect(x, y, width, height), value ?? "", Box);
            if (v.IndexOf('\n') >= 0) v = v.Replace('\n', ' ');
            return v;
        }

        public static GUIStyle Box
        {
            get
            {
                if (_box == null)
                {
                    _box = UiKit.Style(GUI.skin.textArea);
                    _box.wordWrap = true;
                }
                return _box;
            }
        }

        /// Label style that wraps; build styles from GUI.skin only inside OnGUI.
        public static GUIStyle Plain
        {
            get
            {
                if (_plain == null)
                {
                    _plain = UiKit.Style(GUI.skin.label);
                    _plain.wordWrap = true;
                    _plain.alignment = TextAnchor.UpperLeft;
                }
                return _plain;
            }
        }

        public static GUIStyle Dim
        {
            get
            {
                if (_dim == null)
                {
                    _dim = UiKit.Style(Plain);
                    _dim.normal.textColor = UiKit.SoftColour;
                }
                return _dim;
            }
        }
    }
}
