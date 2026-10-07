"""Tests for scripts/log-catalogue.py, on small C# files in a temp folder.

    python scripts/tests/test_log_catalogue.py
"""
import importlib.util
import os
import shutil
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("log_catalogue", os.path.join(HERE, "..", "log-catalogue.py"))
C = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(C)


def tpl(code, consts=None):
    """The template of the first log call in a snippet."""
    calls = C.scan_text("x.cs", code)
    return calls[0][3]


class TemplateTests(unittest.TestCase):
    def test_literals_names_and_expressions(self):
        self.assertEqual(tpl('_log.LogInfo("Restart \'" + s.Id + "\': " + Describe(x) + ".");'),
                         "Restart '<s.Id>': <..>.")

    def test_nested_literals_stay_one_expression(self):
        self.assertEqual(tpl('_log.LogInfo("UI " + (on ? "shown." : "hidden."));'), "UI <..>")

    def test_const_resolved_but_not_a_version(self):
        code = ('const string Prefix = "Load timing: "; const string PluginVersion = "0.24.1";\n'
                '_log.LogInfo(Prefix + "scene " + name); _log.LogInfo("ForestOverlay v" + PluginVersion);')
        calls = C.scan_text("x.cs", code)
        self.assertEqual(calls[0][3], "Load timing: scene <name>")
        self.assertEqual(calls[1][3], "ForestOverlay v<PluginVersion>")

    def test_comments_and_strings_are_not_calls(self):
        code = ('// _log.LogInfo(old);\n/* _log.LogWarning(x); */\nstring s = "_log.LogInfo(y)";\n'
                '_log.LogWarning("Real: " + s);')
        calls = C.scan_text("x.cs", code)
        self.assertEqual([(c[1], c[2], c[3]) for c in calls], [(4, "warning", "Real: <s>")])

    def test_verbatim_and_escaped_quotes(self):
        self.assertEqual(tpl(r'_log.LogInfo("Inventory full: \"can\'t carry\" " + n);'),
                         'Inventory full: "can\'t carry" <n>')
        self.assertEqual(tpl('_log.LogInfo(@"Path: C:\\x ""y""" + p);'), 'Path: C:\\x "y"<p>')

    def test_leading_spaces_mark_a_continuation_row(self):
        self.assertEqual(tpl('_log.LogInfo("  Grown: " + list);'), "  Grown: <list>")


class PrefixTests(unittest.TestCase):
    def check(self, template, want):
        self.assertEqual(C.prefix(template), want, template)

    def test_stops_at_delimiters(self):
        self.check("Perf (<..> s): <..> fps", "Perf")
        self.check("Restart '<s.Id>': <..>.", "Restart")
        self.check("Savestate restore (in place): <inner>", "Savestate restore")
        self.check("Bridge #<id>: <..>", "Bridge")
        self.check("Explorer -> <open>", "Explorer")
        self.check("Pickup gone, inventory unchanged: <..>", "Pickup gone")
        self.check("FirstPersonCharacter not found yet - will keep retrying.", "FirstPersonCharacter not found yet")
        self.check("BookPages bound. select:<..>", "BookPages bound")
        self.check("No staged update.", "No staged update")

    def test_method_call_parens_are_part_of_it(self):
        self.check("Update() threw: <ex>", "Update() threw")
        self.check("OnGUI() threw, HUD disabled: <ex>", "OnGUI() threw")

    def test_value_first_or_spaces_first_has_none(self):
        self.check("<Message> (<url>)", None)
        self.check("  Grown: <list>", None)

    def test_a_word_glued_to_a_value_is_dropped(self):
        self.check("SimpleMouseRotator x<n> reset", "SimpleMouseRotator")
        self.check("Load <n> finished", "Load")
        self.check("Aerial capture <Status>", "Aerial capture")


class Repo(unittest.TestCase):
    """A temp root with src/ and docs/."""

    def setUp(self):
        self.root = tempfile.mkdtemp()
        os.makedirs(os.path.join(self.root, "src", "Game"))
        os.makedirs(os.path.join(self.root, "src", "bin"))
        os.makedirs(os.path.join(self.root, "docs"))

    def tearDown(self):
        shutil.rmtree(self.root)

    def write(self, rel, text):
        with open(os.path.join(self.root, rel), "w", encoding="utf-8") as f:
            f.write(text)

    def doc(self):
        with open(os.path.join(self.root, C.DOC), encoding="utf-8") as f:
            return f.read()

    def generate(self):
        cat, new, old = C.build(self.root)
        self.write(C.DOC, new)
        return cat

    def problems(self):
        return [p.what for p in C.check(self.root)]


class CatalogueTests(Repo):
    def test_groups_by_prefix_with_files_levels_and_counts(self):
        self.write("src/Game/A.cs", '_log.LogInfo("Death (" + k + "): quick-loading.");\n'
                                    '_log.LogWarning("Death (" + k + "): quick-loading.");\n'
                                    '_log.LogInfo("Death (" + k + "): quick-loading.");')
        self.write("src/bin/Skip.cs", '_log.LogInfo(nothing);')
        cat = self.generate()
        self.assertEqual(list(cat.groups), ["Death"])
        self.assertEqual(cat.groups["Death"], [("src/Game/A.cs", "info", "Death (<k>): quick-loading.", 2),
                                               ("src/Game/A.cs", "warning", "Death (<k>): quick-loading.", 1)])
        self.assertIn("Written by A.cs; info / warning.", self.doc())
        self.assertIn("- info `Death (<k>): quick-loading.` (x2)", self.doc())

    def test_meanings_survive_regeneration_and_empty_ones_fail(self):
        self.write("src/Game/A.cs", '_log.LogInfo("Teleport to \'" + n + "\'.");')
        self.generate()
        self.assertEqual(self.problems(), ["log prefix `Teleport to` has no meaning in docs/log-lines.md"])
        self.write(C.DOC, self.doc().replace("Meaning: \n", "Meaning: A teleport to a spot.\n"))
        self.write("src/Game/B.cs", '_log.LogInfo("Restart \'" + id + "\'.");')
        self.generate()
        self.assertIn("Meaning: A teleport to a spot.", self.doc())
        self.assertEqual(self.problems(), ["log prefix `Restart` has no meaning in docs/log-lines.md"])

    def test_stale_doc_fails(self):
        self.write("src/Game/A.cs", '_log.LogInfo("Map: done.");')
        self.generate()
        self.write(C.DOC, self.doc().replace("Meaning: \n", "Meaning: The map.\n"))
        self.assertEqual(self.problems(), [])
        self.write("src/Game/A.cs", '_log.LogInfo("Map: done in " + s + ".");')
        self.assertEqual(self.problems(), ["docs/log-lines.md is out of date with the log calls in src/ and patcher/"])

    def test_unprefixed_call_fails_with_its_line(self):
        self.write("src/Game/A.cs", 'x();\n_log.LogWarning(Message);\n_log.LogInfo("  row " + r);')
        self.generate()
        self.assertEqual(self.problems(), ["log call with no prefix at src/Game/A.cs:2: <Message>"])
        self.assertIn("## No prefix", self.doc())

    def test_declared_prefix_on_the_line_or_above(self):
        self.write("src/Game/A.cs", '_log.LogInfo(summary);   // log: Memory census\n'
                                    '// log: Render probe, Scene census after\n'
                                    '_log.LogInfo(sb.ToString());')
        cat = self.generate()
        self.assertEqual(list(cat.groups), ["Memory census", "Render probe", "Scene census after"])
        self.assertIn("- info `<summary>` *(declared)*", self.doc())

    def test_a_declaration_must_start_a_string_literal(self):
        self.write("src/Game/A.cs", '_log.LogInfo(summary);   // log: Memory census\n'
                                    '_log.LogInfo(other);   // log: Memroy census')
        self.write("src/Game/B.cs", 'string s = "Memory census " + n; // "Memroy census" in a comment does not count')
        self.generate()
        self.assertEqual(self.problems(), [
            "log call at src/Game/A.cs:2 declares `Memroy census`, but no string literal in src/ or patcher/ starts with it",
            "log prefix `Memory census` has no meaning in docs/log-lines.md",
            "log prefix `Memroy census` has no meaning in docs/log-lines.md"])

    def test_comment_above_must_stand_alone(self):
        self.write("src/Game/A.cs", 'Foo();   // log: Map\n_log.LogInfo(line);')
        cat = self.generate()
        self.assertEqual(len(cat.bare), 1)

    def test_bad_declarations_fail(self):
        self.write("src/Game/A.cs", '_log.LogInfo("Map: " + x);   // log: Map\n'
                                    '_log.LogInfo(line);   // log: not a prefix: really')
        self.generate()
        probs = self.problems()
        self.assertTrue(any("declares `Map` but starts with the literal prefix `Map`" in p for p in probs), probs)
        self.assertTrue(any("declares `not a prefix: really`, which is not a prefix" in p for p in probs), probs)


class RealRepoTests(unittest.TestCase):
    def test_scans_the_plugin(self):
        cat = C.Catalogue(C.scan())
        self.assertGreater(cat.calls, 500)
        self.assertIn("Slow tick", cat.groups)
        self.assertIn("Perf", cat.groups)


if __name__ == "__main__":
    unittest.main()
