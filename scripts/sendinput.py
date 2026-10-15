"""Real OS input into The Forest (Windows SendInput), for checks the bridge's
injection cannot reach: UnityEngine.Input.GetKey / GetAxisRaw("Mouse X") and
Rewired's own reads see these, the bridge's press / axis do not (T-0302).

Brings the game window to the front first (author 2026-10-16: tests are
automatic, nobody clicks the game), then sends only while it has focus;
refuses otherwise, so it never types into another window.

  python scripts/sendinput.py move  <delay_s> <steps> <dx> <dy> <interval_s>
  python scripts/sendinput.py wheel <delay_s> <steps> <delta> <interval_s>   (120 = one notch up)
  python scripts/sendinput.py key   <delay_s> <scancode hex> <hold_s> [repeats] [gap_s]   (W = 11)

Run it in the background, then sample over the bridge while it sends."""
import ctypes
import subprocess
import sys
import time
from ctypes import wintypes

GAME_TITLE = "TheForest"

user32 = ctypes.WinDLL("user32", use_last_error=True)


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wintypes.LONG), ("dy", wintypes.LONG), ("mouseData", wintypes.DWORD),
                ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD), ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class INPUT(ctypes.Structure):
    class _U(ctypes.Union):
        _fields_ = [("mi", MOUSEINPUT), ("ki", KEYBDINPUT), ("pad", ctypes.c_byte * 32)]
    _anonymous_ = ("u",)
    _fields_ = [("type", wintypes.DWORD), ("u", _U)]


MOUSEEVENTF_MOVE, MOUSEEVENTF_WHEEL = 0x0001, 0x0800
KEYEVENTF_KEYUP, KEYEVENTF_SCANCODE = 0x0002, 0x0008


def game_focused():
    buf = ctypes.create_unicode_buffer(256)
    user32.GetWindowTextW(user32.GetForegroundWindow(), buf, 256)
    return buf.value == GAME_TITLE


def _game_window():
    hwnd = user32.FindWindowW(None, GAME_TITLE)
    if hwnd:
        return hwnd
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq TheForest.exe", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    pids = set()
    for line in out.splitlines():
        parts = [x.strip('"') for x in line.split('","')]
        if len(parts) > 1 and parts[0].lower() == "theforest.exe":
            pids.add(int(parts[1]))
    found = []
    proto = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def cb(hw, _):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hw, ctypes.byref(pid))
        if pid.value in pids and user32.IsWindowVisible(hw):
            found.append(hw)
        return True

    user32.EnumWindows(proto(cb), 0)
    return found[0] if found else 0


def bring_to_front():
    # A plain SetForegroundWindow is refused from a background process: an
    # Alt tap plus AttachThreadInput to the current foreground thread lets it.
    hwnd = _game_window()
    if not hwnd:
        return False
    kernel32 = ctypes.WinDLL("kernel32")
    user32.ShowWindow(hwnd, 9)
    t_fg = user32.GetWindowThreadProcessId(user32.GetForegroundWindow(), None)
    t_me = kernel32.GetCurrentThreadId()
    user32.AttachThreadInput(t_me, t_fg, True)
    user32.keybd_event(0x12, 0, 0, 0)
    user32.keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0)
    user32.BringWindowToTop(hwnd)
    user32.SetForegroundWindow(hwnd)
    user32.AttachThreadInput(t_me, t_fg, False)
    for _ in range(20):   # the switch lands a few frames later
        if user32.GetForegroundWindow() == hwnd:
            return True
        time.sleep(0.05)
    return False


def mouse(dx, dy, data, flags):
    i = INPUT(type=0)
    i.mi = MOUSEINPUT(dx, dy, ctypes.c_uint32(data).value, flags, 0, 0)
    return user32.SendInput(1, ctypes.byref(i), ctypes.sizeof(INPUT))


def key(scan, up):
    i = INPUT(type=1)
    i.ki = KEYBDINPUT(0, scan, KEYEVENTF_SCANCODE | (KEYEVENTF_KEYUP if up else 0), 0, 0)
    return user32.SendInput(1, ctypes.byref(i), ctypes.sizeof(INPUT))


def main(argv):
    if len(argv) < 2 or argv[0] not in ("move", "wheel", "key"):
        print(__doc__)
        return 2
    mode, a = argv[0], argv[1:]
    if not bring_to_front():
        print("sendinput: could not bring the game window to the front")
    time.sleep(float(a[0]))
    if not game_focused():
        print("sendinput: the game window ('%s') does not have focus - nothing sent" % GAME_TITLE)
        return 1
    t0, sent = time.time(), 0
    if mode == "move":
        steps, dx, dy, iv = int(a[1]), int(a[2]), int(a[3]), float(a[4])
        for _ in range(steps):
            sent += mouse(dx, dy, 0, MOUSEEVENTF_MOVE)
            time.sleep(iv)
    elif mode == "wheel":
        steps, d, iv = int(a[1]), int(a[2]), float(a[3])
        for _ in range(steps):
            sent += mouse(0, 0, d, MOUSEEVENTF_WHEEL)
            time.sleep(iv)
    else:
        scan, hold = int(a[1], 16), float(a[2])
        reps = int(a[3]) if len(a) > 3 else 1
        gap = float(a[4]) if len(a) > 4 else 0.5
        for _ in range(reps):
            sent += key(scan, False)
            time.sleep(hold)
            sent += key(scan, True)
            time.sleep(gap)
    print("sendinput: %s, %d events in %.2f s" % (mode, sent, time.time() - t0))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
