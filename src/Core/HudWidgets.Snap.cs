using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Snapping while a piece is dragged in Edit HUD mode (T-0020, author
    // 2026-10-11): a placed value, the column and the splits / results
    // panels snap to the screen's 25 / 50 / 75 % lines and to each other's
    // edges and centres (Data/HudSnap, tested); a thin line shows each snap
    // while it holds; Alt held drags freely. The other pieces are the rects
    // as last drawn: the placed values (_rects), the column (ColumnRect)
    // and what a module reports (ReportPiece) - nothing is measured twice.
    // ------------------------------------------------------------------
    public sealed partial class HudWidgets
    {
        /// The column's name as a piece (Plugin's drag).
        public const string ColumnPiece = "column";

        private sealed class Piece
        {
            public string Id;
            public Rect Rect;
            public int Frame;
        }

        private readonly List<Piece> _pieces = new List<Piece>();
        private readonly List<SnapBox> _snapOthers = new List<SnapBox>();
        private bool _guideX, _guideY;
        private float _guideAtX, _guideAtY;

        /// A module's piece on screen this frame (in edit mode), so the others
        /// snap to it.
        public void ReportPiece(string id, Rect r)
        {
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (!string.Equals(_pieces[i].Id, id)) continue;
                _pieces[i].Rect = r;
                _pieces[i].Frame = Time.frameCount;
                return;
            }
            _pieces.Add(new Piece { Id = id, Rect = r, Frame = Time.frameCount });
        }

        /// Edit HUD mode: where piece `id`, dragged to `at`, goes - snapped,
        /// or `at` itself with Alt held or outside edit mode. Call EndSnap
        /// when the drag ends.
        public Vector2 SnapDrag(string id, Rect at)
        {
            return Snap(at, id, -1);
        }

        /// The drag ended: the guides go.
        public void EndSnap()
        {
            _guideX = false;
            _guideY = false;
        }

        private Vector2 Snap(Rect at, string selfId, int selfWidget)
        {
            Event e = Event.current;
            if (!Editing || (e != null && e.alt))
            {
                EndSnap();
                return new Vector2(at.x, at.y);
            }

            _snapOthers.Clear();
            for (int i = 0; i < _rects.Length && i < _layout.Widgets.Count; i++)
            {
                if (i == selfWidget || _widgetLine[i] < 0 || !_layout.Widgets[i].Free) continue;
                Add(_rects[i]);
            }
            if (selfId != ColumnPiece) Add(ColumnRect);
            int frame = Time.frameCount;
            for (int i = 0; i < _pieces.Count; i++)
            {
                // A panel not drawn lately (switched off, steps aside) is not there.
                if (string.Equals(_pieces[i].Id, selfId) || frame - _pieces[i].Frame > 2) continue;
                Add(_pieces[i].Rect);
            }

            SnapResult r = HudSnap.Snap(new SnapBox(at.x, at.y, at.width, at.height),
                                        Screen.width, Screen.height, _snapOthers, HudSnap.Threshold);
            _guideX = r.SnappedX;
            _guideY = r.SnappedY;
            _guideAtX = r.GuideX;
            _guideAtY = r.GuideY;
            return new Vector2(r.X, r.Y);
        }

        private void Add(Rect r)
        {
            if (r.width > 0f && r.height > 0f) _snapOthers.Add(new SnapBox(r.x, r.y, r.width, r.height));
        }

        /// The snap lines, one pixel, across the screen.
        private void DrawGuides(Event e)
        {
            if (!Editing || (!_guideX && !_guideY) || e == null || e.type != EventType.Repaint) return;
            Texture2D t = UiKit.AccentTexture;
            if (_guideX) GUI.DrawTexture(new Rect(Mathf.Min(Mathf.Round(_guideAtX), Screen.width - 1f), 0f, 1f, Screen.height), t);
            if (_guideY) GUI.DrawTexture(new Rect(0f, Mathf.Min(Mathf.Round(_guideAtY), Screen.height - 1f), Screen.width, 1f), t);
        }
    }
}
