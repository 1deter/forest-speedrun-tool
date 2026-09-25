using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using ForestOverlay.Data;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Keeps the last few sessions' LogOutput.log (QA tooling, v0.24.55).
    //
    // The log is the test harness (CLAUDE.md gotcha 16) and every launch
    // replaces it, so a tester who restarts before sending it loses the
    // session. BepInEx truncates it before any plugin runs, so this copies
    // as it goes: every couple of seconds the bytes BepInEx appended since
    // the last copy are appended to config/ForestOverlay/logs/
    // LogOutput-<start time>.log. A crash or hang loses a second or two at
    // most. On startup the oldest copies beyond KeptLogs are deleted.
    //
    // BepInEx holds the log open with read sharing, so it is read with
    // FileShare.ReadWrite. Main thread: only the new bytes are read, and
    // only when the length changed.
    // ------------------------------------------------------------------
    public sealed class LogKeeper
    {
        private const float CopyInterval = 2f;

        private readonly ManualLogSource _log;
        private readonly string _source;
        private readonly string _folder;
        private readonly string _target;
        private long _copied;
        private float _nextCopy;
        private bool _failed;
        private readonly byte[] _buffer = new byte[64 * 1024];

        /// The folder holding the kept logs (the report zips it).
        public string Folder { get { return _folder; } }

        /// This session's copy.
        public string CurrentFile { get { return _target; } }

        public LogKeeper(string bepinexRoot, string configDir, int keepPrevious, ManualLogSource log)
        {
            _log = log;
            _source = Path.Combine(bepinexRoot, "LogOutput.log");
            _folder = Path.Combine(configDir, "logs");
            string name = LogArchive.SessionFileName(DateTime.Now);
            _target = Path.Combine(_folder, name);

            try
            {
                if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);

                string[] paths = Directory.GetFiles(_folder);
                List<string> names = new List<string>(paths.Length);
                for (int i = 0; i < paths.Length; i++) names.Add(Path.GetFileName(paths[i]));
                List<string> delete = LogArchive.ToDelete(names, name, keepPrevious);
                for (int i = 0; i < delete.Count; i++)
                {
                    try { File.Delete(Path.Combine(_folder, delete[i])); }
                    catch (Exception ex) { _log.LogWarning("Log copies: could not delete " + delete[i] + ": " + ex.Message); }
                }

                _log.LogInfo("Log copies: this session -> " + _target + " (keeping " + keepPrevious +
                             " previous, " + delete.Count + " old deleted)");
            }
            catch (Exception ex)
            {
                _failed = true;
                _log.LogWarning("Log copies off: " + ex.Message);
            }
        }

        /// Called every frame with Time.realtimeSinceStartup.
        public void Tick(float now)
        {
            if (_failed || now < _nextCopy) return;
            _nextCopy = now + CopyInterval;
            CopyNew();
        }

        /// Copies whatever is new now (quit, report).
        public void CopyNew()
        {
            if (_failed) return;
            try
            {
                if (!File.Exists(_source)) return;
                using (FileStream src = new FileStream(_source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long length = src.Length;
                    if (length == _copied) return;
                    if (length < _copied)
                    {
                        // Should not happen within a session; start over
                        // rather than leave a gap.
                        _copied = 0;
                        File.Delete(_target);
                    }

                    src.Seek(_copied, SeekOrigin.Begin);
                    using (FileStream dst = new FileStream(_target, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        int n;
                        while ((n = src.Read(_buffer, 0, _buffer.Length)) > 0)
                        {
                            dst.Write(_buffer, 0, n);
                            _copied += n;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Once: a failing copy every two seconds would flood the
                // very log it copies.
                _failed = true;
                _log.LogWarning("Log copies stopped: " + ex.Message);
            }
        }
    }
}
