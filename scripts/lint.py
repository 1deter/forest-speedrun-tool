"""Machine checks for the project's checkable rules (docs/harness.md 5a).

Every failure prints WHAT / WHY / FIX, so the fix is in the message.

    python scripts/lint.py                    # all checks (CI, the pre-commit hook)
    python scripts/lint.py --tag v0.24.N [--rev SHA]
                                              # a tag about to be pushed (pre-push hook)
    python scripts/lint.py --update-baseline  # accept today's heuristic hits

The two UI heuristics (a fixed 20 px GUI.Label with variable text, an
allocation in an OnGUI / DrawTab body) cannot tell a list row or a
constant from a bug, so the hits that existed when they were added sit in
scripts/lint-baseline.txt and only new ones fail (author, 2026-10-07).
Fix a baselined line and its entry goes stale; --update-baseline drops it.
The community index is checked by CommunityPacksTests, not here.
Every gotcha index line in docs/areas/*.md ends with [check: <name>],
[check: T-n] (the task building it) or [judgement] (docs/harness.md 5d).
The log catalogue (scripts/log-catalogue.py --check, gotcha 16's check):
every log call has a prefix and docs/log-lines.md is current, with a
meaning per prefix.
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
    print("lint: ok (%d UI hit(s), all baselined)" % len(hits))
    return 0


if __name__ == "__main__":
    sys.exit(main())
