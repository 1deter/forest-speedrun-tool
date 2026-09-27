"""Names the native functions in a Unity crash dump of The Forest.

    python scripts/symbolize-crash.py <crash.dmp> [TheForest.exe]

Unity writes crash.dmp (a minidump: module list + thread stacks) into a
crash-<date> folder beside TheForest.exe. This reads the exe's PDB record,
fetches the matching player PDB from Unity's public symbol server (cached in
%LOCALAPPDATA%\\ForestOverlay\\symbols), and prints the faulting function and
every stack value that points into the exe, mono.dll or JIT code (Mono JIT
frames show as "(jit?)" - their names are not in the dump). The stack scan
is raw: return addresses are real, older values can be stale; read it top
down from the fault. Needs `pip install minidump`. Dev-time only (gotcha 58).
"""
import bisect, os, struct, subprocess, sys, urllib.request

from minidump.minidumpfile import MinidumpFile

DEFAULT_EXE = r"G:\SteamLibrary\steamapps\common\The Forest\TheForest.exe"
SYMBOLS = os.path.join(os.environ.get("LOCALAPPDATA", "."), "ForestOverlay", "symbols")


def pe_sections(d):
    pe = struct.unpack_from("<I", d, 0x3c)[0]
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    optsz = struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    secs = []
    for i in range(nsec):
        o = opt + optsz + i * 40
        vs, va, rs, ra = struct.unpack_from("<IIII", d, o + 8)
        secs.append((va, max(vs, rs), ra))
    return pe, opt, secs


def pdb_record(exe):
    d = open(exe, "rb").read()
    pe, opt, secs = pe_sections(d)
    magic = struct.unpack_from("<H", d, opt)[0]
    dd = opt + (112 if magic == 0x20b else 96)
    rva, size = struct.unpack_from("<II", d, dd + 6 * 8)

    def off(r):
        for va, vs, ra in secs:
            if va <= r < va + vs:
                return r - va + ra
        raise ValueError("rva not in a section")

    for i in range(size // 28):
        typ, sz, _, ptr = struct.unpack_from("<IIII", d, off(rva + i * 28) + 12)
        if typ == 2 and d[ptr:ptr + 4] == b"RSDS":
            g = d[ptr + 4:ptr + 20]
            age = struct.unpack_from("<I", d, ptr + 20)[0]
            name = d[ptr + 24:d.index(b"\0", ptr + 24)].decode("latin1").split("\\")[-1]
            a, b, c = struct.unpack_from("<IHH", g)
            return name, "%08X%04X%04X%s%X" % (a, b, c, g[8:].hex().upper(), age)
    raise ValueError("no PDB record in " + exe)


def fetch_pdb(name, key):
    folder = os.path.join(SYMBOLS, key)
    pdb = os.path.join(folder, name)
    if os.path.exists(pdb):
        return pdb
    os.makedirs(folder, exist_ok=True)
    packed = pdb[:-1] + "_"
    url = "http://symbolserver.unity3d.com/%s/%s/%s_" % (name, key, name[:-1])
    print("fetching", url)
    urllib.request.urlretrieve(url, packed)
    subprocess.check_call([os.path.join(os.environ["WINDIR"], "System32", "expand.exe"), packed, pdb],
                          stdout=subprocess.DEVNULL)
    return pdb


def read_publics(pdb):
    d = open(pdb, "rb").read()
    bs, _, _, dirbytes, _, bmaddr = struct.unpack_from("<IIIIII", d, 32)
    dirblocks = struct.unpack_from("<%dI" % ((dirbytes + bs - 1) // bs), d, bmaddr * bs)
    dirdata = b"".join(d[b * bs:(b + 1) * bs] for b in dirblocks)[:dirbytes]
    n = struct.unpack_from("<I", dirdata, 0)[0]
    sizes = struct.unpack_from("<%dI" % n, dirdata, 4)
    pos, streams = 4 + 4 * n, []
    for s in sizes:
        s = 0 if s == 0xFFFFFFFF else s
        k = (s + bs - 1) // bs
        blks = struct.unpack_from("<%dI" % k, dirdata, pos)
        pos += 4 * k
        streams.append(b"".join(d[b * bs:(b + 1) * bs] for b in blks)[:s])
    dbi = streams[3]
    f = struct.unpack_from("<iIIHHHHHHiiiiiIiiHHI", dbi, 0)
    symrec, modi, secc, secm, srci, tsm, dbgsz, ecs = f[7], f[9], f[10], f[11], f[12], f[13], f[15], f[16]
    dbgidx = struct.unpack_from("<%dH" % (dbgsz // 2), dbi, 64 + modi + secc + secm + srci + tsm + ecs)
    sechdr = streams[dbgidx[5]]
    secva = [struct.unpack_from("<I", sechdr, i * 40 + 12)[0] for i in range(len(sechdr) // 40)]
    rec, p, syms = streams[symrec], 0, []
    while p + 4 <= len(rec):
        ln, kind = struct.unpack_from("<HH", rec, p)
        if kind == 0x110E:  # S_PUB32
            _, off, seg = struct.unpack_from("<IIH", rec, p + 4)
            if 1 <= seg <= len(secva):
                syms.append((secva[seg - 1] + off, rec[p + 14:rec.index(b"\0", p + 14)].decode("latin1")))
        p += ln + 2
    syms.sort()
    return syms


def main():
    dmp = sys.argv[1]
    exe = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_EXE
    name, key = pdb_record(exe)
    syms = read_publics(fetch_pdb(name, key))
    keys = [s[0] for s in syms]

    def sym(rva):
        i = bisect.bisect_right(keys, rva) - 1
        return "%s+0x%x" % (syms[i][1], rva - syms[i][0]) if i >= 0 else "?"

    m = MinidumpFile.parse(dmp)
    mods = [(mo.baseaddress, mo.baseaddress + mo.size, mo.name.split("\\")[-1]) for mo in m.modules.modules]
    exe_name = os.path.basename(exe).lower()

    def where(a):
        for lo, hi, n in mods:
            if lo <= a < hi:
                return (sym(a - lo) if n.lower() == exe_name else "%s+0x%x" % (n, a - lo)), True
        return ("(jit?)" if 0x100000000 <= a < 0x7ff000000000 else None), False

    rec = m.exception.exception_records[0]
    print("exception", rec.ExceptionRecord.ExceptionCode, "at", where(rec.ExceptionRecord.ExceptionAddress)[0])
    th = [t for t in m.threads.threads if t.ThreadId == rec.ThreadId][0]
    lo, size = th.Stack.StartOfMemoryRange, th.Stack.MemoryLocation.DataSize
    with open(dmp, "rb") as f:
        f.seek(th.Stack.MemoryLocation.Rva)
        data = f.read(size)
    # The dump's own context would give RSP; the lowest in-module value is close enough.
    for i in range(0, len(data) - 7, 8):
        v = struct.unpack_from("<Q", data, i)[0]
        label, in_module = where(v)
        if label and (in_module or label == "(jit?)"):
            print(hex(lo + i), hex(v), label)


if __name__ == "__main__":
    main()
