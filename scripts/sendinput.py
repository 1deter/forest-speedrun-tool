"""Real OS input into The Forest (Windows SendInput), for checks the bridge's
injection cannot reach: UnityEngine.Input.GetKey / GetAxisRaw("Mouse X") and
Rewired's own reads see these, the bridge's press / axis do not (T-0302).

Sends only while the game window ('TheForest') has focus; refuses otherwise,
so it never types into another window.

  python scripts/sendinput.py move  <delay_s> <steps> <dx> <dy> <interval_s>
  python scripts/sendinput.py wheel <delay_s> <steps> <delta> <interval_s>   (120 = one notch up)
  python scripts/sendinput.py key   <delay_s> <scancode hex> <hold_s> [repeats] [gap_s]   (W = 11)

Run it in the background, then sample over the bridge while it sends."""
import ctypes
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
