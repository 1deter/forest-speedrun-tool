using System;
using System.Collections.Generic;
using System.IO;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // A LiveSplit splits file (.lss) as comparisons (Next up 9; the parser
    // and matcher are Data/LssFile, tested).
    //
    // The runner drops .lss files in config/ForestOverlay/livesplit/ and
    // links one to a segment in the Runs tab (the link, its timing method
    // and any hand-set row map live in livesplit/links.txt - the runner's
    // own; a segment file never names a LiveSplit file). Our split rows
    // are matched to LiveSplit's splits by name in route order (endgame
    // event names answer to the autosplitter's labels); a row can be set
    // by hand with < >. Then Compare to -> LiveSplit offers the file's PB,
    // its best segments, and every other comparison it holds ("WR" ...).
    //
    // Comparisons only (author, 2026-09-27): never the runner's own PB,
    // golds or sum of best. The ghost and lines race the runner's own PB
    // meanwhile - a LiveSplit file has times, not positions.
    //
    // The file is read once on pick (again if it changed on disk at the
    // next arm or Rescan), never per frame.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const string LssPbName = "Personal best";
        private const string LssGoldsName = "Best segments";

        private string _lssDir;
        private List<LssLink> _lssLinks;
        private string[] _lssFiles = new string[0];
        private bool _lssOpen;
        private bool _lssDirty = true;
        private Segment _lssSegment;
        private int _lssRows = -1;

        private LssRun _lssRun;
        private string _lssRunPath;
        private DateTime _lssRunStamp;
        private string _lssError;
        private LssLink _lssLink;
        private LssComparison _lssComp;
        private string[] _lssSplitNames = new string[0];
        private readonly List<string> _lssCompareNames = new List<string>();
        private readonly List<float[]> _lssCompareSplits = new List<float[]>();
        private int _lssPick;
        // A segment just made from a LiveSplit file (Practice -> Import):
        // its comparison is picked once the file is read.
        private string _pickLssFor;

        private readonly GUIContent _lssPickText = new GUIContent("");
        private readonly GUIContent _lssDirText = new GUIContent("");
        private readonly GUIContent _lssFileText = new GUIContent("");
        private readonly GUIContent _lssStatus = new GUIContent("");
        private readonly List<GUIContent> _lssRowText = new List<GUIContent>();

        private bool LssAvailable { get { return _lssComp != null && _lssCompareSplits.Count > 0; } }

        private string LssDir
        {
            get
            {
                if (_lssDir == null) _lssDir = Path.Combine(Ctx.ConfigDirectory, "livesplit");
                return _lssDir;
            }
        }

        private string LinksPath { get { return Path.Combine(LssDir, "links.txt"); } }

        // --- links -----------------------------------------------------------

        private void LoadLinks()
        {
            if (_lssLinks != null) return;
            _lssLinks = new List<LssLink>();
            try
            {
                if (File.Exists(LinksPath)) _lssLinks = LssLink.ParseAll(File.ReadAllText(LinksPath));
            }
            catch (Exception ex) { Ctx.Log.LogWarning("LiveSplit links: could not read - " + ex.Message); }
        }

        private void SaveLinks()
        {
            try
            {
                Directory.CreateDirectory(LssDir);
                File.WriteAllText(LinksPath, LssLink.FormatAll(_lssLinks));
            }
            catch (Exception ex) { Ctx.Log.LogWarning("LiveSplit links: could not write - " + ex.Message); }
        }

        private LssLink LinkFor(string segmentId)
        {
            LoadLinks();
            for (int i = 0; i < _lssLinks.Count; i++)
                if (_lssLinks[i].SegmentId == segmentId) return _lssLinks[i];
            return null;
        }

        private void ScanLssFiles()
        {
            List<string> found = new List<string>();
            try
            {
                Directory.CreateDirectory(LssDir);
                string[] files = Directory.GetFiles(LssDir, "*.lss");
                for (int i = 0; i < files.Length; i++) found.Add(Path.GetFileName(files[i]));
                found.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Ctx.Log.LogWarning("LiveSplit: could not list " + LssDir + " - " + ex.Message); }
            _lssFiles = found.ToArray();
            _lssDirText.text = (_lssFiles.Length == 0 ? "No LiveSplit files yet. " : _lssFiles.Length + " LiveSplit file(s). ") +
                               "Put .lss files (LiveSplit -> Save Splits As) in " + LssDir;
            _lssDirty = true;
        }

        // --- picking ---------------------------------------------------------

        private void StepLssFile(int dir)
        {
            if (_segment == null) return;
            ScanLssFiles();
            LssLink link = LinkFor(_segment.Id);
            int at = -1;   // -1 = none
            if (link != null)
                for (int i = 0; i < _lssFiles.Length; i++)
                    if (string.Equals(_lssFiles[i], link.File, StringComparison.OrdinalIgnoreCase)) at = i;

            int count = _lssFiles.Length + 1;   // + none
            int next = ((at + 1 + dir) % count + count) % count - 1;

            if (next < 0)
            {
                if (link != null) _lssLinks.Remove(link);
            }
            else
            {
                if (link == null) { link = new LssLink(); link.SegmentId = _segment.Id; _lssLinks.Add(link); }
                if (!string.Equals(link.File, _lssFiles[next], StringComparison.OrdinalIgnoreCase))
                {
                    link.File = _lssFiles[next];
                    link.Map = null;
                    LssRun run = ReadLss(Path.Combine(LssDir, link.File));
                    link.Timing = run != null ? run.PreferredTiming() : LssTiming.RealTime;
                }
            }
            SaveLinks();
            _lssDirty = true;
        }

        /// Practice's LiveSplit import: links the file to the new segment (by
        /// name - the rows were named from it) and picks its PB as the
        /// comparison when the segment arms.
        public void LinkImportedLss(string segmentId, string file, LssTiming timing)
        {
            LssLink link = LinkFor(segmentId);
            if (link == null) { link = new LssLink(); link.SegmentId = segmentId; _lssLinks.Add(link); }
            link.File = file;
            link.Map = null;
            link.Timing = timing;
            SaveLinks();
            _lssDirty = true;
            _pickLssFor = segmentId;
        }

        private void SetLssTiming(LssTiming t)
        {
            if (_lssLink == null || _lssLink.Timing == t) return;
            _lssLink.Timing = t;
            SaveLinks();
            _lssDirty = true;
        }

        /// A row's LiveSplit split, by hand: -1 (none) .. last split.
        private void StepLssRow(int row, int dir)
        {
            if (_lssLink == null || _lssComp == null || _lssSplitNames.Length == 0) return;
            int[] map = (int[])_lssComp.Match.Map.Clone();
            if (row < 0 || row >= map.Length) return;
            int count = _lssSplitNames.Length + 1;
            map[row] = ((map[row] + 1 + dir) % count + count) % count - 1;
            _lssLink.Map = map;
            SaveLinks();
            _lssDirty = true;
        }

        private void ResetLssMap()
        {
            if (_lssLink == null || _lssLink.Map == null) return;
            _lssLink.Map = null;
            SaveLinks();
            _lssDirty = true;
        }

        private void PickLss(int index)
        {
            if (_lssCompareSplits.Count == 0) return;
            _lssPick = (index % _lssCompareSplits.Count + _lssCompareSplits.Count) % _lssCompareSplits.Count;
            _referenceKind = Reference.LiveSplit;
            RefreshLssPickText();
            SelectReference();
            _splitsDirty = true;
        }

        private float[] LssSplits()
        {
            return _lssPick >= 0 && _lssPick < _lssCompareSplits.Count ? _lssCompareSplits[_lssPick] : null;
        }

        // Asked every splits refresh; the joined text is kept per name.
        private string _lssNameOf, _lssNameText;

        private string LssComparisonName()
        {
            if (_lssPick < 0 || _lssPick >= _lssCompareNames.Count) return "LiveSplit";
            string n = _lssCompareNames[_lssPick];
            if (!ReferenceEquals(n, _lssNameOf) || _lssNameText == null) { _lssNameOf = n; _lssNameText = "LiveSplit " + n; }
            return _lssNameText;
        }

        private void RefreshLssPickText()
        {
            if (!LssAvailable) { _lssPickText.text = ""; return; }
            int i = Mathf.Clamp(_lssPick, 0, _lssCompareNames.Count - 1);
            float[] s = _lssCompareSplits[i];
            float total = s.Length > 0 ? s[s.Length - 1] : float.NaN;
            _lssPickText.text = (i + 1) + "/" + _lssCompareNames.Count + "  " + _lssCompareNames[i] +
                                (float.IsNaN(total) ? "" : "  " + Format(total));
        }

        // --- rebuilding (Tick, on change only) --------------------------------

        private LssRun ReadLss(string path)
        {
            string error;
            try
            {
                LssRun run = LssFile.Parse(File.ReadAllText(path), out error);
                if (run == null) _lssError = Path.GetFileName(path) + ": " + error;
                return run;
            }
            catch (Exception ex)
            {
                _lssError = Path.GetFileName(path) + ": " + ex.Message;
                return null;
            }
        }

        /// Called from Tick: rebuilds when the segment, its rows or the link
        /// changed. Allocates only then.
        private void RefreshLss()
        {
            if (!_lssDirty && ReferenceEquals(_segment, _lssSegment) && SplitRows == _lssRows) return;
            _lssDirty = false;
            _lssSegment = _segment;
            _lssRows = SplitRows;
            _lssComp = null;
            _lssError = null;
            _lssCompareNames.Clear();
            _lssCompareSplits.Clear();
            _lssLink = _segment != null ? LinkFor(_segment.Id) : null;

            if (_lssLink == null)
            {
                _lssRun = null;
                _lssRunPath = null;
                _lssFileText.text = "none";
                _lssStatus.text = _segment == null ? "" : "No LiveSplit file linked to this segment.";
                FinishLssRefresh();
                return;
            }

            _lssFileText.text = _lssLink.File;
            string path = Path.Combine(LssDir, _lssLink.File);
            DateTime stamp = DateTime.MinValue;
            try { if (File.Exists(path)) stamp = File.GetLastWriteTime(path); } catch (Exception) { }

            if (stamp == DateTime.MinValue)
            {
                _lssRun = null;
                _lssStatus.text = "'" + _lssLink.File + "' is not in " + LssDir + " any more.";
                FinishLssRefresh();
                return;
            }
            if (_lssRun == null || path != _lssRunPath || stamp != _lssRunStamp)
            {
                _lssRun = ReadLss(path);
                _lssRunPath = path;
                _lssRunStamp = stamp;
                if (_lssRun != null)
                    Ctx.Log.LogInfo("LiveSplit: read '" + _lssLink.File + "' (" + _lssRun.GameName + " " + _lssRun.CategoryName +
                                    ", " + _lssRun.Segments.Count + " splits, " + _lssRun.Attempts.Count + " attempts).");
            }
            if (_lssRun == null)
            {
                _lssStatus.text = "Could not read " + (_lssError ?? _lssLink.File) + ".";
                FinishLssRefresh();
                return;
            }

            int rows = SplitRows;
            string[] rowNames = new string[rows];
            for (int r = 0; r < rows; r++) rowNames[r] = _segment.SplitName(r);
            _lssSplitNames = _lssRun.SegmentNames();

            LssMatch match = _lssLink.Map != null
                ? LssMatch.FromMap(_lssLink.Map, rowNames, _lssSplitNames)
                : LssMatch.Match(rowNames, _lssSplitNames, EndsAtGameEnd(_segment));
            _lssComp = LssComparison.Build(_lssRun, match, _lssLink.Timing);

            _lssCompareNames.Add(LssPbName);
            _lssCompareSplits.Add(_lssComp.PbSplits);
            _lssCompareNames.Add(LssGoldsName);
            _lssCompareSplits.Add(_lssComp.GoldSplits);
            for (int i = 0; i < _lssRun.Comparisons.Count; i++)
            {
                string c = _lssRun.Comparisons[i];
                if (c == LssRun.PersonalBest) continue;
                _lssCompareNames.Add(c);
                _lssCompareSplits.Add(LssComparison.Other(_lssRun, match, c, _lssLink.Timing));
            }

            _lssStatus.text = LssStatusText(match, rows);

            while (_lssRowText.Count < rows) _lssRowText.Add(new GUIContent(""));
            for (int r = 0; r < rows; r++)
            {
                int j = match.Map[r];
                _lssRowText[r].text = rowNames[r] + "  ->  " + (j >= 0 ? _lssSplitNames[j] : "(none)");
            }

            Ctx.Log.LogInfo("LiveSplit: '" + _lssLink.File + "' on '" + _segment.Id + "': " +
                            (rows - match.UnmatchedRows.Count) + "/" + rows + " rows matched" +
                            (_lssLink.Map != null ? " (by hand)" : "") + ", " +
                            (_lssLink.Timing == LssTiming.GameTime ? "game" : "real") + " time, PB " +
                            (float.IsNaN(_lssComp.Pb) ? "-" : Format(_lssComp.Pb)) + ".");
            FinishLssRefresh();
        }

        private void FinishLssRefresh()
        {
            if (_lssPick >= _lssCompareSplits.Count) _lssPick = 0;
            if (_referenceKind == Reference.LiveSplit && !LssAvailable) { _referenceKind = Reference.Best; SelectReference(); }
            RefreshLssPickText();
            _splitsDirty = true;
            if (_pickLssFor != null && _segment != null && _segment.Id == _pickLssFor && LssAvailable)
            {
                _pickLssFor = null;
                PickLss(0);
            }
        }

        private string LssStatusText(LssMatch match, int rows)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            string title = (_lssRun.GameName + " " + _lssRun.CategoryName).Trim();
            if (title.Length > 0) sb.Append(title).Append(": ");
            sb.Append(_lssSplitNames.Length).Append(" splits, ")
              .Append(_lssRun.Attempts.Count).Append(" attempts. ");
            sb.Append(rows - match.UnmatchedRows.Count).Append(" of ").Append(rows).Append(" rows matched")
              .Append(_lssLink.Map != null ? " (set by hand)" : " by name").Append('.');

            if (match.UnmatchedRows.Count > 0)
                sb.Append(" Not matched: ").Append(string.Join(", ", match.UnmatchedRows.ToArray()))
                  .Append(" - rename the split in the segment editor, or pick its LiveSplit split below.");

            if (match.StartAfter >= 0 && match.StartAfter < _lssSplitNames.Length)
                sb.Append(" This segment's clock starts after LiveSplit's '").Append(_lssSplitNames[match.StartAfter]).Append("'.");

            // LiveSplit splits inside the segment that no row takes: their
            // time rolls into the next row's.
            int last = -1;
            for (int i = 0; i < match.Map.Length; i++) if (match.Map[i] > last) last = match.Map[i];
            List<string> inside = new List<string>();
            for (int i = 0; i < match.UnmatchedSplits.Count; i++)
            {
                int j = Array.IndexOf(_lssSplitNames, match.UnmatchedSplits[i]);
                if (j >= 0 && j <= last) inside.Add(match.UnmatchedSplits[i]);
            }
            if (inside.Count > 0)
                sb.Append(" LiveSplit splits inside it with no row (their time rolls into the next row): ")
                  .Append(string.Join(", ", inside.ToArray())).Append('.');

            if (!match.InOrder) sb.Append(" The rows are not in LiveSplit's order - segment times and golds will be off.");
            if (_lssRun.CountTimes(_lssLink.Timing) == 0)
                sb.Append(" The PB has no ").Append(_lssLink.Timing == LssTiming.GameTime ? "game" : "real").Append(" times - try the other timing.");
            return sb.ToString();
        }

        /// An end on the game's ending takes LiveSplit's last split when no
        /// name matches it (a full run's last split is often named freely).
        private static bool EndsAtGameEnd(Segment s)
        {
            if (s == null || s.End.Kind != TriggerKind.Event || s.End.EventName == null) return false;
            string e = s.End.EventName.ToLowerInvariant();
            return e == "game-end" || e == "end-crash" || e == "end-shutdown";
        }

        // --- the Runs tab ----------------------------------------------------

        /// Under "another runner"; returns the new y.
        private float DrawLiveSplitSection(float y, float w)
        {
            if (_segment == null) return y;

            if (LssAvailable)
            {
                bool on = UiKit.Toggle(new Rect(0, y, 130, 20), _referenceKind == Reference.LiveSplit, " LiveSplit");
                if (on && _referenceKind != Reference.LiveSplit) PickLss(_lssPick);
                if (GUI.Button(new Rect(134, y, 24, 20), "<")) PickLss(_lssPick - 1);
                if (GUI.Button(new Rect(162, y, 24, 20), ">")) PickLss(_lssPick + 1);
                GUI.Label(new Rect(192, y, Mathf.Max(40f, w - 312f), 20), _lssPickText);
            }
            if (GUI.Button(new Rect(w - 110, y, 110, 20), _lssOpen ? "LiveSplit file  ^" : "LiveSplit file  v"))
            {
                _lssOpen = !_lssOpen;
                if (_lssOpen) ScanLssFiles();
            }
            y += 24f;
            if (!_lssOpen) return y;

            float x = 10f, cw = w - 10f;
            if (GUI.Button(new Rect(x, y, 84, 20), "Rescan")) ScanLssFiles();
            if (GUI.Button(new Rect(x + 88, y, 92, 20), "Open folder"))
            {
                try { Directory.CreateDirectory(LssDir); Application.OpenURL("file:///" + LssDir.Replace('\\', '/')); }
                catch (Exception) { }
            }
            y += 24f;
            y += UiText.DrawDim(x, y, cw, _lssDirText) + 4f;

            GUI.Label(new Rect(x, y, 40, 20), "File");
            if (GUI.Button(new Rect(x + 44, y, 24, 20), "<")) StepLssFile(-1);
            if (GUI.Button(new Rect(x + 72, y, 24, 20), ">")) StepLssFile(1);
            GUI.Label(new Rect(x + 102, y, cw - 102, 20), _lssFileText);
            y += 24f;

            if (_lssRun != null && _lssLink != null)
            {
                GUI.Label(new Rect(x, y, 60, 20), "Timing");
                if (UiKit.Toggle(new Rect(x + 64, y, 90, 20), _lssLink.Timing == LssTiming.RealTime, " real time")) SetLssTiming(LssTiming.RealTime);
                if (UiKit.Toggle(new Rect(x + 158, y, 90, 20), _lssLink.Timing == LssTiming.GameTime, " game time")) SetLssTiming(LssTiming.GameTime);
                if (_lssLink.Map != null && GUI.Button(new Rect(w - 130, y, 130, 20), "Match by name")) ResetLssMap();
                y += 24f;
            }

            y += UiText.Draw(x, y, cw, _lssStatus) + 2f;

            if (_lssComp != null)
            {
                int rows = Mathf.Min(_lssComp.Match.Map.Length, _lssRowText.Count);
                for (int r = 0; r < rows; r++)
                {
                    if (GUI.Button(new Rect(x, y, 24, 20), "<")) StepLssRow(r, -1);
                    if (GUI.Button(new Rect(x + 28, y, 24, 20), ">")) StepLssRow(r, 1);
                    y += Mathf.Max(22f, UiText.Draw(x + 58, y, cw - 58, _lssRowText[r]));
                }
            }
            return y + 4f;
        }
    }
}
