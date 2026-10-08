"""CI runs every scripts/tests file (T-0147): build.yml discovers them instead of listing them."""
import os
import re
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
WORKFLOWS = os.path.join(HERE, "..", "..", ".github", "workflows")


def read(name):
    with open(os.path.join(WORKFLOWS, name), encoding="utf-8") as f:
        return f.read()


class ScriptTestsInCi(unittest.TestCase):
    def test_build_discovers_every_test_file(self):
        self.assertRegex(read("build.yml"), r"python3? -m unittest discover (-s )?scripts/tests")

    def test_build_lists_no_single_test_file(self):
        # a hand-kept list is how test_agent_cost / test_watch_deploy went unrun
        self.assertEqual(re.findall(r"scripts/tests/test_\w+\.py", read("build.yml")), [])

    def test_discovery_matches_the_files(self):
        names = [n for n in os.listdir(HERE) if re.match(r"test.*\.py$", n)]
        self.assertTrue(names, "unittest discover's default pattern finds nothing")


if __name__ == "__main__":
    unittest.main()
