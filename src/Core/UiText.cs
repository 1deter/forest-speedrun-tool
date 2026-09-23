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

        /// Label style that wraps; build styles from GUI.skin only inside OnGUI.
        public static GUIStyle Plain
        {
            get
            {
                if (_plain == null)
                {
                    _plain = new GUIStyle(GUI.skin.label);
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
                    _dim = new GUIStyle(Plain);
                    _dim.normal.textColor = new Color(0.78f, 0.78f, 0.78f);
                }
                return _dim;
            }
        }
    }
}
