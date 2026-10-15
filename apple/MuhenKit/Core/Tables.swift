import Foundation

/// Izgara tablo üretici (Android TableBuilder.kt / Windows Tables.cs ile aynı): DXF blok içeriği (LINE + TEXT)
/// ve önizleme. Birimler çizim birimidir; ekleme noktası sol üst köşe.
final class TableBuilder {
    /// align: 0 sol, 1 orta, 2 sağ
    private struct Cell { let text: String; let col: Int; let row: Int; let colSpan: Int; let align: Int; let scale: Double; let color: Int }

    private let th: Double
    private var cols: [Double] = []
    private var rows: [Double] = []
    private var cells: [Cell] = []

    init(_ textHeight: Double) { th = textHeight }

    @discardableResult
    func columns(_ widths: [Double]) -> TableBuilder {
        cols += widths.map { $0 * th }
        return self
    }

    func row(_ h: Double = 2.2) -> Int {
        rows.append(h * th)
        return rows.count - 1
    }

    func text(_ row: Int, _ col: Int, _ text: String, align: Int = 1, colSpan: Int = 1, scale: Double = 1, color: Int = 7) {
        cells.append(Cell(text: text, col: col, row: row, colSpan: colSpan, align: align, scale: scale, color: color))
    }

    var width: Double { cols.reduce(0, +) }
    var height: Double { rows.reduce(0, +) }
    private func x(_ c: Int) -> Double { cols.prefix(c).reduce(0, +) }
    private func y(_ r: Int) -> Double { -rows.prefix(r).reduce(0, +) }

    /// Birleşik hücrelerin iç çizgileri atlanarak kenarlar (x1,y1,x2,y2)
    private func edges() -> [[Double]] {
        let nR = rows.count, nC = cols.count
        var owner = Array(repeating: Array(repeating: -1, count: nC), count: nR)
        for (i, c) in cells.enumerated() {
            for r in c.row..<min(nR, c.row + 1) {
                for k in c.col..<min(nC, c.col + c.colSpan) { owner[r][k] = i }
            }
        }
        func same(_ r1: Int, _ c1: Int, _ r2: Int, _ c2: Int) -> Bool {
            guard r1 >= 0, r1 < nR, r2 >= 0, r2 < nR, c1 >= 0, c1 < nC, c2 >= 0, c2 < nC else { return false }
            let a = owner[r1][c1]
            return a >= 0 && a == owner[r2][c2]
        }
        var out: [[Double]] = []
        for r in 0...nR {
            for c in 0..<nC {
                if r >= 1 && r < nR && same(r - 1, c, r, c) { continue }
                out.append([x(c), y(r), x(c + 1), y(r)])
            }
        }
        for c in 0...nC {
            for r in 0..<nR {
                if c >= 1 && c < nC && same(r, c - 1, r, c) { continue }
                out.append([x(c), y(r), x(c), y(r + 1)])
            }
        }
        return out
    }

    private static func num(_ v: Double) -> String {
        var s = String(format: "%.6f", locale: Locale(identifier: "en_US_POSIX"), v)
        if s.contains(".") {
            while s.hasSuffix("0") { s.removeLast() }
            if s.hasSuffix(".") { s.removeLast() }
        }
        return s
    }

    /// Yazı sığmazsa küçültülür (txt.shx karakter genişliği ≈ 0,85·yükseklik)
    private func fitHeight(_ c: Cell) -> Double {
        let w = x(c.col + c.colSpan) - x(c.col) - th * 0.6
        var h = th * c.scale
        let est = Double(c.text.count) * h * 0.85
        if est > w && w > 0 { h *= w / est }
        return h
    }

    func build(id: String, name: String) -> CadSymbol {
        var sb = ""
        func t(_ code: Int, _ v: Any) { sb += "\(code)\n\(v)\n" }
        var segs: [Float] = []
        guard !rows.isEmpty, !cols.isEmpty else {
            return CadSymbol(id: id, name: name, category: "Tablo", width: 1, height: 1, snippet: "", lines: [], fills: [], texts: [])
        }
        for e in edges() {
            t(0, "LINE"); t(100, "AcDbEntity"); t(8, "0"); t(62, 7); t(100, "AcDbLine")
            t(10, Self.num(e[0])); t(20, Self.num(e[1])); t(30, 0); t(11, Self.num(e[2])); t(21, Self.num(e[3])); t(31, 0)
            segs += e.map { Float($0) }
        }
        var texts: [SymText] = []
        for c in cells where !c.text.trimmingCharacters(in: .whitespaces).isEmpty {
            let x0 = x(c.col), x1 = x(c.col + c.colSpan), yTop = y(c.row), yBot = y(c.row + 1)
            let h = fitHeight(c)
            let ax = c.align == 0 ? x0 + th * 0.3 : c.align == 2 ? x1 - th * 0.3 : (x0 + x1) / 2
            let ay = (yTop + yBot) / 2
            t(0, "TEXT"); t(100, "AcDbEntity"); t(8, "0"); t(62, c.color); t(100, "AcDbText")
            t(10, Self.num(ax)); t(20, Self.num(ay)); t(30, 0); t(40, Self.num(h)); t(1, c.text.replacingOccurrences(of: "\n", with: " "))
            t(7, "STANDARD"); t(72, c.align); t(11, Self.num(ax)); t(21, Self.num(ay)); t(31, 0); t(100, "AcDbText"); t(73, 2)
            texts.append(SymText(rgb: 0xFFFFFF, x: Float(ax), y: Float(ay), h: Float(h), rot: 0, hAlign: c.align, vAlign: 2, text: c.text))
        }
        return CadSymbol(id: id, name: name, category: "Tablo", width: width, height: height, snippet: sb,
                         lines: [(0xFFFFFF, segs)], fills: [], texts: texts)
    }
}

/// Proje tabloları: aydınlatma hesabı, toplam güç, firma anteti
enum ProjectTables {
    private static func stamp() -> String { String(Int64(Date().timeIntervalSince1970 * 1000) % 1_000_000) }
    static func d(_ s: String) -> Double? { Calc.num(s) }
    static func f(_ v: Double, _ digits: Int = 2) -> String { Calc.fmt(v, digits) }

    // ───────────────────────── aydınlatma ─────────────────────────

    struct Room: Identifiable {
        let id = UUID()
        var name = "", a = "", b = "", H = "3", work = "0.85", lux = "", refl = "80-50-30"
        var k = "", eta = "", power = "", flux = "", type = "", zCalc = "", zUsed = ""
    }

    static func roomIndex(_ r: Room) -> Double? {
        guard let a = d(r.a), let b = d(r.b), let H = d(r.H) else { return nil }
        let h = H - (d(r.work) ?? 0)
        guard h > 0, a + b > 0 else { return nil }
        return a * b / (h * (a + b))
    }

    private static let etaTable: [(Double, Double)] = [(0.6, 0.27), (0.8, 0.32), (1.0, 0.36), (1.25, 0.41), (1.5, 0.45), (2.0, 0.51),
                                                       (2.5, 0.55), (3.0, 0.58), (4.0, 0.62), (5.0, 0.65)]

    static func suggestEta(_ k: Double) -> Double {
        if k <= etaTable[0].0 { return etaTable[0].1 }
        if k >= etaTable[etaTable.count - 1].0 { return etaTable[etaTable.count - 1].1 }
        for i in 1..<etaTable.count {
            let (k0, e0) = etaTable[i - 1], (k1, e1) = etaTable[i]
            if k <= k1 { return e0 + (e1 - e0) * (k - k0) / (k1 - k0) }
        }
        return etaTable[etaTable.count - 1].1
    }

    /// Z = (E · a · b · 1,25) / (Φ · η)
    static func lampCount(_ r: Room, _ k: Double?) -> Double? {
        guard let e = d(r.lux), let a = d(r.a), let b = d(r.b), let phi = d(r.flux) else { return nil }
        guard let eta = d(r.eta) ?? k.map(suggestEta), phi > 0, eta > 0 else { return nil }
        return e * a * b * 1.25 / (phi * eta)
    }

    static func computed(_ r: Room) -> (k: Double?, eta: Double?, z: Double?, used: String) {
        let k = d(r.k) ?? roomIndex(r)
        let eta = d(r.eta) ?? k.map(suggestEta)
        var rc = r
        if let e = eta { rc.eta = f(e) }
        let z = d(r.zCalc) ?? lampCount(rc, k)
        let used = r.zUsed.trimmingCharacters(in: .whitespaces).isEmpty ? (z.map { String(Int(ceil($0 - 1e-9))) } ?? "") : r.zUsed
        return (k, eta, z, used)
    }

    static func resolved(_ r: Room) -> [String] {
        let c = computed(r)
        let hTxt = [r.H, r.work].filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }.joined(separator: " - ")
        return [r.name, r.a, r.b, r.H, hTxt, r.lux, r.refl, c.k.map { f($0) } ?? "", c.eta.map { f($0) } ?? "", r.power, r.flux, r.type,
                c.z.map { f($0) } ?? "", c.used]
    }

    static let lightRows = [
        "MAHAL İSMİ", "Mahal eni ( a ) ( m )", "Mahal boyu ( b ) ( m )", "Mahal yüksekliği ( H ) ( m )", "Armatür yüksekliği ( h ) ( m )",
        "İstenilen aydınlık şiddeti ( E ) Lüx", "Tavan-duvar-yer yansıtma faktörleri", "Mahal endeksi ( k )  k = (a x b) / h x (a + b)",
        "Mahal verimi (cetvelden) ( η )", "Seçilen armatür gücü ( W )", "Seçilen armatür ışık akısı ( Ø )", "Seçilen armatür tipi",
        "Hesaplanan armatür sayısı  Z = (E x a x b x 1,25) / Ø x η", "Kullanılacak armatür sayısı",
    ]

    static func lightingTable(_ th: Double, _ rooms: [Room], note: String) -> CadSymbol {
        let tb = TableBuilder(th)
        tb.columns([38] + Array(repeating: 9, count: rooms.count))
        let values = rooms.map(resolved)
        for (i, label) in lightRows.enumerated() {
            let r = tb.row(i == 0 ? 2.6 : 2.2)
            let sc = i == 0 ? 1.1 : 1.0
            tb.text(r, 0, label, align: 0, scale: sc)
            for (c, v) in values.enumerated() { tb.text(r, c + 1, v[i], scale: sc) }
        }
        if !note.trimmingCharacters(in: .whitespaces).isEmpty {
            let r = tb.row(2.2)
            tb.text(r, 0, note, align: 0, colSpan: rooms.count + 1, scale: 0.85)
        }
        return tb.build(id: "TBL_AYD_" + stamp(), name: "Aydınlatma hesap tablosu")
    }

    // ───────────────────────── toplam güç ─────────────────────────

    struct Load: Identifiable {
        let id = UUID()
        var name = "", installed = "", demandPct = ""
    }

    static func powerSums(_ loads: [Load]) -> (installed: Double, demand: Double) {
        loads.reduce((0.0, 0.0)) { acc, l in
            let inst = d(l.installed) ?? 0
            return (acc.0 + inst, acc.1 + inst * (d(l.demandPct) ?? 100) / 100)
        }
    }

    static func powerTable(_ th: Double, title: String, _ loads: [Load]) -> CadSymbol {
        let tb = TableBuilder(th)
        tb.columns([4, 18, 11, 9, 11])
        let r0 = tb.row(2.8)
        tb.text(r0, 0, title.trimmingCharacters(in: .whitespaces).isEmpty ? "TOPLAM GÜÇ TABLOSU" : title, colSpan: 5, scale: 1.25)
        let r1 = tb.row(2.6)
        for (i, s) in ["No", "Bölüm / Daire", "Kurulu güç (W)", "Eş zam. (%)", "Talep güç (W)"].enumerated() { tb.text(r1, i, s, scale: 0.9) }
        var sumI = 0.0, sumD = 0.0, n = 0
        for l in loads where !l.name.trimmingCharacters(in: .whitespaces).isEmpty || !l.installed.trimmingCharacters(in: .whitespaces).isEmpty {
            let inst = d(l.installed) ?? 0, pct = d(l.demandPct) ?? 100, dem = inst * pct / 100
            sumI += inst
            sumD += dem
            n += 1
            let r = tb.row()
            tb.text(r, 0, String(n))
            tb.text(r, 1, l.name, align: 0)
            tb.text(r, 2, f(inst, 0))
            tb.text(r, 3, f(pct, 0))
            tb.text(r, 4, f(dem, 0))
        }
        let rt = tb.row(2.6)
        tb.text(rt, 0, "TOPLAM", colSpan: 2, scale: 1.1)
        tb.text(rt, 2, f(sumI, 0), scale: 1.1)
        tb.text(rt, 3, sumI > 0 ? f(sumD / sumI * 100, 0) : "")
        tb.text(rt, 4, f(sumD, 0), scale: 1.1)
        let rs = tb.row(2.4)
        tb.text(rs, 0, "TOPLAM KURULU GÜÇ = \(f(sumI, 0)) W     TOPLAM TALEP GÜÇ = \(f(sumD, 0)) W", align: 0, colSpan: 5)
        return tb.build(id: "TBL_GUC_" + stamp(), name: "Toplam güç tablosu")
    }

    // ───────────────────────── firma anteti ─────────────────────────

    static let titleFields: [(key: String, label: String, def: String)] = [
        ("firma", "Firma adı", ""), ("firmaAlt", "Firma alt yazısı (ör. elektrik – otomasyon)", ""),
        ("proje", "Proje adı", "ELEKTRİK İÇ TESİSAT PROJESİ"), ("sorumlu", "Proje sorumlusu adı soyadı", ""),
        ("smm", "S.M.M. no", ""), ("et", "E.T. (oda sicil) no", ""), ("yaptiran", "Projeyi yaptıran adı soyadı", ""),
        ("yaptiranAdres", "Projeyi yaptıran adresi", ""), ("vergi", "Vergi dairesi / no", ""), ("yapiSahibi", "Yapı sahibi", ""),
        ("il", "İl", ""), ("ilce", "İlçe", ""), ("adres", "Yapı adresi", ""), ("pafta", "Pafta", ""), ("ada", "Ada", ""), ("parsel", "Parsel", ""),
        ("sinif", "Yapı sınıfı", ""), ("kat", "Kat sayısı", ""), ("alan", "Toplam alan (m²)", ""), ("bagimsiz", "Bağımsız bölüm sayısı", ""),
        ("amac", "Kullanım amacı", "KONUT"), ("kurulu", "Toplam kurulu güç (W)", ""), ("talepFaktor", "Talep faktörü (%)", ""),
        ("talep", "Toplam talep güç (W)", ""), ("cizen", "Çizen", ""), ("tarih", "Çizim tarihi", ""), ("olcek", "Ölçek", "1/50"),
        ("projeNo", "Proje no / pafta", ""),
    ]

    static func titleBlock(_ th: Double, _ v: [String: String]) -> CadSymbol {
        func g(_ k: String) -> String { v[k] ?? "" }
        let tb = TableBuilder(th)
        tb.columns([10, 10, 10, 10, 10, 10])
        func pair(_ label: String, _ value: String, _ c: Int, _ span: Int, _ r: Int) {
            tb.text(r, c, label, align: 0, scale: 0.75)
            tb.text(r, c + 1, value, align: 0, colSpan: span)
        }
        var r = tb.row(5.5)
        tb.text(r, 0, g("firma"), colSpan: 6, scale: 2.4)
        if !g("firmaAlt").trimmingCharacters(in: .whitespaces).isEmpty { r = tb.row(2.4); tb.text(r, 0, g("firmaAlt"), colSpan: 6, scale: 1.1) }
        r = tb.row(3.6); tb.text(r, 0, g("proje"), colSpan: 6, scale: 1.6)
        r = tb.row(2.2)
        tb.text(r, 0, "PROJE SORUMLUSU", colSpan: 2, scale: 0.85)
        tb.text(r, 2, "S.M.M. NO", scale: 0.85); tb.text(r, 3, "E.T. NO", scale: 0.85); tb.text(r, 4, "İMZA", colSpan: 2, scale: 0.85)
        r = tb.row(3.0)
        tb.text(r, 0, g("sorumlu"), colSpan: 2); tb.text(r, 2, g("smm")); tb.text(r, 3, g("et")); tb.text(r, 4, "", colSpan: 2)
        r = tb.row(2.2); tb.text(r, 0, "PROJEYİ YAPTIRAN", colSpan: 6, scale: 0.85)
        r = tb.row(); pair("ADI SOYADI", g("yaptiran"), 0, 5, r)
        r = tb.row(); pair("ADRESİ", g("yaptiranAdres"), 0, 5, r)
        r = tb.row(); pair("VERGİ D./NO", g("vergi"), 0, 2, r); pair("YAPI SAHİBİ", g("yapiSahibi"), 3, 2, r)
        r = tb.row(2.2); tb.text(r, 0, "ARSANIN ÖZELLİKLERİ", colSpan: 6, scale: 0.85)
        r = tb.row(2.0)
        for (i, s) in ["İL", "İLÇE", "ADRES", "PAFTA", "ADA", "PARSEL"].enumerated() { tb.text(r, i, s, scale: 0.75) }
        r = tb.row()
        for (i, k) in ["il", "ilce", "adres", "pafta", "ada", "parsel"].enumerated() { tb.text(r, i, g(k)) }
        r = tb.row(2.2); tb.text(r, 0, "YAPININ ÖZELLİKLERİ", colSpan: 6, scale: 0.85)
        r = tb.row(2.0)
        for (i, s) in ["SINIFI", "KAT SAYISI", "TOP. ALAN (m²)", "BAĞ. BÖL. SAY.", "KULLANIM AMACI", "TALEP FAK. (%)"].enumerated() { tb.text(r, i, s, scale: 0.75) }
        r = tb.row()
        for (i, k) in ["sinif", "kat", "alan", "bagimsiz", "amac", "talepFaktor"].enumerated() { tb.text(r, i, g(k)) }
        r = tb.row(2.6)
        tb.text(r, 0, "TOPLAM KURULU GÜÇ = \(g("kurulu")) W", align: 0, colSpan: 3)
        tb.text(r, 3, "TOPLAM TALEP GÜÇ = \(g("talep")) W", align: 0, colSpan: 3)
        r = tb.row(2.0)
        for (i, s) in ["ÇİZEN", "", "ÇİZİM TARİHİ", "", "ÖLÇEK", "PROJE NO"].enumerated() where !s.isEmpty { tb.text(r, i, s, scale: 0.75) }
        r = tb.row()
        tb.text(r, 0, g("cizen"), colSpan: 2); tb.text(r, 2, g("tarih"), colSpan: 2); tb.text(r, 4, g("olcek")); tb.text(r, 5, g("projeNo"))
        return tb.build(id: "TBL_ANTET_" + stamp(), name: "Firma anteti")
    }
}
