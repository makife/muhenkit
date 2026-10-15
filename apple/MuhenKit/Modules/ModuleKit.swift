import SwiftUI
#if os(iOS)
import UIKit
typealias PlatformImage = UIImage
#else
import AppKit
typealias PlatformImage = NSImage
#endif

/// Hesap modüllerinin yapı taşları (Android modules/Common.kt, Windows ModuleUi karşılığı)
enum MK {
    static let ok = Color(red: 0x3D / 255, green: 1, blue: 0x8E / 255)
    static let bad = Color(red: 1, green: 0x6B / 255, blue: 0x5E / 255)
    static let text = Color(red: 0xEE / 255, green: 0xF1 / 255, blue: 0xF5 / 255)
    static let card = Color(red: 0x14 / 255, green: 0x17 / 255, blue: 0x19 / 255)
    static let edge = Color(red: 0x2A / 255, green: 0x2F / 255, blue: 0x33 / 255)
}

/// Modül sayfası: geniş ekranda iki sütun (girdiler solda, sonuçlar sağda), dar ekranda alt alta
struct ModulePage<Left: View, Right: View>: View {
    let route: RouteId
    var subtitle: String?
    @ViewBuilder var left: Left
    @ViewBuilder var right: Right

    var body: some View {
        GeometryReader { geo in
            ScrollView {
                Group {
                    if geo.size.width >= 900 {
                        HStack(alignment: .top, spacing: 16) {
                            VStack(spacing: 14) { left }.frame(maxWidth: .infinity)
                            VStack(spacing: 14) { right }.frame(maxWidth: .infinity)
                        }
                    } else {
                        VStack(spacing: 14) {
                            left
                            right
                        }
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 14)
                .frame(maxWidth: 1400)
                .frame(maxWidth: .infinity)
            }
        }
        .background(Theme.page)
        .navigationTitle(Routes.info(route).title)
        #if os(iOS)
        .navigationBarTitleDisplayMode(.inline)
        #endif
        .safeAreaInset(edge: .top, spacing: 0) {
            Text(subtitle ?? Routes.info(route).subtitle).font(.footnote).foregroundStyle(Theme.muted)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 16).padding(.vertical, 6)
                    .background(MK.card)
        }
    }
}

/// Başlıklı kart
struct MCard<Content: View>: View {
    let title: String
    @ViewBuilder var content: Content

    init(_ title: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).font(.system(size: 15, weight: .semibold)).foregroundStyle(Theme.accent)
            content
        }
        .padding(.horizontal, 16).padding(.top, 12).padding(.bottom, 14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(MK.card, in: RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(MK.edge))
    }
}

/// Yan yana alanlar (eşit genişlik)
struct FieldRow<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        HStack(alignment: .bottom, spacing: 10) { content }
    }
}

/// Sayı alanı (metin tutulur; virgül de kabul)
struct NumField: View {
    let label: String
    var unit: String?
    @Binding var text: String

    init(_ label: String, _ unit: String? = nil, text: Binding<String>) {
        self.label = label
        self.unit = unit
        _text = text
    }

    private var invalid: Bool {
        let t = text.trimmingCharacters(in: .whitespaces)
        return !t.isEmpty && Calc.num(t) == nil
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(unit.map { "\(label) (\($0))" } ?? label).font(.caption).foregroundStyle(Theme.muted).lineLimit(2)
            TextField("", text: $text)
                .textFieldStyle(.plain)
                .padding(.horizontal, 10).padding(.vertical, 8)
                .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 7))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(invalid ? MK.bad : Color.white.opacity(0.12)))
                #if os(iOS)
                .keyboardType(.decimalPad)
                #endif
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Metin alanı
struct TextInput: View {
    let label: String
    @Binding var text: String
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.caption).foregroundStyle(Theme.muted)
            TextField("", text: $text)
                .textFieldStyle(.plain)
                .padding(.horizontal, 10).padding(.vertical, 8)
                .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 7))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(Color.white.opacity(0.12)))
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Tek seçim: az seçenekte düğme grubu, çokta açılır liste
struct ChoiceField<T: Hashable>: View {
    var label: String?
    let options: [T]
    @Binding var selection: T
    let text: (T) -> String

    init(_ label: String?, _ options: [T], selection: Binding<T>, text: @escaping (T) -> String) {
        self.label = label
        self.options = options
        _selection = selection
        self.text = text
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            if let label { Text(label).font(.caption).foregroundStyle(Theme.muted) }
            if options.count <= 6 && options.map { text($0).count }.reduce(0, +) < 80 {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 6) {
                        ForEach(options, id: \.self) { o in
                            let on = o == selection
                            Button { selection = o } label: {
                                Text(text(o)).font(.subheadline.weight(on ? .semibold : .regular))
                                    .padding(.horizontal, 12).padding(.vertical, 6)
                                    .foregroundStyle(on ? Theme.page : MK.text)
                                    .background(on ? Theme.accent : Color.white.opacity(0.07), in: Capsule())
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            } else {
                Picker("", selection: $selection) {
                    ForEach(options, id: \.self) { Text(text($0)).tag($0) }
                }
                .labelsHidden()
                .pickerStyle(.menu)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Ad → değer listesinden seçim (katsayılar, armatürler, iletkenler)
struct NamedPicker: View {
    let label: String
    let options: [(name: String, value: Double)]
    @Binding var value: Double
    var onPick: ((Double) -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.caption).foregroundStyle(Theme.muted)
            Menu {
                ForEach(options.indices, id: \.self) { i in
                    Button("\(options[i].name)  (\(Calc.fmt(options[i].value, 3)))") {
                        value = options[i].value
                        onPick?(options[i].value)
                    }
                }
            } label: {
                HStack {
                    Text(current).lineLimit(1)
                    Spacer()
                    Image(systemName: "chevron.up.chevron.down").font(.caption)
                }
                .padding(.horizontal, 10).padding(.vertical, 8)
                .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 7))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(Color.white.opacity(0.12)))
            }
            .menuStyle(.borderlessButton)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var current: String {
        if let o = options.first(where: { $0.value == value }) { return "\(o.name)  (\(Calc.fmt(o.value, 3)))" }
        return Calc.fmt(value, 3)
    }
}

/// Sonuç satırı: etiket — değer birim; büyük: önemli sonuç
struct ResultRow: View {
    let label: String
    let value: String
    var unit: String = ""
    var ok: Bool?
    var big = false

    init(_ label: String, _ value: String, _ unit: String = "", ok: Bool? = nil, big: Bool = false) {
        self.label = label
        self.value = value
        self.unit = unit
        self.ok = ok
        self.big = big
    }

    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            Text(label).font(.system(size: big ? 15 : 13)).foregroundStyle(Theme.muted)
                .frame(maxWidth: .infinity, alignment: .leading)
            Text(value + (unit.isEmpty ? "" : " " + unit))
                .font(.system(size: big ? 21 : 15, weight: big ? .bold : .semibold))
                .foregroundStyle(ok == true ? MK.ok : ok == false ? MK.bad : MK.text)
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
        }
        .padding(.vertical, big ? 4 : 1)
    }
}

struct Note: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text).font(.caption).foregroundStyle(Theme.muted).frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Oranlı sütunlar: genişlik ağırlıklara göre paylaştırılır
struct WeightedRow: Layout {
    var weights: [Double]
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let w = proposal.width ?? 400
        let widths = colWidths(w, subviews.count)
        var h: CGFloat = 0
        for (i, s) in subviews.enumerated() { h = max(h, s.sizeThatFits(ProposedViewSize(width: widths[i], height: nil)).height) }
        return CGSize(width: w, height: h)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let widths = colWidths(bounds.width, subviews.count)
        var x = bounds.minX
        for (i, s) in subviews.enumerated() {
            s.place(at: CGPoint(x: x, y: bounds.minY), anchor: .topLeading, proposal: ProposedViewSize(width: widths[i], height: nil))
            x += widths[i] + spacing
        }
    }

    private func colWidths(_ total: CGFloat, _ n: Int) -> [CGFloat] {
        guard n > 0 else { return [] }
        let ws = (0..<n).map { $0 < weights.count && weights[$0] > 0 ? weights[$0] : 1 }
        let sum = ws.reduce(0, +)
        let avail = max(0, total - spacing * CGFloat(n - 1))
        return ws.map { avail * CGFloat($0 / sum) }
    }
}

/// Basit tablo: başlık + satırlar; weights: sütun oranları; highlight: vurgulu satır
struct DataTable: View {
    let headers: [String]
    let rows: [[String]]
    var weights: [Double] = []
    var highlight: Int?

    var body: some View {
        VStack(spacing: 0) {
            row(headers, head: true)
                .background(Color.white.opacity(0.13))
            ForEach(rows.indices, id: \.self) { i in
                Divider().overlay(Color.white.opacity(0.07))
                row(rows[i], head: false)
                    .background(i == highlight ? Theme.accent.opacity(0.27) : Color.clear)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.white.opacity(0.13)))
    }

    private func row(_ cells: [String], head: Bool) -> some View {
        WeightedRow(weights: weights) {
            ForEach(0..<headers.count, id: \.self) { i in
                Text(i < cells.count ? cells[i] : "")
                    .font(.system(size: head ? 11.5 : 12.5, weight: head ? .semibold : .regular))
                    .foregroundStyle(head ? Theme.muted : MK.text)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.horizontal, 8).padding(.vertical, head ? 6 : 4)
    }
}

/// Ortak içerikten görsel (core/content/img), beyaz zeminde
struct ContentImage: View {
    let path: String
    init(_ path: String) { self.path = path }

    var body: some View {
        if let img = PlatformImage(contentsOfFile: Content.url(path).path) {
            image(img)
                .resizable()
                .scaledToFit()
                .frame(maxHeight: 640)
                .padding(6)
                .background(Color.white, in: RoundedRectangle(cornerRadius: 8))
        }
    }

    private func image(_ i: PlatformImage) -> Image {
        #if os(iOS)
        Image(uiImage: i)
        #else
        Image(nsImage: i)
        #endif
    }
}

/// Panoya kopyala
enum Clipboard {
    static func copy(_ s: String) {
        #if os(iOS)
        UIPasteboard.general.string = s
        #else
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(s, forType: .string)
        #endif
    }
}

/// Bilimsel gösterim: 1,234E-2
func sci(_ v: Double) -> String {
    guard v.isFinite, v != 0 else { return v == 0 ? "0" : "—" }
    let e = Int(floor(log10(abs(v))))
    let m = v / pow(10, Double(e))
    return String(format: "%.3f", locale: Locale(identifier: "en_US_POSIX"), m).replacingOccurrences(of: ".", with: ",") + "E\(e)"
}
