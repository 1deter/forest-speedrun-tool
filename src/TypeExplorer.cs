using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay
{
    // ------------------------------------------------------------------
    // Runtime type explorer.
    //
    // Performance notes (this is what fixed the ~1s stutter):
    //   * Type list is VIRTUALIZED - only rows inside the scroll viewport
    //     are drawn. Previously every type drew a GUILayout.Button on every
    //     OnGUI pass, and OnGUI runs multiple times per frame.
    //   * All labels are cached as GUIContent at scan time. Building
    //     GUIContent from a string inside OnGUI allocates, and thousands of
    //     allocations per frame is what was feeding the GC spikes.
    //   * Type references are cached, so clicking a row doesn't re-walk
    //     every loaded assembly.
    // ------------------------------------------------------------------
    public class TypeExplorer
    {
        private readonly ManualLogSource _log;

        // Cached scan results - index-aligned arrays.
        private Type[] _types = new Type[0];
        private string[] _fullNames = new string[0];
        private GUIContent[] _rowLabels = new GUIContent[0];

        private readonly List<int> _filtered = new List<int>();
        private string _filter = "";
        private string _lastAppliedFilter = null;

        private int _selectedIndex = -1;
        private readonly List<GUIContent> _fieldLines = new List<GUIContent>();
        private string _selectedHeader = "(select a type on the left)";

        private Vector2 _typeScroll;
        private Vector2 _fieldScroll;
        private Rect _windowRect;
        private bool _windowRectInitialised;

        private GUIStyle _rowStyle;
        private GUIStyle _rowStyleSelected;
        private GUIStyle _lineStyle;
        private GUIStyle _headerStyle;

        public string Summary { get; private set; }

        // Practice-only game freeze, surfaced as a toggle in the window.
        public bool LockPlayer = true;
        public Action<bool> OnLockPlayerChanged;

        private const float RowHeight = 20f;
        private const float LineHeight = 17f;

        public TypeExplorer(ManualLogSource log)
        {
            _log = log;
            Summary = "(not scanned)";
        }

        // ------------------------------------------------------------------
        // Scanning
        // ------------------------------------------------------------------
        public void Rescan()
        {
            List<Type> found = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            int gameAssemblies = 0;

            for (int a = 0; a < assemblies.Length; a++)
            {
                string name;
                try { name = assemblies[a].GetName().Name; }
                catch (Exception) { continue; }

                if (!name.StartsWith("Assembly-CSharp")) continue;
                gameAssemblies++;

                Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    List<Type> partial = new List<Type>();
                    if (ex.Types != null)
                        for (int t = 0; t < ex.Types.Length; t++)
                            if (ex.Types[t] != null) partial.Add(ex.Types[t]);
                    types = partial.ToArray();
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Could not read types from " + name + ": " + ex.Message);
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                    if (types[t] != null) found.Add(types[t]);
            }

            found.Sort(CompareTypesByFullName);

            _types = found.ToArray();
            _fullNames = new string[_types.Length];
            _rowLabels = new GUIContent[_types.Length];

            for (int i = 0; i < _types.Length; i++)
            {
                Type t = _types[i];
                _fullNames[i] = t.FullName ?? t.Name;

                // Short name reads first (that's what you scan for), namespace
                // trails it for disambiguation.
                string ns = string.IsNullOrEmpty(t.Namespace) ? "" : "   (" + t.Namespace + ")";
                _rowLabels[i] = new GUIContent(t.Name + ns);
            }

            _lastAppliedFilter = null;
            _selectedIndex = -1;
            _fieldLines.Clear();
            _selectedHeader = "(select a type on the left)";

            Summary = _types.Length + " types / " + gameAssemblies + " game asm";
            _log.LogInfo("Type explorer scanned " + Summary);
        }

        private static int CompareTypesByFullName(Type a, Type b)
        {
            string x = a.FullName ?? a.Name;
            string y = b.FullName ?? b.Name;
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyFilterIfChanged()
        {
            if (_filter == _lastAppliedFilter) return;
            _lastAppliedFilter = _filter;

            _filtered.Clear();
            string needle = _filter == null ? "" : _filter.ToLowerInvariant().Trim();

            for (int i = 0; i < _types.Length; i++)
            {
                if (needle.Length == 0 || _fullNames[i].ToLowerInvariant().Contains(needle))
                    _filtered.Add(i);
            }

            _typeScroll = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // Field inspection
        // ------------------------------------------------------------------
        private void InspectType(int index)
        {
            _selectedIndex = index;
            _fieldLines.Clear();
            _fieldScroll = Vector2.zero;

            if (index < 0 || index >= _types.Length)
            {
                _selectedHeader = "(invalid selection)";
                return;
            }

            Type target = _types[index];
            _selectedHeader = _fullNames[index];

            UnityEngine.Object liveInstance = null;
            try
            {
                if (typeof(MonoBehaviour).IsAssignableFrom(target))
                    liveInstance = UnityEngine.Object.FindObjectOfType(target);
            }
            catch (Exception) { }

            _fieldLines.Add(new GUIContent(liveInstance != null
                ? ">> live instance found - values are real"
                : ">> no live instance in scene - names only"));

            if (target.BaseType != null)
                _fieldLines.Add(new GUIContent("   base: " + target.BaseType.Name));
            _fieldLines.Add(new GUIContent(""));

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo[] fields;
            try { fields = target.GetFields(flags); }
            catch (Exception ex)
            {
                _fieldLines.Add(new GUIContent("(could not read fields: " + ex.Message + ")"));
                return;
            }

            for (int f = 0; f < fields.Length; f++)
            {
                FieldInfo field = fields[f];
                string value;

                try
                {
                    if (field.IsStatic)
                    {
                        object v = field.GetValue(null);
                        value = v == null ? "null" : v.ToString();
                    }
                    else if (liveInstance != null)
                    {
                        object v = field.GetValue(liveInstance);
                        value = v == null ? "null" : v.ToString();
                    }
                    else value = "-";
                }
                catch (Exception ex)
                {
                    value = "(" + ex.GetType().Name + ")";
                }

                if (value.Length > 48) value = value.Substring(0, 48) + "...";

                _fieldLines.Add(new GUIContent(
                    (field.IsStatic ? "S " : "  ") + field.Name + " : " +
                    field.FieldType.Name + " = " + value));
            }

            if (fields.Length == 0)
                _fieldLines.Add(new GUIContent("(no fields)"));

            _log.LogInfo("Inspected " + _selectedHeader + " (" + fields.Length + " fields)");
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------
        public void Draw(int windowId)
        {
            EnsureStyles();

            if (!_windowRectInitialised)
            {
                // Size to the screen rather than hardcoding, so it stays
                // readable at any resolution.
                float w = Mathf.Clamp(Screen.width * 0.62f, 700f, 1300f);
                float h = Mathf.Clamp(Screen.height * 0.70f, 420f, 900f);
                _windowRect = new Rect(Screen.width * 0.5f - w * 0.5f, 60f, w, h);
                _windowRectInitialised = true;
            }

            ApplyFilterIfChanged();

            _windowRect = GUI.Window(windowId, _windowRect, DrawWindowContents,
                "Type Explorer  -  " + Summary);
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null) return;

            _rowStyle = new GUIStyle(GUI.skin.button);
            _rowStyle.alignment = TextAnchor.MiddleLeft;
            _rowStyle.fontSize = 12;
            _rowStyle.padding = new RectOffset(6, 4, 2, 2);
            _rowStyle.clipping = TextClipping.Clip;

            _rowStyleSelected = new GUIStyle(_rowStyle);
            _rowStyleSelected.fontStyle = FontStyle.Bold;

            _lineStyle = new GUIStyle(GUI.skin.label);
            _lineStyle.fontSize = 12;
            _lineStyle.padding = new RectOffset(2, 2, 0, 0);
            _lineStyle.clipping = TextClipping.Clip;
            _lineStyle.wordWrap = false;

            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontSize = 12;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.clipping = TextClipping.Clip;
        }

        private void DrawWindowContents(int id)
        {
            float w = _windowRect.width;
            float h = _windowRect.height;

            // --- Toolbar ---
            GUI.Label(new Rect(10, 26, 40, 22), "Filter", _lineStyle);
            string newFilter = GUI.TextField(new Rect(52, 26, 230, 22), _filter ?? "");
            if (newFilter != _filter) _filter = newFilter;

            if (GUI.Button(new Rect(290, 26, 60, 22), "Clear"))
                _filter = "";

            if (GUI.Button(new Rect(356, 26, 70, 22), "Rescan"))
                Rescan();

            if (GUI.Button(new Rect(432, 26, 110, 22), "Dump filtered"))
                DumpFiltered();

            bool newLock = GUI.Toggle(new Rect(556, 28, 200, 22), LockPlayer, " Lock player while open");
            if (newLock != LockPlayer)
            {
                LockPlayer = newLock;
                if (OnLockPlayerChanged != null) OnLockPlayerChanged(newLock);
            }

            GUI.Label(new Rect(10, 52, w - 20, 20),
                _filtered.Count + " shown" +
                (_types.Length > 0 ? " of " + _types.Length : "") +
                "   -   click a type to inspect its fields", _lineStyle);

            // --- Layout split ---
            float listW = Mathf.Round(w * 0.42f);
            Rect listRect = new Rect(10, 76, listW, h - 88);
            Rect paneRect = new Rect(listW + 20, 76, w - listW - 30, h - 88);

            DrawTypeList(listRect);
            DrawFieldPane(paneRect);

            // Drag by the title bar only, so the controls stay clickable.
            GUI.DragWindow(new Rect(0, 0, w, 24));
        }

        private void DrawTypeList(Rect listRect)
        {
            GUI.Box(listRect, GUIContent.none);

            int count = _filtered.Count;
            Rect view = new Rect(0, 0, listRect.width - 20f, count * RowHeight);
            _typeScroll = GUI.BeginScrollView(listRect, _typeScroll, view);

            // Virtualization: only draw rows actually inside the viewport.
            int first = Mathf.Max(0, Mathf.FloorToInt(_typeScroll.y / RowHeight) - 1);
            int visible = Mathf.CeilToInt(listRect.height / RowHeight) + 2;
            int last = Mathf.Min(count - 1, first + visible);

            for (int i = first; i <= last; i++)
            {
                int typeIndex = _filtered[i];
                Rect r = new Rect(2, i * RowHeight, view.width - 4, RowHeight - 2);
                GUIStyle style = typeIndex == _selectedIndex ? _rowStyleSelected : _rowStyle;

                if (GUI.Button(r, _rowLabels[typeIndex], style))
                    InspectType(typeIndex);
            }

            GUI.EndScrollView();

            if (count == 0)
                GUI.Label(new Rect(listRect.x + 8, listRect.y + 8, listRect.width - 16, 40),
                    "No types match.\nTry clearing the filter, or Rescan.", _lineStyle);
        }

        private void DrawFieldPane(Rect paneRect)
        {
            GUI.Label(new Rect(paneRect.x, paneRect.y - 20, paneRect.width, 20),
                _selectedHeader, _headerStyle);

            if (_selectedIndex >= 0 &&
                GUI.Button(new Rect(paneRect.xMax - 110, paneRect.y - 22, 110, 20), "Refresh values"))
            {
                InspectType(_selectedIndex);
            }

            GUI.Box(paneRect, GUIContent.none);

            int count = _fieldLines.Count;
            Rect view = new Rect(0, 0, paneRect.width - 20f, count * LineHeight);
            _fieldScroll = GUI.BeginScrollView(paneRect, _fieldScroll, view);

            int first = Mathf.Max(0, Mathf.FloorToInt(_fieldScroll.y / LineHeight) - 1);
            int visible = Mathf.CeilToInt(paneRect.height / LineHeight) + 2;
            int last = Mathf.Min(count - 1, first + visible);

            for (int i = first; i <= last; i++)
            {
                GUI.Label(new Rect(4, i * LineHeight, view.width - 8, LineHeight),
                    _fieldLines[i], _lineStyle);
            }

            GUI.EndScrollView();
        }

        private void DumpFiltered()
        {
            try
            {
                List<Type> subset = new List<Type>();
                for (int i = 0; i < _filtered.Count; i++)
                    subset.Add(_types[_filtered[i]]);

                string path = GameDumper.WriteTypeDetail(_log, subset,
                    string.IsNullOrEmpty(_filter) ? "all" : _filter);
                _log.LogInfo("Filtered detail dump -> " + path);
            }
            catch (Exception ex)
            {
                _log.LogError("Filtered dump failed: " + ex);
            }
        }
    }
}
