import SwiftUI

private let fmt = Calc.fmt

// ───────────────────────── Topraklama ─────────────────────────

struct GroundingModule: View {
    @State private var rho = "100"
    @State private var useStrip = true
    @State private var useRod = true
    @State private var useFound = true
    @State private var useMesh = false
    @State private var stripL = "106"
    @State private var stripH = "1"
    @State private var stripD = 0.008
    @State private var rodL = "1.75"
    @State private var rodD = "0.02"
    @State private var rodN = "8"
    @State private var fa = "17"
    @State private var fb = "28"
    @State private var meshL = "170"
    @State private var ia = "25"
    @State private var ul = "50"
    @State private var wr = ["", "", "", ""]
    @State private var season = "2"
    @State private var ika = "10"
    @State private var t = "1"
    private let conductors = Calc.named("conductors")
    private let aList = ["1", "2", "4", "6"]

    var body: some View {
        var input: [(String, Any?)] = [
            ("rho", rho), ("use_strip", useStrip), ("strip_l", stripL), ("strip_d", stripD), ("strip_h", stripH),
            ("use_rod", useRod), ("rod_l", rodL), ("rod_d", rodD), ("rod_n", rodN), ("use_found", useFound), ("fa", fa), ("fb", fb),
            ("use_mesh", useMesh), ("mesh_l", meshL), ("ia", ia), ("ul", ul), ("season", season), ("ika", ika), ("t", t),
        ]
        for i in 0..<4 { input.append(("w_r.\(i)", wr[i])) }
        let r = Calc.run("grounding", input)
        return ModulePage(route: .grounding, subtitle: "Şerit, çubuk, temel ve gözlü topraklama · Wenner · Onderdonk") {
            MCard("Toprak") {
                NumField("Özgül direnç ρ", "Ωm", text: $rho)
                Note("Ölçüm yaptıysan Wenner bölümünden ortalama ρ'yu bulup buraya yaz.")
            }
            MCard("Topraklayıcılar") {
                Toggle("Gömülü şerit / iletken", isOn: $useStrip)
                if useStrip {
                    FieldRow { NumField("Uzunluk", "m", text: $stripL); NumField("Derinlik", "m", text: $stripH) }
                    NamedPicker(label: "İletken (eşdeğer çap)", options: conductors, value: $stripD)
                }
                Toggle("Çubuk topraklayıcı", isOn: $useRod)
                if useRod {
                    FieldRow { NumField("Boy", "m", text: $rodL); NumField("Çap", "m", text: $rodD); NumField("Adet", text: $rodN) }
                }
                Toggle("Temel topraklayıcı", isOn: $useFound)
                FieldRow { NumField("Temel eni", "m", text: $fa); NumField("Temel boyu", "m", text: $fb) }
                Note("Temel ölçüleri en az çubuk sayısı hesabında da kullanılır.")
                Toggle("Gözlü (ağ) topraklama", isOn: $useMesh)
                if useMesh { NumField("Toplam ağ iletkeni", "m", text: $meshL) }
            }
        } right: {
            MCard("Sonuç") {
                if useStrip { ResultRow("Şerit R", fmt(r.d("r_strip"), 2), "Ω") }
                if useRod { ResultRow("Çubuklar R", fmt(r.d("r_rod"), 2), "Ω") }
                if useFound { ResultRow("Temel R", fmt(r.d("r_found"), 2), "Ω") }
                if useMesh { ResultRow("Gözlü R", fmt(r.d("r_mesh"), 2), "Ω") }
                ResultRow("Paralel eşdeğer R", fmt(r.d("r_total"), 3), "Ω", ok: r.b("ok"), big: true)
                FieldRow { NumField("Ia (koruma akımı)", "A", text: $ia); NumField("UL (dokunma)", "V", text: $ul) }
                ResultRow("İzin verilen en büyük R = UL / Ia", fmt(r.d("r_max"), 3), "Ω")
                ResultRow("Temel alanına göre en az çubuk", r["min_rods"], "adet")
            }
            MCard("Wenner ölçümü (ρ = 2π·a·R)") {
                ForEach(0..<4, id: \.self) { i in
                    HStack(alignment: .bottom, spacing: 10) {
                        Text("a = \(aList[i]) m").frame(width: 70, alignment: .leading).padding(.bottom, 8)
                        NumField("Ölçülen R", "Ω", text: $wr[i])
                        Text("ρ = \(fmt(r.d("w_rho.\(i)"), 1))").frame(width: 110, alignment: .leading).padding(.bottom, 8)
                    }
                }
                NumField("Mevsim katsayısı", text: $season)
                ResultRow("Hesaba esas ortalama ρ", fmt(r.d("rho_avg"), 1), "Ωm", big: true)
            }
            MCard("Onderdonk: kısa devreye göre iletken kesiti") {
                FieldRow { NumField("Kısa devre akımı", "kA", text: $ika); NumField("Süre", "s", text: $t) }
                ResultRow("Kaynak (Cadweld)", fmt(r.d("onderdonk_cadweld"), 1), "mm²")
                ResultRow("Lehim / bindirme kaynağı", fmt(r.d("onderdonk_lehim"), 1), "mm²")
                ResultRow("Cıvatalı / pres", fmt(r.d("onderdonk_civata"), 1), "mm²")
            }
        }
    }
}

// ───────────────────────── Paratoner ─────────────────────────

struct LightningModule: View {
    @State private var l = "26"
    @State private var w = "21"
    @State private var h = "30"
    @State private var nk = "20"
    @State private var mast = "6"
    @State private var c1 = 0.25
    @State private var c2 = 1.0
    @State private var c3 = 0.5
    @State private var c4 = 1.0
    @State private var c5 = 5.0
    @State private var level = 0
    @State private var dt = 60
    private let levelText = ["Otomatik", "Seviye 1", "Seviye 2", "Seviye 3", "Seviye 4"]

    var body: some View {
        let r = Calc.run("lightning", [("l", l), ("w", w), ("h", h), ("nk", nk), ("c1", c1), ("c2", c2), ("c3", c3), ("c4", c4), ("c5", c5),
                                        ("mast", mast), ("delta_t", dt), ("level", level)])
        ModulePage(route: .lightning, subtitle: "Yıldırım risk analizi (Nd/Nc), koruma seviyesi, aktif paratoner yarıçapı") {
            MCard("Yapı") {
                FieldRow { NumField("Boy L", "m", text: $l); NumField("En W", "m", text: $w); NumField("Yükseklik H", "m", text: $h) }
                NumField("Yıllık yıldırımlı gün Nk", "gün", text: $nk)
                Note("Nk: aşağıdaki yıldırımlı gün haritasından.")
                NamedPicker(label: "C1 · çevre", options: Calc.named("lp_c1"), value: $c1)
                NamedPicker(label: "C2 · yapı türü", options: Calc.named("lp_c2"), value: $c2)
                NamedPicker(label: "C3 · içerik", options: Calc.named("lp_c3"), value: $c3)
                NamedPicker(label: "C4 · kullanım", options: Calc.named("lp_c4"), value: $c4)
                NamedPicker(label: "C5 · sonuç", options: Calc.named("lp_c5"), value: $c5)
            }
            MCard("Yıldırımlı gün haritası (Nk)") { ContentImage("img/yildirim_haritasi.jpg") }
        } right: {
            MCard("Risk") {
                ResultRow("Eşdeğer toplama alanı Ae", fmt(r.d("ae"), 0), "m²")
                ResultRow("Yer yıldırım yoğunluğu Ng", fmt(r.d("ng"), 3), "/km²·yıl")
                ResultRow("Beklenen düşme Nd", sci(r.d("nd")))
                ResultRow("Kabul edilebilir Nc", sci(r.d("nc")))
                if r.i("level") == 0 {
                    ResultRow("Sonuç", "Koruma zorunlu değil", ok: true, big: true)
                } else {
                    ResultRow("Sonuç", "E = %\(fmt(r.d("e") * 100, 1)) → Seviye \(r["level"])", ok: false, big: true)
                }
            }
            MCard("Aktif paratoner (ESE)") {
                ChoiceField("Koruma seviyesi", [0, 1, 2, 3, 4], selection: $level) { levelText[$0] }
                NumField("Direk yüksekliği", "m", text: $mast)
                ChoiceField("ΔT (µs)", [15, 25, 45, 60], selection: $dt) { "\($0)" }
                ResultRow("Yuvarlanan küre D", fmt(r.d("d"), 0), "m")
                ResultRow("Kullanılan seviye", r["level_used"])
                ResultRow("Koruma yarıçapı Rp", fmt(r.d("rp"), 1), "m", big: true)
                ResultRow("Yapının yarı köşegeni", fmt(r.d("half_diag"), 1), "m")
                ResultRow("Tek paratoner yeterli mi", r.b("covers") ? "Evet" : "Hayır", ok: r.b("covers"))
            }
        }
    }
}
