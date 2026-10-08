"""Tests for scripts/world_checks.py (the world export's machine checks).

    python scripts/tests/test_world_checks.py

Pure inputs: no game files, UnityPy or numpy needed (T-0134, T-0135).
"""
import importlib.util
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("world_checks", os.path.join(HERE, "..", "world_checks.py"))
W = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(W)

YACHT = ["prefab\tyachtWobblePrefab(Clone)\t1 1 1\n", "part\trender\t0\tHull\t100\t1 0 0 0 0 1 0 0 0 0 1 0\tBoatHull\n",
         "greeble\tyachtWobblePrefab(Clone)\t10 0 -5\t0 0 0 1\n"]


def folder(**files):
    return {os.path.join("world", n.replace("_", "-", 1) + ".txt"): v for n, v in files.items()}


class PlacedDumps(unittest.TestCase):
    def test_one_placed_dump_per_object_is_clean(self):
        d = folder(placed_yacht=YACHT, placed_boat=[YACHT[2].replace("yachtWobble", "rowboat")])
        self.assertEqual({}, W.duplicate_placements(d))
        self.assertIsNone(W.duplicates_message({}))

    def test_a_diagnostic_dump_of_the_same_object_is_found(self):
        # Same object dumped again at another bob position: not caught by "same position".
        again = [YACHT[0], YACHT[2].replace("10 0 -5", "10 0.4 -5")]
        d = folder(placed_yacht=YACHT, placed_yacht2=again, placed_yacht3=again)
        dups = W.duplicate_placements(d)
        self.assertEqual(["yachtWobblePrefab(Clone)"], list(dups))
        self.assertEqual(3, len(dups["yachtWobblePrefab(Clone)"]))
        msg = W.duplicates_message(dups)
        self.assertTrue(msg.startswith("WHAT:"))
        self.assertIn("placed-yacht2.txt", msg)
        self.assertIn("WHY:", msg)
        self.assertIn("FIX:", msg)

    def test_other_files_are_not_placed_dumps(self):
        d = {"world/greebles-surface.txt": YACHT, "world/diag-yacht.txt": YACHT, "world/placed-yacht.txt": YACHT}
        self.assertEqual({}, W.duplicate_placements(d))
        self.assertEqual(["world/placed-yacht.txt"], W.placed_files(d))

    def test_one_file_placing_an_object_twice_counts(self):
        d = {"world/placed-yacht.txt": YACHT + [YACHT[2]]}
        self.assertEqual(1, len(W.duplicate_placements(d)))


class NearBlackTextures(unittest.TestCase):
    def test_threshold(self):
        self.assertTrue(W.is_near_black(0.0))
        self.assertTrue(W.is_near_black(W.NEAR_BLACK))
        self.assertFalse(W.is_near_black(W.NEAR_BLACK + 0.5))
        self.assertFalse(W.is_near_black(96.0))

    def test_the_one_known_black_texture_passes(self):
        self.assertIsNone(W.black_message({"BlackFadeIntoCaves (t/12)": 0.0}, 523))
        self.assertIsNone(W.black_message({}, 523))

    def test_one_more_fails_and_names_them(self):
        black = {"BlackFadeIntoCaves (t/12)": 0.0, "Concrete (t/40)": 0.3}
        msg = W.black_message(black, 523)
        self.assertTrue(msg.startswith("WHAT: 2 of 523 textures"))
        self.assertIn("Concrete (t/40)", msg)
        self.assertIn("WHY:", msg)
        self.assertIn("FIX:", msg)

    def test_the_regression_19_black_textures_fails(self):
        black = {"tex%d (t/%d)" % (i, i): 0.0 for i in range(20)}   # 19 + the meant one
        msg = W.black_message(black, 523)
        self.assertIn("20 of 523", msg)
        self.assertEqual(20, msg.count("(mean 0.00)"))   # the worst 20 are listed


if __name__ == "__main__":
    unittest.main()
