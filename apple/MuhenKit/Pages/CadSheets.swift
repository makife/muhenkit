import SwiftUI

// ───────────────────────── sembol seçici ─────────────────────────

/// Sembol önizlemesi (çizgiler + dolgular, sığdırılmış)
struct SymbolPreview: View {
    let symbol: CadSymbol

    var body: some View {
        Canvas { ctx, size in
            let b = symbol.bounds
            let w = Double(max(b[2] - b[0], 1e-6)), h = Double(max(b[3] - b[1], 1e-6))
            let k = min(size.width / w, size.height / h) * 0.86
            let m = CGAffineTransform(translationX: size.width / 2, y: size.height / 2).scaledBy(x: k, y: -k)
                .translatedBy(x: -Double(b[0] + b[2]) / 2, y: -Double(b[1] + b[3]) / 2)
            for f in symbol.fills {
                var p = Path()
                var i = 0
                while i + 1 < f.pts.count {
                    let q = CGPoint(x: Double(f.pts[i]), y: Double(f.pts[i + 1])).applying(m)
                    if i == 0 { p.move(to: q) } else { p.addLine(to: q) }
                    i += 2
                }
                p.closeSubpath()
                ctx.fill(p, with: .color(CadColors.adapt(f.rgb, alpha: Double(f.alpha))))
            }
            for l in symbol.lines {
                var p = Path()
                var i = 0
                while i + 3 < l.pts.count {
                    p.move(to: CGPoint(x: Double(l.pts[i]), y: Double(l.pts[i + 1])).applying(m))
                    p.addLine(to: CGPoint(x: Double(l.pts[i + 2]), y: Double(l.pts[i + 3])).applying(m))
                    i += 4
                }
                ctx.stroke(p, with: .color(CadColors.adapt(l.rgb)), lineWidth: 1)
            }
        }
    }
}

struct SymbolPicker: View {
    let onPick: (CadSymbol) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var query = ""

    private var groups: [(String, [CadSymbol])] {
        let q = query.trimmingCharacters(in: .whitespaces)
        let tr = Locale(identifier: "tr_TR")
        let list = SymbolLibrary.all.filter { q.isEmpty || ($0.name + " " + $0.category).range(of: q, options: .caseInsensitive, locale: tr) != nil }
        var order: [String] = []
        var map: [String: [CadSymbol]] = [:]
        for s in list {
            if map[s.category] == nil { order.append(s.category); map[s.category] = [] }
            map[s.category]!.append(s)
        }
        return order.map { ($0, map[$0]!) }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 14) {
                    if SymbolLibrary.all.isEmpty { Note("Sembol kütüphanesi bulunamadı.") }
                    ForEach(groups, id: \.0) { cat, syms in
                        Text(cat).font(.headline).foregroundStyle(Theme.accent)
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 92), spacing: 8)], spacing: 8) {
                            ForEach(syms) { s in
                                Button {
                                    onPick(s)
                                    dismiss()
                                } label: {
                                    VStack(spacing: 4) {
                                        SymbolPreview(symbol: s).frame(width: 56, height: 56)
                                        Text(s.name).font(.system(size: 10.5)).multilineTextAlignment(.center).lineLimit(2)
                                    }
                                    .frame(maxWidth: .infinity, minHeight: 96)
                                    .padding(4)
                                    .background(Color.white.opacity(0.05), in: RoundedRectangle(cornerRadius: 8))
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                }
                .padding(16)
            }
            .searchable(text: $query, prompt: "Sembol ara")
            .navigationTitle("Semboller")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
        }
        #if os(macOS)
        .frame(minWidth: 560, minHeight: 560)
        #endif
    }
}

// ───────────────────────── tablo formları ─────────────────────────

enum TableKind: Int, Identifiable, CaseIterable {
    case lighting, power, title
    var id: Int { rawValue }
    var title: String { ["Aydınlatma hesap tablosu", "Toplam güç tablosu", "Firma anteti"][rawValue] }
    var hint: String {
        ["Mahal bazında ölçüler, lüks, verim, armatür seçimi ve sayısı", "Bölüm/daire bazında kurulu güç, eşzamanlılık ve talep gücü",
         "Projeyi yapan firma ve proje bilgileri (bilgiler hatırlanır)"][rawValue]
    }
}

/// Form iskeleti: "Çizime ekle" / "Vazgeç"
private struct FormSheet<Content: View>: View {
    let title: String
    let onDone: () -> Void
    @ViewBuilder var content: Content
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            ScrollView { VStack(alignment: .leading, spacing: 12) { content }.padding(16) }
                .background(Theme.page)
                .navigationTitle(title)
                #if os(iOS)
                .navigationBarTitleDisplayMode(.inline)
                #endif
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) { Button("Vazgeç") { dismiss() } }
                    ToolbarItem(placement: .confirmationAction) { Button("Çizime ekle") { onDone(); dismiss() } }
                }
        }
        #if os(macOS)
        .frame(minWidth: 720, minHeight: 600)
        #endif
    }
}

struct LightingTableForm: View {
    let textHeight: Double
    let onDone: (CadSymbol) -> Void
    @State private var rooms = [ProjectTables.Room(name: "SALON")]
    @State private var note = "Not: Tasarruflu ampül kullanılması tercih edilerek, armatür seçimi yapılmıştır."

    var body: some View {
        FormSheet(title: "Aydınlatma hesap tablosu", onDone: { onDone(ProjectTables.lightingTable(textHeight, rooms, note: note)) }) {
            Note("k, η, Z boş bırakılırsa hesaplanır (gri ipucu): k = (a·b)/(h·(a+b)), h = H − çalışma düzlemi; Z = (E·a·b·1,25)/(Ø·η). η boşsa 80-50-30 için tipik değer önerilir — cetvelden gireceğin değer geçerlidir.")
            ForEach($rooms) { $r in
                let i = rooms.firstIndex { $0.id == r.id } ?? 0
                let c = ProjectTables.computed(r)
                MCard("Mahal \(i + 1)") {
                    HStack {
                        TextInput(label: "Mahal ismi", text: $r.name)
                        if rooms.count > 1 {
                            Button { rooms.removeAll { $0.id == r.id } } label: { Image(systemName: "trash") }.buttonStyle(.borderless)
                        }
                    }
                    FieldRow { NumField("En a", "m", text: $r.a); NumField("Boy b", "m", text: $r.b); NumField("Yükseklik H", "m", text: $r.H) }
                    FieldRow { NumField("Çalışma düz.", "m", text: $r.work); NumField("Aydınlık E", "lx", text: $r.lux); TextInput(label: "Yansıtma", text: $r.refl) }
                    FieldRow {
                        HintField("Mahal endeksi k", text: $r.k, hint: c.k.map { ProjectTables.f($0) })
                        HintField("Verim η", text: $r.eta, hint: c.eta.map { ProjectTables.f($0) })
                    }
                    FieldRow { NumField("Armatür gücü", "W", text: $r.power); NumField("Işık akısı Ø", "lm", text: $r.flux); TextInput(label: "Armatür tipi", text: $r.type) }
                    FieldRow {
                        HintField("Hesaplanan Z", text: $r.zCalc, hint: c.z.map { ProjectTables.f($0) })
                        HintField("Kullanılacak sayı", text: $r.zUsed, hint: c.used.isEmpty ? nil : c.used)
                    }
                }
            }
            Button {
                let l = rooms.last
                rooms.append(ProjectTables.Room(H: l?.H ?? "3", work: l?.work ?? "0.85", refl: l?.refl ?? "80-50-30", power: l?.power ?? "",
                                                flux: l?.flux ?? "", type: l?.type ?? ""))
            } label: { Label("Mahal ekle", systemImage: "plus") }
            .buttonStyle(.bordered)
            TextInput(label: "Tablo altı not", text: $note)
        }
    }
}

/// Boş bırakılınca hesaplanan değeri gri ipucu olarak gösteren alan
struct HintField: View {
    let label: String
    @Binding var text: String
    let hint: String?

    init(_ label: String, text: Binding<String>, hint: String?) {
        self.label = label
        _text = text
        self.hint = hint
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.caption).foregroundStyle(Theme.accent)
            TextField(hint ?? "", text: $text)
                .textFieldStyle(.plain)
                .padding(.horizontal, 10).padding(.vertical, 8)
                .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 7))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(Color.white.opacity(0.12)))
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct PowerTableForm: View {
    let textHeight: Double
    let onDone: (CadSymbol) -> Void
    @State private var loads = [ProjectTables.Load(name: "1. KAT DAİRE", demandPct: "60"), ProjectTables.Load(name: "2. KAT DAİRE", demandPct: "60")]
    @State private var title = "TOPLAM GÜÇ TABLOSU"

    var body: some View {
        let sums = ProjectTables.powerSums(loads)
        FormSheet(title: "Toplam güç tablosu", onDone: { onDone(ProjectTables.powerTable(textHeight, title: title, loads)) }) {
            TextInput(label: "Tablo başlığı", text: $title)
            ForEach($loads) { $l in
                HStack(alignment: .bottom, spacing: 8) {
                    TextInput(label: "Bölüm / daire", text: $l.name).layoutPriority(2)
                    NumField("Kurulu güç", "W", text: $l.installed)
                    NumField("Eş zam.", "%", text: $l.demandPct)
                    Button { loads.removeAll { $0.id == l.id } } label: { Image(systemName: "trash") }.buttonStyle(.borderless).padding(.bottom, 8)
                }
            }
            Button { loads.append(ProjectTables.Load(demandPct: loads.last?.demandPct ?? "100")) } label: { Label("Satır ekle", systemImage: "plus") }
                .buttonStyle(.bordered)
            Divider()
            Text("Toplam kurulu güç: \(ProjectTables.f(sums.installed, 0)) W   ·   Toplam talep güç: \(ProjectTables.f(sums.demand, 0)) W")
                .font(.subheadline.weight(.semibold)).foregroundStyle(Theme.accent)
        }
    }
}

struct TitleBlockForm: View {
    let textHeight: Double
    let onDone: (CadSymbol) -> Void
    @State private var values: [String: String] = {
        let saved = UserDefaults.standard.dictionary(forKey: "antet") as? [String: String] ?? [:]
        var v: [String: String] = [:]
        for f in ProjectTables.titleFields { v[f.key] = saved[f.key] ?? f.def }
        return v
    }()

    var body: some View {
        FormSheet(title: "Firma anteti", onDone: {
            // sonraki projede yeniden yazmamak için sakla
            UserDefaults.standard.set(values, forKey: "antet")
            onDone(ProjectTables.titleBlock(textHeight, values))
        }) {
            Note("Bilgiler bu cihazda saklanır; sonraki projede hazır gelir.")
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 260), spacing: 12)], spacing: 10) {
                ForEach(ProjectTables.titleFields, id: \.key) { f in
                    TextInput(label: f.label, text: Binding(get: { values[f.key] ?? "" }, set: { values[f.key] = $0 }))
                }
            }
        }
    }
}

// ───────────────────────── dışa aktarma seçenekleri ─────────────────────────

struct ExportSheet: View {
    @Binding var options: ExportOptions
    let onGo: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Picker("Biçim", selection: $options.format) {
                    Text("PDF (vektör)").tag(ExportFormat.pdf)
                    Text("Resim (PNG)").tag(ExportFormat.png)
                }
                Picker("Kâğıt", selection: $options.paper) { ForEach(Paper.all, id: \.self) { Text($0.name).tag($0) } }
                Picker("Yön", selection: $options.orientation) {
                    Text("Otomatik").tag(PageOrientation.auto)
                    Text("Yatay").tag(PageOrientation.landscape)
                    Text("Dikey").tag(PageOrientation.portrait)
                }
                Picker("Alan", selection: $options.viewOnly) {
                    Text("Tüm çizim").tag(false)
                    Text("Ekranda görünen").tag(true)
                }
                Toggle("Siyah-beyaz", isOn: $options.monochrome)
            }
            .navigationTitle("Dışa aktar")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Vazgeç") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button("Kaydet…") { dismiss(); onGo() } }
            }
        }
        #if os(macOS)
        .frame(minWidth: 420, minHeight: 360)
        #endif
    }
}
