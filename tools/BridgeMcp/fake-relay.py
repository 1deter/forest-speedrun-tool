"""A fake of the site's live relay (docs/bridge.md *Live sessions*, T-0491) on
127.0.0.1:8765 for checking the MCP remote mode without the site or a game:

    python3 tools/BridgeMcp/fake-relay.py &
    FOREST_LIVE_BASE=http://127.0.0.1:8765 FOREST_SITE_ADMIN_TOKEN=fake-token FOREST_LIVE_CODE=AB12 \\
        dotnet tools/BridgeMcp/bin/Release/net8.0/forest-bridge-mcp.dll --call status {}

One session AB12; each queued line is answered in the out.txt shape on the
next /out poll; `shot <name>` makes a tiny PNG for /file/<name> (no .png, as the site).
"""
import http.server, json, struct, sys, threading, zlib
TOKEN = "fake-token"
out = bytearray(b"== bridge on, ForestOverlay v0.24.402, polling the site\n")
pending, files, n = [], {}, [0]
def png(w=4, h=3):
    raw = b"".join(b"\x00" + b"\xff\x00\x00" * w for _ in range(h))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")
def answer(line):
    if line.startswith("echo "): out.extend((line[5:] + "\n").encode()); return
    n[0] += 1; i = n[0]
    body = {"status": "scene: ForestMain, practice: no, savestates idle", "player": "player at (1, 2, 3), not in caves"}.get(line, "")
    if line.startswith("get "): body = "ScriptSetup.pmControl.ActiveStateName = \"idle\"  (String)"
    if line.startswith("shot "): files[line[5:]] = png(); body = "saved " + line[5:] + ".png"
    if line.startswith("log "): body = "[Info   :ForestOverlay] Bridge #1: status"
    out.extend(("> #%d %s\n%s%s< #%d ok (1 ms)\n" % (i, line, body, "\n" if body else "", i)).encode())
class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a): sys.stderr.write("fake: %s %s token=%s\n" % (self.command, self.path, "ok" if self.headers.get("X-Admin-Token") == TOKEN else "BAD"))
    def send(self, code, body=b"", mime="text/plain", headers={}):
        self.send_response(code); self.send_header("Content-Type", mime); self.send_header("Content-Length", str(len(body)))
        for k, v in headers.items(): self.send_header(k, v)
        self.end_headers(); self.wfile.write(body)
    def do_GET(self):
        if self.headers.get("X-Admin-Token") != TOKEN: return self.send(401)
        p = self.path
        if p == "/api/admin/live": return self.send(200, json.dumps([{"code": "AB12", "opened": "2026-11-28T10:00:00Z", "lastCommand": None, "lastPoll": None, "outBytes": len(out), "state": "open"}]).encode(), "application/json")
        if p.startswith("/api/admin/live/AB12/out?after="):
            while pending: answer(pending.pop(0))
            after = min(int(p.split("after=")[1].split("&")[0]), len(out))
            return self.send(200, bytes(out[after:]), headers={"X-Live-Offset": str(len(out)), "X-Live-State": "open"})
        if p.startswith("/api/admin/live/AB12/file/"):
            f = files.get(p.rsplit("/", 1)[1])
            return self.send(200, f, "image/png") if f else self.send(404)
        self.send(404)
    def do_POST(self):
        if self.headers.get("X-Admin-Token") != TOKEN: return self.send(401)
        body = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        if self.path == "/api/admin/live/AB12/send": pending.extend(l for l in body.split("\n") if l); return self.send(204)
        if self.path == "/api/admin/live/AB12/close": return self.send(204)
        self.send(404)
http.server.ThreadingHTTPServer(("127.0.0.1", 8765), H).serve_forever()
