using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The text side of the dev dumps and exports: one-line values, the
    // tab-separated files' cleaners, filenames, parameter lists, and a
    // PlayMaker field's value as the FSM export writes it.
    //
    // WHY here: the FSM exports in docs/fsm are read by tech research and
    // the knowledge bot, so their format must not drift; the rest of the
    // dump code needs the live game and cannot be tested.
    //
    // Pure, so it is linked into the tests. PlayMaker is reached by type
    // and member names (it is not referenced); the engine's own values
    // (vectors, Unity objects, curves) come back from the caller's
    // `engine` hook - Game/FsmExport supplies it.
    // ------------------------------------------------------------------
    public static class DumpText
    {
        public const int MaxArray = 24;
        public const int MaxText = 200;

        /// Line breaks to spaces, cut at MaxText with "...".
        public static string OneLine(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > MaxText ? s.Substring(0, MaxText) + "..." : s;
        }

        /// A field of a tab-separated record: "-" when empty, no tabs,
        /// line breaks or ';' (the list separator).
        public static string Clean(string s)
        {
            return string.IsNullOrEmpty(s) ? "-" : s.Replace('\t', ' ').Replace('\n', ' ').Replace(';', ',');
        }

        /// A number in a dump file: invariant, up to 5 decimals.
        public static string Num(float v) { return v.ToString("0.#####", CultureInfo.InvariantCulture); }

        /// A dump's filename part: invalid characters, dots and spaces to
        /// '_', at most 40 characters; "all" when empty.
        public static string FilenameSafe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "all";
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < bad.Length; i++)
                s = s.Replace(bad[i], '_');
            s = s.Replace('.', '_').Replace(' ', '_');
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        /// A type's short name, "?" when null or unloadable.
        public static string TypeName(Type t)
        {
            if (t == null) return "?";
            try { return t.Name; }
            catch (Exception) { return "?"; }
        }

        /// "Type name, Type name", "?" when the parameters cannot load.
        public static string ParamList(MethodInfo m)
        {
            try
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0) return "";

                string s = "";
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) s += ", ";
                    s += TypeName(ps[i].ParameterType) + " " + ps[i].Name;
                }
                return s;
            }
            catch (Exception) { return "?"; }
        }

        /// A property (no index) or field by name, public or not; null when
        /// missing or throwing.
        public static object Member(object o, string member)
        {
            if (o == null) return null;
            Type t = o.GetType();
            try
            {
                PropertyInfo p = t.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
                FieldInfo f = t.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(o);
            }
            catch (Exception) { }
            return null;
        }

        /// An FSM action field's value in one line: an FSM variable
        /// reference reads {name}. `engine` returns the text of an engine
        /// value, or null when `v` is not one.
        public static string FsmValue(object v, int depth, Func<object, string> engine)
        {
            if (v == null) return "null";
            if (v is string) return "\"" + OneLine((string)v) + "\"";
            if (v is float) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (v is bool || v is int || v is Enum) return v.ToString();
            string e = engine != null ? engine(v) : null;
            if (e != null) return e;

            Type t = v.GetType();
            string tn = t.Name;
            if (tn == "FsmEvent") return "event " + Member(v, "Name");
            if (tn == "FsmOwnerDefault")
            {
                object opt = Member(v, "OwnerOption");
                return opt != null && opt.ToString() == "UseOwner" ? "owner" : "gameobject " + FsmValue(Member(v, "GameObject"), depth + 1, engine);
            }
            if (tn == "FsmEventTarget")
            {
                string target = "" + Member(v, "target");
                if (target == "Self" || target == "BroadcastAll" || target == "HostFSM" || target == "SubFSMs") return "to " + target;
                string fsmName = FsmValue(Member(v, "fsmName"), depth + 1, engine);
                return "to " + target + " " + FsmValue(Member(v, "gameObject"), depth + 1, engine) +
                       (fsmName != null && fsmName != "\"\"" && fsmName != "none" ? " fsm " + fsmName : "") +
                       (FsmValue(Member(v, "sendToChildren"), depth + 1, engine) == "True" ? " (+children)" : "");
            }
            if (tn == "FsmProperty") return "property " + Member(v, "PropertyName") + " of " + FsmValue(Member(v, "TargetObject"), depth + 1, engine);
            if (tn == "FunctionCall") return "call " + Member(v, "FunctionName") + " (" + Member(v, "ParameterType") + ")";
            if (tn.StartsWith("Fsm") && t.GetProperty("UseVariable") != null)
            {
                string name = Member(v, "Name") as string;
                if (true.Equals(Member(v, "UseVariable")) && !string.IsNullOrEmpty(name)) return "{" + name + "}";
                if (true.Equals(Member(v, "IsNone"))) return "none";
                return depth > 1 ? tn : FsmValue(Member(v, "RawValue") ?? Member(v, "Value"), depth + 1, engine);
            }

            IEnumerable list = v as IEnumerable;
            if (list != null && depth < 2)
            {
                StringBuilder sb = new StringBuilder("[");
                int n = 0;
                foreach (object x in list)
                {
                    if (n == MaxArray) { sb.Append(", ..."); break; }
                    if (n > 0) sb.Append(", ");
                    sb.Append(FsmValue(x, depth + 1, engine));
                    n++;
                }
                return sb.Append(']').ToString();
            }
            if (t.IsValueType) return OneLine(v.ToString());
            return tn;
        }
    }
}
