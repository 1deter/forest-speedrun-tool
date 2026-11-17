using System;
using System.IO;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // An attempt's log on disk while the attempt runs (T-0459, REVIEW.md
    // Top 10 item 4): the chain's new lines are appended to an open file,
    // not the whole log rewritten every 30 s. The chain only ever grows
    // (AttemptChain.Add), so what is on disk is always a prefix of it.
    //
    // A write that fails closes the file and starts it again from the first
    // line on the next Sync, so a half-written append is never built on.
    // A crash can still cut the last line: CompleteLines drops it before
    // the log is read (AttemptChain.Read is strict).
    //
    // The run report is not in this file: it is not part of the chain and
    // is rewritten whole, so it lives in a file of its own (the caller's).
    // Tested against a real temp folder, like Data/SafeFile.
    // ------------------------------------------------------------------
    public sealed class AttemptLogFile
    {
        private readonly string _path;
        private StreamWriter _writer;

        public AttemptLogFile(string path)
        {
            _path = path;
        }

        public string Path { get { return _path; } }

        /// Characters of the chain's text on disk.
        public int Written { get; private set; }

        public bool IsOpen { get { return _writer != null; } }

        /// Appends what the chain gained since the last Sync and flushes it.
        /// Throws on an IO error, with the file closed and Written back to 0.
        public void Sync(AttemptChain chain)
        {
            if (chain == null) return;
            if (chain.Length < Written) Reset();   // not the chain this file holds: start again
            if (chain.Length == Written && _writer != null) return;
            try
            {
                if (_writer == null)
                {
                    string dir = System.IO.Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    // Create: a file left from before is replaced. Others may read it meanwhile.
                    FileStream fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
                    _writer = new StreamWriter(fs, new UTF8Encoding(false));
                    Written = 0;
                }
                if (chain.Length > Written)
                {
                    _writer.Write(chain.TextFrom(Written));
                    Written = chain.Length;
                }
                _writer.Flush();
            }
            catch
            {
                Reset();
                throw;
            }
        }

        /// Closes the file; the next Sync starts it again from the first line.
        public void Close()
        {
            Reset();
        }

        private void Reset()
        {
            StreamWriter w = _writer;
            _writer = null;
            Written = 0;
            if (w == null) return;
            try { w.Close(); }
            catch (Exception) { }
        }

        /// The text up to its last whole line: a crash mid-append leaves a
        /// cut line the strict reader would refuse. A text that already
        /// holds a report (an open log from before T-0459, chain and report
        /// in one) is returned as it is.
        public static string CompleteLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf("\n" + AttemptChain.ReportMarker + "\n", StringComparison.Ordinal) >= 0) return text;
            int last = text.LastIndexOf('\n');
            return last < 0 ? "" : text.Substring(0, last + 1);
        }

        /// A recovered log as the outbox sends it: its whole lines, then the
        /// report after the marker - the same shape as a finished log's.
        /// "" when not even the format line was written (nothing to send).
        public static string Recovered(string log, string report)
        {
            string lines = CompleteLines(log);
            if (lines.Length == 0) return "";
            if (lines.IndexOf("\n" + AttemptChain.ReportMarker + "\n", StringComparison.Ordinal) >= 0) return lines;
            return lines + AttemptChain.ReportMarker + "\n" + (report ?? "");
        }
    }
}
