import json, re, sys
from ezdxf import recover, bbox
sys.stdout.reconfigure(encoding="utf-8")
doc, _ = recover.readfile("C:/Users/LENOVO10OCT2020/Downloads/2+1+3f+2+abone.dxf")
msp = doc.modelspace()
GAP = float(sys.argv[1]) if len(sys.argv) > 1 else 3.0
ESC = re.compile(r"\\U\+([0-9A-Fa-f]{4})")
dec = lambda s: ESC.sub(lambda m: chr(int(m.group(1), 16)), s)

items = []
for e in msp:
    b = bbox.extents([e], fast=True)
    if not b.has_data: continue
    items.append([e.dxf.handle, b.extmin.x, b.extmin.y, b.extmax.x, b.extmax.y, e])
n = len(items)
par = list(range(n))
def f(i):
    while par[i] != i:
        par[i] = par[par[i]]; i = par[i]
    return i
cell = 40.0
grid = {}
for i, it in enumerate(items):
    w, h = it[3] - it[1], it[4] - it[2]
    if w > 3000 or h > 3000: continue  # dev çizgiler
    for gx in range(int((it[1] - GAP) // cell), int((it[3] + GAP) // cell) + 1):
        for gy in range(int((it[2] - GAP) // cell), int((it[4] + GAP) // cell) + 1):
            grid.setdefault((gx, gy), []).append(i)
for ci in grid.values():
    for a in range(len(ci)):
        A = items[ci[a]]
        for b in range(a + 1, len(ci)):
            B = items[ci[b]]
            if A[1] - GAP <= B[3] and B[1] - GAP <= A[3] and A[2] - GAP <= B[4] and B[2] - GAP <= A[4]:
                par[f(ci[a])] = f(ci[b])
groups = {}
for i in range(n): groups.setdefault(f(i), []).append(i)
heads = sys.argv[2].split("|")
res = {}
for g in groups.values():
    texts = []
    for i in g:
        e = items[i][5]
        if e.dxftype() in ("TEXT", "MTEXT"):
            texts.append(dec(e.dxf.text if e.dxftype() == "TEXT" else e.plain_text()).upper())
    for hd in heads:
        if any(hd in t for t in texts):
            x0 = min(items[i][1] for i in g); y0 = min(items[i][2] for i in g)
            x1 = max(items[i][3] for i in g); y1 = max(items[i][4] for i in g)
            res.setdefault(hd, []).append({"n": len(g), "bbox": [x0, y0, x1, y1], "handles": [items[i][0] for i in g]})
for hd in heads:
    for r in res.get(hd, []):
        x0, y0, x1, y1 = r["bbox"]
        print(f"{hd[:34]:34s} nesne={r['n']:5d}  x {x0:7.0f}..{x1:7.0f}  y {y0:7.0f}..{y1:7.0f}")
json.dump(res, open("details_found.json", "w", encoding="utf-8"), ensure_ascii=False)
