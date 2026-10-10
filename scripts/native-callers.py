"""Who calls a native function of The Forest's player (TheForest.exe).

    python scripts/native-callers.py "<symbol part>"                 # direct calls (E8 rel32)
    python scripts/native-callers.py "<symbol part>" --vtable "??_7GfxDeviceClient@@6B@"
                                                                    # a virtual: every call/jmp [reg+slot*8]

Names come from Unity's player PDB (the same cache as symbolize-crash.py).
A virtual's slot is found in the named vtable; the scan then lists every
function with an indirect call through that offset - some hits can be
another class's method at the same slot, so read the names (T-0190: the
GfxDevice `UploadTextureSubData2D` slot is only fonts and video). Dev-time
only, never shipped.
"""
import bisect, importlib.util, os, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("symbolize_crash", os.path.join(HERE, "symbolize-crash.py"))
sc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sc)


def main():
    args = sys.argv[1:]
    vtable = None
    if "--vtable" in args:
        i = args.index("--vtable")
        vtable = args[i + 1]
        del args[i:i + 2]
    if not args:
        print(__doc__)
        return
    query = args[0]
    exe = args[1] if len(args) > 1 else sc.DEFAULT_EXE

    name, key = sc.pdb_record(exe)
    syms = sc.read_publics(sc.fetch_pdb(name, key))
    keys = [s[0] for s in syms]
    d = open(exe, "rb").read()
    pe, opt, secs = sc.pe_sections(d)
    imagebase = struct.unpack_from("<Q", d, opt + 24)[0]

    def name_of(rva):
        i = bisect.bisect_right(keys, rva) - 1
        return syms[i][1] if i >= 0 else "?"

    def file_offset(rva):
        for va, sz, ra in secs:
            if va <= rva < va + sz:
                return ra + (rva - va)
        return None

    targets = [(a, n) for a, n in syms if query in n]
    if not targets:
        print("no symbol contains", query)
        return
    for a, n in targets:
        print("target 0x%x %s" % (a, n))
    va, sz, ra = secs[0]   # .text
    code = d[ra:ra + sz]
    hits = {}

    if vtable is None:
        tset = set(a for a, _ in targets)
        i = code.find(b"\xe8")
        while 0 <= i <= len(code) - 5:
            if va + i + 5 + struct.unpack_from("<i", code, i + 1)[0] in tset:
                hits[name_of(va + i)] = hits.get(name_of(va + i), 0) + 1
            i = code.find(b"\xe8", i + 1)
    else:
        vt = [a for a, n in syms if n == vtable]
        if not vt:
            print("no vtable named", vtable)
            return
        off = file_offset(vt[0])
        tset = set(a for a, _ in targets)
        slot = None
        for k in range(1000):
            if struct.unpack_from("<Q", d, off + k * 8)[0] - imagebase in tset:
                slot = k
                break
        if slot is None:
            print("not in", vtable)
            return
        disp = slot * 8
        print("slot %d (+0x%x)" % (slot, disp))
        i = code.find(b"\xff")
        while 0 <= i < len(code) - 7:
            modrm = code[i + 1]
            mod, reg, rm = modrm >> 6, (modrm >> 3) & 7, modrm & 7
            if reg in (2, 4):   # call / jmp r/m64
                k = i + 2 + (1 if rm == 4 else 0)   # SIB byte (rsp / r12 bases)
                if (mod == 1 and disp < 0x80 and code[k] == disp) or \
                   (mod == 2 and struct.unpack_from("<I", code, k)[0] == disp):
                    hits[name_of(va + i)] = hits.get(name_of(va + i), 0) + 1
            i = code.find(b"\xff", i + 1)

    for fn in sorted(hits):
        print("%3d  %s" % (hits[fn], fn))


if __name__ == "__main__":
    main()
