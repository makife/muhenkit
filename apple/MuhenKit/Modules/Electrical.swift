import SwiftUI

private let fmt = Calc.fmt

// ───────────────────────── Aydınlatma (verim metodu) ─────────────────────────

struct LightingModule: View {
    @State private var a = "6", b = "4", h = "3", work = "0.85", susp = "0", lux = "300"
    @State private var combo = 0
    @State private var soil = 1.25
    @State private var lum = 3600.0
    @State private var flux = "3600"

    private let combos = Calc.table("combos").dropFirst().map { $0[0] }
    private let soiling = Calc.named("soiling")
    private let lums = Calc.named("luminaires")

    var body: some View {
        let r = Calc.run("lighting", [("a", a), ("b", b), ("H", h), ("work", work), ("susp", susp), ("lux", lux),
                                       ("combo", combo), ("soiling", soil), ("flux", flux)])
        let cols = max(1, r.i("cols")), rows = max(1, r.i("rows"))
        let aa = max(Calc.num(a) ?? 1, 0.1), bb = max(Calc.num(b) ?? 1, 0.1)
        ModulePage(route: .lighting, subtitle: "Verim metodu: armatür sayısı, oda indeksi ve yerleşim") {
            MCard("Mahal") {
                FieldRow { NumField("En a", "m", text: $a); NumField("Boy b", "m", text: $b); NumField("Yükseklik H", "m", text: $h) }
                FieldRow { NumField("Çalışma düzlemi", "m", text: $work); NumField("Sarkıt", "m", text: $susp); NumField("Aydınlık E", "lx", text: $lux) }
                ChoiceField("Tavan-duvar-yer yansıtma", Array(combos.indices), selection: $combo) { combos[$0] }
                NamedPicker(label: "Kirlenme (bakım) faktörü", options: soiling, value: $soil)
            }
            MCard("Armatür") {
                NamedPicker(label: "Hazır armatür", options: lums, value: $lum) { flux = Calc.field($0) }
                NumField("Armatür ışık akısı Ø", "lm", text: $flux)
            }
            MCard("Armatür yüksekliği") { ContentImage("img/armatur_yukseklik.jpg") }
        } right: {
            MCard("Sonuç") {
                ResultRow("Hesap yüksekliği h = H − düzlem − sarkıt", fmt(r.d("h"), 2), "m")
                ResultRow("Oda indeksi k = a·b / h·(a+b)", fmt(r.d("k"), 2))
                ResultRow("Verim η (tablodan)", fmt(r.d("eta"), 3))
                ResultRow("Gerekli toplam akı Φ = E·A·d / η", fmt(r.d("total_flux"), 0), "lm")
                ResultRow("Hesaplanan armatür", fmt(r.d("exact_count"), 2))
                ResultRow("Kullanılacak armatür", "\(r.i("count"))  (\(r.i("cols")) × \(r.i("rows")))", big: true)
                ResultRow("Elde edilen aydınlık", fmt(r.d("achieved_lux"), 0), "lx", ok: r.d("achieved_lux") >= (Calc.num(lux) ?? 0))
            }
            MCard("Yerleşim") {
                Canvas { ctx, size in
                    let k = min(size.width / aa, size.height / bb) * 0.9
                    let w = aa * k, hh = bb * k, ox = (size.width - w) / 2, oy = (size.height - hh) / 2
                    ctx.stroke(Path(CGRect(x: ox, y: oy, width: w, height: hh)), with: .color(Color(red: 0.93, green: 0.92, blue: 0.87)), lineWidth: 2)
                    let rr = min(w / Double(cols), hh / Double(rows)) * 0.18
                    for i in 0..<cols {
                        for j in 0..<rows {
                            let p = CGPoint(x: ox + w * (Double(i) + 0.5) / Double(cols), y: oy + hh * (Double(j) + 0.5) / Double(rows))
                            ctx.stroke(Path(ellipseIn: CGRect(x: p.x - rr, y: p.y - rr, width: rr * 2, height: rr * 2)), with: .color(Theme.accent), lineWidth: 2.5)
                            let q = rr * 0.7
                            var x = Path()
                            x.move(to: CGPoint(x: p.x - q, y: p.y - q)); x.addLine(to: CGPoint(x: p.x + q, y: p.y + q))
                            x.move(to: CGPoint(x: p.x - q, y: p.y + q)); x.addLine(to: CGPoint(x: p.x + q, y: p.y - q))
                            ctx.stroke(x, with: .color(Theme.accent), lineWidth: 1.5)
                        }
                    }
                }
                .frame(height: 260)
                Note("Armatürler aralarında eşit, duvara yarım aralık olacak şekilde yerleştirilir: \(fmt(aa / Double(cols), 2)) m × \(fmt(bb / Double(rows), 2)) m.")
            }
        }
    }
}

// ───────────────────────── Gerilim düşümü ─────────────────────────

private let sections: [Double] = [1.5, 2.5, 4, 6, 10, 16, 25, 35, 50, 70, 95, 120, 150, 185, 240, 300]

struct VoltageDropModule: View {
    @State private var three = true, cu = true
    @State private var p = "11008", l = "30", limit = "1.5", cos = "0.8"
    @State private var s = 10.0

    var body: some View {
        let r = Calc.run("voltage_drop", [("three", three), ("k", cu ? 56 : 35), ("p", p), ("l", l), ("s", s), ("limit", limit), ("cos", cos)])
        ModulePage(route: .drop, subtitle: "Bir / üç faz %e, kesit önerisi ve ısınma kontrolü") {
            MCard("Hat") {
                ChoiceField(nil, [true, false], selection: $three) { $0 ? "Üç faz 380 V" : "Bir faz 220 V" }
                ChoiceField(nil, [true, false], selection: $cu) { $0 ? "Bakır (k=56)" : "Alüminyum (k=35)" }
                FieldRow { NumField("Güç P", "W", text: $p); NumField("Uzunluk L", "m", text: $l) }
                FieldRow { NumField("%e sınırı", "%", text: $limit); NumField("cos φ", text: $cos) }
                ChoiceField("Kesit (mm²)", sections, selection: $s) { fmt($0, 1) }
            }
        } right: {
            MCard("Sonuç") {
                ResultRow("Gerilim düşümü %e", fmt(r.d("e"), 3), "%", ok: r.b("drop_ok"), big: true)
                ResultRow("ΔU", fmt(r.d("dv"), 2), "V")
                ResultRow("Akım", fmt(r.d("i"), 2), "A")
                ResultRow("Sigorta", r["fuse"], "A")
                if r["selected.name"].isEmpty {
                    ResultRow("Isınma (kablo ≥ sigorta)", "tabloda yok")
                } else {
                    ResultRow("Isınma (kablo ≥ sigorta)", "\(r["selected.name"]): \(fmt(r.d("selected.ampacity"), 0)) A", ok: r.b("heat_ok"))
                }
                ResultRow("Sınır için en az kesit", fmt(r.d("s_min"), 2), "mm²")
                ResultRow("Önerilen kablo", r["suggested.name"], big: true)
            }
        }
    }
}

// ───────────────────────── Eşzamanlılık ─────────────────────────

struct DiversityModule: View {
    @State private var flatW = "16000", flats = "12", common = "6000", cf = "1", cos = "0.8"
    private let rows = Calc.table("diversity").dropFirst().map { t in [t[1] == "99999" ? "\(t[0]) ve üstü" : "\(t[0]) – \(t[1])", "%" + t[2]] }

    var body: some View {
        let r = Calc.run("diversity", [("flat_w", flatW), ("flats", flats), ("common", common), ("common_factor", cf), ("cos", cos)])
        ModulePage(route: .diversity, subtitle: "Daire %60/%40 kuralı ve bina eşzamanlılık katsayısı") {
            MCard("Bina") {
                FieldRow { NumField("Daire kurulu gücü", "W", text: $flatW); NumField("Daire sayısı", text: $flats) }
                FieldRow { NumField("Ortak alan gücü", "W", text: $common); NumField("Ortak alan faktörü", text: $cf) }
                NumField("cos φ", text: $cos)
                Note("Ortak alan: merdiven, asansör, hidrofor vb.")
            }
        } right: {
            MCard("Sonuç") {
                ResultRow("Daire talep gücü (8 kW'a kadar %60, üstü %40)", fmt(r.d("per_flat"), 0), "W")
                ResultRow("Bina eşzamanlılık katsayısı", "%" + r["factor"])
                ResultRow("Dairelerin talep gücü", fmt(r.d("flats_demand"), 0), "W")
                ResultRow("Ortak alan talep gücü", fmt(r.d("common_demand"), 0), "W")
                ResultRow("Toplam talep gücü", fmt(r.d("total"), 0), "W", big: true)
                ResultRow("Akım", fmt(r.d("i"), 2), "A")
                ResultRow("Ana sigorta", r["fuse"], "A", big: true)
            }
            MCard("Bina eşzamanlılık tablosu") {
                Note("Konutlarda bir dairenin eşzamanlı yükü: kurulu gücün 8 kW'a kadar olan bölümü %60, kalan bölümü %40. Binanın eşzamanlı yükü için daire sayısına göre katsayı:")
                DataTable(headers: ["Daire sayısı", "Katsayı"], rows: rows, highlight: r.i("row"))
            }
        }
    }
}

// ───────────────────────── Kablo seçimi ─────────────────────────

struct CableModule: View {
    @State private var amps = "45", factor = "1"
    @State private var mode = 0
    private let cables = Calc.table("cables").dropFirst().map { [$0[0], $0[2], $0[3], $0[4], $0[5]] }
    private let nyy = Calc.table("nyy").dropFirst().map { Array($0.prefix(5)) }

    var body: some View {
        let r = Calc.run("cable", [("amps", amps), ("mode", mode), ("factor", factor)])
        let has = !r["cable.name"].isEmpty
        ModulePage(route: .cable, subtitle: "NYY akım taşıma tablolarına göre sigortaya uygun kesit") {
            MCard("Girdi") {
                NumField("Yük akımı", "A", text: $amps)
                ChoiceField("Döşeme", [0, 1, 2], selection: $mode) { ["Hesap (ATK)", "Toprakta", "Havada"][$0] }
                NumField("Düzeltme katsayısı (sıcaklık, gruplama)", text: $factor)
            }
            MCard("Hesap tablosu (ATK)") {
                DataTable(headers: ["Kablo", "Hesap A", "Toprak A", "Hava A", "R Ω/km"], rows: cables, weights: [1.4, 1, 1, 1, 1],
                          highlight: has ? r.i("index") : nil)
            }
        } right: {
            MCard("Sonuç") {
                ResultRow("Sigorta", r["fuse"], "A")
                ResultRow("Seçilen kablo", has ? r["cable.name"] : "tabloda yok", ok: has, big: true)
                if has {
                    ResultRow("Faz kesiti", fmt(r.d("cable.section"), 1), "mm²")
                    ResultRow("R / X", "\(fmt(r.d("cable.r"), 4)) / \(fmt(r.d("cable.x"), 3))", "Ω/km")
                }
            }
            MCard("NYY (YVV) 0,6/1 kV kablo kataloğu · TS 212") {
                DataTable(headers: ["Kesit mm²", "R Ω/km", "Toprak A", "Hava A", "kg/km"], rows: nyy, weights: [1.4, 1, 1, 1, 1])
            }
        }
    }
}

// ───────────────────────── Motor & TMŞ ─────────────────────────

struct MotorModule: View {
    @State private var kw = "7.5", cos = "0.85", eff = "0.88"
    private let rows = Calc.table("motor").dropFirst().map { Array($0.prefix(8)) }

    var body: some View {
        let r = Calc.run("motor", [("kw", kw), ("cos", cos), ("eff", eff)])
        ModulePage(route: .motor, subtitle: "Motor gücüne göre akım, termik röle, sigorta ve kesit (Tablo 1.1)") {
            MCard("Motor") {
                NumField("Motor gücü", "kW", text: $kw)
                FieldRow { NumField("cos φ", text: $cos); NumField("Verim η", text: $eff) }
            }
            MCard("Tablo 1.1 · Motor gücüne göre termik röle ve sigorta seçimi") {
                DataTable(headers: ["kW", "HP", "Akım A", "mm²", "Termik A", "Buşon N", "Buşon G", "NH A"], rows: rows,
                          weights: [0.8, 0.8, 0.9, 0.7, 1.3, 1, 1, 1.1], highlight: r.i("row"))
                Note("Buşon N: buşonlu normal · Buşon G: buşonlu gecikmeli · NH: bıçaklı sigorta")
            }
        } right: {
            MCard("Sonuç (tablodan \(r["kw"]) kW / \(r["hp"]) HP)") {
                ResultRow("Hesaplanan akım", fmt(r.d("i_calc"), 2), "A")
                ResultRow("Tablo akımı", fmt(r.d("amps"), 2), "A")
                ResultRow("Termik röle ayar aralığı", r["termik"], "A", big: true)
                ResultRow("Buşonlu sigorta (normal)", r["buson_normal"], "A")
                ResultRow("Buşonlu sigorta (gecikmeli)", r["buson_gecikmeli"], "A")
                ResultRow("NH sigorta", r["nh"], "A")
                ResultRow("İletken kesiti", fmt(r.d("kesit"), 1), "mm²", big: true)
            }
        }
    }
}
