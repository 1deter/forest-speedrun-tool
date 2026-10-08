"""Machine checks for the 3D world export (scripts/world-extract.py).

Pure (file names and numbers in, a message out): no game files, no UnityPy,
no numpy, so scripts/tests/test_world_checks.py runs anywhere.

  placed-*.txt   every one in the world dump folder goes into the export, so
                 two that place the same object are a diagnostic dump, not
                 data (gotcha 78, T-0134)
"""
import os


def placed_files(names):
    """The placed-*.txt files among `names` (file names or paths), sorted."""
    return sorted(n for n in names if os.path.basename(n).startswith("placed-") and n.endswith(".txt"))


def placed_objects(lines):
    """The object names a placed dump places: its `greeble` lines
    (WorldDump.Placed writes exactly one, the object's own name)."""
    out = []
    for line in lines:
        p = line.rstrip("\n").split("\t")
        if p[0] == "greeble" and len(p) > 1:
            out.append(p[1])
    return out


def duplicate_placements(files):
    """{object name: [files]} for every object placed by more than one
    placed-*.txt, or twice by one. `files` maps a path to its text lines."""
    by_name = {}
    for path in placed_files(files):
        for name in placed_objects(files[path]):
            by_name.setdefault(name, []).append(path)
    return {n: ps for n, ps in by_name.items() if len(ps) > 1}


def duplicates_message(dups):
    """WHAT / WHY / FIX for duplicate_placements, or None when there are none."""
    if not dups:
        return None
    lines = ["WHAT: %d object(s) placed by more than one placed-*.txt in the world dump folder:" % len(dups)]
    for name, paths in sorted(dups.items()):
        lines.append("  %s: %s" % (name, ", ".join(os.path.basename(p) for p in paths)))
    lines.append("WHY: the export reads every placed-*.txt, so a diagnostic dump becomes a second copy of the "
                 "geometry (gotcha 78: 28 dumps went into two exports).")
    lines.append("FIX: move the extra dumps out of the folder (or name them diag-*.txt, which the export skips) "
                 "and run the export again.")
    return "\n".join(lines)
