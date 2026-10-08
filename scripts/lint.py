"""Machine checks for the project's checkable rules (docs/harness.md 5a).

Every failure prints WHAT / WHY / FIX, so the fix is in the message.

    python scripts/lint.py                    # all checks (CI, the pre-commit hook)
    python scripts/lint.py --tag v0.24.N [--rev SHA]
                                              # a tag about to be pushed (pre-push hook)
    python scripts/lint.py --update-baseline  # accept today's heuristic hits

The baselined heuristics (a fixed 20 px GUI.Label with variable text, an
allocation in an OnGUI / DrawTab body, a Resources.FindObjectsOfTypeAll
call site) cannot tell a list row, a constant or a one-off probe from a bug, so the hits that existed when they were added sit in
scripts/lint-baseline.txt and only new ones fail (author, 2026-10-07).
Fix a baselined line and its entry goes stale; --update-baseline drops it.
The community index is checked by CommunityPacksTests, not here.
Every gotcha index line in docs/areas/*.md ends with [check: <name>],
[check: T-n] (the task building it) or [judgement] (docs/harness.md 5d).
The log catalogue (scripts/log-catalogue.py --check, gotcha 16's check):
every log call has a prefix and docs/log-lines.md is current, with a
meaning per prefix.
The quality document (docs/quality.md, docs/harness.md 10d): grades A-D,
an area's grade the worst of its four, a C / D row names an open task,
every tracked file falls under some area's Paths.
Unity message methods (gotcha 3): every Awake / Update / OnRenderObject /
... on a MonoBehaviour in src/ is wrapped in try / catch, after plain
guards only (author, 2026-10-07: wrap all, no baseline).
Code that reads a UnityWebRequest also checks responseCode (gotcha 15).
Every OnRenderObject in src/ checks DrawTarget.ShouldDraw() (gotcha 12).
scripts/deploy.ps1 copies the plugin DLL only (gotcha 10).
Text files (gotcha 9): no tracked file holds PowerShell 5.1 mojibake.
"""
import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_spec = importlib.util.spec_from_file_location("log_catalogue", os.path.join(ROOT, "scripts", "log-catalogue.py"))
log_catalogue = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(log_catalogue)
CSPROJ = "ForestOverlay.csproj"
PLUGIN = "src/Plugin.cs"
CHANGELOG = "CHANGELOG.md"
BASELINE = os.path.join(ROOT, "scripts", "lint-baseline.txt")
# Top-level folders the plugin build never sees: build output, git, and
# src/ itself (the one folder the plugin is made of).
NOT_PROJECTS = {"src", "bin", "obj", ".git", "node_modules"}
REMOVE_KINDS = ("Compile", "None", "EmbeddedResource")


class Problem(object):
    def __init__(self, what, why, fix):
        self.what, self.why, self.fix = what, why, fix

    def __str__(self):
        return "ERROR: %s\nWHY: %s\nFIX: %s" % (self.what, self.why, self.fix)


def read(path, root=ROOT):
    with open(os.path.join(root, path), encoding="utf-8-sig") as f:
        return f.read()


# ---------------------------------------------------------------- versions

def csproj_versions(text):
    """{'Version': '0.24.1', 'AssemblyVersion': ..., 'FileVersion': ...} from the csproj."""
    out = {}
    for tag in ("Version", "AssemblyVersion", "FileVersion"):
        m = re.search(r"<%s>([^<]+)</%s>" % (tag, tag), text)
        if m:
            out[tag] = m.group(1).strip()
    return out


def plugin_version(text):
    m = re.search(r'PluginVersion\s*=\s*"([^"]+)"', text)
    return m.group(1) if m else None


def changelog_has(text, version):
    return re.search(r"^## v%s(\s|$)" % re.escape(version), text, re.M) is not None


def check_versions(csproj, plugin, changelog, tag=None):
    """The csproj, Plugin.PluginVersion and CHANGELOG agree (and match `tag` if given)."""
    probs = []
    v = csproj_versions(csproj)
    ver = v.get("Version")
    if not ver:
        return [Problem("no <Version> in %s" % CSPROJ, "the release version lives there",
                        "python scripts/bump.py 0.24.N \"bullet\" (it writes every version field)")]
    for tag_name in ("AssemblyVersion", "FileVersion"):
        if v.get(tag_name) != ver + ".0":
            probs.append(Problem(
                "%s %s is %s, <Version> is %s" % (CSPROJ, tag_name, v.get(tag_name), ver),
                "the game and the updater read the assembly's version; it must match the release",
                "set <%s>%s.0</%s>, or redo the bump with scripts/bump.py" % (tag_name, ver, tag_name)))
    pv = plugin_version(plugin)
    if pv != ver:
        probs.append(Problem(
            "%s PluginVersion is %s, %s <Version> is %s" % (PLUGIN, pv, CSPROJ, ver),
            "the HUD title and BepInEx show PluginVersion; runners would see the wrong version (router rule 3)",
            "release with scripts/bump.py, which writes both; by hand: PluginVersion = \"%s\"" % ver))
    if not changelog_has(changelog, ver):
        probs.append(Problem(
            "%s has no \"## v%s\" section" % (CHANGELOG, ver),
            "CI publishes the tag's section as the release notes and fails the release without one (router rule 3)",
            "python scripts/bump.py %s \"bullet\" writes it; or add \"## v%s - <date>\" with the runner-facing lines" % (ver, ver)))
    if tag is not None and tag != "v" + ver:
        probs.append(Problem(
            "tag %s points at a commit whose %s <Version> is %s" % (tag, CSPROJ, ver),
            "the tag publishes this commit's DLL; its version must be the tag (gotcha 65: an empty release)",
            "tag the commit scripts/bump.py %s made, or delete the tag: git tag -d %s" % (tag[1:], tag)))
    return probs


# ---------------------------------------------------------------- csproj Remove

def removed_folders(csproj):
    """{'Compile': {'tools', ...}, ...} - the top-level folders each Remove line names."""
    out = {}
    for kind in REMOVE_KINDS:
        m = re.search(r'<%s\s+Remove="([^"]*)"' % kind, csproj)
        names = set()
        for part in (m.group(1).split(";") if m else []):
            part = part.strip().replace("\\", "/")
            if part.endswith("/**"):
                names.add(part[:-3].rstrip("/"))
        out[kind] = names
    return out


def project_folders(root=ROOT):
    """Top-level folders with a .cs or .csproj anywhere below (the plugin's glob would take them)."""
    out = []
    for name in sorted(os.listdir(root)):
        path = os.path.join(root, name)
        if name in NOT_PROJECTS or not os.path.isdir(path):
            continue
        if _has_cs(path):
            out.append(name)
    return out


def _has_cs(path):
    for dirpath, dirs, files in os.walk(path):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj", "node_modules", ".git")]
        if any(f.endswith((".cs", ".csproj")) for f in files):
            return True
    return False


def check_removes(csproj, folders):
    removed = removed_folders(csproj)
    probs = []
    for folder in folders:
        missing = [k for k in REMOVE_KINDS if folder not in removed[k]]
        if missing:
            probs.append(Problem(
                "%s/ holds C# but is not in %s's %s Remove line(s)" % (folder, CSPROJ, ", ".join(missing)),
                "the net35 plugin project globs every .cs under the repo; CI broke on bot/ this way (gotcha 92)",
                "add %s/** to the Compile, None and EmbeddedResource Remove lines in %s, then build the plugin" % (folder, CSPROJ)))
    return probs


# ---------------------------------------------------------------- gotcha markers

GOTCHAS = "docs/gotchas.md"
AREAS = "docs/areas"
TASKS = "tasks/tasks.jsonl"
MARKER = re.compile(r"\[(check: ([^\]]+)|judgement)\]\s*$")
CLOSED = ("confirmed", "wontfix")


def gotcha_numbers(text):
    return [int(n) for n in re.findall(r"^(\d+)\. \*\*", text, re.M)]


def index_lines(root=ROOT):
    """[(path, number, line)] - the numbered lines of every area doc's ## Gotchas section."""
    out = []
    folder = os.path.join(root, AREAS)
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".md"):
            continue
        path = "%s/%s" % (AREAS, name)
        sec = read(path, root).split("\n## Gotchas", 1)
        if len(sec) < 2:
            continue
        for line in re.split(r"\n## ", sec[1])[0].splitlines():
            m = re.match(r"(\d+)\. ", line)
            if m:
                out.append((path, int(m.group(1)), line.rstrip()))
    return out


def task_statuses(root=ROOT):
    out = {}
    with open(os.path.join(root, TASKS), encoding="utf-8-sig") as f:
        for line in f:
            if line.strip():
                t = json.loads(line)
                out[t["id"]] = t["status"]
    return out


def check_gotchas(numbers, index, statuses):
    """Every gotcha has one index line, and every index line says how it is checked (docs/harness.md 5d)."""
    probs = []
    where = {}
    for path, n, line in index:
        where.setdefault(n, []).append(path)
        m = MARKER.search(line)
        if not m:
            probs.append(Problem(
                "gotcha %d's index line in %s has no [check: ...] or [judgement] marker" % (n, path),
                "a lesson a lint, test or log assertion can catch ships with that check (docs/harness.md 5d)",
                "end the line with [check: <lint / test name>], [check: T-n] after tasks.py add for the check, or [judgement]"))
            continue
        for tid in re.findall(r"T-\d{4}", m.group(2) or ""):
            st = statuses.get(tid)
            if st is None:
                probs.append(Problem("gotcha %d's marker names %s, which is not in %s" % (n, tid, TASKS),
                                     "the marker points at the task that builds the check",
                                     "fix the id, or file the check: python scripts/tasks.py add ..."))
            elif st in CLOSED:
                probs.append(Problem("gotcha %d's marker names %s, which is %s" % (n, tid, st),
                                     "a done task has built its check (or dropped it); the index names the check itself",
                                     "replace %s with the check's name (lint.py <check> / <TestName>), or [judgement] if it was dropped" % tid))
    for n in numbers:
        if n not in where:
            probs.append(Problem("gotcha %d has no index line in %s/*.md" % (n, AREAS),
                                 "the area indexes are how a session finds a lesson (one line each)",
                                 "add \"%d. **<lesson>** - <one line> [check: ...]\" under ## Gotchas in its area doc" % n))
    for n, paths in sorted(where.items()):
        if len(paths) > 1:
            probs.append(Problem("gotcha %d is indexed %d times (%s)" % (n, len(paths), ", ".join(paths)),
                                 "one home per fact (router rule 11)", "keep the line in the area it belongs to"))
        if n not in numbers:
            probs.append(Problem("%s indexes gotcha %d, which %s does not have" % (paths[0], n, GOTCHAS),
                                 "numbers are stable and cited; an index line points at a full entry",
                                 "add the entry to %s or fix the number" % GOTCHAS))
    return probs


# ---------------------------------------------------------------- quality document

QUALITY = "docs/quality.md"
GRADES = "ABCD"
# Not graded on their own: docs/ and tasks/ are each area's legibility, the
# test projects its evidence, and root files are listed where they belong.
NOT_GRADED = re.compile(r"^(docs/|tasks/|scripts/tests/|tests/ForestOverlay\.Tests/|(?:[^/]+/)?[^/]+\.Tests/|[^/]+$)")


def glob_re(pattern):
    """A Paths glob as a regex: * within a folder, ** across, {a,b} either, a trailing / the whole folder."""
    out, i = "", 0
    while i < len(pattern):
        if pattern.startswith("**", i):
            out, i = out + ".*", i + 2
        elif pattern[i] == "*":
            out, i = out + "[^/]*", i + 1
        elif pattern[i] == "{" and "}" in pattern[i:]:
            j = pattern.index("}", i)
            out, i = out + "(?:%s)" % "|".join(re.escape(x) for x in pattern[i + 1:j].split(",")), j + 1
        else:
            out, i = out + re.escape(pattern[i]), i + 1
    return re.compile(out + (".*" if pattern.endswith("/") else "") + "$")


def quality_doc(text):
    """(rows, paths) from docs/quality.md: rows = [{area, grade, dims, tasks, reviewed}] from the
    ## Grades table, paths = {area: [glob]} from each ### section's Paths: line."""
    rows = []
    sec = text.split("\n## Grades", 1)
    if len(sec) == 2:
        for line in re.split(r"\n## ", sec[1])[0].splitlines():
            if not line.startswith("|"):
                continue
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            if len(cells) != 8 or cells[0] == "Area" or not cells[0].strip("-: "):
                continue
            rows.append({"area": cells[0], "grade": cells[1], "dims": cells[2:6],
                         "tasks": re.findall(r"T-\d{4}", cells[6]), "reviewed": cells[7]})
    paths, area = {}, None
    for line in text.splitlines():
        if line.startswith("## "):
            area = None
        elif line.startswith("### "):
            area = line[4:].strip()
        elif area and line.startswith("Paths:"):
            paths[area] = re.findall(r"`([^`]+)`", line)
    return rows, paths


def tracked_files(root=ROOT):
    out = subprocess.run(["git", "ls-files"], cwd=root, capture_output=True, text=True, encoding="utf-8", check=True)
    return [f for f in out.stdout.splitlines() if f]


def check_quality(rows, paths, statuses, files):
    """docs/quality.md (docs/harness.md 10d): grades A-D, the area's grade the worst of its four,
    a C / D row names an open task, every area has its paths, every tracked file is in an area."""
    fix_row = "edit the row in %s's ## Grades table" % QUALITY
    if not rows:
        return [Problem("%s has no ## Grades table rows" % QUALITY,
                        "the quality document is how a session knows where the project is weak (docs/harness.md 10d)",
                        "put back the ## Grades table: | Area | Grade | Verification | Legibility | Stability | Gaps | Tasks | Reviewed |")]
    probs = []
    for r in rows:
        a = r["area"]
        bad = [g for g in [r["grade"]] + r["dims"] if g not in GRADES or len(g) != 1]
        if bad:
            probs.append(Problem("%s: grade %s is not one of A, B, C, D" % (a, ", ".join(repr(b) for b in bad)),
                                 "each dimension and the area are graded A-D (%s *How a grade is set*)" % QUALITY, fix_row))
            continue
        worst = max(r["dims"])
        if r["grade"] != worst:
            probs.append(Problem("%s is graded %s, but its worst dimension is %s" % (a, r["grade"], worst),
                                 "an area's grade is the worst of its four (author, 2026-10-07)",
                                 "set %s's Grade to %s, or re-grade the dimension" % (a, worst)))
        open_tasks = [t for t in r["tasks"] if statuses.get(t) not in (None,) + CLOSED]
        if r["grade"] in "CD" and not open_tasks:
            probs.append(Problem("%s is graded %s and names no open task" % (a, r["grade"]),
                                 "a C or D row feeds the task list: some open task works on it (docs/harness.md 10d)",
                                 "file one (python scripts/tasks.py add ...) and put its id in the row's Tasks cell"))
        for t in r["tasks"]:
            if t not in statuses:
                probs.append(Problem("%s names %s, which is not in %s" % (a, t, TASKS),
                                     "the Tasks cell points at the work on the row's gaps", "fix the id"))
            elif statuses[t] in CLOSED:
                probs.append(Problem("%s names %s, which is %s" % (a, t, statuses[t]),
                                     "a finished task may have closed a gap: the row's grades are out of date",
                                     "re-grade %s (its evidence, grades, Reviewed) and drop %s from Tasks" % (a, t)))
        if not re.match(r"\d{4}-\d{2}-\d{2}$", r["reviewed"]):
            probs.append(Problem("%s: Reviewed %r is not a date" % (a, r["reviewed"]),
                                 "session-start.py compares it with the area's last change", "write YYYY-MM-DD"))
    names = set(r["area"] for r in rows)
    for a in sorted(names - set(paths)):
        probs.append(Problem("%s has no ### %s section with a Paths: line" % (a, a),
                             "an area's paths say which files it grades and when its row is stale",
                             "add \"### %s\" under ## Areas with Paths: `glob` `glob` ..." % a))
    for a in sorted(set(paths) - names):
        probs.append(Problem("### %s has paths but no row in ## Grades" % a,
                             "every area is graded", "add its row to the ## Grades table"))
    for a, globs in sorted(paths.items()):
        for g in globs:
            rx = glob_re(g)
            if not any(rx.match(f) for f in files):
                probs.append(Problem("%s's path `%s` matches no tracked file" % (a, g),
                                     "a moved or deleted file leaves its area grading nothing there",
                                     "fix or drop the path in ### %s" % a))
    every = [glob_re(g) for globs in paths.values() for g in globs]
    loose = [f for f in files if not NOT_GRADED.match(f) and not any(rx.match(f) for rx in every)]
    if loose:
        probs.append(Problem("%d tracked file(s) in no area of %s: %s%s" % (
                                 len(loose), QUALITY, ", ".join(loose[:8]), " ..." if len(loose) > 8 else ""),
                             "every part of the project is graded, so new work cannot hide (author, 2026-10-07)",
                             "add them to the Paths of the area they belong to, or a new area (a row + a ### section)"))
    return probs


def stale_areas(rows, paths, changes):
    """[(area, n)] - areas with n changed files after their Reviewed date; changes = [(YYYY-MM-DD, path)]."""
    out = []
    for r in rows:
        rxs = [glob_re(g) for g in paths.get(r["area"], [])]
        hit = set(p for d, p in changes if d > r["reviewed"] and any(rx.match(p) for rx in rxs))
        if hit:
            out.append((r["area"], len(hit)))
    return out


# ---------------------------------------------------------------- Unity message methods

# Unity calls these by name on a MonoBehaviour; an exception escaping one
# logs every frame (Update, OnRenderObject) or leaves the object half set
# up (Awake, OnEnable) - for the plugin, while BepInEx still says "loaded".
MESSAGES = ("Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "Update", "LateUpdate", "FixedUpdate",
            "OnGUI", "OnRenderObject", "OnApplicationQuit", "OnApplicationFocus", "OnApplicationPause",
            "OnPreCull", "OnPreRender", "OnPostRender", "OnRenderImage", "OnWillRenderObject",
            "OnBecameVisible", "OnBecameInvisible", "OnLevelWasLoaded", "OnValidate", "Reset",
            "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit", "OnCollisionEnter", "OnCollisionStay",
            "OnCollisionExit")
MESSAGE_SIG = re.compile(r"^\s*(?:(?:private|public|protected|internal|override|virtual|new)\s+)*"
                         r"(?:void|IEnumerator)\s+(" + "|".join(MESSAGES) + r")\s*\([^)]*\)")
CLASS_DECL = re.compile(r"\bclass\s+(\w+)([^{]*)")
UNITY_BASE = re.compile(r":.*\b(MonoBehaviour|BaseUnityPlugin)\b")
# A plain guard before the try: null / bool checks only, no call (author, 2026-10-07).
GUARD = re.compile(r"^if\s*\(([\w.!=<>&|\s]*)\)\s*return\s*;\s*")


STRING_LIT = re.compile(r'@"(?:[^"]|"")*"|\$?"(?:[^"\\]|\\.)*"|\'(?:[^\'\\]|\\.)+\'')


# Strings, // comments and /* */ comments in one scan, so a "/*" inside a
# string or after a // is not a comment start.
COMMENT_OR_STRING = re.compile(r'@"(?:[^"]|"")*"|\$?"(?:[^"\\\n]|\\.)*"|\'(?:[^\'\\]|\\.)+\'|//[^\n]*|/\*.*?\*/', re.S)


def strip_block_comments(text):
    """The text with every /* */ comment blanked to spaces (newlines kept, so line numbers hold): a brace in one is not a brace (T-0187)."""
    def blank(m):
        t = m.group(0)
        return re.sub(r"[^\n]", " ", t) if t.startswith("/*") else t
    return COMMENT_OR_STRING.sub(blank, text)


def blank_strings(line):
    """The line with every string / char literal's inside blanked (same length), so a '{' in one is not a brace."""
    return STRING_LIT.sub(lambda m: m.group(0)[0] + " " * (len(m.group(0)) - 2) + m.group(0)[-1], line)


def _block_end(code, i):
    """Index just past the {...} block starting at code[i] == '{' (code with strings blanked; -1 if unbalanced)."""
    depth = 0
    for j in range(i, len(code)):
        if code[j] == "{":
            depth += 1
        elif code[j] == "}":
            depth -= 1
            if depth == 0:
                return j + 1
    return -1


def body_wrapped(body):
    """True when a method body (the text between its braces) is guards, then one try / catch / finally and nothing after."""
    rest = body.strip()
    if not rest:
        return True
    m = GUARD.match(rest)
    while m:
        rest = rest[m.end():]
        m = GUARD.match(rest)
    if not rest.startswith("try"):
        return False
    rest = rest[3:].lstrip()
    if not rest.startswith("{"):
        return False
    end = _block_end(rest, 0)
    if end < 0:
        return False
    rest = rest[end:].lstrip()
    handlers = 0
    while True:
        m = re.match(r"(catch\b\s*(\([^)]*\))?|finally\b)\s*", rest)
        if not m:
            break
        rest = rest[m.end():]
        if not rest.startswith("{"):
            return False
        end = _block_end(rest, 0)
        if end < 0:
            return False
        rest = rest[end:].lstrip()
        handlers += 1
    return handlers > 0 and rest == ""


def message_methods(text):
    """(class, method, line) of every Unity message method on a MonoBehaviour / BaseUnityPlugin, and its body text (None: no block body)."""
    code = [blank_strings(strip_comment(l)) for l in strip_block_comments(text).splitlines()]
    out = []
    # (class name, or None when not a MonoBehaviour; the brace depth of its body):
    # a method belongs to the class whose body it sits directly in, so a
    # nested class neither hides nor takes over the outer class's methods.
    classes = []
    pending = False
    depth = 0
    for i, line in enumerate(code):
        c = CLASS_DECL.search(line)
        if c:
            pending, name = True, (c.group(1) if UNITY_BASE.search(c.group(2)) else None)
        m = MESSAGE_SIG.search(line)
        if m and classes and classes[-1][0] is not None and classes[-1][1] == depth:
            joined = "\n".join(code[i:])
            tail = joined[len(m.group(0)):].lstrip()
            if not tail.startswith("{"):
                out.append((classes[-1][0], m.group(1), i + 1, None))   # => expression body or abstract
            else:
                end = _block_end(tail, 0)
                out.append((classes[-1][0], m.group(1), i + 1, tail[1:end - 1] if end > 0 else None))
        for ch in line:
            if ch == "{":
                depth += 1
                if pending:
                    classes.append((name, depth))
                    pending = False
            elif ch == "}":
                if classes and classes[-1][1] == depth:
                    classes.pop()
                depth -= 1
    return out


def lifecycle_hits(root=ROOT):
    hits = []
    for dirpath, dirs, files in os.walk(os.path.join(root, "src")):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            full = os.path.join(dirpath, f)
            rel = os.path.relpath(full, root).replace("\\", "/")
            with open(full, encoding="utf-8-sig") as fh:
                text = fh.read()
            for cls, method, n, body in message_methods(text):
                if body is None or not body_wrapped(body):
                    hits.append((rel, n, cls, method))
    return hits


def check_lifecycle(hits):
    return [Problem("%s.%s in %s:%d is not wrapped in try / catch" % (cls, method, path, n),
                    "Unity calls it by name: a throw escapes every frame (Update, OnRenderObject) or leaves the object "
                    "half set up (Awake, OnEnable) - a throwing Awake once killed the plugin while BepInEx said "
                    "\"loaded\" (gotcha 3)",
                    "put the whole body in try { ... } catch (Exception ex) { Lifecycle.Fail(\"%s.%s\", ex); } "
                    "(Core/Lifecycle logs it once); only plain guards (if (x == null) return;) may stay before the try"
                    % (cls, method))
            for path, n, cls, method in hits]


# ---------------------------------------------------------------- OnRenderObject

# OnRenderObject methods that draw nothing, so there is no view to pick.
RENDER_NO_DRAW = {
    ("src/Game/LatePass.cs", "LatePass"): "only records the camera's render target for the late pass; draws nothing",
}


def src_texts(root=ROOT):
    """[(path, text)] of every .cs under src/."""
    out = []
    for dirpath, dirs, files in os.walk(os.path.join(root, "src")):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
        for f in sorted(files):
            if f.endswith(".cs"):
                full = os.path.join(dirpath, f)
                with open(full, encoding="utf-8-sig") as fh:
                    out.append((os.path.relpath(full, root).replace("\\", "/"), fh.read()))
    return out


def render_hits(path, text):
    """(path, line, class) of every OnRenderObject that never calls DrawTarget.ShouldDraw() (gotcha 12)."""
    out = []
    for cls, method, n, body in message_methods(text):
        if method != "OnRenderObject" or (path, cls) in RENDER_NO_DRAW:
            continue
        if body is None or not re.search(r"\bDrawTarget\.ShouldDraw\s*\(\s*\)", body):
            out.append((path, n, cls))
    return out


def check_render(hits):
    return [Problem("%s.OnRenderObject in %s:%d does not check DrawTarget.ShouldDraw()" % (cls, path, n),
                    "OnRenderObject runs once per camera - reflections and UI included; without the check a GL overlay is "
                    "drawn into every view (gotcha 12)",
                    "start the body with if (!DrawTarget.ShouldDraw()) return; (inside the try); if it draws nothing, "
                    "add (file, class) to RENDER_NO_DRAW in scripts/lint.py with the reason")
            for path, n, cls in hits]


# ---------------------------------------------------------------- UnityWebRequest

def web_request_hits(path, text):
    """[(path, first line)] when a src/ file reads a UnityWebRequest's body (downloadHandler) but never looks at
    responseCode (gotcha 15). The type is reached by reflection, so its name is in a string: only comments are cut."""
    code = [(n, strip_comment(l)) for n, l in enumerate(strip_block_comments(text).splitlines(), 1)]
    uses = [n for n, c in code if "UnityWebRequest" in c]
    reads = any("downloadHandler" in c for n, c in code)
    if uses and reads and not any("responseCode" in c for n, c in code):
        return [(path, uses[0])]
    return []


def check_web_requests(hits):
    return [Problem("%s:%d reads a UnityWebRequest but never checks responseCode" % (path, n),
                    "Unity 5.6's UnityWebRequest does not treat a 404 as an error: the 'Not Found' body arrives as "
                    "ordinary data (gotcha 15)",
                    "read the request's responseCode (see Core/WebRequest.cs) and treat anything but 200 as a failure "
                    "before using the body")
            for path, n in hits]


# ---------------------------------------------------------------- UI heuristics

LABEL_20 = re.compile(r'GUI\.Label\(\s*new\s+Rect\((?:[^()]|\([^()]*\))*,\s*20f?\s*\)\s*,\s*(?=[^"\s])')
DRAW_METHOD = re.compile(r"\bvoid\s+(OnGUI|DrawTab)\s*\([^)]*\)\s*(\{.*)?$")
# `new` of a struct allocates nothing; everything else does.
STRUCTS = r"(?:Rect|Vector2|Vector3|Vector4|Color|Color32|Quaternion|Matrix4x4|KeyValuePair<[^>]*>)"
ALLOC = [
    (re.compile(r"\bnew\s+(?!%s\s*\()[A-Za-z_][\w.<>,\[\] ]*[\(\[{]" % STRUCTS), "new"),
    # A literal joined to a non-literal; "a" + "b" (also across lines) is a constant.
    (re.compile(r'"\s*\+\s*[\w(]|[\w)\]]\s*\+\s*"'), "string +"),
    (re.compile(r'\$"|\bstring\.(Format|Concat|Join)\('), "string building"),
    (re.compile(r"\.ToString\("), "ToString"),
    (re.compile(r"(?<!Mathf)\.(Select|Where|OrderBy|OrderByDescending|Any|All|First|FirstOrDefault|ToList|ToArray|Count|Sum|Max|Min)\("), "LINQ"),
]


def strip_comment(line):
    """The line without a trailing // comment (not inside a string)."""
    in_str = False
    i = 0
    while i < len(line):
        c = line[i]
        if c == '"' and (i == 0 or line[i - 1] != "\\"):
            in_str = not in_str
        elif not in_str and line.startswith("//", i):
            return line[:i]
        i += 1
    return line


def label_hits(path, text):
    hits = []
    for n, line in enumerate(text.splitlines(), 1):
        code = strip_comment(line)
        if LABEL_20.search(code):
            hits.append(("label20", path, n, line.strip()))
    return hits


def draw_bodies(text):
    """(first line, last line) of every OnGUI / DrawTab body (1-based, inclusive)."""
    lines = text.splitlines()
    out = []
    i = 0
    while i < len(lines):
        if DRAW_METHOD.search(strip_comment(lines[i]).rstrip()):
            depth, start, j = 0, None, i
            while j < len(lines):
                code = strip_comment(lines[j])
                for c in code:
                    if c == "{":
                        depth += 1
                        if start is None:
                            start = j
                    elif c == "}":
                        depth -= 1
                if start is not None and depth == 0:
                    break
                j += 1
            if start is not None and j > start:
                out.append((start + 2, j))  # the lines between the braces
            i = j + 1
        else:
            i += 1
    return out


def alloc_hits(path, text):
    lines = text.splitlines()
    hits = []
    for first, last in draw_bodies(text):
        for n in range(first, last + 1):
            code = strip_comment(lines[n - 1])
            for rx, _ in ALLOC:
                if rx.search(code):
                    hits.append(("alloc", path, n, lines[n - 1].strip()))
                    break
    return hits


FIND_ALL = re.compile(r"\bResources\.FindObjectsOfTypeAll\s*\(")


def code_lines(text):
    """[(line number, code)] - the text without /* */ and // comments and with string contents blanked."""
    return [(n, blank_strings(strip_comment(l))) for n, l in enumerate(strip_block_comments(text).splitlines(), 1)]


def findall_hits(path, text):
    """Every Resources.FindObjectsOfTypeAll call site (gotcha 11, T-0125); the hits that existed sit in the baseline."""
    lines = text.splitlines()
    return [("findall", path, n, lines[n - 1].strip()) for n, code in code_lines(text) if FIND_ALL.search(code)]


def source_hits(path, text):
    """The baselined source-text rules for one src/ file."""
    return findall_hits(path, text)


def ui_hits(root=ROOT):
    hits = []
    for dirpath, dirs, files in os.walk(os.path.join(root, "src")):
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            full = os.path.join(dirpath, f)
            rel = os.path.relpath(full, root).replace("\\", "/")
            with open(full, encoding="utf-8-sig") as fh:
                text = fh.read()
            hits.extend(label_hits(rel, text))
            hits.extend(alloc_hits(rel, text))
            hits.extend(source_hits(rel, text))
    return hits


UI_TEXT = {
    "label20": ("GUI.Label at a fixed 20 px with variable text",
                "variable text in a 20 px label is cut off or clipped (the UiText rule, src/CLAUDE.md; reported three times)",
                "y += UiText.Draw(x, y, w, text) - it wraps and returns the height; a constant or a virtualised list row is fine: "
                "then python scripts/lint.py --update-baseline"),
    "alloc": ("an allocation inside an OnGUI / DrawTab body",
              "OnGUI runs several times a frame; garbage there is a GC hitch (src/CLAUDE.md: never allocate in DrawTab/OnGUI)",
              "build the string in Tick (throttled) and keep it / its GUIContent in a field; a false positive (no allocation): "
              "python scripts/lint.py --update-baseline"),
    "findall": ("a Resources.FindObjectsOfTypeAll call",
                "it walks every loaded object (22-25 ms in ForestMain); on a refresh it was a visible stutter (gotcha 11)",
                "find once and keep it (Game/SceneCache), rate-limit the re-search, or use the game's static handle; a one-off "
                "dump / probe that runs on a command: python scripts/lint.py --update-baseline"),
}


def baseline_key(hit):
    return "%s\t%s\t%s" % (hit[0], hit[1], hit[3])


def load_baseline(path=BASELINE):
    counts = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.rstrip("\n")
                if line and not line.startswith("#"):
                    counts[line] = counts.get(line, 0) + 1
    return counts


def new_hits(hits, baseline):
    """Hits beyond the baseline's count for the same (rule, file, line text); and stale entries."""
    left = dict(baseline)
    fresh = []
    for h in hits:
        k = baseline_key(h)
        if left.get(k, 0) > 0:
            left[k] -= 1
        else:
            fresh.append(h)
    stale = sum(v for v in left.values() if v > 0)
    return fresh, stale


def check_ui(hits, baseline):
    fresh, stale = new_hits(hits, baseline)
    probs = []
    for rule, path, n, text in fresh:
        what, why, fix = UI_TEXT[rule]
        probs.append(Problem("%s in %s:%d: %s" % (what, path, n, text), why, fix))
    return probs, stale


def write_baseline(hits, path=BASELINE):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# scripts/lint.py: UI heuristic hits accepted on purpose (rule, file, line text).\n"
                "# Written by `python scripts/lint.py --update-baseline` - new code fixes the line instead.\n")
        for k in sorted(baseline_key(h) for h in hits):
            f.write(k + "\n")


# ---------------------------------------------------------------- PowerShell mojibake

# What Windows PowerShell 5.1 leaves when it reads BOM-less UTF-8 as cp1252 and
# writes it back (gotcha 9, T-0123): an em dash becomes a-circumflex, euro, quote.
# Spelled with escapes so this file does not trip its own check.
MOJIBAKE = re.compile("\u00e2\u20ac"
                      "|\u00c3[\u0080-\u00bf\u0152\u0153\u0160\u0161\u0178\u017d\u017e\u0192\u02c6\u02dc\u2013-\u203a\u20ac\u2122]"
                      "|\u00c2[\u00a0-\u00bf]")
# Files that quote the garbled form to warn about it.
MOJIBAKE_QUOTED = {
    "CLAUDE.md": "the rule that names it",
    "docs/gotchas.md": "gotcha 9 shows the garbled form and the grep that finds it",
    "docs/areas/workflow.md": "the gotcha 9 index line shows the garbled form",
}


def tracked_text(root=ROOT):
    """{path: text} of every tracked file that is UTF-8 text."""
    out = {}
    for f in tracked_files(root):
        try:
            with open(os.path.join(root, f), "rb") as fh:
                raw = fh.read()
        except OSError:
            continue
        if b"\0" in raw:
            continue
        try:
            out[f] = raw.decode("utf-8")
        except UnicodeDecodeError:
            continue
    return out


def check_mojibake(files):
    """No tracked text file holds the garbled UTF-8 a PowerShell 5.1 round trip leaves (gotcha 9)."""
    probs = []
    for path, text in sorted(files.items()):
        if path in MOJIBAKE_QUOTED:
            continue
        lines = [n for n, l in enumerate(text.splitlines(), 1) if MOJIBAKE.search(l)]
        if lines:
            probs.append(Problem(
                "%s has PowerShell mojibake on line(s) %s" % (path, ", ".join(str(n) for n in lines[:6]) + (" ..." if len(lines) > 6 else "")),
                "Windows PowerShell 5.1 read the UTF-8 file as cp1252 and wrote it back: every em dash became "
                "three garbled characters (gotcha 9)",
                "restore the file from git (git checkout -- %s) and redo the edit with the Edit tool or a Python script "
                "with encoding=\"utf-8\"; never Get-Content | Set-Content" % path))
    return probs


# ---------------------------------------------------------------- deploy script

DEPLOY = "scripts/deploy.ps1"
PS_COPY = re.compile(r"(?:^|[;|(]|\s)(Copy-Item|Move-Item|xcopy|robocopy|cp|copy|Expand-Archive)\b(.*)$", re.I)


def ps_code(text):
    """A PowerShell script without <# #> blocks and # comments (the script holds no '#' in a string)."""
    text = re.sub(r"<#.*?#>", lambda m: re.sub(r"[^\n]", " ", m.group(0)), text, flags=re.S)
    return [re.sub(r"#.*$", "", l) for l in text.splitlines()]


def check_deploy(text):
    """scripts/deploy.ps1 copies the plugin DLL and nothing else (gotcha 10): runners only get what is in the DLL."""
    code = ps_code(text)
    probs = []
    why = ("a file only deploy.ps1 copies is missing for every runner, who installs the DLL alone (gotcha 10: "
           "the 100% list did exactly that)")
    fix = "embed the data in the DLL (src/Data/ShippedData.cs, an EmbeddedResource) and keep deploy.ps1 copying $dll only"
    dll_var = None
    for l in code:
        m = re.match(r"\s*\$(\w+)\s*=.*ForestOverlay\.dll", l)
        if m:
            dll_var = m.group(1)
    copies = 0
    for n, l in enumerate(code, 1):
        m = PS_COPY.search(l)
        if not m:
            continue
        copies += 1
        args = [a for a in m.group(2).replace(",", " ").split() if not a.startswith("-") or a.lower() == "-path"]
        args = [a for a in args if a.lower() != "-path"]
        src = args[0] if args else ""
        if dll_var is None or src.strip("\"'") != "$" + dll_var:
            probs.append(Problem("%s:%d copies %s, not the plugin DLL: %s" % (DEPLOY, n, src or "?", l.strip()), why, fix))
    if copies == 0:
        probs.append(Problem("%s has no Copy-Item of the plugin DLL" % DEPLOY,
                             "the script's one job is to put ForestOverlay.dll in BepInEx/plugins",
                             "copy the built ForestOverlay.dll with Copy-Item $dll $pluginDir"))
    return probs


# ---------------------------------------------------------------- main

def git_show(rev, path):
    return subprocess.run(["git", "show", "%s:%s" % (rev, path)], cwd=ROOT, capture_output=True,
                          text=True, encoding="utf-8", check=True).stdout.lstrip("﻿")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--tag", help="check a tag about to be pushed (vX.Y.Z)")
    ap.add_argument("--rev", default="HEAD", help="the commit the tag points at (with --tag)")
    ap.add_argument("--update-baseline", action="store_true")
    a = ap.parse_args(argv)

    if a.tag:
        probs = check_versions(git_show(a.rev, CSPROJ), git_show(a.rev, PLUGIN),
                               git_show(a.rev, CHANGELOG), tag=a.tag)
        for p in probs:
            print(p, file=sys.stderr)
        if not probs:
            print("lint: tag %s ok" % a.tag)
        return 1 if probs else 0

    hits = ui_hits()
    if a.update_baseline:
        write_baseline(hits)
        print("lint: baseline holds %d hit(s)" % len(hits))
        return 0

    csproj = read(CSPROJ)
    probs = check_versions(csproj, read(PLUGIN), read(CHANGELOG))
    probs += check_removes(csproj, project_folders())
    probs += check_gotchas(gotcha_numbers(read(GOTCHAS)), index_lines(), task_statuses())
    probs += log_catalogue.check()
    probs += check_quality(*quality_doc(read(QUALITY)), statuses=task_statuses(), files=tracked_files())
    probs += check_lifecycle(lifecycle_hits())
    probs += check_web_requests([h for p, t in src_texts() for h in web_request_hits(p, t)])
    probs += check_render([h for p, t in src_texts() for h in render_hits(p, t)])
    probs += check_mojibake(tracked_text())
    probs += check_deploy(read(DEPLOY))
    ui, stale = check_ui(hits, load_baseline())
    probs += ui
    for p in probs:
        print(p, file=sys.stderr)
        print(file=sys.stderr)
    if stale:
        print("lint: %d baseline entr%s fixed - python scripts/lint.py --update-baseline drops them"
              % (stale, "y" if stale == 1 else "ies"))
    if probs:
        print("lint: %d problem(s)" % len(probs), file=sys.stderr)
        return 1
    print("lint: ok (%d baselined hit(s), all accepted)" % len(hits))
    return 0


if __name__ == "__main__":
    sys.exit(main())
