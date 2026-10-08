using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Captures the player's full numeric state as named channels.
    //
    // WHY REFLECTION OVER A FIXED STRUCT
    // The brief was "record everything useful and let the runner filter
    // later", so hand-picking fields would be exactly the wrong shape: it
    // decides today what matters, and anything left out is unbackfillable
    // because the runs are already recorded. Instead every numeric and
    // boolean field on PlayerStats becomes a channel automatically. If a
    // game update adds a stat, it is captured without a code change.
    //
    // Channels are discovered ONCE and the FieldInfo array cached, so a
    // sample is an array walk rather than a reflection lookup per field.
    // Each field also gets a typed getter delegate (Game/FastField) bound
    // once: FieldInfo.GetValue boxed every one of the ~60 values (24-32
    // bytes each, five times a second, for the whole of a run).
    //
    // Bools are stored as 0/1 so a sample is a flat float[] - one shape
    // for the file format, the comparison maths and the eventual viewer.
    //
    // Confirmed present on PlayerStats: Health, Stamina, Energy, Fullness,
    // Thirst, BodyTemp, Armor, ColdArmor, BatteryCharge, Stealth,
    // PedometerSteps, Hunger, InfectionChance, BleedChance, Cold, IsTired,
    // Run, Dead, Sitted and ~40 more.
    // ------------------------------------------------------------------
    public sealed class PlayerStateReader
    {
        private readonly ManualLogSource _log;

        private Component _stats;
        private FieldInfo[] _fields;
        private string[] _channels;
        private float[] _values;

        // One getter per channel, by the field's type (the others stay null).
        private enum Kind : byte { Float, Int, Bool, Double, Short, Byte }
        private Kind[] _kinds = new Kind[0];
        private Func<object, float>[] _getFloat = new Func<object, float>[0];
        private Func<object, int>[] _getInt = new Func<object, int>[0];
        private Func<object, bool>[] _getBool = new Func<object, bool>[0];
        private Func<object, double>[] _getDouble = new Func<object, double>[0];
        private Func<object, short>[] _getShort = new Func<object, short>[0];
        private Func<object, byte>[] _getByte = new Func<object, byte>[0];

        private const float RetryInterval = 1f;
        private float _nextResolve;

        public bool Available { get { return _stats != null && _fields != null; } }
        public string[] Channels { get { return _channels; } }

        public PlayerStateReader(ManualLogSource log)
        {
            _log = log;
            _channels = new string[0];
            _values = new float[0];
        }

        public void Reset()
        {
            _stats = null;
            _fields = null;
            _channels = new string[0];
            _values = new float[0];
        }

        /// Re-resolves whenever the component dies, the same way the
        /// inventory does - a save load replaces it.
        public void Resolve()
        {
            if (_stats != null) return;

            // FindObjectOfType walks every loaded object. Called from a
            // per-frame Tick, a miss (mid-load, or no PlayerStats) meant a
            // full scene walk every frame - rate-limit the retry.
            if (Time.unscaledTime < _nextResolve) return;
            _nextResolve = Time.unscaledTime + RetryInterval;

            Type t = GameBridge.FindGameType("PlayerStats");
            if (t == null) return;

            UnityEngine.Object found;
            try { found = UnityEngine.Object.FindObjectOfType(t); }
            catch (Exception) { return; }
            if (found == null) return;

            _stats = found as Component;
            Bind(t);
        }

        private void Bind(Type t)
        {
            FieldInfo[] all = t.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                          BindingFlags.NonPublic);

            List<FieldInfo> usable = new List<FieldInfo>();
            List<string> names = new List<string>();

            for (int i = 0; i < all.Length; i++)
            {
                FieldInfo f = all[i];
                if (!IsCapturable(f.FieldType)) continue;

                // Compiler-generated backing fields would appear as
                // "<Prop>k__BackingField"; the property itself is not
                // captured, so skip the noise rather than emit unreadable
                // channel names.
                if (f.Name.IndexOf('<') >= 0) continue;

                usable.Add(f);
                names.Add(f.Name);
            }

            _fields = usable.ToArray();
            _channels = names.ToArray();
            _values = new float[_channels.Length];
            BindGetters();

            _log.LogInfo("PlayerStats bound: " + _channels.Length + " state channels.");
        }

        private void BindGetters()
        {
            int n = _fields.Length;
            _kinds = new Kind[n];
            _getFloat = new Func<object, float>[n];
            _getInt = new Func<object, int>[n];
            _getBool = new Func<object, bool>[n];
            _getDouble = new Func<object, double>[n];
            _getShort = new Func<object, short>[n];
            _getByte = new Func<object, byte>[n];
            for (int i = 0; i < n; i++)
            {
                Type t = _fields[i].FieldType;
                if (t == typeof(float)) { _kinds[i] = Kind.Float; _getFloat[i] = FastField.Instance<float>(_fields[i]); }
                else if (t == typeof(int)) { _kinds[i] = Kind.Int; _getInt[i] = FastField.Instance<int>(_fields[i]); }
                else if (t == typeof(bool)) { _kinds[i] = Kind.Bool; _getBool[i] = FastField.Instance<bool>(_fields[i]); }
                else if (t == typeof(double)) { _kinds[i] = Kind.Double; _getDouble[i] = FastField.Instance<double>(_fields[i]); }
                else if (t == typeof(short)) { _kinds[i] = Kind.Short; _getShort[i] = FastField.Instance<short>(_fields[i]); }
                else { _kinds[i] = Kind.Byte; _getByte[i] = FastField.Instance<byte>(_fields[i]); }
            }
        }

        private static bool IsCapturable(Type t)
        {
            return t == typeof(float) || t == typeof(int) || t == typeof(bool) ||
                   t == typeof(double) || t == typeof(short) || t == typeof(byte);
        }

        /// Reads the current values. The returned array is REUSED between
        /// calls - copy it if you intend to keep it.
        public float[] Read()
        {
            if (!Available) return null;

            for (int i = 0; i < _fields.Length; i++)
            {
                try
                {
                    switch (_kinds[i])
                    {
                        case Kind.Float: _values[i] = _getFloat[i](_stats); break;
                        case Kind.Int: _values[i] = _getInt[i](_stats); break;
                        case Kind.Bool: _values[i] = _getBool[i](_stats) ? 1f : 0f; break;
                        case Kind.Double: _values[i] = (float)_getDouble[i](_stats); break;
                        case Kind.Short: _values[i] = _getShort[i](_stats); break;
                        default: _values[i] = _getByte[i](_stats); break;
                    }
                }
                catch (Exception)
                {
                    _values[i] = 0f;
                }
            }

            return _values;
        }
    }
}
