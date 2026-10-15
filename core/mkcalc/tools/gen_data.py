# Eski Flutter uygulamasının veri dosyalarından C++ tablo üretir (elle kopyalama hatası olmasın):
#   lib/data/lux_table.dart  → LuxCategory/LuxItem
#   lib/data/tables.dart     → priceList (PriceItem)
# Kullanım: python gen_data.py <elektrik_cizim/lib> <çıktı data.cpp>
import re
import sys

lib, out = sys.argv[1], sys.argv[2]


def cstr(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


lux = open(f"{lib}/data/lux_table.dart", encoding="utf-8").read()
rows = []
cat = None
for line in lux.splitlines():
    m = re.search(r"LuxCategory\('([^']*)'", line)
    if m:
        cat = m.group(1)
        continue
    m = re.search(r"LuxItem\('([^']*)',\s*([0-9.]+|null),\s*([0-9.]+|null)\)", line)
    if m:
        g = -1 if m.group(2) == "null" else float(m.group(2))
        o = -1 if m.group(3) == "null" else float(m.group(3))
        rows.append((cat, m.group(1), g, o))

tables = open(f"{lib}/data/tables.dart", encoding="utf-8").read()
prices = re.findall(r"PriceItem\('([^']*)',\s*'([^']*)',\s*'([^']*)',\s*([0-9.]+)\)", tables)

with open(out, "w", encoding="utf-8") as f:
    f.write("// OTOMATİK ÜRETİLDİ: tools/gen_data.py — elle düzenlemeyin.\n")
    f.write("// Kaynak: enaz aydınlık düzeyi tablosu; AG fiyat listesi (Schneider Electric, 26 Mayıs 2018).\n")
    f.write('#include "tables.h"\n\nnamespace mc {\n\n')
    f.write("const LuxItem kLux[] = {\n")
    for c, n, g, o in rows:
        f.write(f"    {{{cstr(c)}, {cstr(n)}, {g}, {o}}},\n")
    f.write("};\n")
    f.write(f"const int kLuxCount = {len(rows)};\n\n")
    f.write("const PriceItem kPrices[] = {\n")
    for g, n, r, p in prices:
        f.write(f"    {{{cstr(g)}, {cstr(n)}, {cstr(r)}, {p}}},\n")
    f.write("};\n")
    f.write(f"const int kPriceCount = {len(prices)};\n\n}}  // namespace mc\n")
print(f"lux={len(rows)} fiyat={len(prices)}")
