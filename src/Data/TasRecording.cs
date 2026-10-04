using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A TAS input recording (Experimental, practice only): what the game
    // read through TheForest.Utils.Input on every frame of a run, from the
    // frame after a restart placed the player, so a replay can feed the
    // same values back on the same frame numbers (Game/TasInput,
    // Modules/TasModule). Pure: frames, names and values come in as
    // arguments.
    //
    // Stored as CHANGES only: a button's held state, an axis's value and
    // the frame rate are written on the frames they differ from the frame
    // before. A recording is built allocation-light (a struct list grown
    // by doubling, names added once) and written as text only at save.
    //
    // Text (runs/<id>/inputs/<stamp>.tas):
    //   # ForestOverlay TAS recording
    //   version|1
    //   segment|<id>          name|<segment name>      recorded|<utc>
    //   note|<how it ended>
    //   lockfps|<n>           0 = recorded at the game's own rate
    //   frames|<N>            seconds|<game time of the N frames>
    //   buttons|Jump|Run|...  axes|Horizontal|Mouse X|...  (index order)
    //   f|<frame>|<token> <token> ...
    //       +3 / -3   button 3 pressed / released
    //       a1=0.25   axis 1's value from this frame (round-trip floats)
    //       @144      the frame's rate (round(1 / unscaled delta time))
    //   p|<frame>|<t>|<x>|<y>|<z>|<yaw>|<pitch>   30 Hz position + look
    // ------------------------------------------------------------------
    public struct TasChange
    {
        public const byte Button = 0, Axis = 1, Fps = 2;
        public int Frame;
        public byte Kind;
        public int Channel;
        public float Value;
    }

    public struct TasSample
    {
        public int Frame;
        public float T;
        public float X, Y, Z;
        public float Yaw, Pitch;
    }

    public sealed class TasRecording
    {
        public const int Version = 1;
        public const string Extension = ".tas";
        /// Position samples per second of game time (as the run recorder).
        public const float SampleRate = 30f;

        public string SegmentId = "";
        public string SegmentName = "";
        public string RecordedUtc = "";
        /// How it ended, in words ("run finished in 1:02.345", "stopped by hand").
        public string Note = "";
        /// The fixed frame rate it was recorded at (0 = the game's own).
        public int LockFps;
        public int Frames;
        /// Game time from frame 0 to the last frame, seconds.
        public float Seconds;

        public readonly List<string> Buttons = new List<string>();
        public readonly List<string> Axes = new List<string>();
        public readonly List<TasChange> Changes;
        public readonly List<TasSample> Samples;

        private readonly Dictionary<string, int> _buttonIx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _axisIx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // The value each channel has now (the last change), for change-only writes.
        private readonly List<bool> _held = new List<bool>();
        private readonly List<float> _axis = new List<float>();
        private int _fps;
        private float _nextSampleT;

        public TasRecording() : this(1024, 256) { }

        public TasRecording(int changeCapacity, int sampleCapacity)
        {
            Changes = new List<TasChange>(changeCapacity);
            Samples = new List<TasSample>(sampleCapacity);
        }

        // --- building ----------------------------------------------------

        /// The channel of a button name, added on first sight.
        public int ButtonChannel(string name)
        {
            int i;
            if (_buttonIx.TryGetValue(name, out i)) return i;
            i = Buttons.Count;
            Buttons.Add(name);
            _buttonIx[name] = i;
            _held.Add(false);
            return i;
        }

        public int AxisChannel(string name)
        {
            int i;
            if (_axisIx.TryGetValue(name, out i)) return i;
            i = Axes.Count;
            Axes.Add(name);
            _axisIx[name] = i;
            _axis.Add(0f);
            return i;
        }

        public int FindButton(string name)
        {
            int i;
            return name != null && _buttonIx.TryGetValue(name, out i) ? i : -1;
        }

        public int FindAxis(string name)
        {
            int i;
            return name != null && _axisIx.TryGetValue(name, out i) ? i : -1;
        }

        public void SetButton(int frame, int channel, bool held)
        {
            if (channel < 0 || channel >= _held.Count || _held[channel] == held) return;
            _held[channel] = held;
            Add(frame, TasChange.Button, channel, held ? 1f : 0f);
        }

        public void SetAxis(int frame, int channel, float value)
        {
            if (channel < 0 || channel >= _axis.Count || _axis[channel] == value) return;
            _axis[channel] = value;
            Add(frame, TasChange.Axis, channel, value);
        }

        public void SetFps(int frame, int fps)
        {
            if (fps == _fps) return;
            _fps = fps;
            Add(frame, TasChange.Fps, 0, fps);
        }

        /// Frame `frame` is complete: the recording is frame + 1 long.
        public void EndFrame(int frame)
        {
            if (frame + 1 > Frames) Frames = frame + 1;
        }

        /// True when a position sample is due at game time `t` (seconds
        /// since frame 0): frame 0, then every 1/30 s.
        public bool SampleDue(float t)
        {
            return Samples.Count == 0 || t >= _nextSampleT;
        }

        public void AddSample(TasSample s)
        {
            Samples.Add(s);
            _nextSampleT = (Samples.Count) / SampleRate;
            // Catch up after a long frame rather than sampling every frame after it.
            if (s.T >= _nextSampleT) _nextSampleT = (float)Math.Floor(s.T * SampleRate + 1f) / SampleRate;
        }

        private void Add(int frame, byte kind, int channel, float value)
        {
            TasChange c;
            c.Frame = frame;
            c.Kind = kind;
            c.Channel = channel;
            c.Value = value;
            Changes.Add(c);
        }

        // --- text -----------------------------------------------------------

        public static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        public string Write()
        {
            StringBuilder sb = new StringBuilder(64 + Changes.Count * 8 + Samples.Count * 64);
            sb.Append("# ForestOverlay TAS recording (experimental): inputs the game read, frame by frame\n");
            sb.Append("version|").Append(Version).Append('\n');
            sb.Append("segment|").Append(Clean(SegmentId)).Append('\n');
            sb.Append("name|").Append(Clean(SegmentName)).Append('\n');
            sb.Append("recorded|").Append(Clean(RecordedUtc)).Append('\n');
            sb.Append("note|").Append(Clean(Note)).Append('\n');
            sb.Append("lockfps|").Append(LockFps).Append('\n');
            sb.Append("frames|").Append(Frames).Append('\n');
            sb.Append("seconds|").Append(F(Seconds)).Append('\n');
            sb.Append("buttons");
            for (int i = 0; i < Buttons.Count; i++) sb.Append('|').Append(Clean(Buttons[i]));
            sb.Append('\n');
            sb.Append("axes");
            for (int i = 0; i < Axes.Count; i++) sb.Append('|').Append(Clean(Axes[i]));
            sb.Append('\n');

            int frame = int.MinValue;
            for (int i = 0; i < Changes.Count; i++)
            {
                TasChange c = Changes[i];
                if (c.Frame != frame)
                {
                    if (frame != int.MinValue) sb.Append('\n');
                    frame = c.Frame;
                    sb.Append("f|").Append(frame).Append('|');
                }
                else sb.Append(' ');
                switch (c.Kind)
                {
                    case TasChange.Button: sb.Append(c.Value != 0f ? '+' : '-').Append(c.Channel); break;
                    case TasChange.Axis: sb.Append('a').Append(c.Channel).Append('=').Append(F(c.Value)); break;
                    default: sb.Append('@').Append((int)c.Value); break;
                }
            }
            if (frame != int.MinValue) sb.Append('\n');

            for (int i = 0; i < Samples.Count; i++)
            {
                TasSample s = Samples[i];
                sb.Append("p|").Append(s.Frame).Append('|').Append(F(s.T)).Append('|')
                  .Append(F(s.X)).Append('|').Append(F(s.Y)).Append('|').Append(F(s.Z)).Append('|')
                  .Append(F(s.Yaw)).Append('|').Append(F(s.Pitch)).Append('\n');
            }
            return sb.ToString();
        }

        /// The header only (no changes or samples): the lines before the
        /// first `f|` / `p|` - enough for a list row, without reading a
        /// long recording whole.
        public static TasRecording ParseHeader(System.IO.TextReader reader, out string error)
        {
            StringBuilder sb = new StringBuilder();
            string line;
            int n = 0;
            while ((line = reader.ReadLine()) != null && n++ < 64)
            {
                if (line.StartsWith("f|") || line.StartsWith("p|")) break;
                sb.Append(line).Append('\n');
            }
            return Parse(sb.ToString(), out error);
        }

        /// Null with `error` set when the text is not a recording.
        public static TasRecording Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "empty file"; return null; }
            TasRecording r = new TasRecording();
            bool sawVersion = false;
            int lastFrame = -1;
            string[] lines = text.Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n].TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('|');
                switch (p[0])
                {
                    case "version":
                        int v;
                        if (p.Length < 2 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { error = "bad version line"; return null; }
                        if (v > Version) { error = "made by a newer version (format " + v + ")"; return null; }
                        sawVersion = true;
                        break;
                    case "segment": r.SegmentId = p.Length > 1 ? p[1] : ""; break;
                    case "name": r.SegmentName = p.Length > 1 ? p[1] : ""; break;
                    case "recorded": r.RecordedUtc = p.Length > 1 ? p[1] : ""; break;
                    case "note": r.Note = p.Length > 1 ? p[1] : ""; break;
                    case "lockfps": r.LockFps = p.Length > 1 ? Int(p[1]) : 0; break;
                    case "frames": r.Frames = p.Length > 1 ? Int(p[1]) : 0; break;
                    case "seconds": r.Seconds = p.Length > 1 ? Flt(p[1]) : 0f; break;
                    case "buttons": for (int i = 1; i < p.Length; i++) r.ButtonChannel(p[i]); break;
                    case "axes": for (int i = 1; i < p.Length; i++) r.AxisChannel(p[i]); break;
                    case "f":
                    {
                        if (p.Length < 3) { error = "line " + (n + 1) + ": bad frame line"; return null; }
                        int frame = Int(p[1]);
                        if (frame < lastFrame) { error = "line " + (n + 1) + ": frames out of order"; return null; }
                        lastFrame = frame;
                        string[] toks = p[2].Split(' ');
                        for (int t = 0; t < toks.Length; t++)
                            if (toks[t].Length > 0 && !ParseToken(r, frame, toks[t]))
                            { error = "line " + (n + 1) + ": bad change '" + toks[t] + "'"; return null; }
                        break;
                    }
                    case "p":
                    {
                        if (p.Length < 8) { error = "line " + (n + 1) + ": bad sample line"; return null; }
                        TasSample s;
                        s.Frame = Int(p[1]);
                        s.T = Flt(p[2]); s.X = Flt(p[3]); s.Y = Flt(p[4]); s.Z = Flt(p[5]);
                        s.Yaw = Flt(p[6]); s.Pitch = Flt(p[7]);
                        r.Samples.Add(s);
                        break;
                    }
                }
            }
            if (!sawVersion) { error = "not a TAS recording (no version line)"; return null; }
            if (lastFrame >= r.Frames) r.Frames = lastFrame + 1;
            return r;
        }

        private static bool ParseToken(TasRecording r, int frame, string tok)
        {
            char k = tok[0];
            if (k == '+' || k == '-')
            {
                int ch = Int(tok.Substring(1));
                if (ch < 0 || ch >= r.Buttons.Count) return false;
                r.SetButton(frame, ch, k == '+');
                return true;
            }
            if (k == 'a')
            {
                int eq = tok.IndexOf('=');
                if (eq < 2) return false;
                int ch = Int(tok.Substring(1, eq - 1));
                float v;
                if (ch < 0 || ch >= r.Axes.Count || !float.TryParse(tok.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                r.SetAxis(frame, ch, v);
                return true;
            }
            if (k == '@')
            {
                int fps = Int(tok.Substring(1));
                if (fps <= 0) return false;
                r.SetFps(frame, fps);
                return true;
            }
            return false;
        }

        private static int Int(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        private static float Flt(string s)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
        }

        /// "20261004-142233" from a UTC time - the file's name.
        public static string Stamp(DateTime utc)
        {
            return utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }
    }

    // ------------------------------------------------------------------
    // A recording expanded to per-frame values for the replay: O(1), no
    // allocation per read. Down / Up are derived from the held state the
    // frame before (Rewired's own definition); GetAxisDown is the game's
    // "previous value 0 ? value : 0" (Input.GetAxisDown, IL).
    // ------------------------------------------------------------------
    public sealed class TasPlayback
    {
        public readonly TasRecording Recording;
        public int Frames { get { return Recording.Frames; } }
        private readonly bool[][] _held;
        private readonly float[][] _axis;
        private readonly int[] _fps;

        public TasPlayback(TasRecording r)
        {
            Recording = r;
            int n = Math.Max(0, r.Frames);
            _held = new bool[r.Buttons.Count][];
            for (int i = 0; i < _held.Length; i++) _held[i] = new bool[n];
            _axis = new float[r.Axes.Count][];
            for (int i = 0; i < _axis.Length; i++) _axis[i] = new float[n];
            _fps = new int[n];

            bool[] held = new bool[_held.Length];
            float[] axis = new float[_axis.Length];
            int fps = 0;
            int c = 0;
            for (int f = 0; f < n; f++)
            {
                while (c < r.Changes.Count && r.Changes[c].Frame <= f)
                {
                    TasChange ch = r.Changes[c++];
                    if (ch.Kind == TasChange.Button) held[ch.Channel] = ch.Value != 0f;
                    else if (ch.Kind == TasChange.Axis) axis[ch.Channel] = ch.Value;
                    else fps = (int)ch.Value;
                }
                for (int i = 0; i < held.Length; i++) _held[i][f] = held[i];
                for (int i = 0; i < axis.Length; i++) _axis[i][f] = axis[i];
                _fps[f] = fps;
            }
        }

        public int FindButton(string name) { return Recording.FindButton(name); }
        public int FindAxis(string name) { return Recording.FindAxis(name); }

        public bool Held(int ch, int frame)
        {
            return ch >= 0 && ch < _held.Length && frame >= 0 && frame < _fps.Length && _held[ch][frame];
        }

        public bool Down(int ch, int frame) { return Held(ch, frame) && !Held(ch, frame - 1); }
        public bool Up(int ch, int frame) { return !Held(ch, frame) && Held(ch, frame - 1); }

        public float Axis(int ch, int frame)
        {
            return ch >= 0 && ch < _axis.Length && frame >= 0 && frame < _fps.Length ? _axis[ch][frame] : 0f;
        }

        public float AxisDown(int ch, int frame)
        {
            return Axis(ch, frame - 1) == 0f ? Axis(ch, frame) : 0f;
        }

        /// The frame's recorded rate, 0 when unknown.
        public int Fps(int frame)
        {
            return frame >= 0 && frame < _fps.Length ? _fps[frame] : 0;
        }
    }

    // ------------------------------------------------------------------
    // How far a replay went from its recording: the recording's 30 Hz
    // samples against the replay's on the same frame numbers.
    // ------------------------------------------------------------------
    public sealed class TasDrift
    {
        public int Compared;
        public float Max;
        public float MaxAtT;
        public int MaxAtFrame;
        public float End;
        public float MaxLook;

        public static TasDrift Compare(IList<TasSample> recorded, IList<TasSample> replayed)
        {
            TasDrift d = new TasDrift();
            int j = 0;
            for (int i = 0; i < recorded.Count; i++)
            {
                TasSample a = recorded[i];
                while (j < replayed.Count && replayed[j].Frame < a.Frame) j++;
                if (j >= replayed.Count) break;
                TasSample b = replayed[j];
                if (b.Frame != a.Frame) continue;
                float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                d.Compared++;
                d.End = dist;
                if (dist > d.Max || d.Compared == 1) { d.Max = dist; d.MaxAtT = a.T; d.MaxAtFrame = a.Frame; }
                float look = Math.Max(Math.Abs(Angle(a.Yaw - b.Yaw)), Math.Abs(Angle(a.Pitch - b.Pitch)));
                if (look > d.MaxLook) d.MaxLook = look;
            }
            return d;
        }

        /// -180..180.
        public static float Angle(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return deg;
        }

        public string Describe()
        {
            if (Compared == 0) return "no samples to compare";
            CultureInfo c = CultureInfo.InvariantCulture;
            return "max drift " + Max.ToString("0.00", c) + " m at " + MaxAtT.ToString("0.00", c) + " s (frame " + MaxAtFrame +
                   "), " + End.ToString("0.00", c) + " m at the end, look up to " + MaxLook.ToString("0.0", c) + " deg, " +
                   Compared + " sample(s) compared";
        }
    }
}
