"""Tests for scripts/audit.py, the weekly cleanup's finder (docs/harness.md 10e, T-0014).

    python scripts/tests/test_audit.py
"""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import audit as A  # noqa: E402


def found(findings):
    return sorted((x["kind"], x["file"], x["name"]) for x in findings)


class DocPaths(unittest.TestCase):
    FILES = ["docs/a.md", "docs/areas/b.md", "scripts/tasks.py", "src/Data/RunCategory.cs"]

    def run_on(self, texts):
        fs, dirs = A.known_paths(self.FILES)
        return found(A.doc_paths(texts, fs, dirs, root=tempfile.gettempdir()))

    def test_links_resolve_from_the_doc_folder(self):
        texts = {"docs/areas/b.md": "[ok](../a.md) [ok](b.md#top) [gone](docs/a.md) [web](https://x.y) [x](#a)\n"}
        self.assertEqual(self.run_on(texts), [("doc-path", "docs/areas/b.md", "docs/a.md")])

    def test_backtick_paths_from_the_root(self):
        texts = {"docs/a.md": "`scripts/tasks.py` `scripts/tasks.py:12` `scripts/gone.py` `src/Data/RunCategory` "
                              "`src/Data/RunCategory.Parse` `docs/tests/<date>.md` `SlotN.deter-backup` `a/b`\n"}
        self.assertEqual(self.run_on(texts), [("doc-path", "docs/a.md", "scripts/gone.py")])

    def test_history_docs_are_not_read(self):
        self.assertEqual(self.run_on({"CHANGELOG.md": "`scripts/gone.py`\n", "docs/tasks.md": "`scripts/gone.py`\n"}), [])


class Baseline(unittest.TestCase):
    def test_entries_without_a_hit_are_stale(self):
        hits = [("alloc", "src/A.cs", 3, "x = a + b;")]
        base = {"alloc\tsrc/A.cs\tx = a + b;": 1, "label20\tsrc/B.cs\tGUI.Label(r, t);": 1}
        self.assertEqual(found(A.stale_baseline(hits, base)),
                         [("baseline", "scripts/lint-baseline.txt", "label20 src/B.cs")])


CS = """
namespace X
{
    public class Used { }
    internal class Lonely { }
    public class Thing : MonoBehaviour
    {
        private int _dead;
        private int _live;
        public int Live { get { return _live; } }
        private void Update() { }
        public override string Describe() { return ""; }
        [HarmonyPostfix]   // the game calls it
        private static void Hooked() { }
        public string Status { get; set; }
        public int ToolOnly() { return 1; }
    }
}
"""


class CSharp(unittest.TestCase):
    def test_declarations_skip_conventions_attributes_and_overrides(self):
        names = [n for _, _, n in A.cs_declarations("src/X.cs", CS)]
        for n in ("Used", "Lonely", "Thing", "_dead", "_live", "Live", "Status", "ToolOnly"):
            self.assertIn(n, names)
        for n in ("Update", "Describe", "Hooked"):
            self.assertNotIn(n, names)

    def test_test_classes_are_not_types_to_judge(self):
        names = [n for _, _, n in A.cs_declarations("tests/ForestOverlay.Tests/XTests.cs", "public class XTests\n{\n}\n")]
        self.assertEqual(names, [])

    def test_unused_means_named_once_and_not_by_the_site_js(self):
        texts = {"src/X.cs": CS, "src/Y.cs": "var u = new Used(); Thing t; var l = t.Live;",
                 "site/wwwroot/app.js": "row.status", "scripts/e2e.py": "call('ToolOnly')"}
        names = [n for _, _, n in found(A.cs_unused(texts, A.word_counts(texts)))]
        self.assertEqual(names, ["Lonely", "_dead"])

    def test_the_ignore_list_and_the_baseline_are_not_uses(self):
        texts = {"src/X.cs": CS, "scripts/audit-ignore.txt": "cs-unused\tsrc/X.cs\t_dead\tbridge\n",
                 "scripts/lint-baseline.txt": "alloc\tsrc/X.cs\tLonely x = new Lonely();\n"}
        counts = A.word_counts(texts)
        self.assertEqual((counts["_dead"], counts["Lonely"]), (1, 1))


class Python(unittest.TestCase):
    def test_unused_functions(self):
        texts = {"scripts/a.py": "def main():\n    helper()\ndef helper(): pass\ndef lost(): pass\n"
                                 "def test_x(): pass\n    def do_GET(self): pass\n"}
        self.assertEqual(found(A.py_unused(texts, A.word_counts(texts))), [("py-unused", "scripts/a.py", "lost")])


class Orphans(unittest.TestCase):
    def test_named_imported_or_globbed_files_are_not_orphans(self):
        files = ["scripts/used.py", "scripts/world_pack.py", "scripts/lost.py", "qa/list.txt",
                 "site/ForestSite/wwwroot/map.js", "site/ForestSite/wwwroot/old.js", "scripts/README.md",
                 "src/Foo.cs", "ForestOverlay.csproj", "CLAUDE.md"]
        texts = {"CLAUDE.md": "python scripts/used.py\n", "scripts/used.py": "import world_pack\n",
                 "ForestOverlay.csproj": '<EmbeddedResource Include="qa\\*.txt" />',
                 "site/ForestSite/wwwroot/map.js": "", "site/ForestSite/wwwroot/old.js": "",
                 "scripts/lost.py": "# lost.py names itself\n", "qa/list.txt": "",
                 "docs/session-log.md": "old.js was removed", "scripts/world_pack.py": "",
                 "site/ForestSite/wwwroot/index.html": '<script src="map.js"></script>'}
        files.append("site/ForestSite/wwwroot/index.html")
        self.assertEqual([n for _, _, n in found(A.orphans(files, texts))], ["lost.py", "index.html", "old.js"])   # path order


class Ignore(unittest.TestCase):
    def write(self, text):
        f = tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8")
        f.write(text)
        f.close()
        self.addCleanup(os.remove, f.name)
        return f.name

    def test_a_line_without_a_reason_is_refused(self):
        with self.assertRaises(ValueError):
            A.load_ignore(self.write("# c\ncs-unused\tsrc/A.cs\tX\t\n"))
        with self.assertRaises(ValueError):
            A.load_ignore(self.write("dead\tsrc/A.cs\tX\twhy\n"))

    def test_ignored_findings_go_and_unused_lines_are_reported(self):
        entries = A.load_ignore(self.write("cs-unused\tsrc/A.cs\tX\tbridge reads it\n"
                                           "orphan\tqa/old.txt\t*\tshipped by name\n"))
        kept = A.apply_ignore([A.Finding("cs-unused", "src/A.cs", 3, "X", "w"),
                               A.Finding("cs-unused", "src/A.cs", 9, "Y", "w")], entries)
        self.assertEqual(found(kept), [("cs-unused", "src/A.cs", "Y"),
                                       ("ignore", "scripts/audit-ignore.txt", "orphan qa/old.txt *")])


class Filing(unittest.TestCase):
    def test_one_task_per_kind_and_file_never_a_second_open_one(self):
        findings = [A.Finding("cs-unused", "src/A.cs", 3, "X", "member named nowhere else"),
                    A.Finding("cs-unused", "src/A.cs", 9, "Y", "member named nowhere else"),
                    A.Finding("cs-unused", "bot/B.cs", 1, "Z", "member named nowhere else"),
                    A.Finding("doc-path", "docs/a.md", 4, "x.md", "gone"),
                    A.Finding("orphan", "scripts/lost.py", 0, "lost.py", "nobody"),
                    A.Finding("quality", "docs/quality.md", 0, "Bot", "re-grade")]
        tasks = [{"id": "T-0000", "status": "todo", "source": None},
                 {"id": "T-0001", "status": "todo", "source": "audit:doc-path:docs/a.md"},
                 {"id": "T-0002", "status": "confirmed", "source": "audit:orphan:scripts/lost.py"},
                 {"id": "T-0003", "status": "wontfix", "source": "audit:cs-unused:bot/B.cs"}]
        added = []
        filed = A.file_tasks(findings, tasks, added.append)
        self.assertEqual(filed, ["audit:cs-unused:src/A.cs", "audit:orphan:scripts/lost.py"])
        a = added[0]
        self.assertEqual((a["area"], a["title"]), ("plugin", "Remove or wire up unused code in src/A.cs"))
        self.assertIn("X:3; Y:9", a["behavior"])
        self.assertEqual(added[1]["area"], "harness")

    def test_areas(self):
        self.assertEqual([A.area_of(p) for p in ("src/A.cs", "site/x.js", "bot/B.cs", "knowledge/c.md",
                                                  "patcher/P.cs", "scripts/a.py", "docs/a.md", "README.md")],
                         ["plugin", "site", "bot", "knowledge", "release", "harness", "docs", "harness"])


class Repo(unittest.TestCase):
    def test_the_repo_audits_cleanly_to_known_kinds(self):
        for x in A.audit(quality=False):
            self.assertIn(x["kind"], A.KINDS)


if __name__ == "__main__":
    unittest.main()
