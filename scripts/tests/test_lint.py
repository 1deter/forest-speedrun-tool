"""Tests for scripts/lint.py: each lint fails on a planted violation with its fix text.

    python scripts/tests/test_lint.py
"""
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import lint as L  # noqa: E402

CSPROJ = """<Project><PropertyGroup>
<Version>0.24.9</Version><AssemblyVersion>0.24.9.0</AssemblyVersion><FileVersion>0.24.9.0</FileVersion>
</PropertyGroup><ItemGroup>
<Compile Remove="tools/**;tests/**;bot/**" />
<None Remove="tools/**;tests/**;bot/**" />
<EmbeddedResource Remove="tools/**;tests/**" />
</ItemGroup></Project>"""
PLUGIN = 'public const string PluginVersion = "0.24.9";'
CHANGELOG = "# Changelog\n\n## v0.24.9 - 2026-10-07\n\n- a line\n\n## v0.24.8 - 2026-10-06\n"


def text(probs):
    return "\n".join(str(p) for p in probs)


class Versions(unittest.TestCase):
    def test_agreeing_versions_pass(self):
        self.assertEqual(L.check_versions(CSPROJ, PLUGIN, CHANGELOG), [])
        self.assertEqual(L.check_versions(CSPROJ, PLUGIN, CHANGELOG, tag="v0.24.9"), [])

    def test_plugin_version_differs(self):
        out = text(L.check_versions(CSPROJ, PLUGIN.replace("0.24.9", "0.24.8"), CHANGELOG))
        self.assertIn("ERROR: src/Plugin.cs PluginVersion is 0.24.8", out)
        self.assertIn("WHY:", out)
        self.assertIn("FIX: release with scripts/bump.py", out)

    def test_assembly_version_differs(self):
        out = text(L.check_versions(CSPROJ.replace("0.24.9.0</File", "0.24.8.0</File"), PLUGIN, CHANGELOG))
        self.assertIn("FileVersion is 0.24.8.0", out)

    def test_no_changelog_section(self):
        out = text(L.check_versions(CSPROJ, PLUGIN, CHANGELOG.replace("## v0.24.9", "## v0.24.7")))
        self.assertIn('has no "## v0.24.9" section', out)
        self.assertIn("FIX: python scripts/bump.py 0.24.9", out)

    def test_section_prefix_is_not_enough(self):
        self.assertFalse(L.changelog_has("## v0.24.90 - x\n", "0.24.9"))

    def test_tag_differs(self):
        out = text(L.check_versions(CSPROJ, PLUGIN, CHANGELOG, tag="v0.24.10"))
        self.assertIn("tag v0.24.10 points at a commit whose ForestOverlay.csproj <Version> is 0.24.9", out)
        self.assertIn("gotcha 65", out)


class Removes(unittest.TestCase):
    def test_listed_folders_pass(self):
        removed = L.removed_folders(CSPROJ)
        self.assertEqual(removed["Compile"], {"tools", "tests", "bot"})
        self.assertEqual(L.check_removes(CSPROJ, ["tools", "tests"]), [])

    def test_new_project_folder_fails(self):
        out = text(L.check_removes(CSPROJ, ["tools", "bot", "newproj"]))
        self.assertIn("bot/ holds C# but is not in ForestOverlay.csproj's EmbeddedResource Remove line(s)", out)
        self.assertIn("newproj/ holds C# but is not in ForestOverlay.csproj's Compile, None, EmbeddedResource", out)
        self.assertIn("gotcha 92", out)
        self.assertIn("FIX: add newproj/** to the Compile, None and EmbeddedResource Remove lines", out)

    def test_project_folders_finds_cs(self):
        d = tempfile.mkdtemp()
        try:
            for sub in ("a/deep", "b", "src", "c/bin"):
                os.makedirs(os.path.join(d, sub))
            open(os.path.join(d, "a", "deep", "X.cs"), "w").close()
            open(os.path.join(d, "b", "readme.md"), "w").close()
            open(os.path.join(d, "src", "Y.cs"), "w").close()
            open(os.path.join(d, "c", "bin", "Z.cs"), "w").close()
            self.assertEqual(L.project_folders(d), ["a"])
        finally:
            shutil.rmtree(d)


class Gotchas(unittest.TestCase):
    NUMBERS = [1, 2, 3]
    STATUS = {"T-0001": "todo", "T-0002": "confirmed"}

    def index(self, *lines):
        return [("docs/areas/a.md", int(l.split(".")[0]), l) for l in lines]

    def test_marked_lines_pass(self):
        idx = self.index("1. **a** - x [check: lint.py alloc]", "2. **b** [judgement]", "3. **c** [check: T-0001]")
        self.assertEqual(L.check_gotchas(self.NUMBERS, idx, self.STATUS), [])

    def test_planted_unmarked_line_fails_with_fix_text(self):
        out = text(L.check_gotchas(self.NUMBERS, self.index("1. **a** [judgement]", "2. **b** - no marker",
                                                            "3. **c** [judgement]"), self.STATUS))
        self.assertIn("ERROR: gotcha 2's index line in docs/areas/a.md has no [check: ...] or [judgement] marker", out)
        self.assertIn("WHY:", out)
        self.assertIn("FIX: end the line with [check: <lint / test name>]", out)

    def test_task_markers_must_be_open_tasks(self):
        out = text(L.check_gotchas([1, 2], self.index("1. **a** [check: T-0002]", "2. **b** [check: x, T-0099]"),
                                   self.STATUS))
        self.assertIn("names T-0002, which is confirmed", out)
        self.assertIn("FIX: replace T-0002 with the check's name", out)
        self.assertIn("names T-0099, which is not in tasks/tasks.jsonl", out)

    def test_missing_repeated_and_unknown_numbers(self):
        idx = self.index("1. **a** [judgement]", "1. **a again** [judgement]", "4. **d** [judgement]")
        out = text(L.check_gotchas(self.NUMBERS, idx, self.STATUS))
        self.assertIn("gotcha 2 has no index line", out)
        self.assertIn("gotcha 3 has no index line", out)
        self.assertIn("gotcha 1 is indexed 2 times", out)
        self.assertIn("indexes gotcha 4, which docs/gotchas.md does not have", out)

    def test_index_stops_at_the_next_section(self):
        d = tempfile.mkdtemp()
        try:
            os.makedirs(os.path.join(d, "docs", "areas"))
            with open(os.path.join(d, "docs", "areas", "x.md"), "w", encoding="utf-8") as f:
                f.write("# X\n\n1. not an index\n\n## Gotchas\n\n### Sub\n\n5. **e** [judgement]\n\n## After\n\n6. no\n")
            self.assertEqual(L.index_lines(d), [("docs/areas/x.md", 5, "5. **e** [judgement]")])
        finally:
            shutil.rmtree(d)

    def test_repo_has_95_or_more_markers(self):
        idx = L.index_lines()
        self.assertGreaterEqual(len(idx), 95)
        self.assertTrue(all(L.MARKER.search(l) for _, _, l in idx))


DRAW = '''class M {
    public override void DrawTab(Rect area)
    {
        GUI.Label(new Rect(0, y, w, 20), "Constant");
        GUI.Label(new Rect(0, y, w - 4, 20), _status);
        GUI.Label(new Rect(0, Mathf.Max(1f, y), w, 20f), _status);
        y += UiText.Draw(0, y, w, "Installed: v" + _version);
        var r = new Rect(0, 0, 1, 1);
        float h = Mathf.Max(1f, y);
        string s = "joined " +
                   "constant";   // a constant
        // _x = "a" + b; in a comment
        _list = _items.Where(i => i.On).ToList();
        _style = new GUIStyle(GUI.skin.label);
    }
    void Tick() { _status = "made here " + n; }
}
'''


QUALITY = """# Quality document

## How a grade is set

| | Verification | Agent legibility | Test stability | Known gaps |
|---|---|---|---|---|
| **A** | x | x | x | x |

## Grades

| Area | Grade | Verification | Legibility | Stability | Gaps | Tasks | Reviewed |
|---|---|---|---|---|---|---|---|
| Saves | C | A | C | A | B | T-0001 | 2026-10-07 |
| Site | A | A | A | A | A | | 2026-10-07 |

## Areas

### Saves

Paths: `src/Save*.cs` `scripts/{save,diff}.py`

### Site

Paths: `site/`

## Simplification log
"""
FILES = ["src/SaveA.cs", "src/SaveB.cs", "scripts/save.py", "site/a.cs", "site/b/c.js",
         "docs/x.md", "tasks/tasks.jsonl", "README.md", "scripts/tests/test_x.py",
         "tests/ForestOverlay.Tests/T.cs", "site/ForestSite.Tests/A.cs"]


class Quality(unittest.TestCase):
    def check(self, doc=QUALITY, statuses=None, files=FILES):
        rows, paths = L.quality_doc(doc)
        return text(L.check_quality(rows, paths, {"T-0001": "todo"} if statuses is None else statuses, files))

    def test_parses_only_the_grades_table(self):
        rows, paths = L.quality_doc(QUALITY)
        self.assertEqual([r["area"] for r in rows], ["Saves", "Site"])
        self.assertEqual(rows[0]["dims"], ["A", "C", "A", "B"])
        self.assertEqual(rows[0]["tasks"], ["T-0001"])
        self.assertEqual(paths, {"Saves": ["src/Save*.cs", "scripts/{save,diff}.py"], "Site": ["site/"]})

    def test_good_doc_passes(self):
        self.assertEqual(self.check(), "")

    def test_globs(self):
        self.assertTrue(L.glob_re("src/Save*.cs").match("src/SaveA.cs"))
        self.assertFalse(L.glob_re("src/*.cs").match("src/Game/A.cs"))
        self.assertTrue(L.glob_re("src/**.cs").match("src/Game/A.cs"))
        self.assertTrue(L.glob_re("site/").match("site/b/c.js"))
        self.assertTrue(L.glob_re("x/{a,b}.py").match("x/b.py"))
        self.assertFalse(L.glob_re("x/{a,b}.py").match("x/ab.py"))

    def test_bad_grade(self):
        out = self.check(QUALITY.replace("| Site | A | A |", "| Site | A | E |"))
        self.assertIn("Site: grade 'E' is not one of A, B, C, D", out)

    def test_grade_must_be_the_worst_dimension(self):
        out = self.check(QUALITY.replace("| Saves | C |", "| Saves | B |"))
        self.assertIn("Saves is graded B, but its worst dimension is C", out)
        self.assertIn("FIX: set Saves's Grade to C", out)

    def test_c_row_needs_an_open_task(self):
        out = self.check(statuses={"T-0001": "confirmed"})
        self.assertIn("Saves is graded C and names no open task", out)
        self.assertIn("Saves names T-0001, which is confirmed", out)
        self.assertIn("Saves names T-0001, which is not in", self.check(statuses={}))

    def test_uncovered_file_fails(self):
        out = self.check(files=FILES + ["bot/new.cs"])
        self.assertIn("1 tracked file(s) in no area", out)
        self.assertIn("bot/new.cs", out)
        self.assertIn("FIX: add them to the Paths", out)

    def test_dead_path_and_missing_section(self):
        out = self.check(files=[f for f in FILES if f != "scripts/save.py"])
        self.assertIn("Saves's path `scripts/{save,diff}.py` matches no tracked file", out)
        out = self.check(QUALITY.replace("### Site\n\nPaths: `site/`\n", ""))
        self.assertIn("Site has no ### Site section", out)
        self.assertIn("in no area", out)

    def test_stale_areas(self):
        rows, paths = L.quality_doc(QUALITY)
        changes = [("2026-10-07", "src/SaveA.cs"), ("2026-10-08", "src/SaveA.cs"),
                   ("2026-10-09", "src/SaveB.cs"), ("2026-10-09", "docs/x.md")]
        self.assertEqual(L.stale_areas(rows, paths, changes), [("Saves", 2)])

    def test_repo_doc_parses(self):
        rows, paths = L.quality_doc(L.read(L.QUALITY))
        self.assertGreaterEqual(len(rows), 16)
        self.assertEqual(set(r["area"] for r in rows), set(paths))


class Ui(unittest.TestCase):
    def test_label20_with_variable_text(self):
        hits = L.label_hits("src/M.cs", DRAW)
        self.assertEqual([h[2] for h in hits], [5, 6])

    def test_alloc_only_inside_draw_bodies(self):
        hits = L.alloc_hits("src/M.cs", DRAW)
        self.assertEqual([h[2] for h in hits], [7, 13, 14])

    def test_planted_hit_fails_with_fix_text(self):
        probs, stale = L.check_ui(L.label_hits("src/M.cs", DRAW) + L.alloc_hits("src/M.cs", DRAW), {})
        out = text(probs)
        self.assertEqual(len(probs), 5)
        self.assertIn("ERROR: GUI.Label at a fixed 20 px with variable text in src/M.cs:5", out)
        self.assertIn("FIX: y += UiText.Draw(x, y, w, text)", out)
        self.assertIn("ERROR: an allocation inside an OnGUI / DrawTab body in src/M.cs:7", out)
        self.assertIn("FIX: build the string in Tick", out)

    def test_baseline_counts_and_stale(self):
        hits = L.label_hits("src/M.cs", DRAW)
        base = {L.baseline_key(hits[0]): 1, "label20\tsrc/Gone.cs\tGUI.Label(x)": 1}
        probs, stale = L.check_ui(hits, base)
        self.assertEqual(len(probs), 1)       # the second, different line is new
        self.assertEqual(stale, 1)            # Gone.cs was fixed
        # the same line twice needs two entries
        twice = [hits[0], hits[0]]
        self.assertEqual(len(L.check_ui(twice, {L.baseline_key(hits[0]): 1})[0]), 1)

    def test_empty_virtual_body_is_skipped(self):
        self.assertEqual(L.draw_bodies("public virtual void DrawTab(Rect area) { }\n"), [])

    def test_repo_passes(self):
        # The real tree against the committed baseline - what CI and the pre-commit hook run.
        self.assertEqual(L.main([]), 0)


if __name__ == "__main__":
    unittest.main()
