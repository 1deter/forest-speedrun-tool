using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The look the timer's panels share (T-0467): the splits panel, the
    // results panel, the replay bar and the checkpoint marks on the run
    // line draw their times with these styles and colour their deltas from
    // one palette. One instance, Modules/RunTimerModule's (Look), so the
    // panels look alike whichever module draws them.
    //
    // Built on first use inside OnGUI (GUI.skin is only there).
    // ------------------------------------------------------------------
    public sealed class SplitLook
    {
        /// The delta colours by SplitColour: the panel's, the results' and
        /// the checkpoint markers' on the run line (T-0371) - one table.
        public static readonly Color[] Palette =
        {
            Color.white,
            new Color(0.25f, 0.85f, 0.35f),   // ahead, gaining
            new Color(0.6f, 0.95f, 0.6f),     // ahead, losing
            new Color(0.95f, 0.6f, 0.6f),     // behind, gaining
            new Color(0.9f, 0.25f, 0.25f),    // behind, losing
            new Color(1f, 0.8f, 0.2f),        // gold
        };

        /// A time, right-aligned, one line, clipped.
        public GUIStyle Cell;
        /// A row's name, left-aligned.
        public GUIStyle Name;
        /// A panel's title, bold.
        public GUIStyle Title;
        /// What an empty panel says (Edit HUD), dim and wrapped.
        public GUIStyle Placeholder;
        /// A cell in each palette colour (by SplitColour).
        public GUIStyle[] Colours;
        /// The panels' card.
        public GUIStyle Panel;

        public void Ensure()
        {
            if (Cell != null) return;
            Cell = UiKit.Style(GUI.skin.label);
            Cell.alignment = TextAnchor.MiddleRight;
            Cell.padding = new RectOffset(0, 2, 0, 0);
            Cell.wordWrap = false;
            Cell.clipping = TextClipping.Clip;
            Name = UiKit.Style(Cell);
            Name.alignment = TextAnchor.MiddleLeft;
            Title = UiKit.Style(Name);
            Title.fontStyle = FontStyle.Bold;
            Placeholder = UiKit.Style(GUI.skin.label);
            Placeholder.wordWrap = true;
            Placeholder.normal.textColor = UiKit.DimColour;

            Colours = new GUIStyle[Palette.Length];
            for (int i = 0; i < Palette.Length; i++)
            {
                Colours[i] = UiKit.Style(Cell);
                Colours[i].normal.textColor = Palette[i];
            }

            // The kit's rounded dark card: the same look as the results panel
            // and the HUD widgets (docs/ui-redesign.md).
            Panel = UiKit.Style(UiKit.WidgetCard);
        }
    }
}
