using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using ForestOverlay.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Live reflection for the test bridge (Modules/BridgeModule): find
    // objects, list their components and fields, read / write / call a
    // member by path. Generic - no game names here; the commands name
    // them at run time.
    //
    // Every method writes plain text lines into `into` and returns null,
    // or the reason it could not. Nothing throws out: a bad path is an
    // error line, never an exception in the game.
    //
    // Objects are named by handle, "#<instance id>", as listed by any
    // command. Handles are kept only for objects this probe has printed.
    // ------------------------------------------------------------------
    public sealed class ObjectProbe
    {
        private const int MaxHandles = 20000;
        private const int ValueChars = 160;
        private const int ListItems = 50;

        private readonly Dictionary<int, UnityEngine.Object> _handles = new Dictionary<int, UnityEngine.Object>();
        private List<Type> _allTypes;
        private readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

        /// Resolved each command by the bridge: the player's root, or null.
        public Transform Player;

        // ------------------------------------------------------------------
        // Handles and targets

        public string Handle(UnityEngine.Object o)
        {
            if (o == null) return "null";
            int id = o.GetInstanceID();
            if (!_handles.ContainsKey(id))
            {
                if (_handles.Count >= MaxHandles) _handles.Clear();
                _handles[id] = o;
            }
            return "#" + id.ToString(CultureInfo.InvariantCulture);
        }

        /// A target is an object (GameObject / Component) or, for
        /// "static:Type", a type whose static members the path starts at.
        public string ResolveTarget(string token, out object target, out Type staticType)
        {
            target = null;
            staticType = null;
            if (string.IsNullOrEmpty(token)) return "no target";

            if (token.StartsWith("#"))
            {
                int id;
                if (!int.TryParse(token.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    return "'" + token + "' is not a handle (#<number>)";
                UnityEngine.Object o;
                if (!_handles.TryGetValue(id, out o)) return token + ": unknown handle - list it first (find / type / inspect)";
                if (o == null) return token + ": destroyed";
                target = o;
                return null;
            }

            if (token.StartsWith("static:", StringComparison.OrdinalIgnoreCase))
            {
                string note;
                staticType = FindType(token.Substring(7), out note);
                return staticType == null ? note : null;
            }

            if (string.Equals(token, "player", StringComparison.OrdinalIgnoreCase))
            {
                if (Player == null) return "no player";
                target = Player.gameObject;
                return null;
            }

            if (string.Equals(token, "camera", StringComparison.OrdinalIgnoreCase))
            {
                if (Camera.main == null) return "no main camera";
                target = Camera.main.gameObject;
                return null;
            }

            // A name or a path. GameObject.Find sees active objects only.
            GameObject go = GameObject.Find(token);
            if (go == null)
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(typeof(GameObject));
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject g = all[i] as GameObject;
                    if (g != null && g.name == token && InScene(g)) { go = g; break; }
                }
            }
            if (go == null) return "no GameObject named '" + token + "' (use a #handle for anything ambiguous)";
            target = go;
            return null;
        }

        private static bool InScene(GameObject g)
        {
            return g.hideFlags == HideFlags.None && g.scene.IsValid();
        }

        // ------------------------------------------------------------------
        // Types

        private List<Type> AllTypes()
        {
            if (_allTypes != null) return _allTypes;
            _allTypes = new List<Type>();
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < asms.Length; a++)
            {
                Type[] types;
                try { types = asms[a].GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch (Exception) { continue; }
                if (types == null) continue;
                for (int i = 0; i < types.Length; i++)
                    if (types[i] != null) _allTypes.Add(types[i]);
            }
            return _allTypes;
        }

        /// By full name, else by short name (Assembly-CSharp first when
        /// several share it; `note` names the others).
        public Type FindType(string name, out string note)
        {
            note = null;
            Type cached;
            if (_typeCache.TryGetValue(name, out cached)) return cached;

            List<Type> all = AllTypes();
            Type best = null;
            List<string> others = new List<string>();
            for (int pass = 0; pass < 3 && best == null; pass++)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Type t = all[i];
                    bool match = pass == 0 ? t.FullName == name
                               : pass == 1 ? t.Name == name
                               : string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase);
                    if (!match) continue;
                    if (best == null) best = t;
                    else if (IsGameAssembly(t) && !IsGameAssembly(best)) { others.Add(best.FullName); best = t; }
                    else others.Add(t.FullName);
                }
            }

            if (best == null) { note = "no type '" + name + "' (try: types " + name + ")"; return null; }
            if (others.Count > 0) note = "'" + name + "' is " + best.FullName + " (also: " + string.Join(", ", others.ToArray()) + ")";
            _typeCache[name] = best;
            return best;
        }

        private static bool IsGameAssembly(Type t)
        {
            return t.Assembly.GetName().Name == "Assembly-CSharp";
        }

        public string Types(string filter, int max, List<string> into)
        {
            List<Type> all = AllTypes();
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Type t = all[i];
                if (t.FullName == null || t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n < max) into.Add(t.FullName + "  [" + t.Assembly.GetName().Name + "]" +
                                      (typeof(Component).IsAssignableFrom(t) ? " component" : ""));
                n++;
            }
            if (n > max) into.Add("(" + (n - max) + " more - narrow the text)");
            into.Add(n + " type(s)");
            return null;
        }

        /// Fields, properties and methods declared on a type (and its
        /// bases up to Unity's), static ones marked.
        public string Members(Type t, string filter, List<string> into)
        {
            into.Add(t.FullName + (t.BaseType != null ? " : " + t.BaseType.FullName : "") + "  [" + t.Assembly.GetName().Name + "]");
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.DeclaredOnly;
            for (Type c = t; c != null && !IsEngineBase(c); c = c.BaseType)
            {
                if (c != t) into.Add("-- from " + c.FullName);
                FieldInfo[] fields = c.GetFields(all);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    if (IsHidden(f.Name, filter)) continue;
                    into.Add("  field " + (f.IsStatic ? "static " : "") + TypeName(f.FieldType) + " " + f.Name);
                }
                PropertyInfo[] props = c.GetProperties(all);
                for (int i = 0; i < props.Length; i++)
                {
                    PropertyInfo p = props[i];
                    if (IsHidden(p.Name, filter)) continue;
                    MethodInfo acc = p.GetGetMethod(true) ?? p.GetSetMethod(true);
                    into.Add("  prop  " + (acc != null && acc.IsStatic ? "static " : "") + TypeName(p.PropertyType) + " " + p.Name +
                             (p.CanRead ? " get" : "") + (p.CanWrite ? " set" : ""));
                }
                MethodInfo[] methods = c.GetMethods(all);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m.IsSpecialName || IsHidden(m.Name, filter)) continue;
                    into.Add("  method " + (m.IsStatic ? "static " : "") + TypeName(m.ReturnType) + " " + m.Name + Signature(m));
                }
            }
            return null;
        }

        private static bool IsHidden(string name, string filter)
        {
            if (name.IndexOf('<') >= 0) return true;   // compiler-generated
            return !string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsEngineBase(Type c)
        {
            return c == typeof(object) || c == typeof(ValueType) || c == typeof(UnityEngine.Object) ||
                   c == typeof(Component) || c == typeof(Behaviour) || c == typeof(MonoBehaviour);
        }

        private static string Signature(MethodBase m)
        {
            ParameterInfo[] ps = m.GetParameters();
            StringBuilder sb = new StringBuilder("(");
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(TypeName(ps[i].ParameterType)).Append(' ').Append(ps[i].Name);
            }
            return sb.Append(')').ToString();
        }

        private static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            Type[] args = t.GetGenericArguments();
            StringBuilder sb = new StringBuilder(t.Name.Split('`')[0]).Append('<');
            for (int i = 0; i < args.Length; i++) { if (i > 0) sb.Append(','); sb.Append(TypeName(args[i])); }
            return sb.Append('>').ToString();
        }

        // ------------------------------------------------------------------
        // Finding objects

        /// GameObjects whose name contains `filter` ("*" = any), within
        /// `radius` of the player when > 0, nearest first.
        public string Find(string filter, float radius, bool includeInactive, int max, List<string> into)
        {
            UnityEngine.Object[] objs = includeInactive
                ? Resources.FindObjectsOfTypeAll(typeof(GameObject))
                : UnityEngine.Object.FindObjectsOfType(typeof(GameObject));
            bool any = filter == "*";

            List<Transform> hits = new List<Transform>();
            for (int i = 0; i < objs.Length; i++)
            {
                GameObject g = objs[i] as GameObject;
                if (g == null || !InScene(g)) continue;
                if (!any && g.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!InRadius(g.transform, radius)) continue;
                hits.Add(g.transform);
            }
            ListTransforms(hits, max, into, null);
            return null;
        }

        /// Objects carrying a component of `t` (or deriving from it).
        public string FindByType(Type t, float radius, bool includeInactive, int max, List<string> into)
        {
            if (!typeof(Component).IsAssignableFrom(t) && t != typeof(GameObject))
                return t.FullName + " is not a component - use fields static:" + t.Name + " for its statics";

            UnityEngine.Object[] objs = includeInactive
                ? Resources.FindObjectsOfTypeAll(t)
                : UnityEngine.Object.FindObjectsOfType(t);

            List<Transform> hits = new List<Transform>();
            Dictionary<Transform, string> notes = new Dictionary<Transform, string>();
            for (int i = 0; i < objs.Length; i++)
            {
                Component c = objs[i] as Component;
                if (c == null || !InScene(c.gameObject)) continue;
                if (!InRadius(c.transform, radius)) continue;
                if (notes.ContainsKey(c.transform)) continue;
                hits.Add(c.transform);
                notes[c.transform] = c.GetType() != t ? c.GetType().Name : null;
            }
            ListTransforms(hits, max, into, notes);
            return null;
        }

        private bool InRadius(Transform t, float radius)
        {
            if (radius <= 0f) return true;
            if (Player == null) return false;
            return Vector3.Distance(t.position, Player.position) <= radius;
        }

        private void ListTransforms(List<Transform> hits, int max, List<string> into, Dictionary<Transform, string> notes)
        {
            if (Player != null)
            {
                Vector3 p = Player.position;
                hits.Sort(delegate(Transform a, Transform b)
                {
                    return (a.position - p).sqrMagnitude.CompareTo((b.position - p).sqrMagnitude);
                });
            }

            int shown = Math.Min(max, hits.Count);
            for (int i = 0; i < shown; i++)
            {
                Transform t = hits[i];
                string note;
                if (notes == null || !notes.TryGetValue(t, out note)) note = null;
                into.Add(Handle(t.gameObject) + "  " + PathOf(t) + "  " + Vec(t.position) +
                         (Player != null ? "  " + Vector3.Distance(t.position, Player.position).ToString("0.0", CultureInfo.InvariantCulture) + " m" : "") +
                         (t.gameObject.activeInHierarchy ? "" : "  [inactive]") +
                         (note != null ? "  (" + note + ")" : ""));
            }
            if (hits.Count > shown) into.Add("(" + (hits.Count - shown) + " more - max=N, a radius or a narrower name)");
            into.Add(hits.Count + " object(s)" + (Player == null ? " (no player: unsorted, no radius)" : ", nearest first"));
        }

        public string Roots(string filter, List<string> into)
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) { into.Add("scene '" + scene.name + "' (not loaded)"); continue; }
                GameObject[] roots = scene.GetRootGameObjects();
                into.Add("scene '" + scene.name + "': " + roots.Length + " root(s)");
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject g = roots[i];
                    if (!string.IsNullOrEmpty(filter) && g.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    into.Add("  " + Handle(g) + "  " + g.name + "  " + Vec(g.transform.position) +
                             "  children " + g.transform.childCount + (g.activeSelf ? "" : "  [inactive]"));
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Inspecting

        public string Inspect(object target, int depth, List<string> into)
        {
            GameObject go = AsGameObject(target);
            if (go == null) return "inspect needs a GameObject or component";

            Transform t = go.transform;
            into.Add(Handle(go) + "  " + PathOf(t));
            into.Add("  active " + go.activeSelf + (go.activeSelf != go.activeInHierarchy ? " (inactive in hierarchy)" : "") +
                     ", layer " + go.layer + " " + LayerMask.LayerToName(go.layer) + ", tag " + SafeTag(go) +
                     ", scene '" + go.scene.name + "'");
            into.Add("  position " + Vec(t.position) + ", local " + Vec(t.localPosition) +
                     ", rotation " + Vec(t.eulerAngles) + ", scale " + Vec(t.localScale));
            if (t.parent != null) into.Add("  parent " + Handle(t.parent.gameObject) + " " + t.parent.name);
            if (Player != null) into.Add("  distance to the player " + Vector3.Distance(t.position, Player.position).ToString("0.0", CultureInfo.InvariantCulture) + " m");

            Component[] comps = go.GetComponents<Component>();
            into.Add("  components (" + comps.Length + "):");
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null) { into.Add("    (missing script)"); continue; }
                into.Add("    " + c.GetType().FullName + EnabledNote(c));
            }

            if (t.childCount > 0)
            {
                into.Add("  children (" + t.childCount + "):");
                Children(t, 1, depth, into);
            }
            return null;
        }

        private void Children(Transform t, int level, int depth, List<string> into)
        {
            if (level > depth) return;
            const int MaxChildren = 60;
            for (int i = 0; i < t.childCount && i < MaxChildren; i++)
            {
                Transform c = t.GetChild(i);
                Component[] comps = c.GetComponents<Component>();
                StringBuilder names = new StringBuilder();
                for (int k = 0; k < comps.Length; k++)
                {
                    if (comps[k] == null || comps[k] is Transform) continue;
                    if (names.Length > 0) names.Append(", ");
                    names.Append(comps[k].GetType().Name);
                }
                into.Add(new string(' ', 2 + level * 2) + Handle(c.gameObject) + " " + c.name +
                         (c.gameObject.activeSelf ? "" : " [inactive]") +
                         (names.Length > 0 ? "  {" + names + "}" : "") +
                         (c.childCount > 0 && level == depth ? "  +" + c.childCount + " children" : ""));
                Children(c, level + 1, depth, into);
            }
            if (t.childCount > MaxChildren) into.Add(new string(' ', 2 + level * 2) + "(" + (t.childCount - MaxChildren) + " more)");
        }

        private static string SafeTag(GameObject go)
        {
            try { return go.tag; } catch (Exception) { return "?"; }
        }

        private static string EnabledNote(Component c)
        {
            Behaviour b = c as Behaviour;
            if (b != null) return b.enabled ? "" : "  [disabled]";
            Collider col = c as Collider;
            if (col != null) return (col.enabled ? "" : "  [disabled]") + (col.isTrigger ? "  trigger" : "");
            Renderer r = c as Renderer;
            if (r != null) return r.enabled ? "" : "  [disabled]";
            return "";
        }

        /// Every field (and readable property) of the value at `path`, or
        /// of the target itself when `path` is empty. For a GameObject the
        /// path must start with a component name.
        public string Fields(object target, Type staticType, string path, List<string> into)
        {
            object value;
            Type type;
            if (string.IsNullOrEmpty(path))
            {
                if (staticType != null) return StaticFields(staticType, into);
                if (target is GameObject) return "fields on a GameObject needs a component: fields <target> <Component>";
                value = target;
            }
            else
            {
                string err = Walk(target, staticType, path, out value, out type);
                if (err != null) return err;
            }

            if (IsNull(value)) { into.Add("null"); return null; }
            into.Add(Format(value) + "  (" + value.GetType().FullName + ")");
            AppendMembers(value, into);
            return null;
        }

        private string StaticFields(Type t, List<string> into)
        {
            into.Add("static members of " + t.FullName);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            FieldInfo[] fields = t.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Name.IndexOf('<') >= 0) continue;
                into.Add("  " + fields[i].Name + " = " + SafeGet(fields[i], null));
            }
            PropertyInfo[] props = t.GetProperties(flags);
            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo p = props[i];
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                into.Add("  " + p.Name + " = " + SafeGet(p, null) + "  (property)");
            }
            return null;
        }

        // Unity getters that copy an asset per read (and leak it).
        private static readonly string[] CopyingGetters = { "material", "materials", "mesh" };

        private void AppendMembers(object value, List<string> into)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            int lines = 0;
            const int MaxLines = 400;
            for (Type c = value.GetType(); c != null && !IsEngineBase(c); c = c.BaseType)
            {
                bool engine = c.Namespace != null && c.Namespace.StartsWith("UnityEngine");
                if (c != value.GetType()) into.Add("  -- from " + c.FullName);

                FieldInfo[] fields = c.GetFields(flags);
                for (int i = 0; i < fields.Length && lines < MaxLines; i++, lines++)
                {
                    if (fields[i].Name.IndexOf('<') >= 0) continue;
                    into.Add("  " + fields[i].Name + " = " + SafeGet(fields[i], value));
                }

                PropertyInfo[] props = c.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (int i = 0; i < props.Length && lines < MaxLines; i++)
                {
                    PropertyInfo p = props[i];
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    if (engine && (Array.IndexOf(CopyingGetters, p.Name) >= 0 || p.IsDefined(typeof(ObsoleteAttribute), true))) continue;
                    into.Add("  " + p.Name + " = " + SafeGet(p, value) + "  (property)");
                    lines++;
                }
            }
            if (lines >= MaxLines) into.Add("  (cut at " + MaxLines + " lines - use get for one member)");
        }

        private string SafeGet(MemberInfo m, object owner)
        {
            try
            {
                FieldInfo f = m as FieldInfo;
                object v = f != null ? f.GetValue(owner) : ((PropertyInfo)m).GetValue(owner, null);
                return Format(v);
            }
            catch (Exception ex) { return "<threw " + Inner(ex).GetType().Name + ">"; }
        }

        // ------------------------------------------------------------------
        // get / set / call

        public string Get(object target, Type staticType, string path, List<string> into)
        {
            object value;
            Type type;
            string err = Walk(target, staticType, path, out value, out type);
            if (err != null) return err;

            into.Add(path + " = " + Format(value) + (value != null ? "  (" + TypeName(value.GetType()) + ")" : ""));
            AppendItems(value, into);
            return null;
        }

        private void AppendItems(object value, List<string> into)
        {
            if (value == null || value is string) return;

            IDictionary dict = value as IDictionary;
            if (dict != null)
            {
                int n = 0;
                foreach (DictionaryEntry e in dict)
                {
                    if (n++ >= ListItems) { into.Add("  (" + (dict.Count - ListItems) + " more)"); break; }
                    into.Add("  [" + Format(e.Key) + "] " + Format(e.Value));
                }
                return;
            }

            IEnumerable seq = value as IEnumerable;
            if (seq == null) return;
            int i = 0;
            foreach (object item in seq)
            {
                if (i >= ListItems) { into.Add("  (more - index with [n])"); break; }
                into.Add("  [" + i + "] " + Format(item));
                i++;
            }
        }

        public string Set(object target, Type staticType, string path, string text, List<string> into)
        {
            List<BridgeCommand.Step> steps = new List<BridgeCommand.Step>();
            string err;
            if (!BridgeCommand.TryParsePath(path, steps, out err)) return err;
            if (steps[steps.Count - 1].Index >= 0) return "set cannot write a list element (" + path + ")";

            // Keep every owner on the way, so a struct changed on the way
            // down (a Vector3 inside a field) is written back up.
            List<object> owners = new List<object>();
            List<MemberInfo> members = new List<MemberInfo>();
            object cur;
            int first;
            err = Start(target, staticType, steps, out cur, out first);
            if (err != null) return err;

            for (int i = first; i < steps.Count; i++)
            {
                MemberInfo m = FindMember(cur, i == first ? staticType : null, steps[i].Name);
                if (m == null) return "no field or property '" + steps[i].Name + "' on " + OwnerName(cur, staticType);
                owners.Add(cur);
                members.Add(m);
                if (i == steps.Count - 1) break;

                object next;
                err = Read(m, cur, out next);
                if (err != null) return err;
                if (steps[i].Index >= 0)
                {
                    err = Index(next, steps[i].Index, out next);
                    if (err != null) return err;
                    if (next != null && next.GetType().IsValueType) return "set cannot write through a list of structs";
                }
                if (IsNull(next)) return steps[i].Name + " is null";
                cur = next;
                staticType = null;
            }

            MemberInfo last = members[members.Count - 1];
            Type want = MemberType(last);
            object value;
            err = Convert(text, want, out value);
            if (err != null) return err;

            object before;
            Read(last, owners[owners.Count - 1], out before);
            err = Write(last, owners[owners.Count - 1], value);
            if (err != null) return err;

            for (int i = owners.Count - 1; i > 0; i--)
            {
                if (!owners[i].GetType().IsValueType) break;
                err = Write(members[i - 1], owners[i - 1], owners[i]);
                if (err != null) return "set, but writing the struct back failed: " + err;
            }

            object after;
            Read(last, owners[owners.Count - 1], out after);
            into.Add(path + ": " + Format(before) + " -> " + Format(after));
            return null;
        }

        public string Call(object target, Type staticType, string path, List<string> args, MonoBehaviour runner, List<string> into)
        {
            List<BridgeCommand.Step> steps = new List<BridgeCommand.Step>();
            string err;
            if (!BridgeCommand.TryParsePath(path, steps, out err)) return err;

            object owner;
            Type ownerStatic = staticType;
            string methodName = steps[steps.Count - 1].Name;
            if (steps.Count == 1)
            {
                int first;
                err = Start(target, staticType, steps, out owner, out first);
                if (err != null) return err;
                if (first == 1) return "call needs a method after '" + steps[0].Name + "'";
            }
            else
            {
                Type unused;
                err = Walk(target, staticType, JoinSteps(steps, steps.Count - 1), out owner, out unused);
                if (err != null) return err;
                if (IsNull(owner)) return "the method's owner is null";
                ownerStatic = null;
            }

            Type t = ownerStatic ?? owner.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                                 (ownerStatic != null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);
            List<string> tried = new List<string>();
            for (Type c = t; c != null; c = c.BaseType)
            {
                MethodInfo[] ms = c.GetMethods(flags);
                for (int i = 0; i < ms.Length; i++)
                {
                    MethodInfo m = ms[i];
                    if (m.Name != methodName || m.IsGenericMethodDefinition) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != args.Count) { tried.Add(m.Name + Signature(m)); continue; }

                    object[] values = new object[ps.Length];
                    string convErr = null;
                    for (int k = 0; k < ps.Length && convErr == null; k++)
                        convErr = Convert(args[k], ps[k].ParameterType, out values[k]);
                    if (convErr != null) { tried.Add(m.Name + Signature(m) + ": " + convErr); continue; }

                    object result;
                    try { result = m.Invoke(m.IsStatic ? null : owner, values); }
                    catch (Exception ex)
                    {
                        Exception inner = Inner(ex);
                        return m.Name + " threw " + inner.GetType().Name + ": " + BridgeCommand.OneLine(inner.Message, 300);
                    }

                    // A coroutine method does nothing until started.
                    IEnumerator routine = result as IEnumerator;
                    MonoBehaviour host = owner as MonoBehaviour;
                    if (routine != null && typeof(IEnumerator).IsAssignableFrom(m.ReturnType))
                    {
                        MonoBehaviour on = host != null && host.isActiveAndEnabled ? host : runner;
                        on.StartCoroutine(routine);
                        into.Add(m.Name + Signature(m) + ": started as a coroutine on " +
                                 (on == host ? Format(host) : "the overlay (owner inactive)"));
                        return null;
                    }

                    into.Add(m.Name + Signature(m) + (m.ReturnType == typeof(void) ? ": done" : " returned " + Format(result)));
                    AppendItems(result, into);
                    return null;
                }
            }

            if (tried.Count == 0) return "no method '" + methodName + "' on " + t.FullName;
            return "no overload of " + methodName + " takes these " + args.Count + " argument(s); candidates: " +
                   string.Join(" | ", tried.ToArray());
        }

        public string Destroy(object target, List<string> into)
        {
            GameObject go = AsGameObject(target);
            if (go == null) return "destroy needs a GameObject";
            into.Add("destroyed " + Handle(go) + " " + PathOf(go.transform));
            UnityEngine.Object.Destroy(go);
            return null;
        }

        // ------------------------------------------------------------------
        // Path walking

        private static string JoinSteps(List<BridgeCommand.Step> steps, int count)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(steps[i].Name);
                if (steps[i].Index >= 0) sb.Append('[').Append(steps[i].Index).Append(']');
            }
            return sb.ToString();
        }

        /// The value at `path` from the target (or static type).
        public string Walk(object target, Type staticType, string path, out object value, out Type type)
        {
            value = null;
            type = null;
            List<BridgeCommand.Step> steps = new List<BridgeCommand.Step>();
            string err;
            if (!BridgeCommand.TryParsePath(path, steps, out err)) return err;

            object cur;
            int first;
            err = Start(target, staticType, steps, out cur, out first);
            if (err != null) return err;

            for (int i = first; i < steps.Count; i++)
            {
                if (i > first && IsNull(cur)) return JoinSteps(steps, i) + " is null";
                MemberInfo m = FindMember(cur, i == first ? staticType : null, steps[i].Name);
                if (m == null) return "no field or property '" + steps[i].Name + "' on " + OwnerName(cur, i == first ? staticType : null);
                err = Read(m, cur, out cur);
                if (err != null) return err;
                if (steps[i].Index >= 0)
                {
                    err = Index(cur, steps[i].Index, out cur);
                    if (err != null) return JoinSteps(steps, i + 1) + ": " + err;
                }
            }
            value = cur;
            type = cur != null ? cur.GetType() : null;
            return null;
        }

        /// For a GameObject target the first step names a component
        /// ("GameObject" = the object itself; [n] picks the nth of that
        /// type). Returns the step index the member walk starts at.
        private string Start(object target, Type staticType, List<BridgeCommand.Step> steps, out object cur, out int first)
        {
            cur = target;
            first = 0;
            if (staticType != null) return null;

            GameObject go = target as GameObject;
            if (go == null) return null;

            BridgeCommand.Step s = steps[0];
            first = 1;
            if (s.Name == "GameObject" || s.Name == "gameObject") { cur = go; return null; }

            Component[] comps = go.GetComponents<Component>();
            int seen = 0;
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null || !TypeMatches(comps[i].GetType(), s.Name)) continue;
                if (seen++ == Math.Max(0, s.Index)) { cur = comps[i]; return null; }
            }
            return seen > 0
                ? go.name + " has " + seen + " " + s.Name + " component(s), not " + (s.Index + 1)
                : go.name + " has no component '" + s.Name + "' (inspect lists them; GameObject = the object itself)";
        }

        private static bool TypeMatches(Type t, string name)
        {
            for (Type c = t; c != null && c != typeof(Component); c = c.BaseType)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) || c.FullName == name) return true;
            return false;
        }

        private static MemberInfo FindMember(object owner, Type staticType, string name)
        {
            Type t = staticType ?? (owner != null ? owner.GetType() : null);
            if (t == null) return null;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                                 (staticType != null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);

            for (int pass = 0; pass < 2; pass++)
            {
                for (Type c = t; c != null; c = c.BaseType)
                {
                    FieldInfo[] fs = c.GetFields(flags);
                    for (int i = 0; i < fs.Length; i++)
                        if (NameIs(fs[i].Name, name, pass)) return fs[i];
                    PropertyInfo[] ps = c.GetProperties(flags);
                    for (int i = 0; i < ps.Length; i++)
                        if (NameIs(ps[i].Name, name, pass) && ps[i].GetIndexParameters().Length == 0) return ps[i];
                }
            }
            return null;
        }

        private static bool NameIs(string actual, string wanted, int pass)
        {
            return pass == 0 ? actual == wanted : string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase);
        }

        private static string OwnerName(object owner, Type staticType)
        {
            if (staticType != null) return staticType.FullName + " (static)";
            return owner != null ? owner.GetType().FullName : "null";
        }

        private static Type MemberType(MemberInfo m)
        {
            FieldInfo f = m as FieldInfo;
            return f != null ? f.FieldType : ((PropertyInfo)m).PropertyType;
        }

        private static string Read(MemberInfo m, object owner, out object value)
        {
            value = null;
            try
            {
                FieldInfo f = m as FieldInfo;
                if (f != null) { value = f.GetValue(f.IsStatic ? null : owner); return null; }
                PropertyInfo p = (PropertyInfo)m;
                MethodInfo get = p.GetGetMethod(true);
                if (get == null) return p.Name + " has no getter";
                value = get.Invoke(get.IsStatic ? null : owner, null);
                return null;
            }
            catch (Exception ex) { return m.Name + " threw " + Inner(ex).GetType().Name + ": " + BridgeCommand.OneLine(Inner(ex).Message, 200); }
        }

        private static string Write(MemberInfo m, object owner, object value)
        {
            try
            {
                FieldInfo f = m as FieldInfo;
                if (f != null)
                {
                    if (f.IsLiteral) return f.Name + " is a constant";
                    f.SetValue(f.IsStatic ? null : owner, value);
                    return null;
                }
                PropertyInfo p = (PropertyInfo)m;
                MethodInfo set = p.GetSetMethod(true);
                if (set == null) return p.Name + " has no setter";
                set.Invoke(set.IsStatic ? null : owner, new object[] { value });
                return null;
            }
            catch (Exception ex) { return m.Name + " threw " + Inner(ex).GetType().Name + ": " + BridgeCommand.OneLine(Inner(ex).Message, 200); }
        }

        private static string Index(object list, int index, out object item)
        {
            item = null;
            if (list == null) return "null";
            IList il = list as IList;
            if (il != null)
            {
                if (index >= il.Count) return "index " + index + " past the end (" + il.Count + ")";
                item = il[index];
                return null;
            }
            IEnumerable seq = list as IEnumerable;
            if (seq == null || list is string) return list.GetType().Name + " is not a list";
            int i = 0;
            foreach (object o in seq)
            {
                if (i++ == index) { item = o; return null; }
            }
            return "index " + index + " past the end (" + i + ")";
        }

        // ------------------------------------------------------------------
        // Values

        public string Convert(string text, Type t, out object value)
        {
            value = null;
            try
            {
                if (text == "null")
                {
                    if (t.IsValueType) return t.Name + " cannot be null";
                    return null;
                }
                if (t == typeof(string) || t == typeof(object)) { value = text; return null; }
                if (t == typeof(bool))
                {
                    bool b;
                    if (!BridgeCommand.TryParseBool(text, out b)) return "'" + text + "' is not true/false";
                    value = b;
                    return null;
                }
                if (t == typeof(Vector3))
                {
                    Vector3 v;
                    if (!BridgeCommand.TryParseVector3(text, out v)) return "'" + text + "' is not x,y,z";
                    value = v;
                    return null;
                }
                if (t == typeof(Vector2))
                {
                    Vector2 v;
                    if (!BridgeCommand.TryParseVector2(text, out v)) return "'" + text + "' is not x,y";
                    value = v;
                    return null;
                }
                if (t == typeof(Quaternion))
                {
                    Vector3 e;
                    if (!BridgeCommand.TryParseVector3(text, out e)) return "'" + text + "' is not euler x,y,z";
                    value = Quaternion.Euler(e);
                    return null;
                }
                if (t.IsEnum)
                {
                    value = Enum.Parse(t, text, true);
                    return null;
                }
                if (t.IsPrimitive || t == typeof(decimal))
                {
                    value = System.Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
                    return null;
                }
                if (typeof(UnityEngine.Object).IsAssignableFrom(t) && text.StartsWith("#"))
                {
                    object o;
                    Type unused;
                    string err = ResolveTarget(text, out o, out unused);
                    if (err != null) return err;
                    if (t.IsInstanceOfType(o)) { value = o; return null; }
                    GameObject go = AsGameObject(o);
                    if (t == typeof(GameObject) && go != null) { value = go; return null; }
                    if (typeof(Component).IsAssignableFrom(t) && go != null)
                    {
                        Component c = go.GetComponent(t);
                        if (c == null) return text + " has no " + t.Name;
                        value = c;
                        return null;
                    }
                    return text + " is not a " + t.Name;
                }
            }
            catch (Exception ex) { return "'" + text + "' is not a " + t.Name + " (" + Inner(ex).Message + ")"; }
            return "cannot write a " + t.FullName + " from text";
        }

        public string Format(object v)
        {
            if (v == null) return "null";

            UnityEngine.Object uo = v as UnityEngine.Object;
            if (!ReferenceEquals(uo, null))
            {
                if (uo == null) return "null (destroyed)";
                GameObject go = uo as GameObject;
                if (go != null) return Handle(go) + " '" + go.name + "'";
                Component c = uo as Component;
                if (c != null) return Handle(c.gameObject) + " '" + c.gameObject.name + "' (" + c.GetType().Name + ")";
                return "'" + uo.name + "' (" + uo.GetType().Name + ")";
            }

            string s = v as string;
            if (s != null) return "\"" + BridgeCommand.OneLine(s, ValueChars) + "\"";
            if (v is float) return ((float)v).ToString("0.####", CultureInfo.InvariantCulture);
            if (v is double) return ((double)v).ToString("0.####", CultureInfo.InvariantCulture);
            if (v is Vector3) return Vec((Vector3)v);
            if (v is Vector2) { Vector2 v2 = (Vector2)v; return "(" + F(v2.x) + ", " + F(v2.y) + ")"; }
            if (v is Quaternion) return "euler " + Vec(((Quaternion)v).eulerAngles);
            if (v is bool || v is Enum || v.GetType().IsPrimitive) return System.Convert.ToString(v, CultureInfo.InvariantCulture);

            Array arr = v as Array;
            if (arr != null) return TypeName(v.GetType().GetElementType()) + "[" + arr.Length + "]";
            ICollection col = v as ICollection;
            if (col != null) return TypeName(v.GetType()) + " (" + col.Count + ")";
            if (v is Delegate) return "delegate " + TypeName(v.GetType());

            string text;
            try { text = v.ToString(); }
            catch (Exception) { text = null; }
            if (text == null || text == v.GetType().FullName || text == v.GetType().ToString()) return "{" + TypeName(v.GetType()) + "}";
            return BridgeCommand.OneLine(text, ValueChars);
        }

        private static bool IsNull(object v)
        {
            if (v == null) return true;
            UnityEngine.Object uo = v as UnityEngine.Object;
            return !ReferenceEquals(uo, null) && uo == null;
        }

        private static GameObject AsGameObject(object o)
        {
            GameObject go = o as GameObject;
            if (go != null) return go;
            Component c = o as Component;
            return c != null ? c.gameObject : null;
        }

        private static Exception Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        public static string PathOf(Transform t)
        {
            StringBuilder sb = new StringBuilder(t.name);
            int guard = 0;
            for (Transform p = t.parent; p != null && guard < 40; p = p.parent, guard++) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        public static string Vec(Vector3 v)
        {
            return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        }

        private static string F(float f)
        {
            return f.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
