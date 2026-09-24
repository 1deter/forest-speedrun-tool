using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The player's animator, read for the test bridge (`anim`).
    //
    // WHY: a restore should cut a player action in progress (runner maks:
    // the plane axe's swing played on through a reset). The game's full
    // reset (resetTrigger) cut it but flashed the headless body for a
    // frame; snapping the arms layer to "upperBody.idle" did nothing. So:
    // see which layer, state, clips and parameters a swing actually uses
    // before choosing what to reset (gotcha 25).
    // ------------------------------------------------------------------
    public sealed class AnimProbe
    {
        private static PropertyInfo _animatorProp;
        private static FieldInfo _animatorField;
        private static bool _resolved;

        private readonly Dictionary<string, string> _last = new Dictionary<string, string>();
        private float _start;

        public static Animator PlayerAnimator()
        {
            if (!_resolved)
            {
                _resolved = true;
                Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                if (local != null)
                {
                    _animatorProp = local.GetProperty("Animator", stat);
                    if (_animatorProp == null) _animatorField = local.GetField("Animator", stat);
                }
            }
            try
            {
                object a = _animatorProp != null ? _animatorProp.GetValue(null, null)
                         : _animatorField != null ? _animatorField.GetValue(null) : null;
                return a as Animator;
            }
            catch (Exception) { return null; }
        }

        /// One line per layer, then the bool / int parameters that are set.
        public static string Snapshot(List<string> o)
        {
            Animator an = PlayerAnimator();
            if (an == null) return "no player animator";
            for (int i = 0; i < an.layerCount; i++) o.Add(Layer(an, i));

            StringBuilder sb = new StringBuilder("params:");
            AnimatorControllerParameter[] ps = an.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                string v = Param(an, ps[i]);
                if (v != null) sb.Append(' ').Append(ps[i].name).Append('=').Append(v);
            }
            o.Add(sb.ToString());
            return null;
        }

        public void BeginWatch()
        {
            _last.Clear();
            _start = Time.realtimeSinceStartup;
        }

        /// Every layer and bool / int / trigger parameter whose value
        /// changed since the last call, one line each, time-stamped.
        public void Sample(List<string> o)
        {
            Animator an = PlayerAnimator();
            if (an == null) return;
            string t = "+" + (Time.realtimeSinceStartup - _start).ToString("0.000", CultureInfo.InvariantCulture) + " f" + Time.frameCount + " ";

            for (int i = 0; i < an.layerCount; i++)
            {
                string key = "L" + i;
                string sig = LayerSignature(an, i);
                string old;
                if (_last.TryGetValue(key, out old) && old == sig) continue;
                _last[key] = sig;
                o.Add(t + Layer(an, i));
            }

            AnimatorControllerParameter[] ps = an.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].type == AnimatorControllerParameterType.Float) continue;
                string v = Param(an, ps[i]) ?? "off";
                string key = "P" + ps[i].name;
                string old;
                if (_last.TryGetValue(key, out old) && old == v) continue;
                bool first = old == null;
                _last[key] = v;
                if (!first) o.Add(t + "param " + ps[i].name + " = " + v);
            }
        }

        // What makes a layer "the same": state, clips, transition - not the
        // playing time, which changes every frame.
        private static string LayerSignature(Animator an, int i)
        {
            // Clip names without their blend weights, the layer weight to
            // 0.1: a blend or a fade is one line, not one per frame (the
            // first watch was 360 lines of turning in place).
            AnimatorStateInfo s = an.GetCurrentAnimatorStateInfo(i);
            string sig = s.fullPathHash + "|" + ClipNames(an.GetCurrentAnimatorClipInfo(i)) + "|" +
                         an.GetLayerWeight(i).ToString("0.0", CultureInfo.InvariantCulture);
            if (an.IsInTransition(i)) sig += "|-> " + an.GetNextAnimatorStateInfo(i).fullPathHash;
            return sig;
        }

        private static string Layer(Animator an, int i)
        {
            AnimatorStateInfo s = an.GetCurrentAnimatorStateInfo(i);
            StringBuilder sb = new StringBuilder();
            sb.Append('L').Append(i).Append(" '").Append(an.GetLayerName(i)).Append("' w=").Append(Weight(an, i))
              .Append(" state=").Append(s.fullPathHash).Append(TagName(s.tagHash))
              .Append(" t=").Append(s.normalizedTime.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" clips: ").Append(Clips(an.GetCurrentAnimatorClipInfo(i)));
            if (an.IsInTransition(i))
            {
                AnimatorStateInfo n = an.GetNextAnimatorStateInfo(i);
                sb.Append("  -> state=").Append(n.fullPathHash).Append(" clips: ").Append(Clips(an.GetNextAnimatorClipInfo(i)));
            }
            return sb.ToString();
        }

        // The tags playerAnimatorControl.Start hashes; others by number.
        private static readonly string[] KnownTags = { "idling", "held", "attacking", "smash", "block" };

        private static string TagName(int tagHash)
        {
            if (tagHash == 0) return "";
            for (int i = 0; i < KnownTags.Length; i++)
                if (Animator.StringToHash(KnownTags[i]) == tagHash) return " [" + KnownTags[i] + "]";
            return " [tag " + tagHash + "]";
        }

        private static string Weight(Animator an, int i)
        {
            return an.GetLayerWeight(i).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string ClipNames(AnimatorClipInfo[] clips)
        {
            if (clips == null || clips.Length == 0) return "-";
            List<string> names = new List<string>();
            for (int i = 0; i < clips.Length; i++)
            {
                string n = clips[i].clip != null ? clips[i].clip.name : "?";
                if (!names.Contains(n)) names.Add(n);
            }
            names.Sort(StringComparer.Ordinal);
            return string.Join(",", names.ToArray());
        }

        private static string Clips(AnimatorClipInfo[] clips)
        {
            if (clips == null || clips.Length == 0) return "-";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < clips.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(clips[i].clip != null ? clips[i].clip.name : "?")
                  .Append('(').Append(clips[i].weight.ToString("0.00", CultureInfo.InvariantCulture)).Append(')');
            }
            return sb.ToString();
        }

        /// The value, or null when it is at rest (false / 0) so a snapshot
        /// lists only what is set.
        private static string Param(Animator an, AnimatorControllerParameter p)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    return an.GetBool(p.nameHash) ? "on" : null;
                case AnimatorControllerParameterType.Int:
                    int v = an.GetInteger(p.nameHash);
                    return v != 0 ? v.ToString(CultureInfo.InvariantCulture) : null;
                case AnimatorControllerParameterType.Float:
                    float f = an.GetFloat(p.nameHash);
                    return Mathf.Abs(f) > 0.001f ? f.ToString("0.##", CultureInfo.InvariantCulture) : null;
            }
            return null;
        }
    }
}
