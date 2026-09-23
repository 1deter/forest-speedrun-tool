using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The survival book's open page, kept by savestates (author,
    // 2026-09-23: a savestate keeps the page it was captured on, in place
    // or with a load; a quick-load keeps the game's default).
    //
    // Confirmed from IL (SelectPageNumber): the book has no page number.
    // A link click deactivates its own page (`transform.parent`, or
    // `ThisPageOverride`) and activates `MyPageNew`; an index or tab click
    // runs TurnOffAllPages (every child of `Pages` off) and activates
    // `MyPageNew` (a tab also hides `IndexPage`). Each click then copies the
    // new page's material onto `LocalPlayer.AnimatedBook`, the book model
    // shown while it opens and closes. So the state is which page objects
    // are active, and restoring it is what a click does.
    //
    // Found under the player each time (capture and restore only - never
    // per frame): a load replaces every object.
    // ------------------------------------------------------------------
    public sealed class BookPages
    {
        private readonly ManualLogSource _log;

        private bool _resolved;
        private Type _selectType;
        private FieldInfo _pages;          // GameObject
        private FieldInfo _indexPage;      // GameObject
        private FieldInfo _myPageNew;      // GameObject
        private FieldInfo _animatedBook;   // static SkinnedMeshRenderer on LocalPlayer
        private FieldInfo _playerGo;       // static GameObject on LocalPlayer

        public BookPages(ManualLogSource log)
        {
            _log = log;
        }

        private bool Resolve()
        {
            if (!_resolved)
            {
                _resolved = true;
                BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                _selectType = GameBridge.FindGameType("SelectPageNumber");
                if (_selectType != null)
                {
                    _pages = _selectType.GetField("Pages", inst);
                    _indexPage = _selectType.GetField("IndexPage", inst);
                    _myPageNew = _selectType.GetField("MyPageNew", inst);
                }

                Type lp = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                if (lp != null)
                {
                    _animatedBook = lp.GetField("AnimatedBook", stat);
                    _playerGo = lp.GetField("GameObject", stat);
                }

                _log.LogInfo("BookPages bound. select:" + (_selectType != null) + " pages:" + (_pages != null) +
                             " index:" + (_indexPage != null) + " myPage:" + (_myPageNew != null) +
                             " animatedBook:" + (_animatedBook != null) + " player:" + (_playerGo != null));
            }
            return _selectType != null && _pages != null && _myPageNew != null && _playerGo != null;
        }

        /// The value for the savestate header, or "" with `note` saying why.
        public string Capture(out string note)
        {
            try
            {
                Component[] selectors;
                List<GameObject> pages = PageObjects(out selectors, out note);
                if (pages == null) return "";

                bool[] active = new bool[pages.Count];
                for (int i = 0; i < pages.Count; i++) active[i] = pages[i].activeSelf;

                note = "book: " + ShownNames(pages, active);
                return BookPageState.Encode(active);
            }
            catch (Exception ex)
            {
                note = "book: capture failed (" + ex.Message + ")";
                _log.LogWarning("Book page capture failed: " + ex);
                return "";
            }
        }

        /// Puts the pages back as captured. Returns a note for the log line.
        public string Apply(string state)
        {
            if (string.IsNullOrEmpty(state)) return "book: not in this savestate (captured before v0.24.0)";

            try
            {
                Component[] selectors;
                string note;
                List<GameObject> pages = PageObjects(out selectors, out note);
                if (pages == null) return note;

                bool[] active;
                string why;
                if (!BookPageState.TryDecode(state, pages.Count, out active, out why))
                    return "book: left as is - " + why;

                int changed = 0;
                for (int i = 0; i < pages.Count; i++)
                {
                    if (pages[i].activeSelf == active[i]) continue;
                    pages[i].SetActive(active[i]);
                    changed++;
                }

                bool material = ShowOnAnimatedBook(selectors);
                return "book: " + ShownNames(pages, active) + " (" + changed + " page object(s) switched" +
                       (material ? "" : ", animated book not updated") + ")";
            }
            catch (Exception ex)
            {
                _log.LogWarning("Book page restore failed: " + ex);
                return "book: restore failed (" + ex.Message + ")";
            }
        }

        // Every child of each distinct Pages container, then each distinct
        // IndexPage not already listed - in hierarchy order, which is the
        // same for every game (the book is part of the player prefab).
        private List<GameObject> PageObjects(out Component[] selectors, out string note)
        {
            selectors = null;
            note = null;

            if (!Resolve()) { note = "book: unavailable (a SelectPageNumber field is missing)"; return null; }

            GameObject player = _playerGo.GetValue(null) as GameObject;
            if (player == null) { note = "book: no player"; return null; }

            selectors = player.GetComponentsInChildren(_selectType, true);
            if (selectors.Length == 0) { note = "book: no pages found under the player"; return null; }

            List<GameObject> pages = new List<GameObject>();
            HashSet<int> seen = new HashSet<int>();
            HashSet<int> containers = new HashSet<int>();

            for (int s = 0; s < selectors.Length; s++)
            {
                GameObject container = _pages.GetValue(selectors[s]) as GameObject;
                if (container == null || !containers.Add(container.GetInstanceID())) continue;

                Transform t = container.transform;
                for (int c = 0; c < t.childCount; c++)
                {
                    GameObject page = t.GetChild(c).gameObject;
                    if (seen.Add(page.GetInstanceID())) pages.Add(page);
                }
            }

            if (_indexPage != null)
            {
                for (int s = 0; s < selectors.Length; s++)
                {
                    GameObject index = _indexPage.GetValue(selectors[s]) as GameObject;
                    if (index != null && seen.Add(index.GetInstanceID())) pages.Add(index);
                }
            }

            if (pages.Count == 0) { note = "book: " + selectors.Length + " page links but no pages"; return null; }
            return pages;
        }

        // What a click does last: the book model seen while opening and
        // closing shows the new page's material.
        private bool ShowOnAnimatedBook(Component[] selectors)
        {
            if (_animatedBook == null) return false;
            Renderer book = _animatedBook.GetValue(null) as Renderer;
            if (book == null) return false;

            for (int s = 0; s < selectors.Length; s++)
            {
                GameObject page = _myPageNew.GetValue(selectors[s]) as GameObject;
                if (page == null || !page.activeSelf) continue;

                Renderer r = page.GetComponent<Renderer>();
                if (r == null) continue;
                book.sharedMaterial = r.sharedMaterial;
                return true;
            }
            return false;
        }

        private static string ShownNames(List<GameObject> pages, bool[] active)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < pages.Count; i++)
                if (active[i]) names.Add("'" + pages[i].name + "'");

            if (names.Count == 0) return "no page open (of " + pages.Count + ")";
            return "showing " + string.Join(", ", names.ToArray()) + " (of " + pages.Count + ")";
        }
    }
}
