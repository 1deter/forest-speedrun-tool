"""Upload the baked aerial map tiles to the website.

    python scripts/aerial-upload.py [site url] [tile folder]

Zips the bake's output (scripts/aerial-bake.py -> site/aerial-out, not in
git) into chunks under 90 MB (whole files per chunk; Cloudflare caps a
request at 100 MB) and POSTs each to <site>/api/admin/aerial. The first
chunk carries ?clear=1 (the server empties its tile folder first);
aerial.json goes in the last chunk, so the map only turns the photo layer
on once every tile is there.

The site defaults to https://forest.deter.cloud. The owner token comes from
the environment variable FOREST_SITE_ADMIN_TOKEN (never printed; the script
refuses to run without it). Standard library only.
"""
import io
import json
import os
import sys
import urllib.error
import urllib.request
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SITE = "https://forest.deter.cloud"
DEFAULT_TILES = os.path.join(ROOT, "site", "aerial-out")
CHUNK_MAX = 90 * 1024 * 1024
ZIP_OVERHEAD = 200          # local + central directory headers per entry, roughly, plus the name
USER_AGENT = "ForestOverlay-aerial-upload/1.0 (+https://github.com/1deter/forest-speedrun-tool)"


def tile_files(folder):
    """(zip path, disk path, size) for every file the server accepts, tiles first."""
    tiles = []
    for layer in sorted(os.listdir(folder)):
        ldir = os.path.join(folder, layer)
        if not os.path.isdir(ldir):
            continue
        for level in sorted(os.listdir(ldir), key=lambda s: (len(s), s)):
            vdir = os.path.join(ldir, level)
            if not os.path.isdir(vdir):
                continue
            for name in sorted(os.listdir(vdir)):
                if name.endswith(".jpg"):
                    p = os.path.join(vdir, name)
                    tiles.append(("%s/%s/%s" % (layer, level, name), p, os.path.getsize(p)))
    meta = os.path.join(folder, "aerial.json")
    if not os.path.exists(meta):
        sys.exit("no aerial.json in %s - run scripts/aerial-bake.py first" % folder)
    return tiles, ("aerial.json", meta, os.path.getsize(meta))


def chunks(tiles, meta):
    """Lists of files, each under CHUNK_MAX zipped (stored, so the size is known); aerial.json last."""
    out, cur, size = [], [], 0
    for f in tiles + [meta]:
        need = f[2] + len(f[0]) * 2 + ZIP_OVERHEAD
        if need > CHUNK_MAX:
            sys.exit("%s alone is over the chunk size" % f[0])
        if cur and size + need > CHUNK_MAX:
            out.append(cur)
            cur, size = [], 0
        cur.append(f)
        size += need
    out.append(cur)
    return out


def zipped(files):
    buf = io.BytesIO()
    # Stored: the tiles are JPEGs already, deflate would only cost time.
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_STORED) as z:
        for name, path, _ in files:
            z.write(path, name)
    return buf.getvalue()


def post(url, token, body):
    req = urllib.request.Request(url, data=body, method="POST", headers={
        "X-Admin-Token": token,
        "Content-Type": "application/zip",
        "User-Agent": USER_AGENT,
    })
    try:
        with urllib.request.urlopen(req, timeout=600) as r:
            return json.loads(r.read().decode("utf-8") or "{}")
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")[:300]
        sys.exit("HTTP %d from %s: %s" % (e.code, url.split("?")[0], detail))
    except urllib.error.URLError as e:
        sys.exit("could not reach %s: %s" % (url.split("?")[0], e.reason))


def main():
    site = (sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SITE).rstrip("/")
    folder = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_TILES
    token = os.environ.get("FOREST_SITE_ADMIN_TOKEN", "").strip()
    if not token:
        sys.exit("set FOREST_SITE_ADMIN_TOKEN to the site owner's admin token first")
    if not os.path.isdir(folder):
        sys.exit("no tile folder %s - run scripts/aerial-bake.py first" % folder)
    tiles, meta = tile_files(folder)
    parts = chunks(tiles, meta)
    total = sum(f[2] for f in tiles) + meta[2]
    print("%d tiles, %.1f MB, %d chunk(s) to %s" % (len(tiles), total / 1e6, len(parts), site))
    sent = 0
    for i, files in enumerate(parts):
        body = zipped(files)
        url = site + "/api/admin/aerial" + ("?clear=1" if i == 0 else "")
        print("chunk %d/%d: %d files, %.1f MB ..." % (i + 1, len(parts), len(files), len(body) / 1e6), end=" ", flush=True)
        answer = post(url, token, body)
        print("server stored %s" % answer.get("files"))
        sent += answer.get("files") or 0
    print("done: %d files stored" % sent)


if __name__ == "__main__":
    main()
