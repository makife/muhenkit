import json, statistics, os
import ezdxf
from ezdxf import recover, bbox
os.chdir(os.path.dirname(os.path.abspath(__file__)))
from curated import CURATED
src, _ = recover.readfile("C:/Users/LENOVO10OCT2020/Downloads/2+1+3f+2+abone.dxf")
clusters = {c["id"]: c for c in json.load(open("unique.json", encoding="cp1254"))}
# örnek projenin ortanca yazı yüksekliği: hedef çizime ölçeklemenin referansı
hs = [e.dxf.height for e in src.modelspace().query("TEXT") if e.dxf.height > 0]
ref_h = statistics.median(hs)
meta = []
for cid, sid, name, cat in CURATED:
    c = clusters[cid]
    ents = [src.entitydb[h] for h in c["handles"]]
    out = ezdxf.new("R2000", setup=True)
    msp = out.modelspace()
    flat = []
    for e in ents:
        if e.dxftype() == "INSERT":
            for v in e.virtual_entities():  # blok patlat
                flat.append((v, e))
            for a in e.attribs:
                flat.append((a, e))
        else:
            flat.append((e, None))
    geo = [v for v, _ in flat if v.dxftype() not in ("TEXT", "MTEXT", "ATTRIB")] or [v for v, _ in flat]
    bb = bbox.extents(geo, fast=True)
    cx, cy = (bb.extmin.x + bb.extmax.x) / 2, (bb.extmin.y + bb.extmax.y) / 2
    for v, parent in flat:
        if v.dxftype() == "ATTRIB":
            n = msp.add_text(v.dxf.text, dxfattribs={k: v.dxf.get(k) for k in ("insert", "height", "rotation", "halign", "valign", "align_point", "width", "color", "layer", "style") if v.dxf.hasattr(k)})
        else:
            n = v.copy()
            msp.add_entity(n)
        layer_name = n.dxf.get("layer", "0")
        if parent is not None and layer_name == "0":
            layer_name = parent.dxf.layer
        lay = src.layers.get(layer_name) if src.layers.has_entry(layer_name) else None
        col = n.dxf.get("color", 256)
        if col == 256 and lay is not None: n.dxf.color = abs(lay.color)
        elif col == 0:
            pc = parent.dxf.get("color", 256) if parent is not None else 256
            n.dxf.color = pc if pc not in (0, 256) else abs(lay.color if lay else 7)
        lt = n.dxf.get("linetype", "BYLAYER")
        if lt.upper() == "BYLAYER" and lay is not None and lay.dxf.linetype.upper() != "CONTINUOUS":
            n.dxf.linetype = lay.dxf.linetype
        n.dxf.layer = "0"
        n.translate(-cx, -cy, 0)
    out.saveas(f"symdxf/{sid}.dxf")
    meta.append({"id": sid, "name": name, "cat": cat, "w": bb.extmax.x - bb.extmin.x, "h": bb.extmax.y - bb.extmin.y})
json.dump({"ref_h": ref_h, "symbols": meta}, open("symdxf/meta.json", "w", encoding="utf-8"), ensure_ascii=False)
print("sembol", len(meta), "referans yazi yuksekligi", round(ref_h, 3))
