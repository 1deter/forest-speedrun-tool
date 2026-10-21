using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // More than one comparison line (T-0374; author, 2026-10-16: "all of
    // them" - PB, last run, best segments, site runs). The main comparison
    // line (Compare to) stays as it is; up to Max EXTRA lines are drawn
    // beside it, each in its own colour with a name tag, and aiming the
    // crosshair at a line names it. This is the pure part:
    //
    //   ExtraPicks   which extras are on: a short token list kept in one
    //                config string ("pb,last,best,runner:ab12cd")
    //   LineAim      the point of a polyline nearest the crosshair (the
    //                screen centre), for the tag's place and for the pick
    //   BestSegmentsRun  the composite run of your best segments: each
    //                segment taken from the attempt that did it fastest
    //
    // The module (Modules/PracticeRunModule.ExtraLines) resolves the tokens
    // to runs and draws; Game/DebugDraw RunLineBehaviour draws the ribbons.
    // ------------------------------------------------------------------
    public static class ExtraPicks
    {
        public const int Max = 6;
        public const string Pb = "pb", Last = "last", Best = "best", RunnerPrefix = "runner:";

        public static string RunnerToken(string runnerId) { return RunnerPrefix + runnerId; }

        public static List<string> Parse(string text)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] parts = text.Split(',');
            for (int i = 0; i < parts.Length && list.Count < Max; i++)
            {
                string t = parts[i].Trim();
                if (t.Length == 0 || IndexOf(list, t) >= 0) continue;
                list.Add(t);
            }
            return list;
        }

        public static string Serialize(List<string> list)
        {
            return string.Join(",", list.ToArray());
        }

        public static int IndexOf(List<string> list, string token)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], token, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// Turns a token on or off; false when it is on a full list (Max).
        public static bool Set(List<string> list, string token, bool on)
        {
            int at = IndexOf(list, token);
            if (!on) { if (at >= 0) list.RemoveAt(at); return true; }
            if (at >= 0) return true;
            if (list.Count >= Max) return false;
            list.Add(token);
            return true;
        }
    }

    public static class LineAim
    {
        /// The point of the polyline points[start .. end) nearest the aim
        /// (the camera's forward axis, the screen centre). `dist` is how far
        /// off it is on screen per unit of depth (multiply by pixels per
        /// unit depth for pixels). `index` is the segment, `along` 0..1
        /// inside it (to read the time there), `point` the world position.
        /// Segments are clipped at `near` in front of the camera. False when
        /// no part of the line is in front of it.
        public static bool Nearest(Vector3[] points, int start, int end, Vector3 eye, Vector3 fwd, Vector3 right, Vector3 up,
                                   float near, out int index, out float along, out Vector3 point, out float dist)
        {
            index = -1; along = 0f; point = Vector3.zero; dist = float.MaxValue;
            if (points == null) return false;
            if (start < 0) start = 0;
            if (end > points.Length) end = points.Length;
            for (int i = start; i + 1 < end; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                Vector3 da = a - eye, db = b - eye;
                float za = Vector3.Dot(da, fwd), zb = Vector3.Dot(db, fwd);
                if (za <= near && zb <= near) continue;
                // Clip to the near plane; t0..t1 is the kept share of the segment.
                float t0 = 0f, t1 = 1f;
                if (za < near) t0 = (near - za) / (zb - za);
                else if (zb < near) t1 = (za - near) / (za - zb);
                Vector3 ca = a + (b - a) * t0, cb = a + (b - a) * t1;
                Vector3 ea = ca - eye, eb = cb - eye;
                float dza = Vector3.Dot(ea, fwd), dzb = Vector3.Dot(eb, fwd);
                float ax = Vector3.Dot(ea, right) / dza, ay = Vector3.Dot(ea, up) / dza;
                float bx = Vector3.Dot(eb, right) / dzb, by = Vector3.Dot(eb, up) / dzb;
                float dx = bx - ax, dy = by - ay;
                float len2 = dx * dx + dy * dy;
                float s = len2 > 1e-12f ? -(ax * dx + ay * dy) / len2 : 0f;
                if (s < 0f) s = 0f; else if (s > 1f) s = 1f;
                float qx = ax + dx * s, qy = ay + dy * s;
                float d = (float)Math.Sqrt(qx * qx + qy * qy);
                if (d >= dist) continue;

                // Perspective-correct: the screen share s back to the share of the clipped segment.
                float lam = (s * dza) / ((1f - s) * dzb + s * dza);
                dist = d;
                index = i;
                along = t0 + lam * (t1 - t0);
                point = a + (b - a) * along;
            }
            return index >= 0;
        }
    }

    public static class BestSegmentsRun
    {
        /// Your best segments as one run: segment k (the stretch between
        /// checkpoint k-1 and k; the last ends at the finish) is taken from
        /// the completed attempt that ran it fastest, its samples shifted to
        /// where the golds put them. Null without a completed attempt that
        /// kept `rows` splits (checkpoints + the end), in order.
        public static Attempt Build(IList<Attempt> attempts, int checkpoints)
        {
            if (attempts == null) return null;
            int rows = checkpoints + 1;
            Attempt[] pick = new Attempt[rows];
            float[] gold = new float[rows];
            for (int k = 0; k < rows; k++) gold[k] = float.MaxValue;

            for (int a = 0; a < attempts.Count; a++)
            {
                Attempt at = attempts[a];
                if (!Eligible(at, checkpoints)) continue;
                for (int k = 0; k < rows; k++)
                {
                    float seg = End(at, k) - Start(at, k);
                    if (seg > 0f && seg < gold[k]) { gold[k] = seg; pick[k] = at; }
                }
            }
            for (int k = 0; k < rows; k++) if (pick[k] == null) return null;

            Attempt run = new Attempt();
            run.Completed = true;
            run.Splits = new float[checkpoints];
            float clock = 0f;
            for (int k = 0; k < rows; k++)
            {
                Attempt at = pick[k];
                float from = Start(at, k), to = End(at, k), shift = clock - from;
                for (int i = 0; i < at.Samples.Count; i++)
                {
                    RunSample s = at.Samples[i];
                    if (s.T < from) continue;
                    if (s.T > to) break;
                    s.T += shift;
                    run.Samples.Add(s);
                }
                clock += gold[k];
                if (k < checkpoints) run.Splits[k] = clock;
            }
            run.Duration = clock;
            run.RunnerName = "Best segments";
            return run;
        }

        private static bool Eligible(Attempt a, int checkpoints)
        {
            if (a == null || !a.Completed || a.Splits == null || a.Splits.Length != checkpoints || a.Samples.Count < 2) return false;
            float prev = 0f;
            for (int i = 0; i < checkpoints; i++)
            {
                float t = a.Splits[i];
                if (float.IsNaN(t) || t < prev) return false;
                prev = t;
            }
            return a.Duration >= prev;
        }

        private static float Start(Attempt a, int k) { return k == 0 ? 0f : a.Splits[k - 1]; }
        private static float End(Attempt a, int k) { return k < a.Splits.Length ? a.Splits[k] : a.Duration; }
    }
}
