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


BEHAVIOURS = """using UnityEngine;
public sealed class Good : MonoBehaviour
{
    private void OnEnable() { try { Go(); } catch (Exception ex) { Lifecycle.Fail("Good.OnEnable", ex); } }
    private void Update()
    {
        if (_host == null) return;
        if (!Show && _count < 2) return;
        try
        {
            if (x) { y(); }
        }
        catch (Exception ex) { Lifecycle.Fail("Good.Update", ex); }
        finally { z(); }
    }
    private void OnDestroy() { }
}
public sealed class Bad : MonoBehaviour, ILateDrawer
{
    private void LateUpdate() { LatePass.Sync(DrawTarget.View()); }
    private void OnRenderObject()
    {
        if (!DrawTarget.ShouldDraw()) return;
        try { Draw(); }
        catch (Exception) { }
    }
    private void FixedUpdate()
    {
        try { Step(); }
        catch (Exception) { }
        After();
    }
    private void OnGUI() => Draw();
}
public sealed class Plain
{
    public void Update() { Tick(); }
    public void Start(string extra) { Go(); }
}
"""


class Lifecycle(unittest.TestCase):
    def methods(self):
        return [(c, m, n, body is not None and L.body_wrapped(body)) for c, m, n, body in L.message_methods(BEHAVIOURS)]

    def test_wrapped_and_plain_guards_pass(self):
        ok = [(c, m) for c, m, n, w in self.methods() if w]
        self.assertEqual(ok, [("Good", "OnEnable"), ("Good", "Update"), ("Good", "OnDestroy")])

    def test_unwrapped_call_guard_tail_and_arrow_fail(self):
        bad = [(c, m, n) for c, m, n, w in self.methods() if not w]
        # a guard with a call is not plain; a statement after the catch escapes; => has no body to wrap
        self.assertEqual(bad, [("Bad", "LateUpdate", 20), ("Bad", "OnRenderObject", 21),
                               ("Bad", "FixedUpdate", 27), ("Bad", "OnGUI", 33)])

    def test_non_unity_class_is_skipped(self):
        self.assertNotIn("Plain", [c for c, m, n, w in self.methods()])

    def test_fails_with_fix_text(self):
        out = text(L.check_lifecycle([("src/Game/Bad.cs", 20, "Bad", "LateUpdate")]))
        self.assertIn("ERROR: Bad.LateUpdate in src/Game/Bad.cs:20 is not wrapped in try / catch", out)
        self.assertIn("gotcha 3", out)
        self.assertIn('FIX: put the whole body in try { ... } catch (Exception ex) { Lifecycle.Fail("Bad.LateUpdate", ex); }', out)

    def test_nested_class_keeps_the_outer_scope(self):
        src = ("public class A : MonoBehaviour\n{\n    private class Inner\n    {\n        void Update() { Go(); }\n    }\n"
               "    private void Update() { Go(); }\n}\n")
        self.assertEqual([(c, m, n) for c, m, n, b in L.message_methods(src)], [("A", "Update", 7)])

    def test_brace_in_a_string_is_not_a_brace(self):
        src = ("public class A : MonoBehaviour\n{\n    private void OnGUI()\n    {\n"
               "        try { var s = \"}\"; var c = '{'; var v = @\"a\"\"}\"; }\n"
               "        catch (Exception ex) { Lifecycle.Fail(\"A.OnGUI\", ex); }\n    }\n}\n")
        (c, m, n, body), = L.message_methods(src)
        self.assertTrue(L.body_wrapped(body))

    def test_brace_in_a_block_comment_is_not_a_brace(self):
        src = ("public class A : MonoBehaviour\n{\n    private void Update()\n    {\n"
               "        /* old: if (x) { */\n        try { Go(); }\n        catch (Exception ex) { Lifecycle.Fail(\"A.Update\", ex); }\n"
               "        /*\n         }\n        */\n    }\n    private void LateUpdate() { Go(); }\n}\n")
        got = [(c, m, n, b is not None and L.body_wrapped(b)) for c, m, n, b in L.message_methods(src)]
        self.assertEqual(got, [("A", "Update", 3, True), ("A", "LateUpdate", 12, False)])

    def test_block_comment_strip_keeps_lines_and_strings(self):
        src = 'a /* x\n y */ b "/* not */" // /* nor */\nc'
        out = L.strip_block_comments(src)
        self.assertEqual(out.count("\n"), src.count("\n"))
        self.assertIn('"/* not */"', out)
        self.assertIn("// /* nor */", out)
        self.assertNotIn(" x", out)

    def test_repo_has_no_unwrapped_method(self):
        self.assertEqual(L.lifecycle_hits(), [])


class Mojibake(unittest.TestCase):
    EM_DASH_GARBLED = "\u00e2\u20ac\u201d"

    def test_clean_text_passes(self):
        self.assertEqual(L.check_mojibake({"a.md": "plain \u2014 dash, caf\u00e9, \u00c9cole\n"}), [])

    def test_garbled_dash_fails_with_fix_text(self):
        out = text(L.check_mojibake({"docs/x.md": "ok\nthe %s here\n" % self.EM_DASH_GARBLED}))
        self.assertIn("ERROR: docs/x.md has PowerShell mojibake on line(s) 2", out)
        self.assertIn("gotcha 9", out)
        self.assertIn("FIX: restore the file from git (git checkout -- docs/x.md)", out)

    def test_garbled_accent_and_nbsp_fail(self):
        for bad in ("caf\u00c3\u00a9", "a\u00c2\u00a0b"):
            self.assertEqual(len(L.check_mojibake({"a.cs": bad})), 1, bad)

    def test_quoting_docs_are_allowed(self):
        self.assertEqual(L.check_mojibake({"docs/gotchas.md": self.EM_DASH_GARBLED}), [])

    def test_repo_has_none(self):
        self.assertEqual(text(L.check_mojibake(L.tracked_text())), "")


DEPLOY_OK = """<#
    Copy-Item is only mentioned here.
#>
$dll = Join-Path $root "bin\\$Configuration\\net35\\ForestOverlay.dll"
Copy-Item $dll $pluginDir -Force   # the DLL
"""


class Deploy(unittest.TestCase):
    def test_dll_only_passes(self):
        self.assertEqual(L.check_deploy(DEPLOY_OK), [])

    def test_data_copy_fails_with_fix_text(self):
        out = text(L.check_deploy(DEPLOY_OK + 'Copy-Item "$root\\locations" $pluginDir -Recurse\n'))
        self.assertIn("ERROR: scripts/deploy.ps1:6 copies", out)
        self.assertIn("gotcha 10", out)
        self.assertIn("FIX: embed the data in the DLL", out)

    def test_other_copy_commands_fail(self):
        for cmd in ("xcopy data $pluginDir", "robocopy data $pluginDir", "Copy-Item -Path data -Destination $pluginDir",
                    "cp data $pluginDir"):
            self.assertEqual(len(L.check_deploy(DEPLOY_OK + cmd + "\n")), 1, cmd)

    def test_dll_by_path_flag_passes(self):
        self.assertEqual(L.check_deploy(DEPLOY_OK.replace("Copy-Item $dll", "Copy-Item -Path $dll")), [])

    def test_no_copy_at_all_fails(self):
        self.assertEqual(len(L.check_deploy("$dll = 'ForestOverlay.dll'\n")), 1)

    def test_repo_script_is_clean(self):
        self.assertEqual(L.check_deploy(L.read(L.DEPLOY)), [])


FINDALL_SRC = """class A {
    // Resources.FindObjectsOfTypeAll(t) in a comment
    /* Resources.FindObjectsOfTypeAll(t) in a block */
    object[] Tick() { return Resources.FindObjectsOfTypeAll(typeof(Foo)); }
    string s = "Resources.FindObjectsOfTypeAll(";
    object[] Other() { return Object.FindObjectsOfType(typeof(Foo)); }
}
"""


class FindAll(unittest.TestCase):
    def test_only_real_call_sites_hit(self):
        hits = L.findall_hits("src/Game/A.cs", FINDALL_SRC)
        self.assertEqual([(h[0], h[2]) for h in hits], [("findall", 4)])

    def test_new_call_fails_with_fix_text(self):
        hits = L.findall_hits("src/Game/A.cs", FINDALL_SRC)
        probs, stale = L.check_ui(hits, {})
        out = text(probs)
        self.assertIn("ERROR: a Resources.FindObjectsOfTypeAll call in src/Game/A.cs:4", out)
        self.assertIn("gotcha 11", out)
        self.assertIn("FIX: find once and keep it (Game/SceneCache)", out)

    def test_baselined_call_passes_and_a_second_one_fails(self):
        hits = L.findall_hits("src/Game/A.cs", FINDALL_SRC)
        self.assertEqual(L.check_ui(hits, {L.baseline_key(hits[0]): 1}), ([], 0))
        twice = hits + hits
        self.assertEqual(len(L.check_ui(twice, {L.baseline_key(hits[0]): 1})[0]), 1)

    def test_repo_hits_are_all_baselined(self):
        probs, stale = L.check_ui(L.ui_hits(), L.load_baseline())
        self.assertEqual((text(probs), stale), ("", 0))


RENDER_SRC = """public class Good : MonoBehaviour
{
    private void OnRenderObject()
    {
        try
        {
            if (!DrawTarget.ShouldDraw()) return;
            Draw();
        }
        catch (Exception ex) { Lifecycle.Fail("Good.OnRenderObject", ex); }
    }
}
public class Bad : MonoBehaviour
{
    private void OnRenderObject()
    {
        try { Draw(); }
        catch (Exception ex) { Lifecycle.Fail("Bad.OnRenderObject", ex); }
    }
    private void Update() { }
}
public class Mentions : MonoBehaviour
{
    private void OnRenderObject()
    {
        try { /* DrawTarget.ShouldDraw() */ Draw(); }
        catch (Exception ex) { Lifecycle.Fail("Mentions.OnRenderObject", ex); }
    }
}
"""


class RenderObject(unittest.TestCase):
    def test_unchecked_draw_hits(self):
        hits = L.render_hits("src/Game/X.cs", RENDER_SRC)
        self.assertEqual([(c, n) for p, n, c in hits], [("Bad", 15), ("Mentions", 24)])

    def test_fails_with_fix_text(self):
        out = text(L.check_render(L.render_hits("src/Game/X.cs", RENDER_SRC)[:1]))
        self.assertIn("ERROR: Bad.OnRenderObject in src/Game/X.cs:15 does not check DrawTarget.ShouldDraw()", out)
        self.assertIn("gotcha 12", out)
        self.assertIn("FIX: start the body with if (!DrawTarget.ShouldDraw()) return;", out)

    def test_allow_listed_class_is_skipped(self):
        src = "public class LatePass : MonoBehaviour\n{\n    private void OnRenderObject() { try { Go(); } catch (Exception) { } }\n}\n"
        self.assertEqual(L.render_hits("src/Game/LatePass.cs", src), [])
        self.assertEqual(len(L.render_hits("src/Game/Other.cs", src)), 1)

    def test_repo_overlays_all_check(self):
        hits = [h for p, t in L.src_texts() for h in L.render_hits(p, t)]
        self.assertEqual(text(L.check_render(hits)), "")


WEB_OK = """// UnityWebRequest in a comment
var t = Type.GetType("UnityEngine.Networking.UnityWebRequest, UnityEngine");
var h = t.GetProperty("downloadHandler");
var c = t.GetProperty("responseCode");
"""


class WebRequests(unittest.TestCase):
    def test_reader_with_response_code_passes(self):
        self.assertEqual(L.web_request_hits("src/Core/A.cs", WEB_OK), [])

    def test_reader_without_response_code_hits(self):
        bad = WEB_OK.replace("responseCode", "isError")
        self.assertEqual(L.web_request_hits("src/Core/A.cs", bad), [("src/Core/A.cs", 2)])

    def test_response_code_only_in_a_comment_does_not_count(self):
        bad = WEB_OK.replace('GetProperty("responseCode")', 'GetProperty("isError")') + "// responseCode\n/* responseCode */\n"
        self.assertEqual(len(L.web_request_hits("src/Core/A.cs", bad)), 1)

    def test_file_without_a_request_or_a_body_read_passes(self):
        self.assertEqual(L.web_request_hits("a.cs", "var x = responseCode;\n"), [])
        self.assertEqual(L.web_request_hits("a.cs", 'var t = Type.GetType("UnityWebRequest");\n'), [])

    def test_fails_with_fix_text(self):
        out = text(L.check_web_requests([("src/Core/A.cs", 2)]))
        self.assertIn("ERROR: src/Core/A.cs:2 reads a UnityWebRequest but never checks responseCode", out)
        self.assertIn("gotcha 15", out)
        self.assertIn("FIX: read the request's responseCode", out)

    def test_repo_readers_all_check(self):
        hits = [h for p, t in L.src_texts() for h in L.web_request_hits(p, t)]
        self.assertEqual(text(L.check_web_requests(hits)), "")


MOVE_SRC = """class M {
    private string Good(Vector3 to)
    {
        string area = _areas.ForTeleport(to);
        if (!Ctx.Player.MoveTo(to, rot)) return "no";
        return area;
    }
    private void Bad(Vector3 at)
    {
        Ctx.Player.MoveTo(at, rot);
    }
    public bool Other(Vector3 at)
    {
        // Ctx.Player.MoveTo(at, rot);
        return Ctx.Camera.MoveTo(at);
    }
    private void AlsoBad(Vector3 at)
    {
        player.MoveTo(at, rot);
    }
}
"""


class MoveTo(unittest.TestCase):
    def test_only_callers_without_for_teleport_hit(self):
        hits = L.moveto_hits("src/Modules/M.cs", MOVE_SRC)
        self.assertEqual([(h[0], h[2]) for h in hits], [("moveto", 10), ("moveto", 19)])

    def test_for_teleport_in_another_method_does_not_cover(self):
        src = "class M {\n  void A() { _areas.ForTeleport(x); }\n  void B()\n  {\n    Ctx.Player.MoveTo(x, r);\n  }\n}\n"
        self.assertEqual(len(L.moveto_hits("m.cs", src)), 1)

    def test_statements_do_not_start_a_method(self):
        src = ("class M {\n  void A(Vector3 x)\n  {\n    _areas.ForTeleport(x);\n    if (ok)\n    {\n"
               "      return Foo(x);\n    }\n    Ctx.Player.MoveTo(x, r);\n  }\n}\n")
        self.assertEqual(L.moveto_hits("m.cs", src), [])

    def test_fails_with_fix_text_and_baseline_allows_it(self):
        hits = L.moveto_hits("src/Modules/M.cs", MOVE_SRC)
        out = text(L.check_ui(hits, {})[0])
        self.assertIn("ERROR: a player MoveTo call in a method that does not run AreaKeeper.ForTeleport in src/Modules/M.cs:10", out)
        self.assertIn("gotcha 34", out)
        self.assertIn("FIX: call string area = _areas.ForTeleport(dest) before MoveTo", out)
        base = {L.baseline_key(h): 1 for h in hits}
        self.assertEqual(L.check_ui(hits, base), ([], 0))

    def test_the_definition_is_not_a_caller(self):
        self.assertEqual(L.moveto_hits("src/Game/PlayerRef.cs", "public bool MoveTo(Vector3 position, Quaternion rotation)\n{\n}\n"), [])


CFG_SRC = """class M {
    float Bad(float y)
    {
        float op = GUI.HorizontalSlider(new Rect(0, y, 90, 16), Opacity, 0f, 1f);
        if (op != Opacity) _opacityCfg.Value = op;
        return y;
    }
    string BadText(float y)
    {
        string t = GUI.TextField(new Rect(0, y, 90, 22), _name);
        if (t != _name) { _name = t; _nameCfg.Value = t; }
        return t;
    }
    float Settled(float y)
    {
        float op = GUI.HorizontalSlider(new Rect(0, y, 90, 16), Opacity, 0f, 1f);
        if (op != Opacity) { _now = op; _writeAt = Time.unscaledTime + 0.5f; }
        if (Time.unscaledTime > _writeAt) _opacityCfg.Value = _now;
        return y;
    }
    float OnRelease(float y)
    {
        float op = GUI.HorizontalSlider(new Rect(0, y, 90, 16), Opacity, 0f, 1f);
        if (Event.current.type == EventType.MouseUp) _opacityCfg.Value = op;
        return y;
    }
    void Toggle(float y)
    {
        float op = GUI.HorizontalSlider(new Rect(0, y, 90, 16), Opacity, 0f, 1f);
        bool on = GUI.Toggle(new Rect(0, y, 90, 16), _on.Value, "x");
        if (on != _on.Value) _on.Value = on;
        if (a == b) c = d;
    }
}
"""


class CfgWrite(unittest.TestCase):
    def test_write_on_every_change_hits(self):
        hits = L.cfgwrite_hits("src/Modules/M.cs", CFG_SRC)
        self.assertEqual([(h[0], h[2]) for h in hits], [("cfgwrite", 5), ("cfgwrite", 11)])

    def test_only_the_unsettled_writes_hit(self):
        hits = L.cfgwrite_hits("src/Modules/M.cs", CFG_SRC)
        self.assertEqual(sorted(set(h[2] for h in hits)), [5, 11])

    def test_fails_with_fix_text_and_baseline_allows_it(self):
        hits = L.cfgwrite_hits("src/Modules/M.cs", CFG_SRC)
        out = text(L.check_ui(hits, {})[0])
        self.assertIn("ERROR: a ConfigEntry .Value write right after a slider / text field in src/Modules/M.cs:5", out)
        self.assertIn("gotcha 60", out)
        self.assertIn("FIX: keep the value in the module while it changes and write once it settles", out)
        self.assertEqual(L.check_ui(hits, {L.baseline_key(h): 1 for h in hits}), ([], 0))

    def test_comment_is_not_a_write(self):
        src = "void A()\n{\n    float v = GUI.HorizontalSlider(r, x, 0, 1);\n    // cfg.Value = v;\n}\n"
        self.assertEqual(L.cfgwrite_hits("m.cs", src), [])


if __name__ == "__main__":
    unittest.main()
