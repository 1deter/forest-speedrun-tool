using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // What the comparison line's checkpoint markers show (T-0388; author
    // 2026-10-16): off, the dots alone, or the dots with labels built from
    // a chosen set of fields - the split time, your delta, the segment
    // time, the speed there and any state channel the comparison run
    // recorded (Health, Stamina ...). Pure: the setting's text form, and
    // the label text for one marker. The module caches the text when the
    // comparison, the choice or the decimals change (never per frame).
    //
    // Config text: "off", "dot", or comma-separated fields
    // "time,delta,segment,speed,ch:Health,item:Soda". The default (author
    // 2026-10-16, T-0400) is time, delta, speed, Health, Stamina, Armor;
    // OldDefault, "time,delta", is what v0.24.316 (T-0371) to v0.24.333
    // drew, moved to the new default once by the module.
    // ------------------------------------------------------------------
    public sealed class MarkSpec
    {
        public const string Default = "time,delta,speed,ch:Health,ch:Stamina,ch:Armor";
        public const string OldDefault = "time,delta";
        public const string ChannelPrefix = "ch:";
        public const string ItemPrefix = "item:";

        /// The stats the Runs tab offers (author 2026-10-16, T-0400: a
        /// short list in runner words, out of the ~80 PlayerStats fields a
        /// run records). Channel names, confirmed on PlayerStats.
        public static readonly string[] Stats = { "Health", "Stamina", "Energy", "Fullness", "Thirst", "BodyTemp", "Armor" };

        public enum Kind { Off, Dots, Labels }

        public Kind Mode = Kind.Labels;
        public bool Time = true, Delta = true, Segment, Speed = true;
        public readonly List<string> Channels = new List<string> { "Health", "Stamina", "Armor" };
        public readonly List<string> Items = new List<string>();

        /// A channel in the runner's words ("BodyTemp" -> "Body temp").
        public static string StatName(string channel)
        {
            if (string.Equals(channel, "BodyTemp", StringComparison.OrdinalIgnoreCase)) return "Body temp";
            return channel;
        }

        /// The config value after the one-time move off the old default:
        /// `moved` = the move already happened (a later "time,delta" was
        /// chosen by hand and stays).
        public static string MoveOldDefault(string value, bool moved)
        {
            return !moved && value == OldDefault ? Default : value;
        }

        /// Armor is left out of a label while it is 0 (author: "armor (if any)").
        public static bool HiddenAtZero(string channel)
        {
            return string.Equals(channel, "Armor", StringComparison.OrdinalIgnoreCase);
        }

        /// A value from the config; anything unreadable gives the default
        /// (a hand-edited file never breaks the lines).
        public static MarkSpec Parse(string text)
        {
            MarkSpec s = new MarkSpec();
            if (text == null) return s;
            string t = text.Trim();
            if (t.Length == 0) return s;
            if (string.Equals(t, "off", StringComparison.OrdinalIgnoreCase)) { s.Mode = Kind.Off; return s; }
            if (string.Equals(t, "dot", StringComparison.OrdinalIgnoreCase)) { s.Mode = Kind.Dots; return s; }

            MarkSpec f = new MarkSpec();
            f.Time = f.Delta = f.Speed = false;
            f.Channels.Clear();
            bool any = false;
            string[] parts = t.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                string lower = p.ToLowerInvariant();
                if (lower == "time") { f.Time = true; any = true; }
                else if (lower == "delta") { f.Delta = true; any = true; }
                else if (lower == "segment") { f.Segment = true; any = true; }
                else if (lower == "speed") { f.Speed = true; any = true; }
                else if (lower.StartsWith(ChannelPrefix) && p.Length > ChannelPrefix.Length)
                {
                    string name = p.Substring(ChannelPrefix.Length).Trim();
                    if (name.Length > 0 && !f.HasChannel(name)) { f.Channels.Add(name); any = true; }
                }
                else if (lower.StartsWith(ItemPrefix) && p.Length > ItemPrefix.Length)
                {
                    string name = p.Substring(ItemPrefix.Length).Trim();
                    if (name.Length > 0 && !f.HasItem(name)) { f.Items.Add(name); any = true; }
                }
            }
            return any ? f : s;
        }

        public string Serialize()
        {
            if (Mode == Kind.Off) return "off";
            if (Mode == Kind.Dots) return "dot";
            StringBuilder sb = new StringBuilder();
            if (Time) sb.Append("time,");
            if (Delta) sb.Append("delta,");
            if (Segment) sb.Append("segment,");
            if (Speed) sb.Append("speed,");
            for (int i = 0; i < Channels.Count; i++) sb.Append(ChannelPrefix).Append(Channels[i].Replace(",", "")).Append(',');
            for (int i = 0; i < Items.Count; i++) sb.Append(ItemPrefix).Append(Items[i].Replace(",", "")).Append(',');
            if (sb.Length == 0) return Default;   // no field = never saved by the UI; read as the default
            sb.Length--;
            return sb.ToString();
        }

        public bool HasChannel(string name)
        {
            for (int i = 0; i < Channels.Count; i++)
                if (string.Equals(Channels[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void SetChannel(string name, bool on)
        {
            for (int i = 0; i < Channels.Count; i++)
                if (string.Equals(Channels[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!on) Channels.RemoveAt(i);
                    return;
                }
            if (on) Channels.Add(name);
        }

        /// Item names match exactly, as the run file and ItemCountAt do.
        public bool HasItem(string name)
        {
            return Items.IndexOf(name) >= 0;
        }

        public void SetItem(string name, bool on)
        {
            int at = Items.IndexOf(name);
            if (!on && at >= 0) Items.RemoveAt(at);
            else if (on && at < 0) Items.Add(name);
        }

        /// How many fields are chosen (labels mode); the UI keeps at least
        /// one, so "labels with nothing in them" cannot be saved.
        public int FieldCount
        {
            get { return (Time ? 1 : 0) + (Delta ? 1 : 0) + (Segment ? 1 : 0) + (Speed ? 1 : 0) + Channels.Count + Items.Count; }
        }

        /// True when this shows labels and any of them draws text of the
        /// comparison run's own (time, segment, speed, channels, items).
        public bool Labels { get { return Mode == Kind.Labels; } }
    }

    public static class MarkLabels
    {
        public const string Gap = "  ";

        /// The label's own text at marker `i` (the part drawn in white): the
        /// chosen fields among time, segment, speed and channels, in that
        /// order, joined by two spaces. A field with no data there is left
        /// out - never "NaN", never a gap. "" = nothing to draw (the
        /// delta, drawn apart in its colour, is not in this text).
        ///
        /// `times` are the markers' times (NaN = unknown), `run` the
        /// comparison run (null = none: only the time can show).
        public static string LabelText(MarkSpec spec, Attempt run, float[] times, int i, int decimals)
        {
            if (spec == null || !spec.Labels || times == null || i < 0 || i >= times.Length) return "";
            float t = times[i];
            if (!Real(t)) return "";
            StringBuilder sb = null;
            string s;

            if (spec.Time) Add(ref sb, SplitTable.Time(t, decimals));

            if (spec.Segment)
            {
                // The first marker's stretch starts at 0; an unknown earlier
                // marker leaves this stretch unknown.
                float from = 0f;
                bool known = true;
                if (i > 0) { from = times[i - 1]; known = Real(from); }
                if (known && t >= from) Add(ref sb, "seg " + SplitTable.Time(t - from, decimals));
            }

            if (spec.Speed && run != null)
            {
                float v;
                if (ReplayStats.SpeedAt(run.Samples, t, out v) && Real(v))
                    Add(ref sb, v.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
            }

            if (run != null)
            {
                for (int c = 0; c < spec.Channels.Count; c++)
                {
                    float v;
                    int ch = run.ChannelIndex(spec.Channels[c]);
                    if (ch >= 0 && run.TryStateAt(ch, t, out v) && Real(v))
                    {
                        if (v == 0f && MarkSpec.HiddenAtZero(run.Channels[ch])) continue;
                        s = MarkSpec.StatName(run.Channels[ch]) + " " + v.ToString("0.#", CultureInfo.InvariantCulture);
                        Add(ref sb, s);
                    }
                }
                for (int c = 0; c < spec.Items.Count; c++)
                    Add(ref sb, spec.Items[c] + " " + run.ItemCountAt(spec.Items[c], t).ToString(CultureInfo.InvariantCulture));
            }
            return sb == null ? "" : sb.ToString();
        }

        /// Your delta's text (drawn apart, in the splits panel's colour):
        /// "" when the delta is not chosen or your run is not past the marker.
        public static string DeltaText(MarkSpec spec, float delta, int decimals)
        {
            if (spec == null || !spec.Labels || !spec.Delta || !Real(delta)) return "";
            return SplitTable.Delta(delta, decimals);
        }

        /// One sentence for the Runs tab: why nothing shows, or what is
        /// missing. "" when everything is as chosen. `untimed`: the spot
        /// chosen now is a plain spot, so a comparison still loaded is
        /// another spot's and is not described (T-0401).
        public static string Status(MarkSpec spec, Attempt run, int markers, int splitCount, bool untimed = false)
        {
            if (spec != null && spec.Mode == MarkSpec.Kind.Off) return "Checkpoint dots are off: no dots, no labels.";
            if (untimed) return "This spot is not timed: no checkpoints to show.";
            if (run == null) return "No comparison run yet: its checkpoints show here once one is loaded.";
            if (markers == 0) return "The comparison run has no end time to mark.";
            if (splitCount == 0) return "The comparison run kept no splits, so only its end is marked.";
            if (spec != null && spec.Labels && spec.Speed && run.Samples.Count == 0) return "The comparison run has no speed recorded.";
            if (spec != null && spec.Labels && spec.Channels.Count > 0)
            {
                for (int c = 0; c < spec.Channels.Count; c++)
                    if (run.ChannelIndex(spec.Channels[c]) < 0 || run.States.Count == 0)
                        return MarkSpec.StatName(spec.Channels[c]) + " is not recorded in this comparison run: left out of its labels.";
            }
            return "";
        }

        /// The items the comparison run ever held, in the order it first
        /// got them (the Runs tab's Items list). `into` is cleared first.
        public static void ItemNames(Attempt run, List<string> into)
        {
            into.Clear();
            if (run == null) return;
            for (int i = 0; i < run.Items.Count; i++)
                if (run.Items[i].Count > 0 && into.IndexOf(run.Items[i].Name) < 0) into.Add(run.Items[i].Name);
        }

        private static void Add(ref StringBuilder sb, string part)
        {
            if (sb == null) sb = new StringBuilder(32);
            else sb.Append(Gap);
            sb.Append(part);
        }

        private static bool Real(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
