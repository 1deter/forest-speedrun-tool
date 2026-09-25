"""Rewrites community/index.txt from the .foseg files in community/.

Run after adding, changing or removing a community pack:
    python scripts/community-index.py

The hash is the plugin's CommunityIndex.HashOf: FNV-1a over the text's
UTF-16 code units (BOM dropped, CRLF as LF), then a separator step, as
eight hex digits. tests/.../CommunityIndexTests pins one value from here.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
FOLDER = os.path.join(os.path.dirname(HERE), "community")


def fnv(text):
    h = 2166136261
    data = text.encode("utf-16-le")
    for i in range(0, len(data), 2):
        h ^= data[i] | (data[i + 1] << 8)
        h = (h * 16777619) & 0xFFFFFFFF
    h ^= 31
    h = (h * 16777619) & 0xFFFFFFFF
    return "%08x" % h


def hash_of(text):
    if text.startswith("﻿"):
        text = text[1:]
    return fnv(text.replace("\r\n", "\n"))


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--hash":
        print(hash_of(sys.argv[2]))
        return
    lines = ["# ForestOverlay community packs: <file> <hash>. Written by",
             "# scripts/community-index.py - do not edit by hand."]
    names = sorted(n for n in os.listdir(FOLDER) if n.lower().endswith(".foseg"))
    for name in names:
        with open(os.path.join(FOLDER, name), encoding="utf-8-sig", newline="") as f:
            lines.append("%s %s" % (name, hash_of(f.read())))
    with open(os.path.join(FOLDER, "index.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("%d pack(s) in community/index.txt" % len(names))


if __name__ == "__main__":
    main()
