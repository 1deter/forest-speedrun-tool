"""Tests for scripts/site-smoke.py's pure parts (no site, no browser).

    python scripts/tests/test_site_smoke.py

The whole smoke runs in CI (.github/workflows/site.yml); a planted page
error failing it was checked by hand (T-0011 evidence).
"""
import importlib.util
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("site_smoke", os.path.join(HERE, "..", "site-smoke.py"))
S = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(S)

ORIGIN = "http://127.0.0.1:5000"


class PageProblems(unittest.TestCase):
    def test_clean_page_has_none(self):
        self.assertEqual([], S.page_problems([], [], [], [], set(), ORIGIN))

    def test_console_errors_and_exceptions_count(self):
        out = S.page_problems(["CSP violation"], ["boom"], [], [], set(), ORIGIN)
        self.assertEqual(["console: CSP violation", "exception: boom"], out)

    def test_answered_request_is_not_failed(self):
        # Chromium calls a 204 (no aerial layer) aborted; it was answered.
        url = ORIGIN + "/aerial/aerial.json"
        self.assertEqual([], S.page_problems([], [], [(url, "net::ERR_ABORTED")], [], {url}, ORIGIN))

    def test_unanswered_same_origin_request_fails(self):
        url = ORIGIN + "/app.js"
        out = S.page_problems([], [], [(url, "net::ERR_CONNECTION_REFUSED")], [], set(), ORIGIN)
        self.assertEqual(1, len(out))
        self.assertIn("/app.js", out[0])

    def test_other_origins_are_ignored(self):
        font = "https://fonts.googleapis.com/css2"
        self.assertEqual([], S.page_problems([], [], [(font, "net::ERR_FAILED")], [(font, 500)], set(), ORIGIN))

    def test_error_status_counts(self):
        out = S.page_problems([], [], [], [(ORIGIN + "/api/spots/x", 404)], set(), ORIGIN)
        self.assertEqual(["HTTP 404: " + ORIGIN + "/api/spots/x"], out)


class SiteEnv(unittest.TestCase):
    def test_no_webhook_no_sync_temp_data(self):
        os.environ["FOREST_DISCORD_WEBHOOK"] = "https://discord.example/hook"
        try:
            env = S.site_env("C:/tmp/data")
        finally:
            del os.environ["FOREST_DISCORD_WEBHOOK"]
        self.assertNotIn("FOREST_DISCORD_WEBHOOK", env)
        self.assertEqual("off", env["FOREST_SRC_SYNC"])
        self.assertEqual("C:/tmp/data", env["FOREST_DATA"])
        self.assertTrue(env["FOREST_ADMIN_TOKEN"])


class Fixture(unittest.TestCase):
    def test_fixture_matches_the_constants(self):
        with open(S.FIXTURE, encoding="utf-8") as f:
            text = f.read()
        self.assertIn("id       = " + S.SPOT, text)
        self.assertIn(S.RUNNER, text)


if __name__ == "__main__":
    unittest.main()
