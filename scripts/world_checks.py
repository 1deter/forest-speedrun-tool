"""Machine checks for the 3D world export (scripts/world-extract.py).

Pure (file names and numbers in, a message out): no game files, no UnityPy,
no numpy, so scripts/tests/test_world_checks.py runs anywhere.

  placed-*.txt   every one in the world dump folder goes into the export, so
                 two that place the same object are a diagnostic dump, not
                 data (gotcha 78, T-0134)
  textures       a texture whose mean colour is ~0 is a resize that read the
                 alpha as coverage (gotcha 88, T-0135); the game has exactly
                 KNOWN_BLACK of them on purpose (BlackFadeIntoCaves)
"""
import os


# Mean of a texture's R, G and B (0-255) at or below this counts as black.
NEAR_BLACK = 2.0
# Textures the game really draws black: 1 of 523 (BlackFadeIntoCaves).
KNOWN_BLACK = 1


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


def is_near_black(mean_rgb):
    """True for a texture whose mean R, G, B (0-255) is ~0."""
    return mean_rgb <= NEAR_BLACK


def black_message(black, total, known=KNOWN_BLACK):
    """WHAT / WHY / FIX when more than `known` of the `total` textures are
    near-black, else None. `black` = {texture name or key: mean}."""
    if len(black) <= known:
        return None
    worst = sorted(black.items(), key=lambda kv: kv[1])[:20]
    lines = ["WHAT: %d of %d textures came out near-black (mean <= %g); the game has %d on purpose (BlackFadeIntoCaves):"
             % (len(black), total, NEAR_BLACK, known)]
    for name, mean in worst:
        lines.append("  %s (mean %.2f)" % (name, mean))
    if len(black) > len(worst):
        lines.append("  ... and %d more" % (len(black) - len(worst)))
    lines.append("WHY: a resize that reads the alpha as coverage writes smoothness-in-alpha textures solid black "
                 "(gotcha 88: 19 lab textures, 2026-10-03).")
    lines.append("FIX: shrink colour and alpha apart in Export.texture (as it does), then look at the textures above.")
    return "\n".join(lines)
