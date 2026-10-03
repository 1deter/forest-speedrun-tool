using System.Text;
using System.Text.RegularExpressions;

namespace ForestBot.Code;

// ------------------------------------------------------------------
// The game's decompiled C# (ILSpy project output, one file per type,
// file-scoped namespaces, tab indents, braces on their own lines - kept
// privately, never in the repo; docs/knowledge-bot.md *Decompiled code*).
// Indexed by type and member so the model can read one method's body,
// a type's outline, or every line mentioning a name (string-started
// calls like SendMessage("openDoorRoutine") included - gotcha 13).
// ------------------------------------------------------------------
public sealed class CodeIndex
{
    public sealed class TypeEntry
    {
        public string Name;        // "ElevatorSystem" (nested: "ElevatorSystem.MessageToGo")
        public string Namespace;   // "TheForest.World" or ""
        public string File;        // relative path
        public int Line;           // 0-based declaration line
        public int Indent;
        public string Declaration;
        public List<Member> Members = new List<Member>();
        public string FullName => Namespace.Length > 0 ? Namespace + "." + Name : Name;
    }

    public sealed class Member
    {
        public TypeEntry Type;
        public string Name;
        public string Signature;
        public int Start;          // 0-based, the declaration line
        public int End;            // 0-based, inclusive
        public bool HasBody;
    }

    private readonly Dictionary<string, string[]> _files = new Dictionary<string, string[]>(StringComparer.Ordinal);
    public readonly List<TypeEntry> Types = new List<TypeEntry>();
    private readonly Dictionary<string, List<TypeEntry>> _byName = new Dictionary<string, List<TypeEntry>>(StringComparer.OrdinalIgnoreCase);

    public int FileCount => _files.Count;

    private static readonly Regex TypeDecl = new Regex(
        @"^(\t*)(?:(?:public|private|internal|protected|sealed|abstract|static|partial|unsafe|readonly|new)\s+)*(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);
    private static readonly Regex Namespace = new Regex(@"^namespace\s+([A-Za-z0-9_.]+)\s*;", RegexOptions.Compiled);
    private static readonly Regex MethodName = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^()]*>)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex LastIdent = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*(?:=.*)?;?\s*$", RegexOptions.Compiled);

    public static CodeIndex Load(string folder, Action<string> log)
    {
        CodeIndex idx = new CodeIndex();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            log?.Invoke("Code: no decompiled source at '" + folder + "' - code tools off");
            return idx;
        }
        foreach (string path in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (rel.StartsWith("obj/") || rel.StartsWith("Properties/")) continue;
            idx.AddFile(rel, File.ReadAllText(path));
        }
        log?.Invoke("Code: " + idx._files.Count + " files, " + idx.Types.Count + " types");
        return idx;
    }

    /// Indexes one file (exposed for tests).
    public void AddFile(string rel, string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        _files[rel] = lines;
        string ns = "";
        List<TypeEntry> stack = new List<TypeEntry>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Match nm = Namespace.Match(line);
            if (nm.Success) { ns = nm.Groups[1].Value; continue; }

            int indent = Tabs(line);
            // A type's own closing brace (member bodies are skipped whole below).
            if (stack.Count > 0 && line == new string('\t', stack[^1].Indent) + "}") { stack.RemoveAt(stack.Count - 1); continue; }

            Match tm = TypeDecl.Match(line);
            if (tm.Success && tm.Groups[1].Value.Length == indent)
            {
                TypeEntry t = new TypeEntry
                {
                    Name = (stack.Count > 0 ? stack[^1].Name + "." : "") + tm.Groups[3].Value,
                    Namespace = ns, File = rel, Line = i, Indent = indent, Declaration = line.Trim(),
                };
                Types.Add(t);
                AddName(tm.Groups[3].Value, t);
                if (stack.Count > 0) AddName(t.Name, t);
                stack.Add(t);
                continue;
            }

            if (stack.Count == 0) continue;
            TypeEntry owner = stack[^1];
            if (indent != owner.Indent + 1) continue;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed == "{" || trimmed == "}" || trimmed.StartsWith("[") || trimmed.StartsWith("//")) continue;

            Member m = new Member { Type = owner, Start = i, End = i, Signature = trimmed };
            string open = new string('\t', indent) + "{", close = new string('\t', indent) + "}";
            if (i + 1 < lines.Length && lines[i + 1] == open)
            {
                int end = i + 2;
                while (end < lines.Length && lines[end] != close) end++;
                m.End = Math.Min(end, lines.Length - 1);
                m.HasBody = true;
            }
            else if (trimmed.Contains("=>"))
            {
                int end = i;
                while (end < lines.Length - 1 && !lines[end].TrimEnd().EndsWith(";")) end++;
                m.End = end;
                m.HasBody = true;
            }
            m.Name = NameOf(trimmed, owner);
            if (m.Name == null) continue;
            owner.Members.Add(m);
            if (m.HasBody) i = m.End;
        }
    }

    private static string NameOf(string sig, TypeEntry owner)
    {
        string head = sig;
        int arrow = head.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0) head = head.Substring(0, arrow);
        Match mm = MethodName.Match(head);
        if (mm.Success && head.IndexOf('(') >= 0 && (head.IndexOf('=') < 0 || head.IndexOf('(') < head.IndexOf('=')))
        {
            string n = mm.Groups[1].Value;
            if (n == "this") return "this[]";
            return n;
        }
        // field / property / event: the last identifier before "=", ";" or the end
        int eq = head.IndexOf('=');
        if (eq >= 0) head = head.Substring(0, eq);
        Match li = LastIdent.Match(head.Trim().TrimEnd(';', ',').Trim());
        return li.Success ? li.Groups[1].Value : null;
    }

    private void AddName(string name, TypeEntry t)
    {
        if (!_byName.TryGetValue(name, out List<TypeEntry> list)) _byName[name] = list = new List<TypeEntry>();
        list.Add(t);
    }

    private static int Tabs(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == '\t') n++;
        return n;
    }

    /// Types by short, nested ("Outer.Inner") or full name.
    public List<TypeEntry> FindTypes(string name)
    {
        name = (name ?? "").Trim();
        if (_byName.TryGetValue(name, out List<TypeEntry> list)) return list;
        List<TypeEntry> found = new List<TypeEntry>();
        foreach (TypeEntry t in Types)
            if (string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase)) found.Add(t);
        return found;
    }

    /// "Type.Member", "Namespace.Type.Member" or "Type": the member's code
    /// (every overload), or the type's outline. A useful message when
    /// nothing matches.
    public string Read(string target, int maxChars = 16000)
    {
        if (_files.Count == 0) return "The game's code is not available on this server.";
        target = (target ?? "").Trim().Replace("::", ".").TrimEnd('(', ')');
        if (target.Length == 0) return "Give a type or Type.Member.";

        List<TypeEntry> types = FindTypes(target);
        if (types.Count > 0) return Limit(string.Join("\n\n", types.Take(3).Select(Outline)), maxChars);

        int dot = target.LastIndexOf('.');
        if (dot > 0)
        {
            string typeName = target.Substring(0, dot), member = target.Substring(dot + 1);
            StringBuilder b = new StringBuilder();
            foreach (TypeEntry t in FindTypes(typeName))
                foreach (Member m in t.Members)
                    if (string.Equals(m.Name, member, StringComparison.OrdinalIgnoreCase))
                    {
                        if (b.Length > 0) b.Append("\n\n");
                        b.Append("// ").Append(t.FullName).Append(" - ").Append(t.File).Append(':').Append(m.Start + 1).Append('\n');
                        string[] lines = _files[t.File];
                        for (int i = m.Start; i <= m.End; i++) b.Append(Dedent(lines[i], t.Indent + 1)).Append('\n');
                    }
            if (b.Length > 0) return Limit(b.ToString(), maxChars);
            List<TypeEntry> owners = FindTypes(typeName);
            if (owners.Count > 0)
                return "No member '" + member + "' in " + owners[0].FullName + ". Its outline:\n\n" + Limit(Outline(owners[0]), maxChars);
        }
        return "Nothing called '" + target + "' in the game's code. Try code_search.";
    }

    /// A type's declaration and its members' signatures (fields with their
    /// initial values), with line numbers.
    public string Outline(TypeEntry t)
    {
        StringBuilder b = new StringBuilder();
        b.Append("// ").Append(t.File).Append(':').Append(t.Line + 1).Append('\n');
        if (t.Namespace.Length > 0) b.Append("namespace ").Append(t.Namespace).Append(";\n");
        b.Append(t.Declaration).Append('\n');
        foreach (Member m in t.Members)
            b.Append("  ").Append(m.Signature).Append(m.HasBody && !m.Signature.Contains("=>") ? "  { ... lines " + (m.Start + 1) + "-" + (m.End + 1) + " }" : "").Append('\n');
        foreach (TypeEntry nested in Types)
            if (nested.File == t.File && nested.Name.StartsWith(t.Name + ".") && nested.Name.IndexOf('.', t.Name.Length + 1) < 0)
                b.Append("  nested: ").Append(nested.Declaration).Append('\n');
        return b.ToString();
    }

    /// Names first (types, then members, exact before partial), then every
    /// line containing the text - each with the member it is in.
    public string Search(string query, int maxResults = 30)
    {
        if (_files.Count == 0) return "The game's code is not available on this server.";
        query = (query ?? "").Trim();
        if (query.Length < 2) return "Search for at least 2 characters.";
        StringBuilder b = new StringBuilder();
        int n = 0;

        string needle = query.Replace("::", ".");
        string lastPart = needle.Contains('.') ? needle.Substring(needle.LastIndexOf('.') + 1) : needle;
        List<(int rank, string line)> names = new List<(int, string)>();
        foreach (TypeEntry t in Types)
        {
            int r = Rank(t.Name, needle);
            if (r >= 0) names.Add((r, "type " + t.FullName + "  (" + t.File + ")"));
            foreach (Member m in t.Members)
            {
                int rm = Rank(m.Name, lastPart);
                if (rm >= 0 && (needle == lastPart || Rank(t.Name, needle.Substring(0, needle.LastIndexOf('.'))) >= 0))
                    names.Add((rm + 1, t.Name + "." + m.Name + "  " + Short(m.Signature)));
            }
        }
        names.Sort((a, c) => a.rank.CompareTo(c.rank));
        if (names.Count > 0) b.Append("Names:\n");
        foreach ((int _, string line) in names)
        {
            if (n++ >= maxResults / 2) { b.Append("  ...\n"); break; }
            b.Append("  ").Append(line).Append('\n');
        }

        int lines = 0;
        StringBuilder text = new StringBuilder();
        foreach (TypeEntry t in Types)
        {
            if (t.Name.Contains('.')) continue;   // nested types share the file - searched once
            string[] fileLines = _files[t.File];
            for (int i = 0; i < fileLines.Length && lines < maxResults; i++)
                if (fileLines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    text.Append("  ").Append(Where(t.File, i)).Append(": ").Append(Short(fileLines[i].Trim())).Append('\n');
                    lines++;
                }
            if (lines >= maxResults) { text.Append("  ... (more - narrow the search)\n"); break; }
        }
        if (text.Length > 0) b.Append("Lines containing '").Append(query).Append("':\n").Append(text);
        return b.Length == 0 ? "Nothing in the game's code matches '" + query + "'." : b.ToString();
    }

    /// "Type.Member" for a line, or the file:line.
    private string Where(string file, int line)
    {
        foreach (TypeEntry t in Types)
        {
            if (t.File != file) continue;
            foreach (Member m in t.Members)
                if (line >= m.Start && line <= m.End) return t.Name + "." + m.Name + " (" + file + ":" + (line + 1) + ")";
        }
        return file + ":" + (line + 1);
    }

    private static int Rank(string name, string query)
    {
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (query.Length >= 4 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 4;
        return -1;
    }

    private static string Short(string s) => s.Length <= 160 ? s : s.Substring(0, 157) + "...";

    private static string Dedent(string line, int tabs)
    {
        int n = 0;
        while (n < tabs && n < line.Length && line[n] == '\t') n++;
        return line.Substring(n).Replace("\t", "    ");
    }

    private static string Limit(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "\n... (cut at " + max + " characters - ask for a narrower member)";
}
