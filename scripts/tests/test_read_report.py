"""Tests for scripts/read-report.py, on a small zip built in memory.

    python scripts/tests/test_read_report.py
"""
import importlib.util
import io
import os
import unittest
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("read_report", os.path.join(HERE, "..", "read-report.py"))
R = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(R)

REPORT = """ForestOverlay QA list (2026-09-25, v0.24.43)
Tester: maks
Plugin: v0.24.145
Written: 2026-09-27 20:58
Pass 1, fail 0, skipped 0, not answered 1

Resetting
1) [P] Swing, then press F7.
2) [-] Same, but press F7 right as you click.

Note: elevator door opens while still running

Marks this session: 1
  MARK #1: 20:57:09, at (-515.2, 707.5, -1971.9), in a cave, spot 's-175b9deb25e0'
"""

LOG = """[Message:   BepInEx] BepInEx 5.4.23.5 - TheForest
[Info   :ForestOverlay] ForestOverlay v0.24.145 loaded.
[Info   :ForestOverlay] System: Intel CPU (8 threads), 32 GB
[Warning:ForestOverlay] Slow tick: 'collectibles' took 7.4 ms (a visible hitch if it repeats).
[Warning:ForestOverlay] Slow tick: 'collectibles' took 77.6 ms (a visible hitch if it repeats).
[Warning:ForestOverlay] Slow tick: 'deaths' took 51.0 ms (a visible hitch if it repeats).
[Info   :ForestOverlay] Perf (30 s): 203.0 fps, worst 265 ms, 3 over 50 ms, GC x7
[Info   :ForestOverlay] Perf (30 s): 114.0 fps, worst 900 ms, 10 over 50 ms, GC x7
[Warning:ForestOverlay] Pickup gone, inventory unchanged: Rock (53) at (697.2, 73.0, 175.9), count 5 -> 5.
[Warning:ForestOverlay] Pickup gone, inventory unchanged: Rock (54) at (1.0, 2.0, 3.0), count 4 -> 4.
[Info   :ForestOverlay] Restart 's-e6': closing the game's menu failed: Argument is out of range.
Parameter name: index.
[Info   :ForestOverlay] Savestate restore start state of 'x': sun snapped.
[Info   :ForestOverlay] Savestate restore start state of 'x' in place: done in 222 ms
[Error  :ForestOverlay] Update() threw: NullReferenceException
[Info   :ForestOverlay] MARK #1: 20:57:09, at (-515.2, 707.5, -1971.9)
"""


def make_zip():
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        z.writestr("report.txt", REPORT)
        z.writestr("logs/LogOutput-2026-09-27_20-16-14.log", LOG)
        z.writestr("savestates/segments/s-1.fosave", b"x" * 1024)
        z.writestr("unity/output_log.txt", "NullReferenceException: Object reference not set\n  at Foo.Bar () [0x0]\n")
    buf.seek(0)
    return buf


class ParseLog(unittest.TestCase):
    def setUp(self):
        self.info = R.parse_log(LOG.splitlines())

    def test_version_and_system(self):
        self.assertEqual(self.info["version"], "v0.24.145")
        self.assertEqual(self.info["system"], "Intel CPU (8 threads), 32 GB")

    def test_slow_ticks(self):
        self.assertEqual(self.info["slow"]["collectibles"], (2, 77.6))
        self.assertEqual(self.info["slow"]["deaths"], (1, 51.0))

    def test_perf(self):
        self.assertEqual(self.info["perf"], [(203.0, 265, 3), (114.0, 900, 10)])

    def test_errors_take_continuations(self):
        errs = self.info["errors"]
        self.assertEqual(len(errs), 2)
        self.assertIn("failed: Argument is out of range. | Parameter name: index.", errs[0])
        self.assertIn("Update() threw", errs[1])

    def test_warnings_group(self):
        groups = R.group(self.info["warnings"])
        self.assertEqual(len(groups), 1)
        self.assertEqual(groups[0][0], 2)

    def test_actions_skip_restore_detail(self):
        acts = self.info["actions"]
        self.assertFalse(any("sun snapped" in a for a in acts))
        self.assertTrue(any("done in 222 ms" in a for a in acts))
        self.assertTrue(acts[-1].startswith("MARK #1"))


class Summary(unittest.TestCase):
    def test_whole_zip(self):
        out = "\n".join(R.summarise(make_zip()))
        for want in ("Tester: maks", "Plugin: v0.24.145", "Answered:", "1) [P] Swing",
                     "Note: elevator door", "MARK #1", "1 savestates",
                     "Slow ticks: collectibles 2x (worst 77.6 ms)",
                     "fps min 114 / median 203, worst frame 900 ms, 13 frames over 50 ms",
                     "Errors (2):", "unity/output_log.txt: 1 exception", "at Foo.Bar"):
            self.assertIn(want, out)
        self.assertNotIn("2) [-]", out)

    def test_bad_zip(self):
        self.assertEqual(R.main([os.path.join(HERE, "test_read_report.py")]), 1)


if __name__ == "__main__":
    unittest.main()
