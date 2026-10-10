using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BepInEx.Logging;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Main-thread stall watch (T-0284). A Slot 1 load from the title
    // screen hung once with the main thread spinning in some managed
    // OnGUI; a native stack sample cannot say whose. Here the main thread
    // stamps a heartbeat every Update and leaves a breadcrumb (which of our
    // hooks, which module, which tab) around every call into a module; a
    // background thread notices when the heartbeat stops and writes where
    // the main thread was:
    //
    //   Stall: main thread stuck 10 s in OnGUI > DrawPanel MainWindow > tab Practice (frame 81234)
    //   Stall: main thread stuck 10 s outside the plugin's Update / OnGUI (game code, a coroutine or a Harmony patch) (frame 81234)
    //   Stall: main thread back after 14.2 s
    //
    // The log copy (LogKeeper) runs on the main thread, so during a hang
    // the lines go straight to logs/stall.log (zipped with the report);
    // once the main thread is back they are logged as usual too.
    //
    // Main thread: two int writes per breadcrumb and one long per frame,
    // nothing allocated. The watch thread never touches a Unity API.
    // ------------------------------------------------------------------
    public static class StallWatch
    {
        public enum Hook
        {
            None, Update, ModuleTick, Hud, OnGui, DrawHud, DrawScreen, DrawScreenAlways, DrawPanel, Notice,
            Window, ExplorerWindow   // a GUI.Window function: IMGUI runs it after our OnGUI returned
        }

        private const double StallSeconds = 10.0;
        private const double RepeatSeconds = 60.0;

        private static long _beat;                 // Stopwatch ticks of the last Update
        private static volatile int _frame;
        private static volatile int _hook;
        private static volatile int _module = -1;
        private static volatile int _tab = -1;
        private static volatile bool _paused;      // unfocused with runInBackground off: no frames, no stall
        private static volatile bool _hasLines;    // lines the watch wrote, for the main thread's log
        private static readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();

        private static string[] _names = new string[0];
        private static string _file;
        private static ManualLogSource _log;
        private static Thread _thread;
        private static readonly object Gate = new object();

        public static void Start(ManualLogSource log, string logFolder, string[] moduleNames)
        {
            if (_thread != null) return;
            _log = log;
            _names = moduleNames ?? new string[0];
            _file = logFolder != null ? Path.Combine(logFolder, "stall.log") : null;
            Interlocked.Exchange(ref _beat, Stopwatch.GetTimestamp());
            try
            {
                _thread = new Thread(Watch);
                _thread.IsBackground = true;
                _thread.Name = "ForestOverlay stall watch";
                _thread.Start();
                log.LogInfo("Stall: watching the main thread (a line when it stops for " + StallSeconds.ToString("0") + " s)");
            }
            catch (Exception ex) { log.LogWarning("Stall: watch not started: " + ex.Message); }
        }

        /// Main thread, first thing every Update.
        public static void Beat(int frame)
        {
            Interlocked.Exchange(ref _beat, Stopwatch.GetTimestamp());
            _frame = frame;
            if (!_hasLines) return;
            string[] lines;
            lock (Gate)
            {
                lines = Lines.ToArray();
                Lines.Clear();
                _hasLines = false;
            }
            if (_log == null) return;
            for (int i = 0; i < lines.Length; i++) _log.LogWarning(lines[i]);   // log: Stall
        }

        public static void At(Hook hook, int module)
        {
            _hook = (int)hook;
            _module = module;
            _tab = -1;
        }

        /// MainWindow, around the active tab's DrawTab.
        public static void Tab(int module) { _tab = module; }

        public static void Leave()
        {
            _hook = (int)Hook.None;
            _module = -1;
            _tab = -1;
        }

        /// OnApplicationFocus with runInBackground off: Unity stops the
        /// frames, which is not a stall.
        public static void SetPaused(bool paused)
        {
            _paused = paused;
            if (!paused) Interlocked.Exchange(ref _beat, Stopwatch.GetTimestamp());
        }

        // ------------------------------------------------------------------
        private static void Watch()
        {
            long stalledAt = 0;          // the beat that stopped (0 = running)
            double reportedUpTo = 0.0;
            while (true)
            {
                try
                {
                    Thread.Sleep(500);
                    long beat = Interlocked.Read(ref _beat);
                    double since = (Stopwatch.GetTimestamp() - beat) / (double)Stopwatch.Frequency;

                    if (stalledAt != 0 && beat != stalledAt)
                    {
                        Write("main thread back after " + ((beat - stalledAt) / (double)Stopwatch.Frequency).ToString("0.0") + " s");
                        stalledAt = 0;
                    }
                    if (_paused || since < StallSeconds) continue;
                    if (stalledAt == 0)
                    {
                        stalledAt = beat;
                        reportedUpTo = 0.0;
                    }
                    if (since < reportedUpTo + (reportedUpTo == 0.0 ? StallSeconds : RepeatSeconds)) continue;
                    reportedUpTo = since;
                    Write("main thread stuck " + since.ToString("0") + " s " + Where() + " (frame " + _frame + ")");
                }
                catch (ThreadAbortException) { return; }
                catch (Exception) { }
            }
        }

        // Read once each: the main thread may move on between the reads,
        // which only makes the line less exact.
        private static string Where()
        {
            int hook = _hook, module = _module, tab = _tab;
            if (hook == (int)Hook.None) return "outside the plugin's Update / OnGUI (game code, a coroutine or a Harmony patch)";
            bool update = hook == (int)Hook.Update || hook == (int)Hook.ModuleTick || hook == (int)Hook.Hud;
            string s = update ? "in Update" : "in OnGUI";
            if (hook != (int)Hook.Update && hook != (int)Hook.OnGui) s += " > " + ((Hook)hook);
            if (module >= 0) s += " " + Name(module);
            if (tab >= 0) s += " > tab " + Name(tab);
            return s;
        }

        private static string Name(int i)
        {
            string[] names = _names;
            return i < names.Length ? names[i] : "module " + i;
        }

        private static void Write(string what)
        {
            string line = "Stall: " + what;
            try
            {
                if (_file != null)
                    File.AppendAllText(_file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch (Exception) { }
            lock (Gate)
            {
                Lines.Add(line);
                _hasLines = true;
            }
        }
    }
}
