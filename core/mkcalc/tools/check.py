# Rastgele girdilerle eski (Dart) ve yeni (C++) hesapları karşılaştırır.
# Kullanım: python check.py <çalışma klasörü> [durum sayısı]
import random, subprocess, sys, os, math

work = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 300
here = os.path.dirname(os.path.abspath(__file__))
random.seed(7)
R = random.uniform


def case(i, mod, **kv):
    lines = [f"case={i}", f"module={mod}"] + [f"{k}={v}" for k, v in kv.items()]
    return "\n".join(lines) + "\n---\n"


cases = []
i = 0
for _ in range(N):
    cases.append(case(i, "lighting", a=R(2, 30), b=R(2, 25), H=R(2.5, 6), work=random.choice([0, 0.75, 0.85]), susp=R(0, 0.5),
                      lux=random.choice([50, 100, 200, 300, 500, 750]), combo=random.randint(0, 9), soiling=random.choice([1.25, 1.4, 1.55, 1.7]),
                      flux=random.choice([730, 2050, 3600, 5200, 12000]))); i += 1
    cases.append(case(i, "voltage_drop", three=random.randint(0, 1), k=random.choice([56, 35]), p=R(200, 80000), l=R(1, 200),
                      s=random.choice([1.5, 2.5, 4, 6, 10, 16, 25, 35, 50, 70, 95]), limit=random.choice([1, 1.5, 3, 5]), cos=R(0.7, 1))); i += 1
    cases.append(case(i, "diversity", flat_w=R(3000, 30000), flats=random.randint(1, 80), common=R(0, 20000), common_factor=R(0.5, 1), cos=R(0.7, 1))); i += 1
    g = dict(rho=R(10, 1000), fa=R(5, 60), fb=R(5, 60), use_strip=random.randint(0, 1), strip_l=R(10, 300), strip_d=random.choice([0.0045, 0.008, 0.014]),
             strip_h=R(0.5, 1.5), use_rod=random.randint(0, 1), rod_l=R(1, 3), rod_d=0.02, rod_n=random.randint(1, 12), use_found=random.randint(0, 1),
             use_mesh=random.randint(0, 1), mesh_l=R(50, 500), ika=R(1, 40), t=R(0.1, 3), season=R(1, 3))
    for w in range(4):
        g[f"w_r.{w}"] = random.choice([0, R(0.5, 50)])
    cases.append(case(i, "grounding", **g)); i += 1
    cases.append(case(i, "lightning", l=R(5, 120), w=R(5, 80), h=R(3, 120), nk=R(5, 60), c1=random.choice([0.25, 0.5, 1, 2]), c2=random.choice([0.5, 1, 2.5, 3]),
                      c3=random.choice([0.5, 1, 2, 3]), c4=random.choice([0.5, 1, 3]), c5=random.choice([1, 5, 10]), mast=R(2, 10),
                      delta_t=random.choice([15, 25, 45, 60]), level=random.choice([0, 0, 1, 2, 3, 4]))); i += 1
    cases.append(case(i, "motor", kw=R(0.05, 330), cos=R(0.7, 0.95), eff=R(0.7, 0.96))); i += 1
    cases.append(case(i, "cable", amps=R(1, 1300), mode=random.randint(0, 2), factor=R(0.6, 1.2))); i += 1
    n = random.randint(0, 9)
    ls = dict(three=random.randint(0, 1), demand_mode=random.randint(0, 1), demand_factor=R(0.3, 1), kolon_l=R(2, 80), limit=random.choice([1, 1.5, 3]),
              cos=R(0.7, 1), n=n)
    for k in range(n):
        ls[f"linye.{k}.type"] = random.choice("LPOM")
        ls[f"linye.{k}.count"] = random.randint(1, 12)
        ls[f"linye.{k}.unit_w"] = random.choice([60, 100, 300, 2000, 4000, R(10, 6000)])
        ls[f"linye.{k}.length"] = R(2, 60)
        ls[f"linye.{k}.phase"] = random.choice(["R", "S", "T", "RST"]) if ls["three"] else "R"
    cases.append(case(i, "load_schedule", **ls)); i += 1

open(f"{work}/cases.txt", "w", newline="\n").write("".join(cases))  # Windows'ta \r\n olmasın
ref = subprocess.run([os.environ.get("DART", "dart"), "run", f"{here}/ref.dart", f"{work}/cases.txt"], capture_output=True, text=True, encoding="utf-8")
if ref.returncode != 0:
    print("DART HATA", ref.stderr[:2000]); sys.exit(1)
new = subprocess.run([f"{work}/run_cases.exe", f"{work}/cases.txt"], capture_output=True, text=True, encoding="utf-8")


def blocks(text):
    out = {}
    for blk in text.split("---\n"):
        kv = {}
        for line in blk.splitlines():
            if "=" in line:
                k, v = line.split("=", 1)
                kv[k] = v
        if "case" in kv: out[kv["case"]] = kv
    return out


A, B = blocks(ref.stdout), blocks(new.stdout)
mods = {c.split("\n")[0][5:]: c.split("\n")[1][7:] for c in cases}
stats, bad = {}, []
for cid, a in A.items():
    b = B.get(cid, {})
    mod = mods[cid]
    st = stats.setdefault(mod, [0, 0, 0])
    st[0] += 1
    ok = True
    for k, va in a.items():
        if k == "case": continue
        st[1] += 1
        vb = b.get(k)
        try:
            fa, fb = float(va), float(vb)
            same = (math.isnan(fa) and math.isnan(fb)) or abs(fa - fb) <= 1e-9 * max(1, abs(fa))
        except (TypeError, ValueError):
            same = (va or "") == (vb or "")
        if not same:
            ok = False
            bad.append((mod, cid, k, va, vb))
    if not ok: st[2] += 1
for m, (n, f, e) in stats.items():
    print(f"{m:14s} durum={n:4d} karsilastirilan alan={f:6d} uyusmayan durum={e}")
for x in bad[:15]: print("  FARK", x)
print("TOPLAM uyusmayan:", len(bad))
