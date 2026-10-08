using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    public struct BookEntry
    {
        public string Name;
        public bool Done;
        public int UnlockLevel;   // bestiary entries reveal progressively
    }

    // ------------------------------------------------------------------
    // Reads the survival book's TODO LIST. INFO-ONLY.
    //
    // The bestiary was read here originally and has been removed: it
    // is not part of the 100% requirement, and the game carries two
    // SurvivalBookBestiary components, so it also rendered as a pair of
    // identical enemy lists. The actual requirement is the todo list
    // plus a unique-item collection, and that list is admin-decided
    // data rather than something to hardcode - see Data/CollectionList.
    //
    // Confirmed from IL:
    //   TheForest.Player.SerializableSurvivalBookTodo - one TodoTask field
    //   per objective, each inheriting ACondition, which carries ._done.
    // ------------------------------------------------------------------
    public sealed class SurvivalBookReader
    {
        private readonly ManualLogSource _log;

        private readonly List<BookEntry> _todo = new List<BookEntry>();

        // Found once and kept. Resources.FindObjectsOfTypeAll walks every
        // loaded object; running it every second was a visible 1 Hz stutter.
        // A save load destroys the component (fake-null), which is what
        // triggers the next search - and searches are rate-limited, since
        // at the main menu there is nothing to find.
        private const float SearchInterval = 5f;
        private Component _host;
        private float _nextSearch;
        private readonly List<FieldInfo> _taskFields = new List<FieldInfo>();
        private readonly List<FieldInfo> _doneFields = new List<FieldInfo>();
        // _done of each task, once a second: no boxing (FastField).
        private readonly List<Func<object, bool>> _doneGets = new List<Func<object, bool>>();
        private int _statusDone = -1, _statusCount = -1;
        private readonly List<string> _taskNames = new List<string>();

        public IList<BookEntry> Todo { get { return _todo; } }
        public string Status { get; private set; }

        public int TodoDone { get; private set; }

        public SurvivalBookReader(ManualLogSource log)
        {
            _log = log;
            Status = "not read yet";
        }

        /// Re-reads everything. Cheap enough to call on a throttle, and
        /// re-reading is what keeps it correct across a save load.
        public void Refresh()
        {
            _todo.Clear();
            TodoDone = 0;

            ReadTodo();

            // Rebuilt when a count moves, not every second.
            if (TodoDone == _statusDone && _todo.Count == _statusCount) return;
            _statusDone = TodoDone;
            _statusCount = _todo.Count;
            Status = _todo.Count > 0
                ? TodoDone + "/" + _todo.Count + " tasks done"
                : "survival book not loaded (open a save first)";
        }

        // ------------------------------------------------------------------
        private void ReadTodo()
        {
            if (_host == null)
            {
                if (Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + SearchInterval;
                // No save loaded, no book: the full scan below was a 12-24 ms
                // hitch every 5 s at the title (maks's v0.24.129 log), as the
                // nature guide's was before v0.24.11.
                if (GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Inventory") as UnityEngine.Object == null) return;

                // The serialisable version is the live one in current builds;
                // the older type is checked too rather than assuming.
                if (!Bind("TheForest.Player.SerializableSurvivalBookTodo"))
                    Bind("TheForest.Player.SurvivalBookTodo");
                if (_host == null) return;
            }

            for (int i = 0; i < _taskFields.Count; i++)
            {
                object task;
                try { task = _taskFields[i].GetValue(_host); }
                catch (Exception) { continue; }
                if (task == null) continue;

                BookEntry entry;
                entry.Name = _taskNames[i];
                entry.UnlockLevel = 0;

                try { entry.Done = _doneGets[i](task); }
                catch (Exception) { continue; }

                _todo.Add(entry);
                if (entry.Done) TodoDone++;
            }
        }

        private bool Bind(string typeName)
        {
            Type type = GameBridge.FindGameType(typeName);
            if (type == null) return false;

            UnityEngine.Object[] found;
            try { found = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception) { return false; }
            if (found == null) return false;

            // Also returns prefab assets; only a scene instance is live.
            Component host = null;
            for (int i = 0; i < found.Length && host == null; i++)
            {
                Component c = found[i] as Component;
                if (c != null && c.gameObject.scene.IsValid()) host = c;
            }
            if (host == null) return false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = type.GetFields(flags);

            _taskFields.Clear();
            _doneFields.Clear();
            _doneGets.Clear();
            _taskNames.Clear();

            for (int i = 0; i < fields.Length; i++)
            {
                // Each objective is its own TodoTask field. Identify them by
                // shape - something carrying _done - rather than by a
                // hardcoded list of names that a game update would break.
                // A bool _done only: each task's `<name>GOs` sibling
                // (TodoEntryGOs) has a GameObject `_done`, and reading
                // that as a bool threw and was caught - 21 exceptions a
                // second, the overlay's biggest idle garbage (T-0033).
                FieldInfo doneField = FindField(fields[i].FieldType, "_done", flags);
                if (doneField == null || doneField.FieldType != typeof(bool)) continue;

                _taskFields.Add(fields[i]);
                _doneFields.Add(doneField);
                _doneGets.Add(FastField.Instance<bool>(doneField));
                _taskNames.Add(Tidy(fields[i].Name));
            }

            if (_taskFields.Count == 0) return false;

            _host = host;
            return true;
        }

        // ------------------------------------------------------------------
        private static FieldInfo FindField(Type t, string name, BindingFlags flags)
        {
            while (t != null)
            {
                FieldInfo f = t.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        /// Turns "_cave1" / "FoundBoar" / "cave_1_tab" into something
        /// readable, since every name source here is an identifier.
        private static string Tidy(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unknown";

            System.Text.StringBuilder sb = new System.Text.StringBuilder(raw.Length + 4);
            bool lastWasSpace = true;

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];

                if (c == '_' || c == '-')
                {
                    if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                    continue;
                }

                // Split camelCase into words.
                if (!lastWasSpace && char.IsUpper(c) && i > 0 && !char.IsUpper(raw[i - 1]))
                {
                    sb.Append(' ');
                    sb.Append(c);
                    lastWasSpace = false;
                    continue;
                }

                sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
                lastWasSpace = false;
            }

            string text = sb.ToString().Trim();
            return text.Length == 0 ? "unknown" : text;
        }
    }
}
