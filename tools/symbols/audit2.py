import sys
from ezdxf import recover
doc, aud = recover.readfile(sys.argv[1])
mk = [b.name for b in doc.blocks if b.name.startswith("MK_")]
ins = [e for e in doc.modelspace().query("INSERT") if e.dxf.name.startswith("MK_")]
print(f"  {doc.dxfversion}: ezdxf hata={len(aud.errors)} duzeltme={len(aud.fixes)} | MK blok={len(mk)} yerlestirme={len(ins)}")
for e in aud.errors[:5]: print("   HATA", e.message)
