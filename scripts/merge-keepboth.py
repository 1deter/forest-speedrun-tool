"""Resolve merge conflicts where both sides only ADDED lines (the usual case
when parallel branches each add a file to the test csproj, a module line, a
docs section): keeps ours then theirs. Look at the conflicts first - this is
wrong for real edits of the same lines.

    python scripts/merge-keepboth.py $(git diff --name-only --diff-filter=U)
"""
import re
import sys

for path in sys.argv[1:]:
    raw = open(path, "rb").read()
    text = raw.decode("utf-8")
    text, n = re.subn(r"<<<<<<< [^\n]*\n(.*?)=======\r?\n(.*?)>>>>>>> [^\n]*\n",
                      lambda m: m.group(1) + m.group(2), text, flags=re.S)
    open(path, "wb").write(text.encode("utf-8"))
    print(path, n, "conflict(s) kept both")
