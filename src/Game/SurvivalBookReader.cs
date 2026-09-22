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

    public sealed class BookPage
    {
        public string Title = "";
        public readonly List<BookEntry> Entries = new List<BookEntry>();

        public int DoneCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Entries.Count; i++) if (Entries[i].Done) n++;
                return n;
            }
        }
    }

    // ------------------------------------------------------------------
    // Reads the survival book: the nature guide (bestiary) and the todo
    // list. INFO-ONLY - nothing here writes.
    //
    // Confirmed from IL, not guessed:
    //
    //   TheForest.Player.SurvivalBookBestiary   (MonoBehaviour)
    //     ._foundEnemyInfos   FoundEnemyInfo[]  the entries on this page
    //     ._tab               SelectPageNumber  which book page this is
    //
    //   FoundEnemyInfo : TodoTask : Task : ACondition
    //     .CurrentUnlockLevel int
    //     ._availableConditionStorage  EnemyContactCondition
    //
    //   ACondition
    //     ._id   int
    //     ._done bool     <- found / not found
    //
    //   EnemyContactCondition._type  EnemyType (enum)
    //
    // ONE COMPONENT PER PAGE. _tab is per-instance, so enumerating the
    // instances gives exactly the "grouped by page, entries listed
    // individually" shape the 100% runners asked for.
    //
    // Entry names come from the EnemyType ENUM rather than the UI labels
    // in _foundEnemyInfosGOs. The enum is stable and locale-independent;
    // the labels are translated NGUI objects that would need walking and
    // would read differently per language.
    // ------------------------------------------------------------------
    public sealed class SurvivalBookReader
    {
        private readonly ManualLogSource _log;

        private readonly List<BookPage> _pages = new List<BookPage>();
        private readonly List<BookEntry> _todo = new List<BookEntry>();

        public IList<BookPage> Pages { get { return _pages; } }
        public IList<BookEntry> Todo { get { return _todo; } }
        public string Status { get; private set; }

        public int TotalEntries { get; private set; }
        public int TotalDone { get; private set; }
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
            _pages.Clear();
            _todo.Clear();
            TotalEntries = 0;
            TotalDone = 0;
            TodoDone = 0;

            ReadBestiary();
            ReadTodo();

            Status = _pages.Count > 0 || _todo.Count > 0
                ? TotalDone + "/" + TotalEntries + " found, " + TodoDone + "/" + _todo.Count + " tasks"
                : "survival book not loaded (open a save first)";
        }

        // ------------------------------------------------------------------
        private void ReadBestiary()
        {
            Type type = GameBridge.FindGameType("TheForest.Player.SurvivalBookBestiary");
            if (type == null) return;

            UnityEngine.Object[] found;
            try { found = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception) { return; }
            if (found == null) return;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo infosField = type.GetField("_foundEnemyInfos", flags);
            FieldInfo tabField = type.GetField("_tab", flags);
            if (infosField == null) return;

            for (int i = 0; i < found.Length; i++)
            {
                Component c = found[i] as Component;
                if (c == null) continue;

                IEnumerable infos;
                try { infos = infosField.GetValue(c) as IEnumerable; }
                catch (Exception) { continue; }
                if (infos == null) continue;

                BookPage page = new BookPage();
                page.Title = PageTitle(c, tabField);

                foreach (object info in infos)
                {
                    if (info == null) continue;

                    BookEntry entry;
                    if (!ReadEntry(info, out entry)) continue;

                    page.Entries.Add(entry);
                    TotalEntries++;
                    if (entry.Done) TotalDone++;
                }

                if (page.Entries.Count > 0) _pages.Add(page);
            }

            _pages.Sort(ComparePages);
        }

        private static int ComparePages(BookPage a, BookPage b)
        {
            return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        }

        private string PageTitle(Component page, FieldInfo tabField)
        {
            // The tab component's object name is the closest thing to a
            // page title the game exposes.
            if (tabField != null)
            {
                try
                {
                    Component tab = tabField.GetValue(page) as Component;
                    if (tab != null) return Tidy(tab.gameObject.name);
                }
                catch (Exception) { }
            }

            return Tidy(page.gameObject.name);
        }

        private bool ReadEntry(object info, out BookEntry entry)
        {
            entry = new BookEntry();

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = info.GetType();

            try
            {
                // _done lives on ACondition, several levels up the chain, so
                // walk the hierarchy rather than assuming it is declared here.
                FieldInfo doneField = FindField(t, "_done", flags);
                if (doneField == null) return false;

                entry.Done = (bool)doneField.GetValue(info);

                FieldInfo levelField = t.GetField("CurrentUnlockLevel", flags);
                if (levelField != null) entry.UnlockLevel = (int)levelField.GetValue(info);

                entry.Name = EntryName(info, t, flags);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string EntryName(object info, Type t, BindingFlags flags)
        {
            // The enum on the contact condition is the readable name.
            FieldInfo storage = FindField(t, "_availableConditionStorage", flags);
            if (storage != null)
            {
                try
                {
                    object condition = storage.GetValue(info);
                    if (condition != null)
                    {
                        FieldInfo typeField = FindField(condition.GetType(), "_type", flags);
                        if (typeField != null)
                        {
                            object value = typeField.GetValue(condition);
                            if (value != null) return Tidy(value.ToString());
                        }
                    }
                }
                catch (Exception) { }
            }

            // Fall back to the condition id so an entry is never nameless.
            FieldInfo idField = FindField(t, "_id", flags);
            if (idField != null)
            {
                try { return "entry " + idField.GetValue(info); }
                catch (Exception) { }
            }

            return "unknown";
        }

        // ------------------------------------------------------------------
        private void ReadTodo()
        {
            // The serialisable version is the live one in current builds;
            // the older type is checked too rather than assuming.
            if (ReadTodoFrom("TheForest.Player.SerializableSurvivalBookTodo")) return;
            ReadTodoFrom("TheForest.Player.SurvivalBookTodo");
        }

        private bool ReadTodoFrom(string typeName)
        {
            Type type = GameBridge.FindGameType(typeName);
            if (type == null) return false;

            UnityEngine.Object[] found;
            try { found = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception) { return false; }
            if (found == null || found.Length == 0) return false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = type.GetFields(flags);

            Component host = found[0] as Component;
            if (host == null) return false;

            for (int i = 0; i < fields.Length; i++)
            {
                // Each objective is its own TodoTask field. Identify them by
                // shape - something carrying _done - rather than by a
                // hardcoded list of names that a game update would break.
                FieldInfo doneField = FindField(fields[i].FieldType, "_done", flags);
                if (doneField == null) continue;

                object task;
                try { task = fields[i].GetValue(host); }
                catch (Exception) { continue; }
                if (task == null) continue;

                BookEntry entry;
                entry.Name = Tidy(fields[i].Name);
                entry.UnlockLevel = 0;

                try { entry.Done = (bool)doneField.GetValue(task); }
                catch (Exception) { continue; }

                _todo.Add(entry);
                if (entry.Done) TodoDone++;
            }

            return _todo.Count > 0;
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
