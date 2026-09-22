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

        public bool Available { get { return _stats != null && _fields != null; } }
        public string[] Channels { get { return _channels; } }
        public int ChannelCount { get { return _channels == null ? 0 : _channels.Length; } }

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

            _log.LogInfo("PlayerStats bound: " + _channels.Length + " state channels.");
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
                    object v = _fields[i].GetValue(_stats);

                    if (v is float) _values[i] = (float)v;
                    else if (v is int) _values[i] = (int)v;
                    else if (v is bool) _values[i] = ((bool)v) ? 1f : 0f;
                    else if (v is double) _values[i] = (float)(double)v;
                    else if (v is short) _values[i] = (short)v;
                    else if (v is byte) _values[i] = (byte)v;
                    else _values[i] = 0f;
                }
                catch (Exception)
                {
                    _values[i] = 0f;
                }
            }

            return _values;
        }

        /// Value of one named channel right now, for HUD readouts.
        public bool TryGet(string channel, out float value)
        {
            value = 0f;
            if (!Available) return false;

            for (int i = 0; i < _channels.Length; i++)
            {
                if (!string.Equals(_channels[i], channel, StringComparison.OrdinalIgnoreCase)) continue;
                float[] v = Read();
                if (v == null) return false;
                value = v[i];
                return true;
            }
            return false;
        }
    }
}
