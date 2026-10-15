import Foundation

/// Hesap sonucu: "anahtar=değer" satırları
struct CalcResult {
    let kv: [String: String]
    subscript(_ k: String) -> String { kv[k] ?? "" }
    func d(_ k: String) -> Double { kv[k].flatMap { Double($0) } ?? .nan }
    func i(_ k: String) -> Int { let v = d(k); return v.isFinite ? Int(v) : 0 }
    func b(_ k: String) -> Bool { kv[k] == "1" }
    var error: String? { kv["error"] }
}

/// C++ hesap çekirdeği (core/mkcalc). Android, Windows, iOS ve macOS aynı formülleri çalıştırır.
enum Calc {
    private static let lock = NSLock()
    private static var tableCache: [String: [[String]]] = [:]

    static func run(_ module: String, _ input: [(String, Any?)]) -> CalcResult {
        let text = input.map { k, v -> String in
            let s: String
            switch v {
            case nil: s = ""
            case let b as Bool: s = b ? "1" : "0"
            case let d as Double: s = d.isFinite ? String(d) : ""
            case let i as Int: s = String(i)
            case let o?: s = "\(o)".trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".")
            }
            return k + "=" + s
        }.joined(separator: "\n")
        lock.lock()
        let out = mc_calc(module, text).map { String(cString: $0) } ?? ""
        lock.unlock()
        var kv: [String: String] = [:]
        for line in out.split(separator: "\n") {
            guard let i = line.firstIndex(of: "=") else { continue }
            kv[String(line[..<i])] = String(line[line.index(after: i)...]).trimmingCharacters(in: CharacterSet(charactersIn: "\r"))
        }
        return CalcResult(kv: kv)
    }

    /// Sekme ayrılmış tablo: ilk satır başlıklar
    static func table(_ name: String) -> [[String]] {
        lock.lock()
        defer { lock.unlock() }
        if let t = tableCache[name] { return t }
        let raw = mc_table(name).map { String(cString: $0) } ?? ""
        let t = raw.split(separator: "\n").map { $0.trimmingCharacters(in: CharacterSet(charactersIn: "\r")) }
            .filter { !$0.isEmpty }.map { $0.components(separatedBy: "\t") }
        tableCache[name] = t
        return t
    }

    /// Ad → değer tablosu (soiling, luminaires, conductors, lp_c1…)
    static func named(_ name: String) -> [(name: String, value: Double)] {
        table(name).dropFirst().map { r in (r[0], r.count > 1 ? Double(r[1]) ?? 0 : 0) }
    }

    /// Türkçe sayı biçimi: ondalık virgül, sondaki sıfırlar atılır
    static func fmt(_ v: Double, _ digits: Int = 2) -> String {
        guard v.isFinite else { return "—" }
        var s = String(format: "%.\(digits)f", locale: Locale(identifier: "en_US_POSIX"), v)
        if s.contains(".") {
            while s.hasSuffix("0") { s.removeLast() }
            if s.hasSuffix(".") { s.removeLast() }
        }
        return s.replacingOccurrences(of: ".", with: ",")
    }

    /// Alan metni → sayı (virgül de kabul)
    static func num(_ s: String) -> Double? {
        Double(s.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: "."))
    }

    /// Sayı → alan metni (nokta ondalıklı)
    static func field(_ v: Double) -> String {
        var s = String(format: "%.6f", locale: Locale(identifier: "en_US_POSIX"), v)
        if s.contains(".") {
            while s.hasSuffix("0") { s.removeLast() }
            if s.hasSuffix(".") { s.removeLast() }
        }
        return s
    }
}
