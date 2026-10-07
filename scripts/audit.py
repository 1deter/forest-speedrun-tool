"""The weekly cleanup's finder (docs/harness.md 10e, T-0014). Read-only unless --file.

    python scripts/audit.py            # the findings, by kind
    python scripts/audit.py --file     # + one P4 task per (kind, file) that has no open one
    python scripts/audit.py --json     # the findings as JSON (the skill weekly-cleanup)

Kinds (author, 2026-10-07: dead code is "anything redundant that ... serves no
purpose / is no longer implemented or needed"):
  doc-path   a markdown link or a `repo/path` in a doc that points at nothing
  baseline   a scripts/lint-baseline.txt entry whose line is gone from its file
  cs-unused  a C# method, field, property, constant or type named nowhere else
  py-unused  a Python function named nowhere else
  orphan     a script, tool file, site asset or shipped data file no other file names
  ignore     a scripts/audit-ignore.txt entry that matches nothing any more
  quality    a docs/quality.md row whose paths changed after its review - the skill
             re-grades it (docs/harness.md decision 6); never filed as a task

"Named nowhere else" counts words over every tracked text file except history
(CHANGELOG, the session log, the generated task view, reviews, sent test lists,
the task file), so a member the docs call over the bridge is not dead. It is a
heuristic: the skill weekly-cleanup checks each candidate before filing, and a
checked false positive goes in scripts/audit-ignore.txt with its reason
(`kind<TAB>file<TAB>name<TAB>reason`, name `*` = the whole file).
"""
import argparse
import collections
import fnmatch
import importlib.util
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lint  # noqa: E402  (the quality doc and the UI baseline helpers)

IGNORE = os.path.join(ROOT, "scripts", "audit-ignore.txt")
KINDS = ("doc-path", "baseline", "cs-unused", "py-unused", "orphan", "ignore", "quality")
FILED = ("doc-path", "baseline", "cs-unused", "py-unused", "orphan", "ignore")
TEXT_EXT = (".md", ".cs", ".py", ".js", ".html", ".css", ".json", ".yml", ".yaml", ".csproj", ".txt",
            ".ps1", ".sh", ".cshtml", ".toml", ".xml", ".props", ".targets", ".foseg", ".mcp.json",
            ".gitattributes", ".gitignore")
# Files that record the past: they may name what is gone, and they never keep anything alive.
HISTORY = re.compile(r"^(CHANGELOG\.md|docs/session-log\.md|docs/tasks\.md|docs/bot-reviews/|docs/tests/|tasks/)")
# Lists that name code without using it: they never keep a name alive either.
NOT_A_USE = re.compile(r"^scripts/(audit-ignore|lint-baseline)\.txt$")
# Top folders a `path` in a doc can start with.
TOPS = ("src", "scripts", "docs", "site", "bot", "tools", "tests", "knowledge", "patcher", ".claude",
        ".github", ".githooks", "locations", "collectibles", "community", "qa", "tasks")
# Where an unreferenced file is an orphan (the code folders are covered by cs-unused / py-unused).
ORPHAN_SCOPE = re.compile(r"^(scripts/(?!tests/)|site/ForestSite/wwwroot/|site/ForestSite/GameCode/|site/deploy/|"
                          r"site/ForestSite\.Tests/(?!.*\.cs$)|tools/(?!.*\.(cs|csproj)$)|qa/|community/|"
                          r"locations/|collectibles/)")
ORPHAN_SKIP = re.compile(r"((^|/)(README\.md|CLAUDE\.md|__init__\.py)|\.csproj)$")
AREA_OF = [("src/", "plugin"), ("tests/", "plugin"), ("locations/", "plugin"), ("collectibles/", "plugin"),
           ("qa/", "plugin"), ("tools/", "plugin"), ("patcher/", "release"), ("site/", "site"),
           ("community/", "site"), ("bot/", "bot"), ("knowledge/", "knowledge"), ("docs/", "docs")]

WORD = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
LINK = re.compile(r"\]\(([^)\s]+)\)")
TICK = re.compile(r"`([^`\s]+)`")
PLACEHOLDER = re.compile(r"[<>*{}\[\]$|]|\bN\b|xxxx|\.\.\.|YYYY")

# C#: names the engine, Harmony, xUnit or ASP.NET call by convention.
CS_CONVENTION = {"Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnEnable", "OnDisable",
                 "OnDestroy", "OnApplicationQuit", "OnApplicationFocus", "OnApplicationPause",
                 "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay", "OnCollisionEnter", "OnCollisionExit",
                 "OnLevelWasLoaded", "OnValidate", "OnRenderObject", "OnPostRender", "OnPreRender",
                 "OnRenderImage", "Reset", "Prefix", "Postfix", "Transpiler", "Finalizer", "Prepare",
                 "TargetMethod", "TargetMethods", "Cleanup", "Initialize", "Patch", "Main", "Dispose",
                 "DisposeAsync", "ToString", "Equals", "GetHashCode", "CompareTo", "Configure",
                 "ConfigureServices", "ExecuteAsync", "StartAsync", "StopAsync", "InitializeAsync"}
CS_KEYWORDS = set("if for foreach while switch catch using lock return new else get set add remove value "
                  "nameof typeof sizeof default base this throw await yield case goto in out ref is as "
                  "operator implicit explicit delegate event".split())
CS_MODS = r"(?:(?:public|private|protected|internal|static|readonly|virtual|abstract|override|sealed|async|" \
          r"unsafe|extern|partial|new|const|volatile|required)\s+)"
CS_TYPE = re.compile(r"^\s*" + CS_MODS + r"*(class|struct|enum|interface|record)\s+([A-Za-z_]\w*)")
CS_MEMBER = re.compile(r"^\s*" + CS_MODS + r"*([A-Za-z_][\w<>\[\],.?]*(?:<[^=;(]*>)?)\s+([A-Za-z_]\w*)\s*(\(|=>|=|;|\{)")
CS_SKIP_START = ("//", "*", "/*", "[", "return", "var ", "throw", "if", "else", "using", "namespace", "case",
                 "await", "yield", "#")
PY_DEF = re.compile(r"^\s*(?:async\s+)?def\s+([A-Za-z_]\w*)\s*\(")
PY_CONVENTION = re.compile(r"^(main|__\w+__|test_\w+|setUp|tearDown|setUpClass|tearDownClass|do_[A-Z]+|"
                           r"log_message|default|run)$")


class Finding(dict):
    def __init__(self, kind, file, line, name, why):
        dict.__init__(self, kind=kind, file=file, line=line, name=name, why=why)


# ---------------------------------------------------------------- the repo

def tracked(root=ROOT):
    return lint.tracked_files(root)


def is_text(path):
    return path.endswith(TEXT_EXT) or "/" not in path and "." not in path


def read_texts(files, root=ROOT):
    out = {}
    for f in files:
        if is_text(f):
            try:
                with open(os.path.join(root, f), encoding="utf-8", errors="replace") as h:
                    out[f] = h.read()
            except OSError:
                pass
    return out


def word_counts(texts):
    """Word -> count over every non-history text file (the 'named nowhere else' corpus)."""
    c = collections.Counter()
    for f, t in texts.items():
        if not HISTORY.match(f) and not NOT_A_USE.match(f):
            c.update(WORD.findall(t))
    return c


def known_paths(files):
    fs, dirs = set(files), set()
    for f in files:
        p = f
        while "/" in p:
            p = p.rsplit("/", 1)[0]
            dirs.add(p)
    return fs, dirs


def area_of(path):
    for prefix, area in AREA_OF:
        if path.startswith(prefix):
            return area
    return "harness"


# ---------------------------------------------------------------- doc-path

def path_exists(p, fs, dirs, root=ROOT):
    p = p.rstrip("/")
    if not p or p in fs or p in dirs or os.path.exists(os.path.join(root, p)):
        return True
    # `src/Data/RunCategory` (a type) or `site/ForestSite/Attempts.Judge` (a member): try the file.
    base = p
    for _ in range(3):
        if base + ".cs" in fs or base + ".py" in fs:
            return True
        if "." not in base.rsplit("/", 1)[-1]:
            break
        base = base.rsplit(".", 1)[0]
    return False


def doc_paths(texts, fs, dirs, root=ROOT):
    out = []
    for f, t in sorted(texts.items()):
        if not f.endswith(".md") or HISTORY.match(f) and f != "docs/session-log.md":
            continue
        for n, line in enumerate(t.splitlines(), 1):
            for m in LINK.finditer(line):
                target = m.group(1)
                if re.match(r"^([a-z]+:|#)", target):
                    continue
                target = re.sub(r"#.*$", "", target)
                target = re.sub(r":\d+(-\d+)?$", "", target)
                if not target or PLACEHOLDER.search(target):
                    continue
                p = os.path.normpath(os.path.join(os.path.dirname(f), target)).replace("\\", "/")
                if not path_exists(p, fs, dirs, root):
                    out.append(Finding("doc-path", f, n, target, "link to a missing file (resolved: %s)" % p))
            for m in TICK.finditer(line):
                target = re.sub(r":\d+(-\d+)?$", "", m.group(1))
                if "/" not in target or target.split("/")[0] not in TOPS or PLACEHOLDER.search(target):
                    continue
                if not path_exists(target, fs, dirs, root):
                    out.append(Finding("doc-path", f, n, target, "names a path that does not exist"))
    return out


# ---------------------------------------------------------------- baseline

def stale_baseline(hits, baseline):
    """Baseline entries (rule\\tfile\\ttext) with no hit left to match them."""
    left = dict(baseline)
    for h in hits:
        k = lint.baseline_key(h)
        if left.get(k, 0) > 0:
            left[k] -= 1
    out = []
    for k, v in sorted(left.items()):
        if v > 0:
            rule, path, text = (k.split("\t", 2) + ["", ""])[:3]
            out.append(Finding("baseline", "scripts/lint-baseline.txt", 0, "%s %s" % (rule, path),
                               "accepted line no longer in %s: %s" % (path, text.strip()[:80])))
    return out


# ---------------------------------------------------------------- cs-unused

def is_test_file(path):
    return path.startswith("tests/") or re.search(r"\.Tests/", path) is not None


def cs_declarations(path, text):
    """[(line, kind, name)] - the declarations a word count can judge."""
    out, lines = [], text.splitlines()
    prev = ""
    for i, l in enumerate(lines):
        s = re.sub(r"\s*//.*$", "", l).strip()
        if not s:
            continue
        attributed = prev.startswith("[") and prev.endswith("]")
        prev = s
        if s.startswith(CS_SKIP_START):
            continue
        m = CS_TYPE.match(l)
        if m:
            if not is_test_file(path) and not attributed:
                out.append((i + 1, m.group(1), m.group(2)))
            continue
        m = CS_MEMBER.match(l)
        if not m or attributed or re.search(r"\b(override|extern|operator)\b", l):
            continue
        typ, name = m.group(1), m.group(2)
        if typ in CS_KEYWORDS or name in CS_KEYWORDS or name in CS_CONVENTION:
            continue
        out.append((i + 1, "member", name))
    return out


def camel(name):
    return name[:1].lower() + name[1:]


def cs_unused(texts, counts):
    js = collections.Counter()
    for f, t in texts.items():
        if f.endswith((".js", ".html")):
            js.update(WORD.findall(t))
    out = []
    for f, t in sorted(texts.items()):
        if not f.endswith(".cs") or "/obj/" in f or "/bin/" in f:
            continue
        for n, kind, name in cs_declarations(f, t):
            if counts[name] == 1 and not js[camel(name)]:
                out.append(Finding("cs-unused", f, n, name, "%s named nowhere else" % kind))
    return out


# ---------------------------------------------------------------- py-unused

def py_unused(texts, counts):
    out = []
    for f, t in sorted(texts.items()):
        if not f.endswith(".py"):
            continue
        for n, line in enumerate(t.splitlines(), 1):
            m = PY_DEF.match(line)
            if m and not PY_CONVENTION.match(m.group(1)) and counts[m.group(1)] == 1:
                out.append(Finding("py-unused", f, n, m.group(1), "function named nowhere else"))
    return out


# ---------------------------------------------------------------- orphan

def include_globs(texts):
    """Globs from csproj Include="..." items, as repo paths (qa\\*.txt -> qa/*.txt)."""
    out = []
    for f, t in texts.items():
        if f.endswith(".csproj"):
            base = os.path.dirname(f)
            for g in re.findall(r'Include="([^"]*\*[^"]*)"', t):
                g = g.replace("\\", "/")
                out.append(os.path.normpath(os.path.join(base, g)).replace("\\", "/") if base else g)
    return out


def orphans(files, texts):
    globs = include_globs(texts)
    out = []
    for f in sorted(files):
        if not ORPHAN_SCOPE.match(f) or ORPHAN_SKIP.search(f):
            continue
        if any(fnmatch.fnmatch(f, g) for g in globs):
            continue
        name = f.rsplit("/", 1)[-1]
        stem = name.rsplit(".", 1)[0]
        names = [name] + ([stem] if f.endswith(".py") else [])
        rx = re.compile(r"(?<![\w.-])(%s)(?![\w-])" % "|".join(re.escape(x) for x in names))
        if not any(o != f and not HISTORY.match(o) and rx.search(t) for o, t in texts.items()):
            out.append(Finding("orphan", f, 0, name, "no other tracked file names it"))
    return out


# ---------------------------------------------------------------- quality

def load_session_start():
    spec = importlib.util.spec_from_file_location("session_start", os.path.join(ROOT, "scripts", "session-start.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def stale_quality(root=ROOT):
    rows, paths = lint.quality_doc(lint.read(lint.QUALITY, root))
    if not rows:
        return []
    ss = load_session_start()
    log = subprocess.run(["git", "log", "--since=" + min(r["reviewed"] for r in rows), "--format=@%cs",
                          "--name-only"], cwd=root, capture_output=True, text=True, encoding="utf-8").stdout
    return [Finding("quality", lint.QUALITY, 0, area, "%d file(s) changed after its review - re-grade it" % n)
            for area, n in lint.stale_areas(rows, paths, ss.parse_changes(log))]


# ---------------------------------------------------------------- ignore list

def load_ignore(path=IGNORE):
    """[(kind, file, name, reason)]; raises ValueError on a line without a reason."""
    out = []
    if not os.path.exists(path):
        return out
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.rstrip("\n")
            if not line.strip() or line.startswith("#"):
                continue
            cells = line.split("\t")
            if len(cells) != 4 or not cells[3].strip() or cells[0] not in KINDS:
                raise ValueError("scripts/audit-ignore.txt:%d: want kind<TAB>file<TAB>name<TAB>reason "
                                 "(kind one of %s)" % (n, ", ".join(KINDS)))
            out.append(tuple(c.strip() for c in cells))
    return out


def ignored(finding, entry):
    kind, file, name, _ = entry
    return finding["kind"] == kind and finding["file"] == file and name in ("*", finding["name"])


def apply_ignore(findings, entries):
    kept = [x for x in findings if not any(ignored(x, e) for e in entries)]
    unused = [Finding("ignore", "scripts/audit-ignore.txt", 0, "%s %s %s" % e[:3],
                      "matches no finding any more - delete the line")
              for e in entries if not any(ignored(x, e) for x in findings)]
    return kept + unused


# ---------------------------------------------------------------- all

def audit(root=ROOT, quality=True):
    files = tracked(root)
    texts = read_texts(files, root)
    counts = word_counts(texts)
    fs, dirs = known_paths(files)
    found = doc_paths(texts, fs, dirs, root)
    found += stale_baseline(lint.ui_hits(root), lint.load_baseline(os.path.join(root, "scripts", "lint-baseline.txt")))
    found += cs_unused(texts, counts)
    found += py_unused(texts, counts)
    found += orphans(files, texts)
    found = apply_ignore(found, load_ignore(os.path.join(root, "scripts", "audit-ignore.txt")))
    if quality:
        found += stale_quality(root)
    return found


def group(findings):
    """(kind, file) -> [finding], in KINDS order."""
    g = collections.OrderedDict()
    for x in sorted(findings, key=lambda x: (KINDS.index(x["kind"]), x["file"], x["line"])):
        g.setdefault((x["kind"], x["file"]), []).append(x)
    return g


def source_of(kind, file):
    return "audit:%s:%s" % (kind, file)


TITLES = {"doc-path": "Fix dead paths in %s", "baseline": "Drop stale lint-baseline entries (%s)",
          "cs-unused": "Remove or wire up unused code in %s", "py-unused": "Remove or wire up unused functions in %s",
          "orphan": "Remove or reference orphan file %s", "ignore": "Prune audit-ignore.txt (%s)"}


def task_for(kind, file, items):
    names = ", ".join(sorted(set(x["name"] for x in items)))
    lines = "; ".join("%s%s" % (x["name"], ":%d" % x["line"] if x["line"] else "") for x in items)
    return {"title": TITLES[kind] % file, "area": area_of(file), "source": source_of(kind, file),
            "behavior": "Weekly cleanup (scripts/audit.py, %s): %s - %s. Each is removed, fixed, or (when "
                        "it is used after all) added to scripts/audit-ignore.txt with the reason."
                        % (kind, items[0]["why"], lines),
            "names": names}


def file_tasks(findings, tasks, add):
    """Calls add(task) for each (kind, file) group with no open task of the same source; returns
    the sources filed. A wontfix task of the same source keeps it from being filed again."""
    blocked = set(t["source"] for t in tasks
                  if (t.get("source") or "").startswith("audit:") and t["status"] != "confirmed")
    filed = []
    for (kind, file), items in group(findings).items():
        if kind not in FILED:
            continue
        task = task_for(kind, file, items)
        if task["source"] in blocked:
            continue
        add(task)
        filed.append(task["source"])
    return filed


def add_task(task):
    r = subprocess.run([sys.executable, os.path.join(ROOT, "scripts", "tasks.py"), "add", task["title"],
                        "--area", task["area"], "--source", task["source"], "--priority", "4",
                        "--needs", "none", "--behavior", task["behavior"],
                        "--notes", "Filed by the weekly cleanup (skill weekly-cleanup)."],
                       cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 0:
        raise RuntimeError(r.stderr.strip() or r.stdout.strip())
    print("filed " + r.stdout.strip())


def report(findings):
    if not findings:
        return "audit: nothing found"
    out, g = [], group(findings)
    counts = collections.Counter(x["kind"] for x in findings)
    out.append("audit: " + ", ".join("%s %d" % (k, counts[k]) for k in KINDS if counts[k]))
    kind = None
    for (k, file), items in g.items():
        if k != kind:
            out.append("\n" + k)
            kind = k
        out.append("  %s: %s" % (file, "; ".join(
            "%s%s" % (x["name"], " (:%d)" % x["line"] if x["line"] else "") for x in items)))
        if len(set(x["why"] for x in items)) == 1:
            out[-1] += "  - " + items[0]["why"]
    return "\n".join(out)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--file", action="store_true", help="file a task per (kind, file) with none open")
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    try:
        findings = audit()
    except ValueError as e:
        print(e, file=sys.stderr)
        return 1
    print(json.dumps(findings, indent=1) if a.json else report(findings))
    if a.file:
        sys.path.insert(0, os.path.join(ROOT, "scripts"))
        import tasks as taskfile
        filed = file_tasks(findings, taskfile.load(), add_task)
        print("filed %d task(s)" % len(filed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
