import json, sys
from ezdxf import recover, bbox
SRC = "C:/Users/LENOVO10OCT2020/Downloads/2+1+3f+2+abone.dxf"
GAP = float(sys.argv[1]) if len(sys.argv) > 1 else 2.0
doc, _ = recover.readfile(SRC, )
msp = doc.modelspace()
X0, X1, Y0, Y1 = 885, 3439, -4555, -3932
items = []
for e in msp:
    b = bbox.extents([e], fast=True)
    if not b.has_data: continue
    x0, y0, x1, y1 = b.extmin.x, b.extmin.y, b.extmax.x, b.extmax.y
    if x0 >= X0 - 1 and x1 <= X1 + 1 and y0 >= Y0 - 1 and y1 <= Y1 + 1:
        if (x1 - x0) > 1000: continue  # çerçeve
        items.append([e.dxf.handle, x0, y0, x1, y1, e.dxftype()])
n = len(items)
par = list(range(n))
def f(i):
    while par[i] != i:
        par[i] = par[par[i]]; i = par[i]
    return i
# kutu yakınlığı ile birleştir (ızgara hızlandırması)
cell = 20.0
grid = {}
for i, it in enumerate(items):
    for gx in range(int((it[1] - GAP) // cell), int((it[3] + GAP) // cell) + 1):
        for gy in range(int((it[2] - GAP) // cell), int((it[4] + GAP) // cell) + 1):
            grid.setdefault((gx, gy), []).append(i)
for cellItems in grid.values():
    for a in range(len(cellItems)):
        for b in range(a + 1, len(cellItems)):
            i, j = cellItems[a], cellItems[b]
            A, B = items[i], items[j]
            if A[1] - GAP <= B[3] and B[1] - GAP <= A[3] and A[2] - GAP <= B[4] and B[2] - GAP <= A[4]:
                par[f(i)] = f(j)
groups = {}
for i in range(n): groups.setdefault(f(i), []).append(i)
cl = []
for g in groups.values():
    x0 = min(items[i][1] for i in g); y0 = min(items[i][2] for i in g)
    x1 = max(items[i][3] for i in g); y1 = max(items[i][4] for i in g)
    cl.append({"handles": [items[i][0] for i in g], "bbox": [x0, y0, x1, y1], "types": sorted(set(items[i][5] for i in g))})
cl.sort(key=lambda c: (round(-c["bbox"][3] / 40), c["bbox"][0]))
json.dump(cl, open("clusters.json", "w"))
print("nesne", n, "kume", len(cl), "tek-yazi kume", sum(1 for c in cl if c["types"] in (["TEXT"], ["MTEXT"])))
