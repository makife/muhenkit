import SwiftUI

private let fmt = Calc.fmt

// ───────────────────────── Sayaç panosu ─────────────────────────

struct MeterModule: View {
    @State private var need = "14"
    @State private var three = false
    @State private var choice = 0

    var body: some View {
        let r = Calc.run("meter_panel", [("need", need), ("three", three)])
        let n = r.i("options")
        let sel = min(max(choice, 0), max(0, n - 1))
        let p = "option.\(sel)."
        ModulePage(route: .meter, subtitle: "Dolap tipi sayaç panosu ve enerji odası asgari ölçüleri (Çizelge 1)") {
            MCard("Girdi") {
                NumField("Gereken sayaç adedi", text: $need)
                ChoiceField(nil, [false, true], selection: $three) { $0 ? "Üç fazlı sayaç" : "Tek fazlı sayaç" }
            }
        } right: {
            MCard("Seçenekler") {
                if n == 0 {
                    Note("Bu adet için tablo dışında: birden fazla pano gerekir.")
                } else {
                    ChoiceField(nil, Array(0..<n), selection: Binding(get: { sel }, set: { choice = $0 })) { i in
                        "\(r.i("option.\(i).floors")) katlı · \(r["option.\(i).per_row"])/kat"
                    }
                }
            }
            if n > 0 {
                MCard("Pano ve enerji odası") {
                    ResultRow("Bir kattaki sayaç", r[p + "per_row"], "adet")
                    ResultRow("Kat sayısı", r[p + "floors"])
                    ResultRow("Toplam sayaç kapasitesi", r[p + "total"], "adet", big: true)
                    ResultRow("Pano eni", fmt(r.d(p + "width"), 1), "cm", big: true)
                    ResultRow("Pano yüksekliği", fmt(r.d(p + "h"), 0), "cm")
                    ResultRow("Enerji odası en az eni (Şekil A)", fmt(r.d(p + "room_a"), 1), "cm")
                    ResultRow("Enerji odası en az derinliği (Şekil B)", fmt(r.d(p + "room_b"), 1), "cm")
                    ResultRow("Enerji odası yüksekliği", fmt(r.d(p + "room_h"), 0), "cm")
                }
            }
        }
        .onChange(of: need) { _ in choice = 0 }
        .onChange(of: three) { _ in choice = 0 }
    }
}

// ───────────────────────── Aydınlık düzeyleri ─────────────────────────

struct LuxModule: View {
    @State private var query = ""
    private let rows = Calc.table("lux").dropFirst().map { $0 }

    private var groups: [(String, [[String]])] {
        let q = query.trimmingCharacters(in: .whitespaces)
        let tr = Locale(identifier: "tr_TR")
        func has(_ s: String) -> Bool { s.range(of: q, options: [.caseInsensitive, .diacriticInsensitive], locale: tr) != nil }
        let f = rows.filter { q.isEmpty || has($0[0]) || (($0.count > 1) && has($0[1])) }
        var order: [String] = []
        var map: [String: [[String]]] = [:]
        for r in f {
            if map[r[0]] == nil { order.append(r[0]); map[r[0]] = [] }
            map[r[0]]!.append([r.count > 1 ? r[1] : "", r.count > 2 && !r[2].isEmpty ? r[2] : "—", r.count > 3 && !r[3].isEmpty ? r[3] : "—"])
        }
        return order.map { ($0, map[$0]!) }
    }

    var body: some View {
        let g = groups
        ModulePage(route: .lux, subtitle: "Mekan türüne göre en az aydınlık düzeyi (genel / özel lüks)") {
            MCard("Aydınlık düzeyleri") {
                TextInput(label: "Ara (ör. mutfak, sınıf)", text: $query)
            }
            if g.isEmpty { Note("Eşleşen mekan yok.") }
            ForEach(g, id: \.0) { title, rows in
                MCard(title) {
                    DataTable(headers: ["Mekan", "Genel lx", "Özel lx"], rows: rows, weights: [2.6, 1, 1])
                }
            }
        } right: {
            EmptyView()
        }
    }
}

// ───────────────────────── Malzeme keşfi ─────────────────────────

struct MaterialsModule: View {
    @State private var qty: [String: Int] = [:]
    @State private var markup = "1"
    @State private var copied = false
    private let rows = Calc.table("prices").dropFirst().map { $0 }

    private func price(_ r: [String]) -> Double { Double(r[3]) ?? 0 }

    private var groups: [(String, [[String]])] {
        var order: [String] = []
        var map: [String: [[String]]] = [:]
        for r in rows {
            if map[r[0]] == nil { order.append(r[0]); map[r[0]] = [] }
            map[r[0]]!.append(r)
        }
        return order.map { ($0, map[$0]!) }
    }

    var body: some View {
        let m = Calc.num(markup) ?? 1
        let chosen = rows.filter { (qty[$0[2]] ?? 0) > 0 }
        let total = chosen.reduce(0) { $0 + price($1) * Double(qty[$1[2]]!) * m }
        let lines = chosen.map { c in "\(c[0]) \(c[1]) (\(c[2])): \(qty[c[2]]!) adet × \(fmt(price(c) * m, 2)) TL" }
        ModulePage(route: .materials, subtitle: "Sigorta, kaçak akım rölesi, kompakt şalter (2018 liste fiyatları)") {
            ForEach(groups, id: \.0) { title, items in
                MCard(title) {
                    ForEach(items, id: \.self) { r in
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(r[1]).font(.subheadline)
                                Text("\(r[2]) · \(fmt(price(r) * m, 2)) TL").font(.caption).foregroundStyle(Theme.muted)
                            }
                            Spacer()
                            Stepper(value: Binding(get: { qty[r[2]] ?? 0 }, set: { qty[r[2]] = max(0, $0); copied = false }), in: 0...999) {
                                Text("\(qty[r[2]] ?? 0)").monospacedDigit().frame(minWidth: 28, alignment: .trailing)
                            }
                            .fixedSize()
                        }
                    }
                }
            }
        } right: {
            MCard("Toplam") {
                NumField("Güncelleme çarpanı (2018 → bugün)", text: $markup)
                ResultRow("Seçilen kalem", "\(chosen.count)")
                ResultRow("Toplam", fmt(total, 2), "TL", big: true)
                Button(copied ? "Kopyalandı ✓" : "Keşfi panoya kopyala") {
                    Clipboard.copy((["Malzeme keşfi"] + lines + ["TOPLAM: \(fmt(total, 2)) TL"]).joined(separator: "\n"))
                    copied = true
                }
                .disabled(chosen.isEmpty)
                Note("Fiyatlar Schneider Electric 26 Mayıs 2018 liste fiyatlarıdır; çarpanla güncelleyin.")
            }
            MCard("Seçilen kalemler") {
                if lines.isEmpty { Note("Henüz kalem seçilmedi.") }
                ForEach(lines, id: \.self) { Text($0).font(.system(size: 12.5)) }
            }
        }
    }
}

// ───────────────────────── rehberler (core/content/guides.json) ─────────────────────────

enum Guides {
    static let json: [String: Any] = {
        guard let d = try? Data(contentsOf: Content.url("guides.json")),
              let o = try? JSONSerialization.jsonObject(with: d) as? [String: Any] else { return [:] }
        return o
    }()

    static func section(_ k: String) -> [String: Any] { json[k] as? [String: Any] ?? [:] }
    static func strings(_ g: [String: Any], _ k: String) -> [String] { (g[k] as? [Any])?.map { "\($0)" } ?? [] }
    static func pairs(_ g: [String: Any], _ k: String) -> [(String, String)] {
        (g[k] as? [[Any]])?.map { ("\($0.first ?? "")", $0.count > 1 ? "\($0[1])" : "") } ?? []
    }
}

struct WeakModule: View {
    private let g = Guides.section("weak")

    var body: some View {
        let steps = Guides.strings(g, "fiberSteps")
        ModulePage(route: .weak, subtitle: g["subtitle"] as? String) {
            MCard("Bina fiber altyapısı (yeni uygulama)") {
                ForEach(steps.indices, id: \.self) { i in Text("\(i + 1). \(steps[i])").font(.subheadline) }
                Note(g["fiberSource"] as? String ?? "")
            }
            MCard("Daire içi dağılım") {
                ForEach(Guides.strings(g, "flatNotes"), id: \.self) { Text("• " + $0).font(.subheadline) }
            }
        } right: {
            ForEach(Guides.pairs(g, "images"), id: \.0) { title, path in
                MCard(title) { ContentImage(path) }
            }
        }
    }
}

struct SmmModule: View {
    private let g = Guides.section("smm")
    @AppStorage("smm.done") private var doneRaw = ""

    private var done: Set<String> { Set(doneRaw.split(separator: ",").map(String.init)) }

    private func toggle(_ k: String) {
        var d = done
        if d.contains(k) { d.remove(k) } else { d.insert(k) }
        doneRaw = d.sorted().joined(separator: ",")
    }

    private func check(_ key: String, _ text: String) -> some View {
        Button { toggle(key) } label: {
            HStack(alignment: .top, spacing: 10) {
                Image(systemName: done.contains(key) ? "checkmark.square.fill" : "square")
                    .foregroundStyle(done.contains(key) ? Theme.accent : Theme.muted)
                Text(text).font(.subheadline).multilineTextAlignment(.leading)
                Spacer(minLength: 0)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    var body: some View {
        let steps = Guides.pairs(g, "steps")
        let docs = Guides.strings(g, "docs")
        let total = steps.reduce(0.0) { $0 + (Double($1.1) ?? 0) }
        let n = steps.count + docs.count
        let k = steps.indices.filter { done.contains("s\($0)") }.count + docs.indices.filter { done.contains("d\($0)") }.count
        let p = n == 0 ? 0 : Double(k) / Double(n)
        ModulePage(route: .smm, subtitle: g["subtitle"] as? String) {
            MCard("Adımlar ve maliyet") {
                ForEach(steps.indices, id: \.self) { i in
                    HStack {
                        check("s\(i)", steps[i].0)
                        Text("\(fmt(Double(steps[i].1) ?? 0, 0)) TL").font(.subheadline)
                    }
                }
                ResultRow("Toplam başlangıç maliyeti", fmt(total, 0), "TL", big: true)
                Note(g["costNote"] as? String ?? "")
            }
            MCard("SMM'ler ne iş yapar, nereden kazanır?") {
                ForEach(Guides.pairs(g, "jobs"), id: \.0) { a, b in
                    Text(a).font(.subheadline.weight(.semibold))
                    Note(b)
                }
            }
        } right: {
            MCard("İlerleme") {
                ProgressView(value: p).tint(Theme.accent)
                ResultRow("Tamamlanan", "%\(Int(p * 100))")
            }
            MCard("Gerekli evraklar") {
                ForEach(docs.indices, id: \.self) { i in check("d\(i)", docs[i]) }
                Text("Şirket / ortak / ücretli için ek").font(.subheadline.weight(.semibold))
                ForEach(Guides.strings(g, "companyExtra"), id: \.self) { Text("• " + $0).font(.subheadline) }
            }
            MCard("Formlar") {
                ForEach(Guides.pairs(g, "forms"), id: \.0) { a, b in
                    Text(a).font(.subheadline.weight(.semibold))
                    Note(b)
                }
            }
        }
    }
}
