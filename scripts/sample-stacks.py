"""Samples the running game's main thread: where a long frozen frame goes.

    python scripts/sample-stacks.py [seconds] [--after "<log text>"] [--until "<log text>"]

Suspends TheForest.exe's main thread every ~50 ms, reads its instruction
pointer and the top of its stack, resumes it, and at the end prints the
functions it was in (from Unity's player PDB, via symbolize-crash.py) and
the most common chains of Unity functions on the stack. --after waits for a
line in LogOutput.log before sampling (e.g. "Quick-load: loading slot"),
--until stops at one (e.g. "scene 'ForestMain_v08' loaded"). Mono JIT code
shows as "(jit)". Dev-time only; windows only; the game is never changed.
"""
import collections, ctypes, ctypes.wintypes as wt, importlib.util, os, struct, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("symcrash", os.path.join(HERE, "symbolize-crash.py"))
symcrash = importlib.util.module_from_spec(spec)
spec.loader.exec_module(symcrash)

GAME = r"G:\SteamLibrary\steamapps\common\The Forest"
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)


class THREADENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ThreadID", wt.DWORD),
                ("th32OwnerProcessID", wt.DWORD), ("tpBasePri", wt.LONG), ("tpDeltaPri", wt.LONG), ("dwFlags", wt.DWORD)]


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ProcessID", wt.DWORD),
                ("th32DefaultHeapID", ctypes.c_void_p), ("th32ModuleID", wt.DWORD), ("cntThreads", wt.DWORD),
                ("th32ParentProcessID", wt.DWORD), ("pcPriClassBase", wt.LONG), ("dwFlags", wt.DWORD),
                ("szExeFile", wt.WCHAR * 260)]


class MODULEINFO(ctypes.Structure):
    _fields_ = [("lpBaseOfDll", ctypes.c_void_p), ("SizeOfImage", wt.DWORD), ("EntryPoint", ctypes.c_void_p)]


k32.CreateToolhelp32Snapshot.restype = wt.HANDLE
k32.OpenProcess.restype = wt.HANDLE
k32.OpenThread.restype = wt.HANDLE


def find_game():
    snap = k32.CreateToolhelp32Snapshot(0x2, 0)
    pe = PROCESSENTRY32W(); pe.dwSize = ctypes.sizeof(pe)
    ok = k32.Process32FirstW(snap, ctypes.byref(pe))
    pid = None
    while ok:
        if pe.szExeFile.lower() == "theforest.exe": pid = pe.th32ProcessID
        ok = k32.Process32NextW(snap, ctypes.byref(pe))
    k32.CloseHandle(snap)
    if pid is None: sys.exit("TheForest.exe is not running")
    snap = k32.CreateToolhelp32Snapshot(0x4, 0)
    te = THREADENTRY32(); te.dwSize = ctypes.sizeof(te)
    ok = k32.Thread32First(snap, ctypes.byref(te))
    main = None
    while ok:
        if te.th32OwnerProcessID == pid and main is None: main = te.th32ThreadID   # the first listed is the main thread
        ok = k32.Thread32Next(snap, ctypes.byref(te))
    k32.CloseHandle(snap)
    return pid, main


def modules(hproc):
    arr = (ctypes.c_void_p * 1024)(); need = wt.DWORD()
    psapi.EnumProcessModulesEx(hproc, arr, ctypes.sizeof(arr), ctypes.byref(need), 0x3)
    out = []
    for i in range(need.value // ctypes.sizeof(ctypes.c_void_p)):
        name = ctypes.create_unicode_buffer(260)
        psapi.GetModuleBaseNameW(hproc, ctypes.c_void_p(arr[i]), name, 260)
        mi = MODULEINFO()
        psapi.GetModuleInformation(hproc, ctypes.c_void_p(arr[i]), ctypes.byref(mi), ctypes.sizeof(mi))
        out.append((mi.lpBaseOfDll, mi.lpBaseOfDll + mi.SizeOfImage, name.value))
    return out


def wait_log(text, since):
    while True:
        try:
            with open(LOG, "rb") as f:
                f.seek(since)
                if text.encode() in f.read(): return
        except OSError: pass
        time.sleep(0.05)


def main():
    args = sys.argv[1:]
    secs = float(args[0]) if args and not args[0].startswith("--") else 60.0
    after = args[args.index("--after") + 1] if "--after" in args else None
    until = args[args.index("--until") + 1] if "--until" in args else None

    exe = os.path.join(GAME, "TheForest.exe")
    name, key = symcrash.pdb_record(exe)
    syms = symcrash.read_publics(symcrash.fetch_pdb(name, key))
    keys = [s[0] for s in syms]
    import bisect

    pid, tid = find_game()
    hproc = k32.OpenProcess(0x0410, False, pid)
    hthr = k32.OpenThread(0x0002 | 0x0008 | 0x0040, False, tid)
    mods = modules(hproc)
    exe_lo = [m for m in mods if m[2].lower() == "theforest.exe"][0]

    def label(a):
        for lo, hi, n in mods:
            if lo <= a < hi:
                if n.lower() == "theforest.exe":
                    i = bisect.bisect_right(keys, a - lo) - 1
                    return syms[i][1] if i >= 0 else "?", True
                return n, True
        return "(jit)", False

    ctx_buf = ctypes.create_string_buffer(1232 + 16)
    ctx_addr = (ctypes.addressof(ctx_buf) + 15) & ~15
    stack = ctypes.create_string_buffer(0x8000)
    got = ctypes.c_size_t()

    log_size = os.path.getsize(LOG) if os.path.exists(LOG) else 0
    if after:
        print("waiting for:", after); wait_log(after, log_size)
    print("sampling main thread %d" % tid)
    top = collections.Counter(); chains = collections.Counter(); n = 0
    t_end = time.time() + secs
    while time.time() < t_end:
        if until and n % 10 == 0:
            with open(LOG, "rb") as f:
                f.seek(log_size)
                if until.encode() in f.read(): break
        if k32.SuspendThread(hthr) == 0xFFFFFFFF: break
        try:
            struct.pack_into("<I", (ctypes.c_char * 1232).from_address(ctx_addr), 0x30, 0x100003)
            if not k32.GetThreadContext(hthr, ctypes.c_void_p(ctx_addr)): continue
            raw = ctypes.string_at(ctx_addr, 1232)
            rsp, rip = struct.unpack_from("<Q", raw, 0x98)[0], struct.unpack_from("<Q", raw, 0xF8)[0]
            k32.ReadProcessMemory(hproc, ctypes.c_void_p(rsp), stack, len(stack), ctypes.byref(got))
        finally:
            k32.ResumeThread(hthr)
        n += 1
        top[label(rip)[0]] += 1
        chain = []
        for i in range(0, got.value - 7, 8):
            v = struct.unpack_from("<Q", stack.raw, i)[0]
            if exe_lo[0] <= v < exe_lo[1]:
                lab = label(v)[0]
                if not chain or chain[-1] != lab: chain.append(lab)
                if len(chain) == 6: break
        chains[" <- ".join(chain)] += 1
        time.sleep(0.05)
    print("%d samples" % n)
    print("\n== where the thread was (instruction pointer)")
    for k, v in top.most_common(25): print("%5d  %5.1f%%  %s" % (v, 100.0 * v / max(n, 1), k))
    print("\n== Unity functions on the stack (nearest first)")
    for k, v in chains.most_common(15): print("%5d  %5.1f%%  %s" % (v, 100.0 * v / max(n, 1), k))


# ----------------------------------------------------------------------
# --snapshot N: N walks of EVERY thread's stack (dbghelp StackWalk64, 5 s
# apart) - who a waiting main thread waits for. JIT frames have no unwind
# data, so a walk can end or go astray at the first one.

class ADDRESS64(ctypes.Structure):
    _fields_ = [("Offset", ctypes.c_uint64), ("Segment", ctypes.c_ushort), ("Mode", ctypes.c_int)]


class STACKFRAME64(ctypes.Structure):
    _fields_ = [("AddrPC", ADDRESS64), ("AddrReturn", ADDRESS64), ("AddrFrame", ADDRESS64), ("AddrStack", ADDRESS64),
                ("AddrBStore", ADDRESS64), ("FuncTableEntry", ctypes.c_void_p), ("Params", ctypes.c_uint64 * 4),
                ("Far", ctypes.c_int), ("Virtual", ctypes.c_int), ("Reserved", ctypes.c_uint64 * 3),
                ("KdHelp", ctypes.c_byte * 0x100)]


def exports(path):
    d = open(path, "rb").read()
    pe, opt, secs = symcrash.pe_sections(d)
    rva, _ = struct.unpack_from("<II", d, opt + 112)

    def off(r):
        for va, vs, ra in secs:
            if va <= r < va + vs: return r - va + ra
    o = off(rva)
    nnames, afuncs, anames, aords = struct.unpack_from("<IIII", d, o + 24)
    out = []
    for i in range(nnames):
        nrva = struct.unpack_from("<I", d, off(anames) + 4 * i)[0]
        ordi = struct.unpack_from("<H", d, off(aords) + 2 * i)[0]
        frva = struct.unpack_from("<I", d, off(afuncs) + 4 * ordi)[0]
        no = off(nrva)
        out.append((frva, d[no:d.index(b"\0", no)].decode("latin1")))
    out.sort()
    return out


def snapshot(count, after):
    import bisect
    exe = os.path.join(GAME, "TheForest.exe")
    name, key = symcrash.pdb_record(exe)
    syms = symcrash.read_publics(symcrash.fetch_pdb(name, key))
    keys = [s[0] for s in syms]
    pid, main_tid = find_game()
    hproc = k32.OpenProcess(0x1F0FFF, False, pid)
    mods = modules(hproc)
    exp = {}
    for lo, hi, n in mods:
        if n.lower() == "mono.dll":
            path = ctypes.create_unicode_buffer(520)
            psapi.GetModuleFileNameExW(hproc, ctypes.c_void_p(lo), path, 520)
            e = exports(path.value)
            exp[n] = ([a for a, _ in e], e)

    def label(a):
        for lo, hi, n in mods:
            if lo <= a < hi:
                if n.lower() == "theforest.exe":
                    i = bisect.bisect_right(keys, a - lo) - 1
                    return "exe!" + (syms[i][1] if i >= 0 else "?")
                if n in exp:
                    ks, e = exp[n]
                    i = bisect.bisect_right(ks, a - lo) - 1
                    return "%s!%s+0x%x" % (n, e[i][1], a - lo - e[i][0]) if i >= 0 else n
                return "%s+0x%x" % (n, a - lo)
        return "(jit) 0x%x" % a

    dbg = ctypes.WinDLL("dbghelp")
    dbg.SymInitialize(hproc, None, True)
    dbg.StackWalk64.argtypes = [wt.DWORD, wt.HANDLE, wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
    fta = ctypes.cast(dbg.SymFunctionTableAccess64, ctypes.c_void_p)
    gmb = ctypes.cast(dbg.SymGetModuleBase64, ctypes.c_void_p)

    log_size = os.path.getsize(LOG) if os.path.exists(LOG) else 0
    if after:
        print("waiting for:", after); wait_log(after, log_size); time.sleep(2)
    for s in range(count):
        snap = k32.CreateToolhelp32Snapshot(0x4, 0)
        te = THREADENTRY32(); te.dwSize = ctypes.sizeof(te)
        tids = []
        ok = k32.Thread32First(snap, ctypes.byref(te))
        while ok:
            if te.th32OwnerProcessID == pid: tids.append(te.th32ThreadID)
            ok = k32.Thread32Next(snap, ctypes.byref(te))
        k32.CloseHandle(snap)
        print("\n######## snapshot %d (%s), %d threads" % (s + 1, time.strftime("%H:%M:%S"), len(tids)))
        for tid in tids:
            h = k32.OpenThread(0x1F03FF, False, tid)
            if not h: continue
            buf = ctypes.create_string_buffer(1232 + 16)
            ctx = (ctypes.addressof(buf) + 15) & ~15
            k32.SuspendThread(h)
            try:
                struct.pack_into("<I", (ctypes.c_char * 1232).from_address(ctx), 0x30, 0x10000B)
                if not k32.GetThreadContext(h, ctypes.c_void_p(ctx)): continue
                raw = ctypes.string_at(ctx, 1232)
                f = STACKFRAME64()
                f.AddrPC.Offset = struct.unpack_from("<Q", raw, 0xF8)[0]; f.AddrPC.Mode = 3
                f.AddrFrame.Offset = struct.unpack_from("<Q", raw, 0xA0)[0]; f.AddrFrame.Mode = 3
                f.AddrStack.Offset = struct.unpack_from("<Q", raw, 0x98)[0]; f.AddrStack.Mode = 3
                frames = []
                while len(frames) < 30 and dbg.StackWalk64(0x8664, hproc, h, ctypes.byref(f), ctypes.c_void_p(ctx),
                                                           None, fta, gmb, None):
                    if f.AddrPC.Offset == 0: break
                    frames.append(label(f.AddrPC.Offset))
            finally:
                k32.ResumeThread(h); k32.CloseHandle(h)
            # threads parked in a plain wait with nothing of the game on top are noise
            if tid != main_tid and all(not x.startswith("exe!") and not x.startswith("mono") and not x.startswith("(jit")
                                       for x in frames):
                continue
            print("-- thread %d%s" % (tid, " (MAIN)" if tid == main_tid else ""))
            for x in frames: print("     " + x[:200])
        if s + 1 < count: time.sleep(5)


if __name__ == "__main__":
    if "--snapshot" in sys.argv:
        a = sys.argv
        snapshot(int(a[a.index("--snapshot") + 1]), a[a.index("--after") + 1] if "--after" in a else None)
    else:
        main()
