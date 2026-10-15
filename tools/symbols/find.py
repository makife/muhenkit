import re, sys
from ezdxf import recover
sys.stdout.reconfigure(encoding="utf-8")
doc, _ = recover.readfile("C:/Users/LENOVO10OCT2020/Downloads/2+1+3f+2+abone.dxf")
ESC = re.compile(r"\\U\+([0-9A-Fa-f]{4})")


def dec(s):
    return ESC.sub(lambda m: chr(int(m.group(1), 16)), s)


keys = sys.argv[1].split(",")
for e in doc.modelspace().query("TEXT MTEXT"):
    t = dec(e.dxf.text if e.dxftype() == "TEXT" else e.plain_text())
    u = t.upper()
    if any(k in u for k in keys):
        p = e.dxf.insert
        h = e.dxf.get("height", 0) if e.dxftype() == "TEXT" else e.dxf.get("char_height", 0)
        print(f"{p.x:8.0f} {p.y:8.0f} h={h:5.1f} {e.dxf.layer[:14]:14s} | {t[:100]}")
