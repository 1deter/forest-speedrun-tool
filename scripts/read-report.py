"""Summarise a tester's ForestOverlay report zip in a few dozen lines.

    python scripts/read-report.py <zip> [--full] [--last N]

A report zip (QA tab -> Write report, src/Modules/QaModule.cs) holds
report.txt, the QA answers, the kept BepInEx logs, sometimes the Unity
output_log, the config, segments and savestates. This reads it in place
(never extracts) and prints: the header and marks from report.txt, then
per log the version, system line, errors, slow ticks, perf, warnings and
the last actions. --full prints every error / warning group instead of
the top few. Tests: scripts/tests/test_read_report.py.
"""
import argparse
import re
import sys
import zipfile
from collections import OrderedDict

LINE = re.compile(r"^\[(\w+)\s*:\s*([^\]]+)\]\s?(.*)$")
SLOW = re.compile(r"Slow tick: '([^']+)' took ([\d.]+) ms")
PERF = re.compile(r"Perf \(\d+ s\): ([\d.]+) fps, worst (\d+) ms, (\d+) over 50 ms")
ERRORISH = re.compile(r"\b(failed|threw|Exception)\b|Parameter name:")
# Lines that say what the player did, newest last (prefixes, ForestOverlay source).
ACTIONS = ("Restart '", "Teleport to ", "Savestate captured", "Savestate restore", "Run '",
           "Death:", "MARK #", "Game event:", "Practice: selected", "Practice: saved",
           "QA note", "Go to ")
HEADER_KEYS = ("Tester:", "Plugin:", "Written:", "Pass ")


def norm(text):
    """A message with its numbers and quoted ids blanked, to group repeats."""
    text = re.sub(r"'[^']*'", "'_'", text)
    return re.sub(r"-?\d+(\.\d+)?", "#", text)


def group(lines):
    """[(count, first example)] most frequent first, keeping first-seen order on ties."""
    groups = OrderedDict()
    for line in lines:
        key = norm(line)
        if key in groups:
            groups[key][0] += 1
        else:
            groups[key] = [1, line]
    return sorted(groups.values(), key=lambda g: -g[0])


def clip(text, width=180):
    return text if len(text) <= width else text[:width - 3] + "..."


def read_text(zf, name):
    return zf.read(name).decode("utf-8", "replace").splitlines()


def summarise_report(lines):
    """Header lines, the free-text note, the marks and the answered QA items of report.txt."""
    out = []
    if lines:
        out.append(lines[0])
    out += [l for l in lines[1:8] if l.startswith(HEADER_KEYS)]
    answered = [l for l in lines if re.match(r"^\d+\) \[[^-]\]", l)]
    if answered:
        out.append("Answered:")
        out += ["  " + clip(l) for l in answered]
    for l in lines:
        if l.startswith("Note:"):
            out.append(clip(l, 400))
    marks = [l.strip() for l in lines if l.strip().startswith("MARK #")]
    if marks:
        out.append("Marks:")
        out += ["  " + clip(m) for m in marks]
    return out


def parse_log(lines):
    """Pulls what matters out of one BepInEx log."""
    info = {"version": None, "system": None, "errors": [], "warnings": [], "slow": {},
            "perf": [], "actions": [], "lines": len(lines)}
    prev = None
    for raw in lines:
        m = LINE.match(raw)
        if not m:
            # A continuation of the line before (an exception's message or stack).
            if prev is not None and raw.strip() and info["errors"] and info["errors"][-1] is prev:
                info["errors"][-1] = prev = prev + " | " + raw.strip()
            continue
        level, source, msg = m.group(1), m.group(2).strip(), m.group(3)
        prev = None
        if source == "ForestOverlay" and msg.startswith("ForestOverlay v") and info["version"] is None:
            info["version"] = msg.split()[1]
        elif msg.startswith("System: ") and info["system"] is None:
            info["system"] = msg[len("System: "):]
        s = SLOW.search(msg)
        if s:
            mod, ms = s.group(1), float(s.group(2))
            n, worst = info["slow"].get(mod, (0, 0.0))
            info["slow"][mod] = (n + 1, max(worst, ms))
            continue
        p = PERF.search(msg)
        if p:
            info["perf"].append((float(p.group(1)), int(p.group(2)), int(p.group(3))))
            continue
        if level in ("Error", "Fatal") or ERRORISH.search(msg):
            prev = "[" + source + "] " + msg
            info["errors"].append(prev)
        elif level == "Warning":
            info["warnings"].append("[" + source + "] " + msg)
        if msg.startswith(ACTIONS) and (not msg.startswith("Savestate restore") or ": done in" in msg):
            info["actions"].append(msg)
    return info


def unity_exceptions(lines):
    """Exception lines of a Unity output_log, each with its first stack frame."""
    found = []
    for i, l in enumerate(lines):
        if re.search(r"\w+Exception\b", l) and not l.startswith(" "):
            nxt = lines[i + 1].strip() if i + 1 < len(lines) else ""
            found.append(l.strip() + (" | " + nxt if nxt else ""))
    return found


def summarise_log(name, info, top, last, prev_system=None):
    out = ["", "== " + name + " (" + str(info["lines"]) + " lines, plugin " + (info["version"] or "?") + ")"]
    if info["system"] and info["system"] != prev_system:
        out.append("System: " + clip(info["system"], 220))
    if info["errors"]:
        out.append("Errors (" + str(len(info["errors"])) + "):")
        out += ["  %dx %s" % (n, clip(ex)) for n, ex in group(info["errors"])[:top]]
    if info["slow"]:
        parts = sorted(info["slow"].items(), key=lambda kv: -kv[1][0])
        out.append("Slow ticks: " + ", ".join("%s %dx (worst %.1f ms)" % (k, n, w) for k, (n, w) in parts))
    if info["perf"]:
        fps = sorted(p[0] for p in info["perf"])
        worst = max(p[1] for p in info["perf"])
        over = sum(p[2] for p in info["perf"])
        out.append("Perf: %d windows, fps min %.0f / median %.0f, worst frame %d ms, %d frames over 50 ms"
                   % (len(fps), fps[0], fps[len(fps) // 2], worst, over))
    if info["warnings"]:
        groups = group(info["warnings"])
        out.append("Warnings (" + str(len(info["warnings"])) + ", " + str(len(groups)) + " kinds):")
        out += ["  %dx %s" % (n, clip(ex)) for n, ex in groups[:top]]
        if len(groups) > top:
            out.append("  ... %d more kinds (--full)" % (len(groups) - top))
    if info["actions"]:
        out.append("Last actions (%d of %d):" % (min(last, len(info["actions"])), len(info["actions"])))
        out += ["  " + clip(a) for a in info["actions"][-last:]]
    return out


def summarise(path, full=False, last=6):
    top = 1000 if full else 5
    out = []
    with zipfile.ZipFile(path) as zf:
        names = zf.namelist()
        sizes = dict((i.filename, i.file_size) for i in zf.infolist())
        out.append("Report: " + str(getattr(path, "name", path)))
        if "report.txt" in names:
            out += summarise_report(read_text(zf, "report.txt"))
        else:
            out.append("(no report.txt)")
        saves = [n for n in names if n.endswith(".fosave")]
        out.append("Contents: %d files, %d logs, %d savestates (%.1f MB), %s" % (
            len(names), len([n for n in names if n.startswith("logs/")]), len(saves),
            sum(sizes[n] for n in saves) / 1048576.0,
            ", ".join(n for n in names if not n.startswith(("logs/", "savestates/")))))
        system = None
        for name in sorted(n for n in names if n.startswith("logs/") and n.endswith(".log")):
            info = parse_log(read_text(zf, name))
            out += summarise_log(name, info, top, last, system)
            system = info["system"] or system
        if "unity/output_log.txt" in names:
            exc = unity_exceptions(read_text(zf, "unity/output_log.txt"))
            out.append("")
            out.append("== unity/output_log.txt: %d exception line(s)" % len(exc))
            out += ["  %dx %s" % (n, clip(e)) for n, e in group(exc)[:top]]
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description="Summarise a ForestOverlay report zip.")
    ap.add_argument("zip")
    ap.add_argument("--full", action="store_true", help="every error / warning group")
    ap.add_argument("--last", type=int, default=6, help="action lines per log (default 6)")
    args = ap.parse_args(argv)
    try:
        lines = summarise(args.zip, args.full, args.last)
    except (OSError, zipfile.BadZipFile) as ex:
        print("read-report: cannot read %s: %s" % (args.zip, ex), file=sys.stderr)
        return 1
    out = "\n".join(lines) + "\n"
    sys.stdout.buffer.write(out.encode("utf-8"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
