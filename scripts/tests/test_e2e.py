"""Tests for scripts/e2e.py's pure parts: the bridge transcript, values, lists, the clean-up of
sent.txt and the local attempt reports. The journeys themselves need the game (python scripts/e2e.py).

    python scripts/tests/test_e2e.py
"""
import importlib.util
import os
import tempfile
import unittest
from pathlib import Path

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("e2e_under_test", os.path.join(HERE, "..", "e2e.py"))
E = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(E)

RUN = """> status
scene 'ForestMain_v08', frame 68559, time 348.7, timeScale 1
player (451.92, 78.48, -3.14)
savestates idle, current spot none
ok (0 ms)
> get BepInEx_Manager OverlayPlugin._host._modules[17]._report.Flags
OverlayPlugin._host._modules[17]._report.Flags = List<String> (1)  (List<String>)
  [0] "the test bridge is on"
ok (1 ms)
> call #5 Foo.Bar
error: no method Bar on Foo
> restore e2e-a load
(no reply yet - still running in game)
-- 4 command(s), 1 error(s), incomplete"""


class Transcript(unittest.TestCase):
    def test_blocks(self):
        b = E.parse_run(RUN)
        self.assertEqual([x.cmd for x in b], ["status", "get BepInEx_Manager OverlayPlugin._host._modules[17]._report.Flags",
                                              "call #5 Foo.Bar", "restore e2e-a load"])
        self.assertTrue(b[0].closed and b[0].error is None)
        self.assertEqual(len(b[0].lines), 3)
        self.assertEqual(b[2].error, "no method Bar on Foo")
        self.assertFalse(b[3].closed)
        self.assertEqual(b[3].lines, [])

    def test_status_values(self):
        b = E.parse_run(RUN)
        self.assertEqual(E.parse_vec(b[0].lines[1]), (451.92, 78.48, -3.14))
        self.assertEqual(E.parse_items(b[1].lines), ["the test bridge is on"])


class Values(unittest.TestCase):
    def test_bool_and_string(self):
        self.assertEqual(E.parse_value("X._enabled.Value = True  (Boolean)"), "True")
        self.assertEqual(E.parse_value('X.Id = "mainwindow"  (String)'), "mainwindow")
        self.assertEqual(E.parse_value('X.Message = "up to date (v0.24.249)"  (String)'), "up to date (v0.24.249)")
        self.assertEqual(E.parse_value('X.GameHash = ""  (String)'), "")

    def test_list_with_none(self):
        self.assertEqual(E.parse_items(["X.Cheats = List<String> (0)  (List<String>)"]), [])


class Cleanup(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.old = E.CONFIG
        E.CONFIG = Path(self.tmp.name)

    def tearDown(self):
        E.CONFIG = self.old
        self.tmp.cleanup()

    def test_forget_sent_keeps_bom_and_others(self):
        d = E.CONFIG / "uploads" / "attempts"
        d.mkdir(parents=True)
        (d / "sent.txt").write_bytes(b"\xef\xbb\xbfa-1|red|t\na-2|green|t\na-3|red|t\n")
        self.assertEqual(E.forget_sent({"a-1", "a-3"}), 2)
        self.assertEqual((d / "sent.txt").read_bytes(), b"\xef\xbb\xbfa-2|green|t\n")
        self.assertEqual(E.forget_sent({"a-9"}), 0)

    def test_own_files_finds_only_e2e_attempt_reports(self):
        r = E.CONFIG / "run-reports"
        r.mkdir()
        (r / "attempt-1.txt").write_text("[runreport]\nstarted = Any% from 'e2e run (Normal)' (own spot e2e-run-normal, x)\n")
        (r / "attempt-1.log").write_text("log")
        (r / "attempt-2.txt").write_text("[runreport]\nstarted = Any% from 'mine' (own spot s-123, x)\n")
        (E.CONFIG / "segments").mkdir()
        (E.CONFIG / "segments" / "e2e.txt").write_text("x")
        (E.CONFIG / "segments" / "my-segments.txt").write_text("x")
        names = sorted(p.name for p in E.own_files())
        self.assertEqual(names, ["attempt-1.log", "attempt-1.txt", "e2e.txt"])


class CrashFolders(unittest.TestCase):
    """T-0189: a Unity crash folder made during a run is found, named and fails the run."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def crash(self, name, dmp=True):
        d = self.root / name
        d.mkdir()
        if dmp:
            (d / "crash.dmp").write_bytes(b"MDMP")
        return d

    def test_finds_only_dated_folders_holding_a_dump(self):
        self.crash("2026-10-07_115524")
        self.crash("2026-10-07_120000", dmp=False)     # no crash.dmp: not a crash
        self.crash("2026-10-07")                       # not YYYY-MM-DD_HHMMSS
        (self.root / "BepInEx").mkdir()
        self.assertEqual(["2026-10-07_115524"], sorted(E.crash_folders(self.root)))

    def test_a_folder_made_after_the_baseline_is_new(self):
        self.crash("2026-10-07_115524")
        before = set(E.crash_folders(self.root))
        self.assertEqual([], E.new_crashes(self.root, before))
        later = self.crash("2026-10-08_090102")
        self.crash("2026-10-08_080000")
        self.assertEqual(["2026-10-08_080000", later.name], [d.name for d in E.new_crashes(self.root, before)])

    def test_a_missing_game_root_has_no_crashes(self):
        self.assertEqual({}, E.crash_folders(self.root / "nowhere"))
        self.assertEqual([], E.new_crashes(self.root / "nowhere", set()))

    def test_new_crash_uses_the_game_root_and_the_suite_baseline(self):
        old_root, old_base = E.GAME_ROOT, E.CRASH_BASELINE
        try:
            E.GAME_ROOT = self.root
            E.CRASH_BASELINE = set()
            self.assertIsNone(E.new_crash())
            d = self.crash("2026-10-08_090102")
            self.assertEqual(d, E.new_crash())
            E.CRASH_BASELINE = {d.name}
            self.assertIsNone(E.new_crash())
        finally:
            E.GAME_ROOT, E.CRASH_BASELINE = old_root, old_base


if __name__ == "__main__":
    unittest.main()
