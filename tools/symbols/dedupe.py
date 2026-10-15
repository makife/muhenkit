import json, math
from collections import Counter
from ezdxf import recover
doc, _ = recover.readfile("C:/Users/LENOVO10OCT2020/Downloads/2+1+3f+2+abone.dxf")
cl = json.load(open("clusters.json"))
def ents(c): return [doc.entitydb[h] for h in c["handles"]]
def text_of(e):
    if e.dxftype() == "TEXT": return e.dxf.text.strip()
    if e.dxftype() == "MTEXT": return e.plain_text().replace("\n", " ").strip()
    return None
for i, c in enumerate(cl):
    es = ents(c)
    c["id"] = i
    c["texts"] = [t for t in (text_of(e) for e in es) if t]
    c["rot"] = [round(e.dxf.rotation) % 360 for e in es if e.dxftype() == "TEXT"]
    c["layers"] = Counter(e.dxf.layer for e in es).most_common(1)[0][0]
    c["geom"] = [e.dxftype() for e in es if e.dxftype() not in ("TEXT", "MTEXT")]
    x0, y0, x1, y1 = c["bbox"]; c["w"], c["h"] = x1 - x0, y1 - y0
# sınıflama: yalnız yazı → etiket; uzun tek çizgi → kablo; çok büyük → şema
labels = [c for c in cl if not c["geom"]]
wires = [c for c in cl if c["geom"] and set(c["geom"]) <= {"LINE", "LWPOLYLINE"} and len(c["geom"]) <= 2 and min(c["w"], c["h"]) < 1 and max(c["w"], c["h"]) > 50]
syms = [c for c in cl if c["geom"] and c not in wires]
# yakın etiketi olmayan sembollere en yakın yazıyı ad olarak ver (≤ 25 birim)
def dist(a, b):
    ax0, ay0, ax1, ay1 = a["bbox"]; bx0, by0, bx1, by1 = b["bbox"]
    dx = max(0, max(ax0, bx0) - min(ax1, bx1)); dy = max(0, max(ay0, by0) - min(ay1, by1))
    return math.hypot(dx, dy)
for s in syms:
    if not s["texts"]:
        near = sorted(labels, key=lambda l: dist(s, l))
        if near and dist(s, near[0]) <= 25: s["near"] = " ".join(near[0]["texts"])
# tekrarları ele: geometri türleri + yazılar + (döndürmeden bağımsız) boyut
def key(c):
    g = tuple(sorted(Counter(c["geom"]).items()))
    t = tuple(sorted(c["texts"]))
    d = tuple(sorted((round(c["w"]), round(c["h"]))))
    return (g, t, d)
uniq = {}
for s in syms:
    k = key(s)
    prev = uniq.get(k)
    # tercih: yazısı düz (0°) olan ve en geniş olan
    score = (sum(1 for r in s["rot"] if r == 0), s["w"] >= s["h"])
    if prev is None or score > prev[0]: uniq[k] = (score, s)
out = [v[1] for v in uniq.values()]
out.sort(key=lambda c: c["id"])
json.dump(out, open("unique.json", "w"), ensure_ascii=False)
print("etiket", len(labels), "kablo", len(wires), "sembol", len(syms), "-> benzersiz", len(out))
for c in out: print(f"#{c['id']:3d} {c['layers'][:22]:22s} {round(c['w']):4d}x{round(c['h']):<4d} yazi={c['texts']} yakin={c.get('near','')}")
