#!/usr/bin/env python3
"""save-diff.py A.json B.json [noise.txt] - diffs two JSONLevelSerializer dumps.
Prints per stored component: added / removed / changed fields (flattened paths).
Fields listed in noise.txt (Type|path per line, from a control diff) are skipped."""
import json, sys, os

def load(p):
    d = json.load(open(p, encoding='utf-8-sig'))
    items = {}
    for it in d['StoredItems']['___contents']:
        key = (it['Type'], it['Name'])
        try:
            data = json.loads(it['Data'].replace(':None', ':null').replace('[None', '[null').replace(',None', ',null'))
        except Exception:
            data = it['Data']
        items.setdefault(key, []).append(data)
    names = {}
    son = d.get('StoredObjectNames', {})
    for o in son.get('___contents', []) if isinstance(son, dict) else []:
        names[o.get('Name')] = o
    return items, names

def flat(o, pre, out):
    if isinstance(o, dict):
        for k, v in o.items():
            if k == '___i':
                continue
            if (k.startswith('contents') or k.startswith('count')) and k[-1:].isdigit() and 'dimension0' in o:
                k = k.rstrip('0123456789')
            flat(v, pre + '.' + k if pre else k, out)
    elif isinstance(o, list):
        for i, v in enumerate(o):
            flat(v, '%s[%d]' % (pre, i), out)
    elif isinstance(o, str) and o[:1] in '{[':
        try:
            flat(json.loads(o.replace(':None', ':null').replace('[None', '[null').replace(',None', ',null')), pre, out)
        except Exception:
            out[pre] = o
    else:
        out[pre] = o
    return out

def main():
    a, an = load(sys.argv[1])
    b, bn = load(sys.argv[2])
    noise = set()
    if len(sys.argv) > 3 and os.path.exists(sys.argv[3]):
        noise = set(l.strip() for l in open(sys.argv[3]) if l.strip())
    out_noise = [] if len(sys.argv) > 4 and sys.argv[4] == '--write-noise' else None
    keys = sorted(set(a) | set(b))
    added = [k for k in keys if k not in a]
    removed = [k for k in keys if k not in b]
    bytype = {}
    for k in keys:
        if k not in a or k not in b:
            continue
        fa = {}
        for i, x in enumerate(a[k]): flat(x, str(i), fa)
        fb = {}
        for i, x in enumerate(b[k]): flat(x, str(i), fb)
        for p in sorted(set(fa) | set(fb)):
            va, vb = fa.get(p, '<none>'), fb.get(p, '<none>')
            if va == vb:
                continue
            gp = k[0] + '|' + ''.join(c for c in p if not c.isdigit())
            if gp in noise:
                continue
            if out_noise is not None:
                out_noise.append(gp)
            bytype.setdefault(k[0], []).append((k[1], p, va, vb))
    if out_noise is not None:
        open(sys.argv[3], 'w').write('\n'.join(sorted(set(out_noise))) + '\n')
    def objname(n):
        o = an.get(n) or bn.get(n)
        return (o.get('Name') if o else None) or n
    print('added in B: %d, removed in B: %d' % (len(added), len(removed)))
    from collections import Counter
    for label, lst in (('added', added), ('removed', removed)):
        c = Counter(k[0] for k in lst)
        for t, n in c.most_common(40):
            print('  %s %s x%d' % (label, t, n))
    for t in sorted(bytype):
        rows = bytype[t]
        print('== %s: %d field change(s) on %d object(s)' % (t, len(rows), len(set(r[0] for r in rows))))
        for n, p, va, vb in rows[:int(os.environ.get('JD_ROWS', '12'))]:
            sa, sb = str(va)[:70], str(vb)[:70]
            print('   %s  %s: %s -> %s' % (n[:8], p, sa, sb))

main()
