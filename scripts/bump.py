"""Release bump: set the version in ForestOverlay.csproj + src/Plugin.cs and add
the CHANGELOG.md section, keeping each file's BOM and line endings.

    python scripts/bump.py 0.24.248 "First bullet." "Second bullet."
    python scripts/bump.py 0.24.248 -f notes.md      # bullets from a file ("- ..." lines)

Then build, commit, tag, push and poll the asset (docs/areas/release.md *Releasing*
goes*). Fails loudly if anything does not match, so a release chain joined
with && stops (gotcha 65)."""
import datetime
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def rw(path, fn):
    raw = open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = (raw[3:] if bom else raw).decode("utf-8")
    crlf = "\r\n" in text
    out = fn(text.replace("\r\n", "\n"))
    if crlf:
        out = out.replace("\n", "\r\n")
    open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + out.encode("utf-8"))


def main():
    if len(sys.argv) < 3 or not re.fullmatch(r"\d+\.\d+\.\d+", sys.argv[1]):
        sys.exit(__doc__)
    new = sys.argv[1]
    if sys.argv[2] == "-f":
        bullets = open(sys.argv[3], encoding="utf-8").read().strip()
    else:
        bullets = "\n".join("- " + b.strip().lstrip("- ") for b in sys.argv[2:])
    if not bullets:
        sys.exit("no changelog bullets")

    def csproj(t):
        for tag, val in (("Version", new), ("AssemblyVersion", new + ".0"), ("FileVersion", new + ".0")):
            t, n = re.subn(r"<%s>[^<]+</%s>" % (tag, tag), "<%s>%s</%s>" % (tag, val, tag), t)
            if n != 1:
                sys.exit("csproj: %s found %d times" % (tag, n))
        return t

    def plugin(t):
        t, n = re.subn(r'PluginVersion = "[^"]+"', 'PluginVersion = "%s"' % new, t)
        if n != 1:
            sys.exit("Plugin.cs: PluginVersion found %d times" % n)
        return t

    def changelog(t):
        if "\n## v%s " % new in t:
            sys.exit("CHANGELOG already has v" + new)
        i = t.index("\n## v")
        return t[:i] + "\n## v%s - %s\n\n%s\n" % (new, datetime.date.today().isoformat(), bullets) + t[i:]

    rw(os.path.join(ROOT, "ForestOverlay.csproj"), csproj)
    rw(os.path.join(ROOT, "src", "Plugin.cs"), plugin)
    rw(os.path.join(ROOT, "CHANGELOG.md"), changelog)
    print("bumped to", new)


if __name__ == "__main__":
    main()
