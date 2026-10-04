using System;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Puts the weather back after a restore (Quick and Full load).
    //
    // WHY (bridge, 2026-10-04): TheForest.World.WeatherSystem is
    // [DoNotSerializePublic] and keeps only LastRainTime in the save, and
    // TheForestAtmosphere's fog distance is not saved either. A Quick load
    // kept the live weather (rain on, overcast 1, fog 300 m stayed through a
    // restore of a clear capture - maks's fog after a Quick load, backlog);
    // a Full load builds the weather afresh (clear, fog 1294 m), so a rainy
    // capture came back dry.
    //
    // WHAT holds the look (decompiled WeatherSystem, read live):
    //  - State / CurrentType, and the rain objects under Scene.RainTypes,
    //    switched by the game's own AllOff() / TurnOn(type);
    //  - the cloud values the game eases (current, target; velocities are
    //    zeroed so the easing starts from rest);
    //  - what is drawn: the cloud material (`CloudOvercastMat`: OvercastAmount,
    //    CloudOpacityScale, AlphaSaturation - the easing reads these back
    //    every frame) and the volumetric clouds' `_Coverage`
    //    (`vClouds.materialUsed`);
    //  - the rain rolls (RainDice, RainDiceStop, RainStopRolls), so growing
    //    clouds turn into the same rain;
    //  - TheForestAtmosphere.FogCurrent (re-rolled 700-2000 every 600 s) and
    //    Visibility (the drawn fog distance, stepping 1 a frame towards it).
    // Put back with every one of those set, a cleared rain looked like a
    // fresh Full load of the clear capture, and a clear sky given the rain
    // values looked like the rain (shots wx-p*/wx-q*, 2026-10-04).
    // Not kept: the rainbow, a lightning flash, the next roll's timers.
    // ------------------------------------------------------------------
    internal sealed class WeatherKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ManualLogSource _log;
        private bool _bound;
        private Type _type, _atmType;
        private FieldInfo _state, _currentType, _dice, _diceStop, _stopRolls, _mat, _vClouds;
        private FieldInfo _overcastCur, _overcastTarget, _overcastVel;
        private FieldInfo _opacityCur, _opacityTarget, _opacityVel;
        private FieldInfo _alphaCur, _alphaTarget, _alphaVel;
        private FieldInfo _skyCur, _skyTarget, _skyVel;
        private FieldInfo _coverageCur, _coverageTarget, _coverageVel;
        private FieldInfo _atmInstance, _fogCurrent, _visibility;
        private MethodInfo _allOff, _turnOn;

        private const string MatOvercast = "OvercastAmount", MatOpacity = "CloudOpacityScale", MatAlpha = "AlphaSaturation";
        private const string VCoverage = "_Coverage";

        public WeatherKeeper(ManualLogSource log) { _log = log; }

        private bool Bind()
        {
            if (_bound) return _state != null;
            _bound = true;
            _type = GameBridge.FindGameType("TheForest.World.WeatherSystem");
            _atmType = GameBridge.FindGameType("TheForestAtmosphere");
            if (_type == null) { _log.LogWarning("WeatherKeeper: TheForest.World.WeatherSystem not found - the weather is not kept."); return false; }
            _state = _type.GetField("State", Inst);
            _currentType = _type.GetField("CurrentType", Inst);
            _dice = _type.GetField("RainDice", Inst);
            _diceStop = _type.GetField("RainDiceStop", Inst);
            _stopRolls = _type.GetField("RainStopRolls", Inst);
            _mat = _type.GetField("CloudOvercastMat", Inst);
            _vClouds = _type.GetField("vClouds", Inst);
            _overcastCur = _type.GetField("CloudOvercastCurrentValue", Inst);
            _overcastTarget = _type.GetField("CloudOvercastTargetValue", Inst);
            _overcastVel = _type.GetField("CloudOvercastVelocity", Inst);
            _opacityCur = _type.GetField("CloudOpacityScaleCurrentValue", Inst);
            _opacityTarget = _type.GetField("CloudOpacityScaleTargetValue", Inst);
            _opacityVel = _type.GetField("CloudOpacityScaleVelocity", Inst);
            _alphaCur = _type.GetField("CloudAlphaSaturationCurrentValue", Inst);
            _alphaTarget = _type.GetField("CloudAlphaSaturationTargetValue", Inst);
            _alphaVel = _type.GetField("CloudAlphaSaturationVelocity", Inst);
            _skyCur = _type.GetField("CloudSkyColorMultiplyerCurrentValue", Inst);
            _skyTarget = _type.GetField("CloudSkyColorMultiplyerTargetValue", Inst);
            _skyVel = _type.GetField("CloudSkyColorMultiplyerVelocity", Inst);
            _coverageCur = _type.GetField("VCloudCoverageCurrentValue", Inst);
            _coverageTarget = _type.GetField("VCloudCoverageTargetValue", Inst);
            _coverageVel = _type.GetField("VCloudCoverageVelocity", Inst);
            _allOff = _type.GetMethod("AllOff", Inst, null, Type.EmptyTypes, null);
            _turnOn = _currentType != null ? _type.GetMethod("TurnOn", Inst, null, new[] { _currentType.FieldType }, null) : null;
            if (_atmType != null)
            {
                _atmInstance = _atmType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _fogCurrent = _atmType.GetField("FogCurrent", Inst);
                _visibility = _atmType.GetField("Visibility", Inst);
            }
            if (_state == null || _currentType == null || _allOff == null || _turnOn == null)
            {
                _log.LogWarning("WeatherKeeper: WeatherSystem State / CurrentType / AllOff / TurnOn not found - the weather is not kept.");
                _state = null;
                return false;
            }
            return true;
        }

        private object Live()
        {
            return GameBridge.ReadStaticField("TheForest.Utils.Scene", "WeatherSystem");
        }

        private object Atmosphere()
        {
            return _atmInstance != null ? _atmInstance.GetValue(null) : null;
        }

        /// The header value at capture; "" when there is no weather.
        public string Capture()
        {
            try
            {
                WeatherState s = Read();
                return s == null ? "" : s.Write();
            }
            catch (Exception ex) { _log.LogWarning("WeatherKeeper: capture failed: " + ex.Message); return ""; }
        }

        private WeatherState Read()
        {
            if (!Bind()) return null;
            object w = Live();
            if (w == null || (w is UnityEngine.Object && (UnityEngine.Object)w == null)) return null;
            WeatherState s = new WeatherState();
            s.State = Convert.ToString(_state.GetValue(w));
            s.Type = Convert.ToString(_currentType.GetValue(w));
            if (_dice != null && _diceStop != null && _stopRolls != null)
            {
                s.HasDice = true;
                s.RainDice = (int)_dice.GetValue(w);
                s.RainDiceStop = (int)_diceStop.GetValue(w);
                s.RainStopRolls = (int)_stopRolls.GetValue(w);
            }
            s.OvercastCurrent = Get(_overcastCur, w); s.OvercastTarget = Get(_overcastTarget, w);
            s.OpacityCurrent = Get(_opacityCur, w); s.OpacityTarget = Get(_opacityTarget, w);
            s.AlphaCurrent = Get(_alphaCur, w); s.AlphaTarget = Get(_alphaTarget, w);
            s.SkyCurrent = Get(_skyCur, w); s.SkyTarget = Get(_skyTarget, w);
            s.CoverageCurrent = Get(_coverageCur, w); s.CoverageTarget = Get(_coverageTarget, w);
            Material mat = CloudMaterial(w);
            if (mat != null)
            {
                s.MatOvercast = MatGet(mat, MatOvercast);
                s.MatOpacity = MatGet(mat, MatOpacity);
                s.MatAlpha = MatGet(mat, MatAlpha);
            }
            Material vc = VCloudsMaterial(w);
            if (vc != null) s.VCloudsCoverage = MatGet(vc, VCoverage);
            object atm = Atmosphere();
            if (atm != null && _fogCurrent != null && _visibility != null)
            {
                s.FogTarget = Get(_fogCurrent, atm);
                s.FogDrawn = Get(_visibility, atm);
            }
            return s;
        }

        /// After a restore (the player placed); the log note, "" when the
        /// file has no weather line.
        public string Restore(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                WeatherState s;
                if (!WeatherState.TryParse(value, out s)) return "weather: unreadable line, left as it is";
                if (!Bind()) return "weather: not kept (game names not found)";
                object w = Live();
                if (w == null || (w is UnityEngine.Object && (UnityEngine.Object)w == null)) return "weather: no weather system loaded, left as it is";

                WeatherState was = Read();
                string before = was != null ? was.Describe() : "?";

                // The rain objects the game's own way: AllOff, then TurnOn
                // for rain (it sets State = Raining and picks rain or snow by
                // where the player stands); State itself after, for the
                // growing / reducing states.
                object type = ParseEnum(_currentType.FieldType, s.Type);
                object state = ParseEnum(_state.FieldType, s.State);
                _allOff.Invoke(w, null);
                if (type != null && Convert.ToInt32(type) != 0 && s.State == "Raining") _turnOn.Invoke(w, new[] { type });
                if (state != null) _state.SetValue(w, state);

                if (s.HasDice && _dice != null && _diceStop != null && _stopRolls != null)
                {
                    _dice.SetValue(w, s.RainDice);
                    _diceStop.SetValue(w, s.RainDiceStop);
                    _stopRolls.SetValue(w, s.RainStopRolls);
                }
                Pair(w, _overcastCur, _overcastTarget, _overcastVel, s.OvercastCurrent, s.OvercastTarget);
                Pair(w, _opacityCur, _opacityTarget, _opacityVel, s.OpacityCurrent, s.OpacityTarget);
                Pair(w, _alphaCur, _alphaTarget, _alphaVel, s.AlphaCurrent, s.AlphaTarget);
                Pair(w, _skyCur, _skyTarget, _skyVel, s.SkyCurrent, s.SkyTarget);
                Pair(w, _coverageCur, _coverageTarget, _coverageVel, s.CoverageCurrent, s.CoverageTarget);

                Material mat = CloudMaterial(w);
                if (mat != null)
                {
                    MatSet(mat, MatOvercast, s.MatOvercast);
                    MatSet(mat, MatOpacity, s.MatOpacity);
                    MatSet(mat, MatAlpha, s.MatAlpha);
                }
                Material vc = VCloudsMaterial(w);
                if (vc != null) MatSet(vc, VCoverage, s.VCloudsCoverage);

                object atm = Atmosphere();
                if (atm != null && _fogCurrent != null && _visibility != null)
                {
                    if (!float.IsNaN(s.FogTarget)) _fogCurrent.SetValue(atm, s.FogTarget);
                    if (!float.IsNaN(s.FogDrawn)) _visibility.SetValue(atm, s.FogDrawn);
                }

                string after = s.Describe();
                return "weather: " + after + (before == after ? " (as it was)" : " put back (was " + before + ")");
            }
            catch (Exception ex) { return "weather: restore failed (" + ex.Message + ")"; }
        }

        private static void Pair(object w, FieldInfo cur, FieldInfo target, FieldInfo vel, float c, float t)
        {
            if (cur != null && !float.IsNaN(c)) cur.SetValue(w, c);
            if (target != null && !float.IsNaN(t)) target.SetValue(w, t);
            if (vel != null && !float.IsNaN(c)) vel.SetValue(w, 0f);
        }

        private static float Get(FieldInfo f, object o)
        {
            if (f == null) return float.NaN;
            object v = f.GetValue(o);
            return v is float ? (float)v : float.NaN;
        }

        private Material CloudMaterial(object w)
        {
            return _mat != null ? _mat.GetValue(w) as Material : null;
        }

        private Material VCloudsMaterial(object w)
        {
            Component vc = _vClouds != null ? _vClouds.GetValue(w) as Component : null;
            if (vc == null) return null;
            FieldInfo f = vc.GetType().GetField("materialUsed", Inst);
            return f != null ? f.GetValue(vc) as Material : null;
        }

        private static float MatGet(Material m, string name)
        {
            return m.HasProperty(name) ? m.GetFloat(name) : float.NaN;
        }

        private static void MatSet(Material m, string name, float value)
        {
            if (!float.IsNaN(value) && m.HasProperty(name)) m.SetFloat(name, value);
        }

        private static object ParseEnum(Type t, string name)
        {
            if (string.IsNullOrEmpty(name) || !t.IsEnum) return null;
            try { return Enum.Parse(t, name); }
            catch (Exception) { return null; }
        }
    }
}
