import Foundation
import CoreGraphics

struct SymText {
    let rgb: UInt32
    let x: Float, y: Float, h: Float, rot: Float
    let hAlign: Int, vAlign: Int
    let text: String
}

/// Sembol / hazır detay (core/content/symbols.dat; Android/Windows ile aynı biçim).
/// Koordinatlar ekleme noktasına (sembol merkezi) göredir.
final class CadSymbol: Identifiable {
    let id: String
    let name: String
    let category: String
    let width: Double
    let height: Double
    let snippet: String
    let lines: [(rgb: UInt32, pts: [Float])]
    let fills: [(rgb: UInt32, alpha: Float, pts: [Float])]
    let texts: [SymText]
    var blockName: String { "MK_" + id }

    init(id: String, name: String, category: String, width: Double, height: Double, snippet: String,
         lines: [(rgb: UInt32, pts: [Float])], fills: [(rgb: UInt32, alpha: Float, pts: [Float])], texts: [SymText]) {
        self.id = id; self.name = name; self.category = category; self.width = width; self.height = height
        self.snippet = snippet; self.lines = lines; self.fills = fills; self.texts = texts
    }

    /// Önizleme sınırları (x0, y0, x1, y1)
    lazy var bounds: [Float] = {
        var x0 = Float.greatestFiniteMagnitude, y0 = x0, x1 = -x0, y1 = -x0
        func add(_ a: [Float]) {
            var i = 0
            while i + 1 < a.count { x0 = min(x0, a[i]); x1 = max(x1, a[i]); y0 = min(y0, a[i + 1]); y1 = max(y1, a[i + 1]); i += 2 }
        }
        for l in lines { add(l.pts) }
        for f in fills { add(f.pts) }
        for t in texts { add([t.x, t.y, t.x, t.y + t.h]) }
        return x0 > x1 ? [-1, -1, 1, 1] : [x0, y0, x1, y1]
    }()

    /// Yerleştirme önizlemesi: (x1,y1,x2,y2) parçaları
    func segments(_ x: Double, _ y: Double, scale s: Double, rot: Double) -> [Float] {
        let r = rot * .pi / 180, cs = cos(r) * s, sn = sin(r) * s
        var out: [Float] = []
        for l in lines {
            var i = 0
            while i + 1 < l.pts.count {
                let a = Double(l.pts[i]), b = Double(l.pts[i + 1])
                out.append(Float(x + a * cs - b * sn))
                out.append(Float(y + a * sn + b * cs))
                i += 2
            }
        }
        return out
    }
}

enum SymbolLibrary {
    /// Örnek projenin ortanca yazı yüksekliği: hedef çizime ölçek referansı
    private(set) static var refTextHeight: Double = 10
    private static var byBlock: [String: CadSymbol] = [:]

    static let all: [CadSymbol] = load()

    static func forBlock(_ name: String) -> CadSymbol? { _ = all; return byBlock[name] }

    private static func load() -> [CadSymbol] {
        guard let text = try? String(contentsOf: Content.url("symbols.dat"), encoding: .utf8) else { return [] }
        func f(_ s: Substring) -> Float { Float(s) ?? 0 }
        func floats(_ p: [Substring], _ from: Int) -> [Float] { p.dropFirst(from).map(f) }
        var out: [CadSymbol] = []
        var head: [String]?
        var lines: [(rgb: UInt32, pts: [Float])] = []
        var fills: [(rgb: UInt32, alpha: Float, pts: [Float])] = []
        var texts: [SymText] = []
        var snippet = ""
        for raw in text.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = raw.hasSuffix("\r") ? raw.dropLast() : raw
            if line.hasPrefix("#ref_h ") {
                refTextHeight = Double(line.dropFirst(7).trimmingCharacters(in: .whitespaces)) ?? 10
            } else if line.hasPrefix("@SYM\t") {
                head = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
                lines = []; fills = []; texts = []; snippet = ""
            } else if line.hasPrefix("L\t") {
                let p = line.dropFirst(2).split(separator: " ")
                guard !p.isEmpty else { continue }
                lines.append((UInt32(p[0], radix: 16) ?? 0, floats(p, 1)))
            } else if line.hasPrefix("F\t") {
                let t = line.split(separator: "\t", omittingEmptySubsequences: false)
                guard t.count > 2 else { continue }
                let p = t[2].split(separator: " ")
                guard !p.isEmpty else { continue }
                fills.append((UInt32(t[1], radix: 16) ?? 0, f(p[0]), floats(p, 1)))
            } else if line.hasPrefix("T\t") {
                let t = line.split(separator: "\t", omittingEmptySubsequences: false)
                guard t.count > 2 else { continue }
                let p = t[2].split(separator: " ")
                guard p.count >= 6 else { continue }
                texts.append(SymText(rgb: UInt32(t[1], radix: 16) ?? 0, x: f(p[0]), y: f(p[1]), h: f(p[2]), rot: f(p[3]),
                                     hAlign: Int(p[4]) ?? 0, vAlign: Int(p[5]) ?? 0, text: t.count > 3 ? String(t[3]) : ""))
            } else if line.hasPrefix("S\t") {
                snippet = String(line.dropFirst(2)).replacingOccurrences(of: "\u{1f}", with: "\n").replacingOccurrences(of: "\u{1e}", with: "\n")
            } else if line == "@END" {
                if let h = head, h.count >= 6 {
                    out.append(CadSymbol(id: h[1], name: h[2], category: h[3], width: Double(h[4]) ?? 1, height: Double(h[5]) ?? 1,
                                         snippet: snippet, lines: lines, fills: fills, texts: texts))
                }
                head = nil
            }
        }
        byBlock = Dictionary(out.map { ($0.blockName, $0) }, uniquingKeysWith: { a, _ in a })
        return out
    }
}
