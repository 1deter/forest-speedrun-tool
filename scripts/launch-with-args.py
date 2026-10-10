"""Start The Forest with extra command-line arguments and press the Unity
launcher's Play button - what the bridge MCP server's `game launch` does
(tools/BridgeMcp/Launcher.cs), plus arguments it cannot pass.

    python scripts/launch-with-args.py -force-gfx-direct

`-force-gfx-direct` turns multithreaded rendering off: the D3D11 submission
then runs on the main thread, so the Frame line (Game/FrameTimer) times the
render thread's work inside each camera (T-0199, docs/game-notes.md *The
main camera's draw calls*). Close the game first (`game close`); after it,
the bridge answers as usual. The game root: FOREST_ROOT (User variable).
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

ROOT = os.environ.get("FOREST_ROOT") or r"G:\SteamLibrary\steamapps\common\The Forest"
ENUM = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
WM_COMMAND = 0x0111


def text(h, fn):
    b = ctypes.create_unicode_buffer(128)
    fn(h, b, 128)
    return b.value


def click_play(pid):
    """Posts BN_CLICKED for the launcher's Play button; True when sent."""
    u = ctypes.windll.user32
    found = []

    def top(h, _):
        owner = wt.DWORD()
        u.GetWindowThreadProcessId(h, ctypes.byref(owner))
        if owner.value != pid or not u.IsWindowVisible(h):
            return True

        def child(c, _):
            if text(c, u.GetClassNameW).lower() == "button" and \
                    text(c, u.GetWindowTextW).replace("&", "").strip().startswith("Play"):
                found.append((h, c, u.GetDlgCtrlID(c)))
            return True
        u.EnumChildWindows(h, ENUM(child), 0)
        return True
    u.EnumWindows(ENUM(top), 0)
    if not found:
        return False
    h, c, cid = found[0]
    u.PostMessageW(h, WM_COMMAND, cid & 0xFFFF, c)
    return True


def main():
    args = sys.argv[1:]
    exe = os.path.join(ROOT, "TheForest.exe")
    if not os.path.isfile(exe):
        print("no " + exe)
        return 1
    p = subprocess.Popen([exe] + args, cwd=ROOT)
    deadline = time.time() + 120
    while time.time() < deadline:
        time.sleep(0.4)
        if p.poll() is not None:
            print("TheForest.exe exited (code %s)" % p.returncode)
            return 1
        if click_play(p.pid):
            print("pressed Play (pid %d, args %s)" % (p.pid, " ".join(args) or "none"))
            return 0
    print("no launcher Play button within 120 s")
    return 1


if __name__ == "__main__":
    sys.exit(main())
