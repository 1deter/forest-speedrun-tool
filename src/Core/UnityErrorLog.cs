using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Unity's own errors into our logs (T-0276). BepInEx keeps them out of
    // LogOutput.log ([Logging.Disk] WriteUnityLog = false), the game writes
    // no output_log.txt, and a render-thread message - "d3d11: failed to
    // create 2D texture ... [D3D error was %x]", the line that would name
    // the d3d11 crash's cause (T-0190) - reaches only
    // Application.logMessageReceivedThreaded. So:
    //
    //   Unity: [Error] d3d11: failed to create 2D texture id=4516 ... (off the main thread)
    //
    // Errors, asserts and exceptions only, filtered by Data/UnityLogFilter
    // (repeats counted, 30 lines a minute). Each line is appended to
    // logs/unity.log at once, from whatever thread logged it - the next
    // step may be the crash, before the 2 s log copy - and handed to the
    // main thread for the session log, as StallWatch does.
    //
    // The callback never touches a Unity API (it can run on the render
    // thread) and does nothing for Log / Warning messages.
    // ------------------------------------------------------------------
    public static class UnityErrorLog
    {
        public const string FileName = "unity.log";
        private const long MaxFileBytes = 1024 * 1024;

        private static readonly object Gate = new object();
        private static readonly UnityLogFilter Filter = new UnityLogFilter();
        private static readonly List<string> Lines = new List<string>();
        private static readonly Stopwatch Clock = new Stopwatch();
        private static volatile bool _hasLines;
        private static ManualLogSource _log;
        private static string _file;
        private static int _mainThread;
        private static bool _started;

        public static void Start(ManualLogSource log, string logFolder, string session)
        {
            if (_started) return;
            _log = log;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            Clock.Start();
            try
            {
                if (logFolder != null)
                {
                    _file = Path.Combine(logFolder, FileName);
                    // One file across sessions, started over once it is big;
                    // each session opens with a header so a crash folder's
                    // copy shows which lines are whose.
                    if (File.Exists(_file) && new FileInfo(_file).Length > MaxFileBytes) File.Delete(_file);
                    File.AppendAllText(_file, Environment.NewLine + Stamp() + " --- session " + session + Environment.NewLine);
                }
                Application.logMessageReceivedThreaded += OnMessage;
                _started = true;
                log.LogInfo("Unity errors: logged here and to logs/" + FileName + " (repeats counted, at most " +
                            UnityLogFilter.PerMinute + " lines a minute)");
            }
            catch (Exception ex) { log.LogWarning("Unity errors: not logged: " + ex.Message); }
        }

        /// Main thread, every Update: the lines another thread wrote.
        public static void Flush()
        {
            if (!_hasLines) return;
            string[] lines;
            lock (Gate)
            {
                lines = Lines.ToArray();
                Lines.Clear();
                _hasLines = false;
            }
            if (_log == null) return;
            for (int i = 0; i < lines.Length; i++) _log.LogWarning(lines[i]);   // log: Unity
        }

        public static void Stop()
        {
            if (!_started) return;
            try { Application.logMessageReceivedThreaded -= OnMessage; } catch (Exception) { }
            _started = false;
        }

        private static void OnMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Assert && type != LogType.Exception) return;
            try
            {
                bool off = Thread.CurrentThread.ManagedThreadId != _mainThread;
                lock (Gate)
                {
                    string line = Filter.Next(type.ToString(), condition, stackTrace, off, Clock.Elapsed.TotalSeconds);
                    if (line == null) return;
                    if (_file != null)
                    {
                        try { File.AppendAllText(_file, Stamp() + " " + line + Environment.NewLine); }
                        catch (Exception) { }
                    }
                    Lines.Add(line);
                    _hasLines = true;
                }
            }
            catch (Exception) { }   // never throw back into Unity's logger
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
