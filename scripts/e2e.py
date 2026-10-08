#!/usr/bin/env python3
"""The end-to-end bridge suite: golden journeys through the live game (docs/harness.md 8a, 8b, 10b, 10c).

    python scripts/e2e.py                  # every journey in tests/e2e/, in name order
    python scripts/e2e.py restart timed    # only journeys whose name contains one of these
    python scripts/e2e.py --smoke          # the post-release smoke: the journeys marked SMOKE
    python scripts/e2e.py --smoke --update --release v0.24.N   # from the release skill
    python scripts/e2e.py --list           # what there is
    python scripts/e2e.py --evidence       # record each passed journey's tasks (tasks.py evidence --by e2e)

Each journey is a file in tests/e2e/ (NAME, SMOKE, TASKS, run(t)). The game is
driven through the forest MCP server's own code: `forest-bridge-mcp --call
<tool> <json>` (built into tools/BridgeMcp/bin/cli, a copy no running MCP
server locks). Every wait has a timeout; nothing waits on a person.

Test hygiene is code (10b): before the first journey the save slot is copied
to SlotN.e2e-backup, uploads are turned off and the config, git status, god
mode and the main window are noted; after the last one - pass or fail - all
of it is put back and compared, the journeys' own files (segments/e2e.txt,
e2e-* savestates, runs/e2e-*) are deleted, and every run mode attempt the
journeys started is deleted from the site through the admin API (10c,
FOREST_SITE_ADMIN_TOKEN, never printed). A difference is a failure.

The report: tests/e2e/reports/<time>.md (git-ignored), one line per check.
Exit code 0 = every journey passed, 1 = a failure, 2 = could not start.
"""

import argparse
import datetime
import hashlib
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
JOURNEYS = ROOT / "tests" / "e2e"
REPORTS = JOURNEYS / "reports"
MCP_DIR = ROOT / "tools" / "BridgeMcp"
CLI_DLL = MCP_DIR / "bin" / "cli" / "forest-bridge-mcp.dll"
DEFAULT_GAME_ROOT = r"G:\SteamLibrary\steamapps\common\The Forest"
SITE = "https://forest.deter.cloud"
PLUGIN = "BepInEx_Manager"
HOST = "OverlayPlugin._host"
SLOT = 1  # the known save every journey loads (docs/bridge.md *Loading a save*)
PREFIX = "e2e-"  # every file, spot and savestate a journey makes starts with this


def user_env(name):
    """A User-scope variable: tool shells do not inherit them (router: Commands)."""
    v = os.environ.get(name)
    if v:
        return v
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
            return winreg.QueryValueEx(k, name)[0] or None
    except (ImportError, OSError):
        return None


GAME_ROOT = Path(user_env("FOREST_ROOT") or DEFAULT_GAME_ROOT)
CONFIG = GAME_ROOT / "BepInEx" / "config" / "ForestOverlay"
BRIDGE = Path(user_env("FOREST_BRIDGE") or CONFIG / "bridge")
LOG = GAME_ROOT / "BepInEx" / "LogOutput.log"
CFG = GAME_ROOT / "BepInEx" / "config" / "com.deter.forestoverlay.cfg"
SAVES = Path(os.environ.get("USERPROFILE", "")) / "AppData" / "LocalLow" / "SKS" / "TheForest"


class Fail(Exception):
    """A check that did not hold: the journey stops, the suite goes on."""


class Abort(Exception):
    """The suite cannot go on (no game, no bridge): later journeys are skipped."""


# ---------------------------------------------------------------------------
# The game, through the MCP server's tools


def ensure_cli():
    """Builds the --call copy when a source file is newer than it."""
    newest = max(p.stat().st_mtime for p in MCP_DIR.glob("*.cs"))
    newest = max(newest, (MCP_DIR / "BridgeMcp.csproj").stat().st_mtime)
    if CLI_DLL.exists() and CLI_DLL.stat().st_mtime >= newest:
        return
    print("e2e: building tools/BridgeMcp into bin/cli ...", flush=True)
    r = subprocess.run(["dotnet", "build", str(MCP_DIR), "-c", "Release", "-o", str(CLI_DLL.parent),
                        "-nologo", "-v", "q"], capture_output=True, text=True)
    if r.returncode != 0:
        raise Abort("tools/BridgeMcp did not build:\n" + (r.stdout + r.stderr)[-2000:])


class Block:
    def __init__(self, cmd):
        self.cmd, self.lines, self.error, self.closed = cmd, [], None, False

    def __repr__(self):
        return "> %s\n%s%s" % (self.cmd, "\n".join(self.lines), "\nerror: " + self.error if self.error else "")


def parse_run(text):
    """The `run` tool's transcript -> blocks (Tools.Run / BridgeReply.Text)."""
    blocks, cur = [], None
    for line in text.replace("\r", "").split("\n"):
        if line.startswith("> "):
            cur = Block(line[2:])
            blocks.append(cur)
        elif cur is None or re.match(r"^-- \d+ command\(s\)", line):
            continue
        elif re.match(r"^ok \([^)]*\)$", line):
            cur.closed = True
        elif line.startswith("error: "):
            cur.error, cur.closed = line[7:], True
        elif line != "(no reply yet - still running in game)":
            cur.lines.append(line)
    return blocks


def parse_value(line):
    """`path = value  (Type)` -> value (ObjectProbe.Get)."""
    v = line.split(" = ", 1)[1] if " = " in line else line
    v = re.sub(r"  \([^()]*\)$", "", v).strip()
    return v[1:-1] if len(v) >= 2 and v[0] == v[-1] == '"' else v


def parse_items(lines):
    """A list's `get`: the value line, then `  [i] item` lines."""
    return [re.sub(r"^\s*\[\d+\]\s*", "", l).strip().strip('"') for l in lines[1:] if re.match(r"^\s*\[\d+\]", l)]


def parse_vec(text):
    nums = re.findall(r"-?\d+(?:\.\d+)?", text)
    if len(nums) < 3:
        raise Fail("no x, y, z in %r" % text)
    return tuple(float(n) for n in nums[:3])


def dist(a, b):
    return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5


class Game:
    def __init__(self):
        self._mods = None

    def call(self, tool, args=None, timeout=900):
        """One MCP tool; its text. An error result raises Fail."""
        try:
            r = subprocess.run(["dotnet", str(CLI_DLL), "--call", tool, json.dumps(args or {})],
                               capture_output=True, text=True, encoding="utf-8", timeout=timeout)
        except subprocess.TimeoutExpired:
            raise Fail("%s took over %d s" % (tool, timeout))
        try:
            res = json.loads(r.stdout.strip().splitlines()[-1])
        except (ValueError, IndexError):
            raise Abort("%s: no result from the MCP CLI (%s)" % (tool, (r.stderr or r.stdout)[-500:]))
        text = "\n".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")
        if res.get("isError"):
            raise Fail("%s: %s" % (tool, text))
        if tool == "game":
            self._mods = None
        return text

    def run(self, *lines, timeout=None, errors_ok=False):
        args = {"commands": list(lines)}
        if timeout:
            args["timeout_s"] = timeout
        try:
            text = self.call("run", args, timeout=(timeout or 600) + 60)
        except Fail as e:
            if "not delivered" in str(e) or "not running" in str(e):
                raise Abort(str(e))
            raise
        blocks = parse_run(text)
        if len(blocks) < len(lines) or not all(b.closed for b in blocks):
            raise Fail("the bridge did not finish %r:\n%s" % (list(lines), text[-1500:]))
        if not errors_ok:
            for b in blocks:
                if b.error:
                    raise Fail("`%s`: %s" % (b.cmd, b.error))
        return blocks

    def get(self, target, path):
        b = self.run('get %s %s' % (target, quote(path)))[0]
        return parse_value(b.lines[0]) if b.lines else ""

    def mod(self, module_id):
        """`BepInEx_Manager OverlayPlugin._host._modules[i]` path by module id (indexes move)."""
        if self._mods is None:
            n = int(self.get(PLUGIN, HOST + ".Count"))
            blocks = self.run(*["get %s %s._modules[%d].Id" % (PLUGIN, HOST, i) for i in range(n)])
            self._mods = {parse_value(b.lines[0]): i for i, b in enumerate(blocks)}
        if module_id not in self._mods:
            raise Fail("no module '%s' (have %s)" % (module_id, ", ".join(sorted(self._mods))))
        return "%s._modules[%d]" % (HOST, self._mods[module_id])

    def status(self):
        lines = self.run("status")[0].lines
        s = {"raw": "\n".join(lines)}
        for line in lines:
            m = re.match(r"scene '([^']*)'", line)
            if m:
                s["scene"] = m.group(1)
            if line.startswith("player "):
                s["player"] = None if "not found" in line else parse_vec(line)
            m = re.match(r"savestates (\w+)", line)
            if m:
                s["savestates"] = m.group(1)
        return s

    def pos(self):
        p = self.status().get("player")
        if p is None:
            raise Fail("no player")
        return p


def quote(arg):
    return '"%s"' % arg if " " in arg else arg


# ---------------------------------------------------------------------------
# The log


class Log:
    def mark(self):
        try:
            return LOG.stat().st_size
        except OSError:
            return 0

    def since(self, mark):
        try:
            with open(LOG, "rb") as f:
                size = f.seek(0, 2)
                f.seek(mark if size >= mark else 0)  # a launch starts a new file
                return f.read().decode("utf-8", "replace")
        except OSError:
            return ""

    def wait(self, pattern, mark, timeout, what=None):
        rx = re.compile(pattern)
        end = time.time() + timeout
        while True:
            m = rx.search(self.since(mark))
            if m:
                return m
            crash = new_crash()
            if crash:
                raise Abort("the game crashed (Unity's crash folder %s) while waiting for /%s/%s - "
                            "python scripts/symbolize-crash.py \"%s\"" % (
                                crash, pattern, " (" + what + ")" if what else "", crash / "crash.dmp"))
            if time.time() > end:
                raise Fail("no log line /%s/ within %d s%s" % (pattern, timeout, " (" + what + ")" if what else ""))
            time.sleep(0.5)


CRASH_FOLDER = re.compile(r"^\d{4}-\d{2}-\d{2}_\d{6}$")   # Unity's crash folder: YYYY-MM-DD_HHMMSS


def crash_folders(root=None):
    """Unity's crash folders (`<date>_<time>/crash.dmp` beside TheForest.exe) in the game root, by name."""
    root = Path(root) if root is not None else GAME_ROOT
    try:
        found = [d for d in root.iterdir() if CRASH_FOLDER.match(d.name) and (d / "crash.dmp").is_file()]
    except OSError:
        return {}
    return {d.name: d for d in found}


CRASH_BASELINE = set(crash_folders())   # what the game root held before this suite ran (T-0189)


def new_crashes(root=None, before=None):
    """The crash folders made since `before` (default: when the suite started), oldest first."""
    before = CRASH_BASELINE if before is None else before
    return [d for name, d in sorted(crash_folders(root).items()) if name not in before]


def new_crash():
    """The first crash folder made since the suite started, or None."""
    found = new_crashes()
    return found[0] if found else None


# ---------------------------------------------------------------------------
# Shared steps


def in_game(g):
    s = g.status()
    return s.get("player") is not None and s.get("scene", "").startswith("ForestMain")


def load_slot(t, slot=SLOT, timeout=150):
    """Title screen -> the slot's save (docs/bridge.md *Loading a save*)."""
    g = t.game
    if in_game(g):
        return
    mark = t.log.mark()
    lines = g.run("type TitleScreen")[0].lines
    m = re.search(r"#(-?\d+)", "\n".join(lines))
    if not m:
        raise Fail("no TitleScreen on screen: %s" % lines)
    h = "#" + m.group(1)
    g.run("call %s TitleScreen.OnLoad" % h, "wait 1", "call %s TitleScreen.OnSlotSelection %d" % (h, slot))
    t.log.wait(r"Load \d+ finished", mark, timeout, "slot %d loading" % slot)
    end = time.time() + 60
    while not in_game(g):
        if time.time() > end:
            raise Fail("slot %d loaded but no player in ForestMain" % slot)
        time.sleep(1)
    t.ok("slot %d loaded in %.0f s" % (slot, time.time() - t.started))


def to_title(t, timeout=90):
    """Back to the title screen without closing the game (docs/bridge.md: a reset)."""
    g = t.game
    g.run("call static:UnityEngine.SceneManagement.SceneManager LoadScene TitleScene")
    end = time.time() + timeout
    while not (g.status().get("scene") == "TitleScene" and
               re.search(r"#-?\d+", "\n".join(g.run("type TitleScreen")[0].lines))):
        if time.time() > end:
            raise Fail("no title screen after %d s" % timeout)
        time.sleep(1)


def segments(t, text):
    """Adds the journey's spots to segments/e2e.txt (the clean-up deletes it) and reloads the list."""
    path = CONFIG / "segments" / "e2e.txt"
    old = path.read_text(encoding="utf-8") if path.exists() else "# e2e test spots - scripts/e2e.py deletes this file\n"
    path.write_text(old + "\n" + text.strip() + "\n", encoding="utf-8")
    t.game.run("call %s %s.Reload" % (PLUGIN, t.game.mod("practice")))


def savestate_header(path):
    head = {}
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            if " = " not in line:
                if head:
                    break
                continue
            k, v = line.rstrip("\n").split(" = ", 1)
            head[k.strip()] = v
            if k.strip() == "data":
                break
    return head


# ---------------------------------------------------------------------------
# A journey's context


class Context:
    def __init__(self, game, log, journey):
        self.game, self.log, self.journey = game, log, journey
        self.lines, self.started, self.shots, self.attempts = [], time.time(), [], []

    def ok(self, what):
        self.lines.append("ok    " + what)
        print("  ok    " + what, flush=True)

    def note(self, what):
        self.lines.append("note  " + what)
        print("  note  " + what, flush=True)

    def check(self, cond, what, detail=""):
        if not cond:
            raise Fail(what + (": " + detail if detail else ""))
        self.ok(what)

    def near(self, pos, want, tol, what):
        d = dist(pos, want)
        self.check(d <= tol, "%s within %.1f m (%.2f m)" % (what, tol, d),
                   "at %s, want %s" % (fmt(pos), fmt(want)))

    def cut_bush(self, near, radius=6):
        """Cuts the bush whose view is nearest `near` (BushDamage.Hit 5); its scene object's name."""
        g = self.game
        g.run("tp %.2f %.2f %.2f" % near, "wait 1.5")
        views = g.run("type BushDamage %d" % radius)[0].lines
        m = re.search(r"#(-?\d+)", "\n".join(views))
        if not m:
            raise Fail("no bush within %d m of %s" % (radius, fmt(near)))
        scene = [l for l in g.run("type LOD_Bush %d" % radius)[0].lines if "Nature_Spawned/" in l]
        if not scene:
            raise Fail("the bush view has no Nature_Spawned scene object")
        name = re.search(r"Nature_Spawned/(\S+)", scene[0]).group(1)
        # The control: `has` finds it while it stands, so "still cut" later means something.
        self.check(self.has(name), "bush %s standing before the cut" % name)
        g.run("call #%s BushDamage.Hit 5" % m.group(1), "wait 1.5")
        self.check(not self.has(name), "bush %s cut" % name)
        return name

    def has(self, name):
        """The bush stands: an active Nature_Spawned/<name>. Not `find all` - with savestates in use
        NatureKeeper keeps each cut bush's copy under an inactive holder (Game/NatureKeeper.cs)."""
        lines = self.game.run("find %s" % name)[0].lines
        return any(re.search(r"\sNature_Spawned/%s\s" % re.escape(name), l + " ") for l in lines)

    def shot(self, name):
        b = self.game.run("shot " + PREFIX + name)[0]
        path = BRIDGE / (PREFIX + name + ".png")
        end = time.time() + 10
        while not (path.exists() and path.stat().st_size > 0) and time.time() < end:
            time.sleep(0.3)
        self.check(path.exists() and path.stat().st_size > 0, "shot %s" % path.name, " ".join(b.lines))
        self.shots.append(path)
        return path


def fmt(v):
    return "(%s)" % ", ".join("%.2f" % x for x in v)


# ---------------------------------------------------------------------------
# Test hygiene (10b, 10c)


def tree_digest(folder):
    h, size = hashlib.sha1(), 0
    for p in sorted(folder.rglob("*")):
        if p.is_file():
            data = p.read_bytes()
            size += len(data)
            h.update(str(p.relative_to(folder)).encode() + b"\0" + data)
    return h.hexdigest(), size


def changed_files(before, after):
    """The relative paths that differ between two folders (added, gone or changed)."""
    def files(d):
        return {str(p.relative_to(d)): p for p in d.rglob("*") if p.is_file()}
    a, b = files(before), files(after)
    return sorted(k for k in set(a) | set(b) if k not in a or k not in b or a[k].read_bytes() != b[k].read_bytes())


def slot_dir():
    for profile in sorted(SAVES.glob("*")):
        d = profile / "SinglePlayer" / ("Slot%d" % SLOT)
        if d.is_dir():
            return d
    return None


def git_status():
    return subprocess.run(["git", "status", "--porcelain"], cwd=ROOT, capture_output=True, text=True).stdout


def own_files():
    found = [CONFIG / "segments" / "e2e.txt"]
    found += list((CONFIG / "savestates").glob(PREFIX + "*.fosave"))
    found += list((CONFIG / "savestates" / "segments").glob(PREFIX + "*.fosave"))
    found += [p for p in (CONFIG / "runs").glob(PREFIX + "*")]
    # A run mode attempt's local report (RunModeModule): its `started` names the e2e run spot.
    for txt in (CONFIG / "run-reports").glob("attempt-*.txt"):
        try:
            if "(own spot %s" % PREFIX in txt.read_text(encoding="utf-8-sig", errors="replace")[:600]:
                found += [txt, txt.with_suffix(".log")]
        except OSError:
            pass
    return [p for p in found if p.exists()]


def forget_sent(attempts):
    """Drops deleted test attempts from the Runs tab's list (uploads/attempts/sent.txt: `id|state|time`)."""
    path = CONFIG / "uploads" / "attempts" / "sent.txt"
    if not attempts or not path.exists():
        return 0
    raw = path.read_bytes()
    lines = raw.split(b"\n")
    keep = [l for l in lines if l.split(b"|", 1)[0].lstrip(b"\xef\xbb\xbf").strip().decode("utf-8", "replace") not in attempts]
    if len(keep) != len(lines):
        if raw.startswith(b"\xef\xbb\xbf") and not keep[0].startswith(b"\xef\xbb\xbf"):
            keep[0] = b"\xef\xbb\xbf" + keep[0]
        path.write_bytes(b"\n".join(keep))
    return len(lines) - len(keep)


def remove(paths):
    for p in paths:
        if p.is_dir():
            shutil.rmtree(p, ignore_errors=True)
        else:
            try:
                p.unlink()
            except OSError:
                pass


class Hygiene:
    def __init__(self, game):
        self.g, self.problems, self.lines = game, [], []
        self.slot = self.backup = None

    def before(self):
        self.git = git_status()
        self.cfg_bytes = CFG.read_bytes() if CFG.exists() else b""
        self.cfg = self.cfg_bytes.decode("utf-8-sig")
        remove(own_files())  # left by a run that was killed
        for old in BRIDGE.glob(PREFIX + "*.png"):
            remove([old])
        self.slot = slot_dir()
        if self.slot:
            self.backup = self.slot.with_name(self.slot.name + ".e2e-backup")
            if self.backup.exists():
                raise Abort("%s exists - an earlier e2e run did not finish: compare it with %s, put it back "
                            "if the slot changed, delete it, then run again" % (self.backup, self.slot.name))
            shutil.copytree(self.slot, self.backup)
            self.slot_digest = tree_digest(self.slot)
            self.lines.append("slot %s backed up (%d bytes)" % (self.slot.name, self.slot_digest[1]))
        up = self.g.mod("upload")
        self.uploads = self.g.get(PLUGIN, up + "._enabled.Value")
        self.g.run("set %s %s._enabled.Value false" % (PLUGIN, up))
        self.god = self.g.get("static:Cheats", "GodMode")
        self.energy = self.g.get("static:Cheats", "InfiniteEnergy")
        self.panel = self.g.get(PLUGIN, self.g.mod("mainwindow") + ".PanelOpen")
        self.lines.append("uploads were %s - off for the suite; god mode %s, infinite energy %s, window open %s"
                          % (self.uploads, self.god, self.energy, self.panel))

    def after(self, attempts):
        g = self.g
        remove(own_files())
        try:
            if in_game(g):
                g.run("call %s %s.Reload" % (PLUGIN, g.mod("practice")))
            g.run("set %s %s._enabled.Value %s" % (PLUGIN, g.mod("upload"), self.uploads.lower()))
            g.run("set static:Cheats GodMode %s" % self.god.lower(), "set static:Cheats InfiniteEnergy %s" % self.energy.lower())
            if g.get(PLUGIN, g.mod("mainwindow") + ".PanelOpen") != self.panel:
                g.run("call %s %s.TogglePanel" % (PLUGIN, g.mod("mainwindow")))
            self.lines.append("uploads, god mode, infinite energy and the window put back")
        except (Fail, Abort) as e:
            if "game: not running" in g.call("status") and self.cfg_bytes:
                # Closed (a crash): the config file is the settings' home - write back the one we found.
                CFG.write_bytes(self.cfg_bytes)
                self.lines.append("game closed: the config file written back as found (uploads %s)" % self.uploads)
            else:
                self.problems.append("could not put the game settings back: %s" % e)

        if self.slot:
            now = tree_digest(self.slot)
            if now == self.slot_digest:
                remove([self.backup])
                self.lines.append("slot %s unchanged (%d bytes); backup removed" % (self.slot.name, now[1]))
            else:
                changed = changed_files(self.backup, self.slot)
                remove([self.slot])
                shutil.copytree(self.backup, self.slot)
                ok = tree_digest(self.slot) == self.slot_digest
                if ok:
                    remove([self.backup])
                self.problems.append("slot %s changed during the suite (%d -> %d bytes; %s) - %s" % (
                    self.slot.name, self.slot_digest[1], now[1], ", ".join(changed) or "no file differs",
                    "put back from the backup" if ok else "put back, but it still differs: the backup is kept"))

        cfg = CFG.read_text(encoding="utf-8-sig") if CFG.exists() else ""
        # The values, not the text: BepInEx rewrites the file (order, comments) when it saves.
        a = set(l.strip() for l in self.cfg.splitlines() if "=" in l and not l.lstrip().startswith("#"))
        b = set(l.strip() for l in cfg.splitlines() if "=" in l and not l.lstrip().startswith("#"))
        if a != b:
            self.problems.append("the plugin's config changed: %s" % "; ".join(
                ["-" + x for x in sorted(a - b)] + ["+" + x for x in sorted(b - a)]))
        else:
            self.lines.append("config values unchanged" + (" (the file was rewritten)" if cfg != self.cfg else ""))

        if git_status() != self.git:
            self.problems.append("git status changed during the suite:\n" + git_status())
        else:
            self.lines.append("git status unchanged")

        left = own_files()
        if left:
            self.problems.append("still on disk: %s" % ", ".join(str(p) for p in left))
        self.delete_attempts(attempts)

    def delete_attempts(self, attempts):
        if not attempts:
            return
        token = user_env("FOREST_SITE_ADMIN_TOKEN")
        if not token:
            self.problems.append("FOREST_SITE_ADMIN_TOKEN not set - delete these test attempts by hand: %s"
                                 % ", ".join(attempts))
            return
        gone = []
        for a in attempts:
            req = urllib.request.Request("%s/api/admin/attempts/%s" % (SITE, a), method="DELETE",
                                         headers={"X-Admin-Token": token, "User-Agent": "forest-e2e"})
            try:
                with urllib.request.urlopen(req, timeout=20) as r:
                    self.lines.append("site: test attempt %s deleted (%d)" % (a, r.status))
                    gone.append(a)
            except urllib.error.HTTPError as e:
                if e.code == 404:
                    self.lines.append("site: test attempt %s was not on the site" % a)
                    gone.append(a)
                else:
                    self.problems.append("site: deleting attempt %s failed: HTTP %d" % (a, e.code))
            except urllib.error.URLError as e:
                self.problems.append("site: deleting attempt %s failed: %s" % (a, e.reason))
        n = forget_sent(gone)
        if n:
            self.g.run("set %s %s._recentDirty true" % (PLUGIN, self.g.mod("upload")), errors_ok=True)
            self.lines.append("Runs tab: %d deleted test attempt(s) dropped from uploads/attempts/sent.txt" % n)


# ---------------------------------------------------------------------------
# The suite


def load_journeys():
    # Journeys `import e2e`: this module, not a second copy whose Fail the
    # runner would not catch.
    sys.modules.setdefault("e2e", sys.modules[__name__])
    found = []
    for path in sorted(JOURNEYS.glob("[0-9][0-9]_*.py")):
        spec = importlib.util.spec_from_file_location("e2e_" + path.stem, path)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        mod.ID = path.stem[3:]
        found.append(mod)
    return found


def expected_version():
    m = re.search(r"<Version>([^<]+)</Version>", (ROOT / "ForestOverlay.csproj").read_text(encoding="utf-8-sig"))
    return m.group(1) if m else None


def record(cmd):
    r = subprocess.run([sys.executable, str(ROOT / "scripts" / "tasks.py")] + cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print("e2e: tasks.py %s failed: %s" % (cmd[0], (r.stdout + r.stderr).strip()))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("only", nargs="*", help="journeys whose name contains one of these")
    ap.add_argument("--smoke", action="store_true", help="only the SMOKE journeys (8b)")
    ap.add_argument("--update", action="store_true", help="install the latest release first (one GitHub API call)")
    ap.add_argument("--release", help="add the result as a note on the tasks released in this version")
    ap.add_argument("--evidence", action="store_true", help="record passed journeys' TASKS (--by e2e)")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args(argv)

    journeys = load_journeys()
    if a.smoke:
        journeys = [j for j in journeys if getattr(j, "SMOKE", False)]
    if a.only:
        journeys = [j for j in journeys if any(o in j.ID for o in a.only)]
    if a.list:
        for j in journeys:
            print("%-14s %-5s %-24s %s" % (j.ID, "smoke" if getattr(j, "SMOKE", False) else "",
                                           " ".join(getattr(j, "TASKS", [])), j.NAME))
        return 0
    if not journeys:
        print("e2e: no journey matches")
        return 2

    game, log = Game(), Log()
    started = datetime.datetime.now()
    results, hygiene, attempts = [], None, []
    try:
        ensure_cli()
        status = game.call("status")
        fresh_launch = False  # the launch journey restarts the game unless this run just (re)started it
        if "game: not running" in status:
            print("e2e: launching the game", flush=True)
            print("  " + game.call("game", {"action": "launch"}).replace("\n", "\n  "), flush=True)
            fresh_launch = True
        if a.update:
            print("e2e: installing the latest release", flush=True)
            text = game.call("update_game")
            fresh_launch = fresh_launch or "installed:" in text  # an install restarts the game
            print("  " + text.replace("\n", "\n  "), flush=True)
        hygiene = Hygiene(game)
        hygiene.before()
        for line in hygiene.lines:
            print("hygiene: " + line)
    except (Abort, Fail) as e:
        print("e2e: cannot start - %s" % e)
        return 2

    aborted = None
    for j in journeys:
        t = Context(game, log, j)
        t.version = expected_version()
        t.fresh_launch = fresh_launch
        print("%s - %s" % (j.ID, j.NAME), flush=True)
        if aborted:
            results.append((j, "skipped", aborted, t, 0))
            continue
        verdict, why = "pass", ""
        try:
            j.run(t)
        except Fail as e:
            verdict, why = "FAIL", str(e)
        except Abort as e:
            verdict, why, aborted = "FAIL", str(e), "after an abort: %s" % e
        except Exception as e:  # a bug in the journey: report it, keep going
            verdict, why = "ERROR", "%s: %s" % (type(e).__name__, e)
        if getattr(j, "FATAL", False) and verdict != "pass" and not aborted:
            aborted = "%s failed" % j.ID
        attempts += t.attempts
        took = time.time() - t.started
        print("  %s (%.0f s)%s" % (verdict, took, " - " + why if why else ""), flush=True)
        results.append((j, verdict, why, t, took))

    crashes = new_crashes()
    if crashes:
        # The crash dialog keeps TheForest.exe up with the bridge dead: close it, so the clean-up's
        # game steps fail fast and the next run launches cleanly. The dump stays for symbolize-crash.py.
        hygiene.problems.append("the game crashed - %s (closed it)" % ", ".join(d.name for d in crashes))
        try:
            game.call("game", {"action": "close"})
        except (Fail, Abort):
            pass
    before = len(hygiene.lines)
    hygiene.after(attempts)
    # One the clean-up itself made (it closes and restarts the game) counts too.
    named = {d.name for d in crashes}
    later = [d for d in new_crashes() if d.name not in named]
    if later:
        hygiene.problems.append("the game crashed during the clean-up - %s" % ", ".join(d.name for d in later))
        crashes += later
    for line in hygiene.lines[before:]:
        print("hygiene: " + line)
    for p in hygiene.problems:
        print("hygiene: PROBLEM " + p)

    passed = all(r[1] == "pass" for r in results) and not hygiene.problems
    path = write_report(started, results, hygiene, passed, a)
    summary = "%d/%d journeys passed%s%s" % (sum(1 for r in results if r[1] == "pass"), len(results),
                                            "" if not hygiene.problems else ", %d hygiene problem(s)" % len(hygiene.problems),
                                            "" if not crashes else ", Unity crash folder(s): %s" % ", ".join(d.name for d in crashes))
    print("e2e: %s - %s" % ("PASS" if passed else "FAIL", summary))
    print("e2e: report %s" % path.relative_to(ROOT))

    if a.evidence:
        for j, verdict, why, t, took in results:
            if verdict == "pass":
                for tid in getattr(j, "TASKS", []):
                    record(["evidence", tid, "e2e %s passed (%s): %s" % (j.ID, started.strftime("%Y-%m-%d %H:%M"),
                                                                      "; ".join(l[6:] for l in t.lines if l.startswith("ok"))),
                            "--by", "e2e"])
    if a.release:
        for tid in released_tasks(a.release):
            record(["note", tid, "post-release smoke %s on %s: %s (%s)" % (a.release, started.strftime("%Y-%m-%d"),
                                                                           "PASS" if passed else "FAIL", summary)])
    return 0 if passed else 1


def released_tasks(version):
    path = ROOT / "tasks" / "tasks.jsonl"
    out = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            t = json.loads(line)
            if t.get("release") == version:
                out.append(t["id"])
    return out


def write_report(started, results, hygiene, passed, a):
    REPORTS.mkdir(parents=True, exist_ok=True)
    path = REPORTS / (started.strftime("%Y%m%d-%H%M%S") + (".smoke" if a.smoke else "") + ".md")
    out = ["# e2e %s - %s" % (started.strftime("%Y-%m-%d %H:%M"), "PASS" if passed else "FAIL"), "",
           "Plugin expected v%s; game %s." % (expected_version(), GAME_ROOT), ""]
    for j, verdict, why, t, took in results:
        out.append("## %s - %s: %s (%.0f s)" % (j.ID, j.NAME, verdict, took))
        out += ["- " + l for l in t.lines]
        if why:
            out.append("- **%s**" % why.replace("\n", " / "))
        for s in t.shots:
            out.append("- shot `%s`" % s)
        out.append("")
    out.append("## Hygiene")
    out += ["- " + l for l in hygiene.lines] + ["- **PROBLEM** " + p for p in hygiene.problems]
    path.write_text("\n".join(out) + "\n", encoding="utf-8")
    return path


if __name__ == "__main__":
    sys.exit(main())
