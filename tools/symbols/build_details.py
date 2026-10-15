# Hazır detaylar: örnek projeden bölge (dikdörtgen) ile seçilen çizimler → symdxf/ altına mini DXF
import json, os, sys
import ezdxf
from ezdxf import recover, bbox
os.chdir(os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding="utf-8")

# (kimlik, ad, kategori, x0, y0, x1, y1) — bölgeye tamamen giren nesneler alınır
DETAILS = [
    ("DTY_CELIK_DONATI", "Çelik donatılı temel topraklama detayı", "Detay", 1745, -1783, 2234, -1372),
    ("DTY_ETB", "Eşpotansiyel topraklama barası (E.T.B)", "Detay", 1153, -2075, 1683, -1783),
    ("DTY_KROM_CUBUK", "Krom kaplı bakır çubuk", "Detay", 1355, -1740, 1558, -1538),
    ("DTY_SEMBOL_TABLO", "Sembol ve açıklamalar tablosu", "Detay", 2310, -1775, 2635, -1473),
    ("DTY_ANA_PANO", "Ana pano boyutları", "Detay", 3218, -1979, 3670, -1574),
    ("DTY_KOLON_SEMA", "Kuvvetli akım kolon şeması", "Detay", 5642, -3300, 6354, -2555),
    ("DTY_TT_SARTNAME", "Türk Telekom bina içi telefon tesisatı teknik şartnamesi", "Detay", 6402, -3300, 7189, -2439),
    ("DTY_ONEMLI_ACIKLAMA", "Önemli açıklamalar", "Detay", 5963, -2424, 6926, -1543),
]

src, _ = recover.readfile("C:/Users/LENOVO10OCT2020/Downloads/2+1+3f+2+abone.dxf")
msp_src = src.modelspace()
cache = []
for e in msp_src:
    b = bbox.extents([e], fast=True)
    if b.has_data: cache.append((e, b.extmin.x, b.extmin.y, b.extmax.x, b.extmax.y))

meta = json.load(open("symdxf/meta.json", encoding="utf-8"))
meta["symbols"] = [m for m in meta["symbols"] if not m["id"].startswith("DTY_")]
for sid, name, cat, X0, Y0, X1, Y1 in DETAILS:
    ents = [e for e, x0, y0, x1, y1 in cache if x0 >= X0 and x1 <= X1 and y0 >= Y0 and y1 <= Y1]
    out = ezdxf.new("R2000", setup=True)
    msp = out.modelspace()
    flat = []
    for e in ents:
        if e.dxftype() == "INSERT":
            for v in e.virtual_entities(): flat.append((v, e))
            for a in e.attribs: flat.append((a, e))
        else:
            flat.append((e, None))
    bb = bbox.extents([v for v, _ in flat], fast=True)
    cx, cy = (bb.extmin.x + bb.extmax.x) / 2, (bb.extmin.y + bb.extmax.y) / 2
    for v, parent in flat:
        if v.dxftype() == "ATTRIB":
            n = msp.add_text(v.dxf.text, dxfattribs={k: v.dxf.get(k) for k in ("insert", "height", "rotation", "halign", "valign", "align_point", "width", "color", "layer", "style") if v.dxf.hasattr(k)})
        else:
            n = v.copy()
            msp.add_entity(n)
        layer_name = n.dxf.get("layer", "0")
        if parent is not None and layer_name == "0": layer_name = parent.dxf.layer
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
        # yazı stili mini dosyada da tanımlı olsun (yoksa STANDARD)
        if n.dxftype() in ("TEXT", "MTEXT") and n.dxf.hasattr("style"):
            sn = n.dxf.style
            if not out.styles.has_entry(sn):
                if src.styles.has_entry(sn):
                    ss = src.styles.get(sn)
                    out.styles.add(sn, font=ss.dxf.get("font", "txt") or "txt")
                else:
                    n.dxf.style = "Standard"
        n.translate(-cx, -cy, 0)
    out.saveas(f"symdxf/{sid}.dxf")
    meta["symbols"].append({"id": sid, "name": name, "cat": cat, "w": bb.extmax.x - bb.extmin.x, "h": bb.extmax.y - bb.extmin.y})
    print(f"{sid:22s} nesne={len(flat):5d}  {bb.extmax.x - bb.extmin.x:6.0f} x {bb.extmax.y - bb.extmin.y:6.0f}")
json.dump(meta, open("symdxf/meta.json", "w", encoding="utf-8"), ensure_ascii=False)
