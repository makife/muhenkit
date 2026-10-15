import Foundation

/// Proje bilgisi (Android/Windows ProjectMeta ile aynı alanlar)
struct ProjectMeta: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var source: String?
    var created: Int64
    var modified: Int64
    /// Orijinal dosyaya kaydedilmemiş değişiklik var
    var dirty: Bool = false
    /// Görünüm (mutlak dünya koordinatı): merkez x, y ve yakınlaştırma
    var view: [Double]?
    var hidden: [Int]?
    var sizeBytes: Int64 = 0
}

/// "Projelerim": açılan her DXF uygulamanın kendi alanına kopyalanır, düzenlemeler bu kopyaya otomatik kaydedilir.
///   Application Support/MuhenKit/projects/<id>/drawing.dxf | meta.json | thumb.png
enum ProjectStore {
    private static let lock = NSLock()
    private static let fm = FileManager.default

    static var root: URL {
        let base = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("MuhenKit/projects", isDirectory: true)
        try? fm.createDirectory(at: base, withIntermediateDirectories: true)
        return base
    }

    private static func dir(_ id: String) -> URL { root.appendingPathComponent(id, isDirectory: true) }
    static func drawing(_ id: String) -> URL { dir(id).appendingPathComponent("drawing.dxf") }
    static func thumb(_ id: String) -> URL { dir(id).appendingPathComponent("thumb.png") }
    private static var lastFile: URL { root.deletingLastPathComponent().appendingPathComponent("last.txt") }

    static var now: Int64 { Int64(Date().timeIntervalSince1970 * 1000) }

    static var lastProject: String? {
        get {
            guard let id = try? String(contentsOf: lastFile, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines),
                  !id.isEmpty, fm.fileExists(atPath: dir(id).path) else { return nil }
            return id
        }
        set { try? (newValue ?? "").write(to: lastFile, atomically: true, encoding: .utf8) }
    }

    static func list() -> [ProjectMeta] {
        let ids = (try? fm.contentsOfDirectory(atPath: root.path)) ?? []
        return ids.compactMap { try? meta($0) }.sorted { $0.modified > $1.modified }
    }

    static func meta(_ id: String) throws -> ProjectMeta {
        lock.lock()
        defer { lock.unlock() }
        var m = try JSONDecoder().decode(ProjectMeta.self, from: Data(contentsOf: dir(id).appendingPathComponent("meta.json")))
        m.id = id
        m.sizeBytes = ((try? fm.attributesOfItem(atPath: drawing(id).path)[.size]) as? NSNumber)?.int64Value ?? 0
        return m
    }

    static func saveMeta(_ m: ProjectMeta) {
        lock.lock()
        defer { lock.unlock() }
        if let data = try? JSONEncoder().encode(m) { atomicWrite(dir(m.id).appendingPathComponent("meta.json"), data) }
    }

    private static func atomicWrite(_ url: URL, _ data: Data) {
        try? fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
    }

    static func create(name: String, bytes: Data, source: String?) -> ProjectMeta {
        lock.lock()
        let t = now
        var id = String(t)
        while fm.fileExists(atPath: dir(id).path) { id += "_" }
        atomicWrite(drawing(id), bytes)
        lock.unlock()
        let m = ProjectMeta(id: id, name: name, source: source, created: t, modified: t, sizeBytes: Int64(bytes.count))
        saveMeta(m)
        return m
    }

    static func writeDrawing(_ id: String, _ bytes: Data) {
        lock.lock()
        defer { lock.unlock() }
        atomicWrite(drawing(id), bytes)
    }

    static func read(_ id: String) throws -> Data { try Data(contentsOf: drawing(id)) }

    static func rename(_ id: String, _ name: String) {
        guard var m = try? meta(id) else { return }
        m.name = name
        saveMeta(m)
    }

    static func delete(_ id: String) {
        lock.lock()
        try? fm.removeItem(at: dir(id))
        lock.unlock()
        if lastProject == id { lastProject = nil }
    }

    static func writeThumb(_ id: String, _ png: Data) {
        lock.lock()
        defer { lock.unlock() }
        atomicWrite(thumb(id), png)
    }

    /// "Yeni proje N" için sıradaki numara
    static func nextBlankNumber() -> Int { list().filter { $0.name.hasPrefix("Yeni proje") }.count + 1 }
}
