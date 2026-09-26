using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The QA tab (v0.24.56): the test list the QA team works through,
    // answered in game, and everything a tester sends in one zip.
    //
    //   * Lists ship in the DLL (qa/*.txt, Data/QaList), numbered as sent,
    //     so answers read the same in chat and in the report.
    //   * Pass / Fail / Skip and a note per item, saved at once to
    //     config/ForestOverlay/qa/answers/<list id>.txt.
    //   * A log listener shows under an item the last log line that proves
    //     it ran (`seen` in the list). Evidence only; never a pass.
    //   * Mark (button, or the unbound "qa.mark" key): one MARK line in the
    //     log with time, position, cave and current spot, for "something
    //     weird just happened". A note can follow.
    //   * Write report: a zip on the desktop - report.txt, the answers,
    //     the kept logs (Core/LogKeeper), the Unity log, the config, the
    //     segment files, the savestates and any nature guide dump.
    //
    // Runners never get the bridge (arbitrary calls); this only reads.
    // Info-only: nothing here writes game state.
    // ------------------------------------------------------------------
    public sealed class QaModule : OverlayModule
    {
        public override string Id { get { return "qa"; } }
        public override string DisplayName { get { return "QA"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "QA"; } }
        public override int TabOrder { get { return 65; } }

        private const int MaxQueued = 2000;
        private const int MaxSeenLength = 300;
        private const long MaxReportFile = 50L * 1024 * 1024;

        private string _qaDir, _answersDir;
        private ConfigEntry<string> _testerName;
        private string _testerField = "";

        private readonly List<QaList> _lists = new List<QaList>();
        private readonly List<string> _listFiles = new List<string>();
        private int _listIndex;
        private QaList _list;
        private Dictionary<int, QaAnswer> _answers = new Dictionary<int, QaAnswer>();
        private bool _dirty;
        private float _saveAt;

        // Built when the list or an answer changes, never in DrawTab.
        private readonly GUIContent _titleText = new GUIContent("");
        private readonly GUIContent _introText = new GUIContent("");
        private readonly GUIContent _problemText = new GUIContent("");
        private GUIContent[] _itemText = new GUIContent[0];
        private GUIContent[] _seenText = new GUIContent[0];
        private GUIContent[] _sectionText = new GUIContent[0];
        private string[] _noteField = new string[0];
        private float[] _itemHeight = new float[0];
        private float _layoutWidth = -1f;
        private float _contentHeight;
        private Vector2 _scroll;

        private static readonly GUIContent PassText = new GUIContent("Pass");
        private static readonly GUIContent FailText = new GUIContent("Fail");
        private static readonly GUIContent SkipText = new GUIContent("Skip");
        private static readonly GUIContent NoteLabel = new GUIContent("Note:");
        private static readonly GUIContent NoteHelp = new GUIContent(
            "Note (optional): Mark now puts it in the log with the mark and clears the box. " +
            "Write report includes whatever is still in the box - no Mark needed.");
        private static readonly Color PassColour = new Color(0.45f, 1f, 0.45f);
        private static readonly Color FailColour = new Color(1f, 0.45f, 0.45f);
        private static readonly Color SkipColour = new Color(1f, 0.9f, 0.4f);

        // Marks.
        private int _markCount;
        private int _lastMark;
        private bool _lastMarkHasNote = true;
        private string _markNote = "";
        private readonly List<string> _marks = new List<string>();
        private readonly GUIContent _markStatus = new GUIContent("");
        private readonly GUIContent _addNoteText = new GUIContent("");
        private readonly GUIContent _reportStatus = new GUIContent("");

        private Listener _listener;

        // --------------------------------------------------------------
        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _qaDir = Path.Combine(ctx.ConfigDirectory, "qa");
            _answersDir = Path.Combine(_qaDir, "answers");

            _testerName = ctx.Config.Bind("QA", "TesterName", "",
                "Your name, put on the QA report so answers can be told apart.");
            _testerField = _testerName.Value ?? "";

            LoadLists();

            _listener = new Listener();
            BepInEx.Logging.Logger.Listeners.Add(_listener);
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("qa.mark", KeyCode.None, "QA: mark 'something weird happened' in the log", delegate { Mark(false); });
            map.Add("tab.qa", KeyCode.None, "Open QA tab", OpenMyTab);
        }

        public override void Shutdown()
        {
            SaveAnswers();
            if (_listener != null) BepInEx.Logging.Logger.Listeners.Remove(_listener);
        }

        // --------------------------------------------------------------
        private void LoadLists()
        {
            _lists.Clear();
            _listFiles.Clear();
            try
            {
                if (Directory.Exists(_qaDir))
                {
                    string[] files = Directory.GetFiles(_qaDir, "*.txt");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < files.Length; i++)
                    {
                        QaList l = QaList.Parse(File.ReadAllText(files[i], Encoding.UTF8));
                        if (l.Id.Length == 0) l.Id = Path.GetFileNameWithoutExtension(files[i]);
                        _lists.Add(l);
                        _listFiles.Add(Path.GetFileName(files[i]));
                    }
                }
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("QA: could not read the test lists: " + ex.Message);
            }

            // Newest list (files are dated) first.
            _listIndex = _lists.Count - 1;
            SelectList(_listIndex);
            Ctx.Log.LogInfo("QA: " + _lists.Count + " test list(s)" +
                            (_list != null ? ", showing '" + _list.Id + "' (" + _list.Items.Count + " items)" : ""));
        }

        private void SelectList(int index)
        {
            SaveAnswers();
            _list = index >= 0 && index < _lists.Count ? _lists[index] : null;
            _answers = new Dictionary<int, QaAnswer>();
            _layoutWidth = -1f;
            _scroll = Vector2.zero;

            if (_list == null)
            {
                _titleText.text = "No test list. Lists ship with the plugin (config/ForestOverlay/qa); update to get the current one.";
                _introText.text = "";
                _problemText.text = "";
                _itemText = new GUIContent[0];
                _seenText = new GUIContent[0];
                _sectionText = new GUIContent[0];
                _noteField = new string[0];
                _itemHeight = new float[0];
                return;
            }

            try
            {
                string path = AnswersPath();
                if (File.Exists(path)) _answers = QaList.ParseAnswers(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("QA: could not read answers: " + ex.Message);
            }

            _titleText.text = (_list.Title.Length > 0 ? _list.Title : _list.Id) +
                              (_lists.Count > 1 ? "   (" + (_listIndex + 1) + " of " + _lists.Count + ")" : "");
            _introText.text = string.Join("\n", _list.Intro.ToArray());
            _problemText.text = _list.Problems.Count == 0 ? ""
                : "List problems (tell the author): " + string.Join("; ", _list.Problems.ToArray());

            int n = _list.Items.Count;
            _itemText = new GUIContent[n];
            _seenText = new GUIContent[n];
            _sectionText = new GUIContent[n];
            _noteField = new string[n];
            _itemHeight = new float[n];
            string section = null;
            for (int i = 0; i < n; i++)
            {
                QaItem item = _list.Items[i];
                _itemText[i] = new GUIContent(item.Number + ") " + item.Text);
                _sectionText[i] = item.Section != section && item.Section.Length > 0 ? new GUIContent(item.Section) : null;
                section = item.Section;
                QaAnswer a = Answer(item.Number, false);
                _noteField[i] = a != null ? a.Note : "";
                _seenText[i] = new GUIContent(SeenLabel(a));
            }
        }

        private static string SeenLabel(QaAnswer a)
        {
            return a != null && a.Seen.Length > 0 ? "Log: " + a.Seen : "";
        }

        private string AnswersPath()
        {
            return Path.Combine(_answersDir, SafeName(_list.Id) + ".txt");
        }

        private QaAnswer Answer(int number, bool create)
        {
            QaAnswer a;
            if (_answers.TryGetValue(number, out a)) return a;
            if (!create) return null;
            a = new QaAnswer();
            _answers[number] = a;
            return a;
        }

        private void Changed()
        {
            _dirty = true;
            _saveAt = Time.realtimeSinceStartup + 1f;
        }

        private void SaveAnswers()
        {
            if (!_dirty || _list == null) return;
            _dirty = false;
            try
            {
                if (!Directory.Exists(_answersDir)) Directory.CreateDirectory(_answersDir);
                File.WriteAllText(AnswersPath(), QaList.FormatAnswers(_list, _answers), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("QA: could not save answers: " + ex.Message);
            }
        }

        // --------------------------------------------------------------
        public override void Tick()
        {
            DrainLog();
            if (_dirty && Time.realtimeSinceStartup >= _saveAt) SaveAnswers();
        }

        private readonly List<string> _drained = new List<string>();

        private void DrainLog()
        {
            if (_listener == null) return;
            _drained.Clear();
            _listener.Drain(_drained);
            if (_list == null || _drained.Count == 0) return;

            for (int d = 0; d < _drained.Count; d++)
            {
                string line = _drained[d];
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    QaItem item = _list.Items[i];
                    if (item.Seen.Count == 0 || !QaList.Matches(item, line)) continue;

                    string text = line.Length > MaxSeenLength ? line.Substring(0, MaxSeenLength) + "..." : line;
                    QaAnswer a = Answer(item.Number, true);
                    a.Seen = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + text;
                    _seenText[i].text = SeenLabel(a);
                    _layoutWidth = -1f;
                    Changed();
                }
            }
        }

        // --------------------------------------------------------------
        private void Mark(bool withNote)
        {
            try
            {
                _markCount++;
                _lastMark = _markCount;
                string note = withNote ? (_markNote ?? "").Trim() : "";
                _lastMarkHasNote = note.Length > 0;

                StringBuilder sb = new StringBuilder();
                sb.Append("MARK #").Append(_markCount).Append(": ")
                  .Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

                if (Ctx.Player != null && Ctx.Player.Found)
                {
                    Vector3 p = Ctx.Player.Transform.position;
                    sb.Append(", at (").Append(p.x.ToString("F1", CultureInfo.InvariantCulture)).Append(", ")
                      .Append(p.y.ToString("F1", CultureInfo.InvariantCulture)).Append(", ")
                      .Append(p.z.ToString("F1", CultureInfo.InvariantCulture)).Append(')');
                    if (Ctx.Bridge != null) sb.Append(Ctx.Bridge.IsInCaves() ? ", in a cave" : ", outside");
                }
                else sb.Append(", no player (menu or loading)");

                PracticeModule practice = Host != null ? Host.Find<PracticeModule>() : null;
                Segment spot = practice != null ? practice.CurrentSegment : null;
                sb.Append(spot != null ? ", spot '" + spot.Id + "'" : ", no current spot");
                if (note.Length > 0) sb.Append(", note: ").Append(note);

                string line = sb.ToString();
                Ctx.Log.LogInfo(line);
                _marks.Add(line);

                _markStatus.text = "Marked #" + _markCount + " at " +
                                   DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                                   (_lastMarkHasNote ? " with your note." : ".");
                _addNoteText.text = "Add the note to mark #" + _markCount;
                if (withNote) _markNote = "";

                if (!TabShowing && Ctx.Notice != null)
                    Ctx.Notice.Show("Marked #" + _markCount + " in the log - add a note in the QA tab (F2) if it helps.", 4f);
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("QA: mark failed: " + ex.Message);
            }
        }

        private void AddNoteToLastMark()
        {
            string note = (_markNote ?? "").Trim();
            if (note.Length == 0 || _lastMark == 0) return;
            string line = "MARK #" + _lastMark + " note: " + note;
            Ctx.Log.LogInfo(line);
            _marks.Add(line);
            _lastMarkHasNote = true;
            _markNote = "";
            _markStatus.text = "Note added to mark #" + _lastMark + ".";
        }

        // --------------------------------------------------------------
        private void WriteReport()
        {
            try
            {
                SaveAnswers();
                if (Ctx.Logs != null) Ctx.Logs.CopyNew();

                string pending = (_markNote ?? "").Trim();
                if (pending.Length > 0) Ctx.Log.LogInfo("QA note (in the report): " + pending);

                string tester = SafeName(_testerField.Trim());
                DateTime now = DateTime.Now;
                string name = "ForestOverlay-report-" + (tester.Length > 0 ? tester + "-" : "") +
                              now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture) + ".zip";
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = Ctx.ConfigDirectory;
                string path = Path.Combine(folder, name);

                List<string> skipped = new List<string>();
                long bytes;
                int count;
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (ZipWriter zip = new ZipWriter(fs))
                {
                    zip.Add("report.txt", BuildReportText(now), now);
                    if (_list != null) zip.Add("qa-answers-" + SafeName(_list.Id) + ".txt", QaList.FormatAnswers(_list, _answers), now);

                    if (Ctx.Logs != null) AddFolder(zip, Ctx.Logs.Folder, "logs", "*.log", false, skipped);
                    AddFile(zip, Path.Combine(Application.dataPath, "output_log.txt"), "unity/output_log.txt", skipped);
                    AddFile(zip, Path.Combine(Paths.ConfigPath, OverlayPlugin.PluginGuid + ".cfg"), "config/" + OverlayPlugin.PluginGuid + ".cfg", skipped);
                    AddFolder(zip, Path.Combine(Ctx.ConfigDirectory, "segments"), "segments", "*.txt", false, skipped);
                    AddFolder(zip, Path.Combine(Ctx.ConfigDirectory, "savestates"), "savestates", "*.fosave", true, skipped);
                    AddFolder(zip, Path.Combine(Paths.GameRootPath, "ForestOverlayDumps"), "dumps", "natureguide_*.txt", false, skipped);

                    zip.Finish();
                    bytes = fs.Length;
                    count = zip.Count;
                }

                string size = (bytes / (1024f * 1024f)).ToString("F1", CultureInfo.InvariantCulture) + " MB";
                Ctx.Log.LogInfo("QA report: " + path + " (" + count + " files, " + size +
                                (skipped.Count > 0 ? "; skipped " + string.Join(", ", skipped.ToArray()) : "") + ")");
                _reportStatus.text = "Written to your " + (folder == Ctx.ConfigDirectory ? "config folder" : "desktop") + ": " +
                                     name + " (" + count + " files, " + size + ")" +
                                     (pending.Length > 0 ? ", with your note" : "") + ". Send that file." +
                                     (skipped.Count > 0 ? " Skipped: " + string.Join(", ", skipped.ToArray()) : "");
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("QA report failed: " + ex);
                _reportStatus.text = "Could not write the report: " + ex.Message;
            }
        }

        private string BuildReportText(DateTime now)
        {
            StringBuilder sb = new StringBuilder();
            string when = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (_list != null)
                sb.Append(QaList.FormatReport(_list, _answers, _testerField.Trim(), OverlayPlugin.PluginVersion, when));
            else
                sb.Append("No test list.\nTester: ").Append(_testerField.Trim()).Append("\nPlugin: v")
                  .Append(OverlayPlugin.PluginVersion).Append("\nWritten: ").Append(when).Append('\n');

            string note = (_markNote ?? "").Trim();
            if (note.Length > 0) sb.Append("\nNote: ").Append(note).Append('\n');

            sb.Append("\nMarks this session: ").Append(_marks.Count).Append('\n');
            for (int i = 0; i < _marks.Count; i++) sb.Append("  ").Append(_marks[i]).Append('\n');
            sb.Append("(Marks from earlier sessions are in their logs as MARK lines.)\n");
            return sb.ToString();
        }

        private static void AddFolder(ZipWriter zip, string dir, string zipDir, string pattern, bool recursive, List<string> skipped)
        {
            if (!Directory.Exists(dir)) return;
            string[] files = Directory.GetFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            string root = dir.TrimEnd('\\', '/');
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(root.Length).TrimStart('\\', '/');
                AddFile(zip, files[i], zipDir + "/" + relative.Replace('\\', '/'), skipped);
            }
        }

        private static void AddFile(ZipWriter zip, string path, string zipName, List<string> skipped)
        {
            try
            {
                if (!File.Exists(path)) return;
                FileInfo info = new FileInfo(path);
                if (info.Length > MaxReportFile) { skipped.Add(zipName + " (too big)"); return; }

                byte[] data;
                // Open logs are shared for writing by whoever holds them.
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    data = new byte[fs.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = fs.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < data.Length) Array.Resize(ref data, read);
                }
                zip.Add(zipName, data, info.LastWriteTime);
            }
            catch (Exception ex)
            {
                skipped.Add(zipName + " (" + ex.Message + ")");
            }
        }

        private static string SafeName(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            }
            return sb.ToString();
        }

        // --------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            float w = area.width;
            float x = 12f, inner = w - 24f;
            float y = 28f;

            GUI.Label(new Rect(x, y, 90, 22), "Your name:");
            string name = GUI.TextField(new Rect(x + 90, y, 200, 22), _testerField);
            if (name != _testerField)
            {
                _testerField = name;
                _testerName.Value = name;
            }
            y += 28f;

            if (_lists.Count > 1)
            {
                if (GUI.Button(new Rect(x, y, 26, 22), "<")) { _listIndex = (_listIndex + _lists.Count - 1) % _lists.Count; SelectList(_listIndex); }
                if (GUI.Button(new Rect(x + 30, y, 26, 22), ">")) { _listIndex = (_listIndex + 1) % _lists.Count; SelectList(_listIndex); }
                y += UiText.Draw(x + 62, y, inner - 62, _titleText);
            }
            else y += UiText.Draw(x, y, inner, _titleText);
            y += UiText.DrawDim(x, y, inner, _introText);
            y += UiText.Draw(x, y, inner, _problemText) + 4f;

            // Mark and report, then the items.
            if (GUI.Button(new Rect(x, y, 110, 24), "Mark now")) Mark(true);
            if (GUI.Button(new Rect(x + 118, y, 170, 24), "Write report")) WriteReport();
            y += 28f;
            y += UiText.Draw(x, y, inner, NoteHelp);
            float boxH = UiText.BoxHeight(_markNote, inner);
            _markNote = UiText.TextBox(x, y, inner, boxH, _markNote);
            y += boxH + 4f;
            if (!_lastMarkHasNote && _lastMark > 0 && _markNote.Length > 0)
            {
                if (GUI.Button(new Rect(x, y, 220, 22), _addNoteText)) AddNoteToLastMark();
                y += 26f;
            }
            y += UiText.Draw(x, y, inner, _markStatus);
            y += UiText.Draw(x, y, inner, _reportStatus) + 4f;

            if (_list == null) return;
            DrawItems(new Rect(x, y, inner, Mathf.Max(60f, area.height - y - 8f)));
        }

        private void DrawItems(Rect view)
        {
            float innerW = view.width - 20f;
            if (!Mathf.Approximately(innerW, _layoutWidth)) Layout(innerW);

            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, innerW, Mathf.Max(_contentHeight, 20f)));
            float top = _scroll.y, bottom = _scroll.y + view.height;
            float y = 0f;
            for (int i = 0; i < _itemText.Length; i++)
            {
                float h = _itemHeight[i];
                if (y + h >= top && y <= bottom) DrawItem(i, y, innerW);
                y += h;
            }
            GUI.EndScrollView();
        }

        // Heights change only with the width or a new seen line; measured
        // once then, so a pass over the list costs no CalcHeight.
        private void Layout(float width)
        {
            _layoutWidth = width;
            float total = 0f;
            for (int i = 0; i < _itemText.Length; i++)
            {
                float h = 0f;
                if (_sectionText[i] != null) h += 8f + Mathf.Max(20f, UiText.Plain.CalcHeight(_sectionText[i], width)) + 2f;
                h += Mathf.Max(20f, UiText.Plain.CalcHeight(_itemText[i], width)) + 2f;
                h += Mathf.Max(22f, UiText.BoxHeight(_noteField[i], NoteWidth(width))) + 4f; // buttons + note
                if (_seenText[i].text.Length > 0) h += Mathf.Max(20f, UiText.Dim.CalcHeight(_seenText[i], width - 12f)) + 2f;
                h += 8f;
                _itemHeight[i] = h;
                total += h;
            }
            _contentHeight = total;
        }

        private void DrawItem(int i, float y, float width)
        {
            QaItem item = _list.Items[i];
            if (_sectionText[i] != null)
            {
                y += 8f;
                y += UiText.Draw(0, y, width, _sectionText[i]);
            }
            y += UiText.Draw(0, y, width, _itemText[i]);

            QaAnswer a = Answer(item.Number, false);
            QaResult r = a != null ? a.Result : QaResult.None;
            if (ResultButton(new Rect(12, y, 56, 22), PassText, r == QaResult.Pass, PassColour)) SetResult(item.Number, r == QaResult.Pass ? QaResult.None : QaResult.Pass);
            if (ResultButton(new Rect(72, y, 56, 22), FailText, r == QaResult.Fail, FailColour)) SetResult(item.Number, r == QaResult.Fail ? QaResult.None : QaResult.Fail);
            if (ResultButton(new Rect(132, y, 56, 22), SkipText, r == QaResult.Skip, SkipColour)) SetResult(item.Number, r == QaResult.Skip ? QaResult.None : QaResult.Skip);
            GUI.Label(new Rect(196, y + 1, 40, 22), NoteLabel);
            float noteW = NoteWidth(width);
            float noteH = UiText.BoxHeight(_noteField[i], noteW);
            string note = UiText.TextBox(236, y, noteW, noteH, _noteField[i]);
            if (note != _noteField[i])
            {
                _noteField[i] = note;
                Answer(item.Number, true).Note = note;
                Changed();
                if (!Mathf.Approximately(UiText.BoxHeight(note, noteW), noteH)) _layoutWidth = -1f;   // grew or shrank a line
            }
            y += Mathf.Max(22f, noteH) + 4f;

            if (_seenText[i].text.Length > 0) UiText.DrawDim(12, y, width - 12f, _seenText[i]);
        }

        private static float NoteWidth(float width)
        {
            return Mathf.Max(80f, width - 240f);
        }

        private static bool ResultButton(Rect r, GUIContent text, bool selected, Color colour)
        {
            Color old = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = colour;
            bool clicked = GUI.Button(r, text);
            GUI.backgroundColor = old;
            return clicked;
        }

        private void SetResult(int number, QaResult r)
        {
            Answer(number, true).Result = r;
            Changed();
        }

        // --------------------------------------------------------------
        // Log lines arrive on whatever thread logged them; only queued
        // here, matched in Tick on the main thread.
        private sealed class Listener : ILogListener
        {
            private readonly object _lock = new object();
            private readonly Queue<string> _queue = new Queue<string>();

            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                if (eventArgs == null || eventArgs.Data == null) return;
                string text = eventArgs.Data.ToString();
                lock (_lock)
                {
                    if (_queue.Count >= MaxQueued) _queue.Dequeue();
                    _queue.Enqueue(text);
                }
            }

            public void Drain(List<string> into)
            {
                lock (_lock)
                {
                    while (_queue.Count > 0) into.Add(_queue.Dequeue());
                }
            }

            public void Dispose() { }
        }
    }
}
