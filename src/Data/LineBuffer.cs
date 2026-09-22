using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A run's path as drawable points, built incrementally.
    //
    // WHY
    // The current run's line used to be rebuilt from scratch - a fresh
    // Vector3[] of every sample - each time a sample was added, 30 times a
    // second. Ten minutes in that is ~18,000 points copied 30 times a
    // second, megabytes of garbage a second for Mono's non-generational
    // GC to stop the game over. And every sample became a vertex drawn
    // every frame, including hundreds piled up on one spot while standing
    // still.
    //
    // Now new samples are APPENDED (capacity doubles, so growth is rare)
    // and a point closer than MinSpacing to the last kept one is dropped.
    // At running speed that keeps roughly every other sample; standing
    // still adds nothing.
    //
    // Pure, so it is linked into the tests.
    // ------------------------------------------------------------------
    public sealed class LineBuffer
    {
        public const float DefaultSpacing = 0.75f;

        private readonly float _minSpacingSqr;

        /// The kept points. Replaced (not resized in place) when capacity
        /// grows, so re-read it after Sync.
        public Vector3[] Points = new Vector3[256];
        public int Count;

        /// How many source samples have been consumed, so Sync only walks
        /// the new ones.
        public int SourceCount;

        public LineBuffer() : this(DefaultSpacing) { }

        public LineBuffer(float minSpacing)
        {
            _minSpacingSqr = minSpacing * minSpacing;
        }

        public void Clear()
        {
            Count = 0;
            SourceCount = 0;
        }

        public void Append(Vector3 p)
        {
            if (Count > 0 && (p - Points[Count - 1]).sqrMagnitude < _minSpacingSqr) return;

            if (Count == Points.Length)
            {
                Vector3[] bigger = new Vector3[Points.Length * 2];
                Array.Copy(Points, bigger, Count);
                Points = bigger;
            }

            Points[Count++] = p;
        }

        /// Appends any samples added since the last call. If the source
        /// shrank (a new run started) the line starts over.
        public void Sync(IList<RunSample> samples)
        {
            if (samples == null) { Clear(); return; }
            if (samples.Count < SourceCount) Clear();

            for (int i = SourceCount; i < samples.Count; i++) Append(samples[i].P);
            SourceCount = samples.Count;
        }
    }
}
