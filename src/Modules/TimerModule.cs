using System;
using ForestOverlay.Core;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Manual timer. INFO-ONLY.
    //
    // Counts unscaledDeltaTime on purpose: the game changes timeScale
    // during its own sequences, and a timer that slows down with the game
    // is useless for comparing attempts. This is real elapsed time.
    //
    // Splits are kept in a fixed-size ring so that holding the split key
    // can never grow a list without bound during a long session.
    // ------------------------------------------------------------------
    public sealed class TimerModule : OverlayModule
    {
        private const int MaxSplits = 8;

        public override string Id { get { return "timer"; } }
        public override string DisplayName { get { return "Timer"; } }

        private bool _running;
        private float _elapsed;

        private readonly float[] _splits = new float[MaxSplits];
        private int _splitCount;

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("timer.toggle", KeyCode.F8, "Start / stop timer", ToggleRunning);
            map.Add("timer.reset", KeyCode.F9, "Reset timer", Reset);
            map.Add("timer.split", KeyCode.Backslash, "Split (free timer)", Split);
        }

        private void ToggleRunning()
        {
            _running = !_running;
        }

        private void Reset()
        {
            _running = false;
            _elapsed = 0f;
            _splitCount = 0;
        }

        private void Split()
        {
            if (!_running) return;
            if (_splitCount < MaxSplits) _splits[_splitCount++] = _elapsed;
            else
            {
                // Keep the most recent splits, drop the oldest.
                for (int i = 1; i < MaxSplits; i++) _splits[i - 1] = _splits[i];
                _splits[MaxSplits - 1] = _elapsed;
            }
        }

        public override void Tick()
        {
            if (_running) _elapsed += Time.unscaledDeltaTime;
        }

        public override void ContributeHud(HudBuilder hud)
        {
            hud.Pair("Timer", Format(_elapsed) + (_running ? "   [RUN]" : "   [STOP]"));

            if (_splitCount > 0)
            {
                float last = _splits[_splitCount - 1];
                hud.Pair("Split", "#" + _splitCount + "  " + Format(last) +
                                  "   (+" + Format(_elapsed - last) + ")");
            }
        }

        public static string Format(float seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return ((int)ts.TotalMinutes).ToString("00") + ":" +
                   ts.Seconds.ToString("00") + "." +
                   ts.Milliseconds.ToString("000");
        }
    }
}
