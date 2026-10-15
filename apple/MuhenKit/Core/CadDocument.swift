import Foundation

struct CadLayer {
    let name: String
    let rgb: UInt32
    let offInFile: Bool
    let count: Int
}

/// Çizgi partisi: vertices içinde [first, first+count) noktaları, (x1,y1,x2,y2) çiftleri
struct LineBatch {
    let layer: Int
    let rgb: UInt32
    let first: Int
    let count: Int
    let minX: Float, minY: Float, maxX: Float, maxY: Float
}

struct FillGroup {
    let layer: Int
    let rgb: UInt32
    let alpha: Float
    let loopFirst: [Int]
    let loopCount: [Int]
}

struct CadText {
    let x: Float, y: Float, height: Float, rotation: Float, widthFactor: Float, boxWidth: Float
    let layer: Int
    let rgb: UInt32
    let hAlign: Int, vAlign: Int
    let text: String
}

/// Çizilmeye hazır sahne. Koordinatlardan köken (originX/Y) çıkarılmıştır: büyük (UTM) koordinatlarda
/// float hassasiyeti kaybolmasın (Android/Windows ile aynı yaklaşım).
final class CadScene {
    let name: String
    let version: String
    let entityCount: Int
    let layers: [CadLayer]
    let vertices: [Float]
    let batches: [LineBatch]
    let fills: [FillGroup]
    let texts: [CadText]
    let minX: Float, minY: Float, maxX: Float, maxY: Float
    let parseMs: Int

    init(name: String, version: String, entityCount: Int, layers: [CadLayer], vertices: [Float], batches: [LineBatch],
         fills: [FillGroup], texts: [CadText], minX: Float, minY: Float, maxX: Float, maxY: Float, parseMs: Int) {
        self.name = name; self.version = version; self.entityCount = entityCount; self.layers = layers
        self.vertices = vertices; self.batches = batches; self.fills = fills; self.texts = texts
        self.minX = minX; self.minY = minY; self.maxX = maxX; self.maxY = maxY; self.parseMs = parseMs
    }

    /// Parti başına dünya koordinatlı yol (bir kez kurulur)
    lazy var batchPaths: [CGPath] = batches.map { b in
        let p = CGMutablePath()
        let v = vertices
        var i = b.first
        while i + 1 < b.first + b.count {
            p.move(to: CGPoint(x: CGFloat(v[i * 2]), y: CGFloat(v[i * 2 + 1])))
            p.addLine(to: CGPoint(x: CGFloat(v[i * 2 + 2]), y: CGFloat(v[i * 2 + 3])))
            i += 2
        }
        return p
    }

    lazy var fillPaths: [CGPath] = fills.map { f in
        let p = CGMutablePath()
        let v = vertices
        for q in 0..<f.loopFirst.count {
            let a = f.loopFirst[q]
            guard f.loopCount[q] > 1 else { continue }
            p.move(to: CGPoint(x: CGFloat(v[a * 2]), y: CGFloat(v[a * 2 + 1])))
            for i in 1..<f.loopCount[q] { p.addLine(to: CGPoint(x: CGFloat(v[(a + i) * 2]), y: CGFloat(v[(a + i) * 2 + 1]))) }
            p.closeSubpath()
        }
        return p
    }
}

struct CadError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Açık DXF belgesi (çekirdekte açık kalır). Tüm çağrılar kilitli: çekirdek tek iş parçacıklıdır.
final class CadDocument {
    private var h: OpaquePointer?
    private let lock = NSLock()
    let name: String
    let originX: Double
    let originY: Double
    private(set) var scene: CadScene!

    private init(h: OpaquePointer, name: String, ox: Double, oy: Double) {
        self.h = h
        self.name = name
        originX = ox
        originY = oy
    }

    deinit {
        if let h { mk_close(h) }
    }

    static var lastError: String {
        guard let p = mk_last_error() else { return "" }
        return String(cString: p)
    }

    static func open(name: String, data: Data) throws -> CadDocument {
        let start = Date()
        let h: OpaquePointer? = data.withUnsafeBytes { raw in
            mk_open(raw.bindMemory(to: UInt8.self).baseAddress, Int64(data.count))
        }
        guard let h else { throw CadError(message: lastError.isEmpty ? "DXF okunamadı." : lastError) }
        if mk_build_scene(h) != 0 {
            let e = lastError
            mk_close(h)
            throw CadError(message: e)
        }
        var ext = [Float](repeating: 0, count: 4)
        mk_extents(h, &ext)
        let doc = CadDocument(h: h, name: name, ox: (Double(ext[0]) + Double(ext[2])) / 2, oy: (Double(ext[1]) + Double(ext[3])) / 2)
        doc.scene = doc.extract(start)
        return doc
    }

    private func handle() throws -> OpaquePointer {
        guard let h else { throw CadError(message: "Çizim kapatıldı.") }
        return h
    }

    private func locked<T>(_ f: () throws -> T) rethrows -> T {
        lock.lock()
        defer { lock.unlock() }
        return try f()
    }

    @discardableResult
    func rebuild() -> CadScene {
        locked {
            let start = Date()
            if let h { _ = mk_build_scene(h) }
            scene = extract(start)
            return scene
        }
    }

    var dirty: Bool { locked { h.map { mk_is_dirty($0) == 1 } ?? false } }
    func setDirty(_ d: Bool) { locked { if let h { mk_set_dirty(h, d ? 1 : 0) } } }

    func save() -> Data { locked { bytes { mk_save($0, $1) } } }
    func saveCopy() -> Data { locked { bytes { mk_save_copy($0, $1) } } }

    private func bytes(_ f: (OpaquePointer, UnsafeMutablePointer<Int64>) -> UnsafePointer<UInt8>?) -> Data {
        guard let h else { return Data() }
        var n: Int64 = 0
        guard let p = f(h, &n), n > 0 else { return Data() }
        return Data(bytes: p, count: Int(n))
    }

    // ───────────────── düzenleme: tüm koordinatlar göreli (köken çıkarılmış) ─────────────────

    func pick(_ x: Double, _ y: Double, tol: Double) -> Int {
        locked { h.map { Int(mk_pick($0, x + originX, y + originY, tol)) } ?? -1 }
    }

    /// (tür, x, y) göreli; tür 0: yakalama yok. 1 uç, 2 orta, 3 merkez, 4 ekleme noktası
    func snap(_ x: Double, _ y: Double, tol: Double) -> (kind: Int, x: Double, y: Double) {
        locked {
            guard let h else { return (0, x, y) }
            var o = [Double](repeating: 0, count: 2)
            let k = Int(mk_snap(h, x + originX, y + originY, tol, &o))
            return k > 0 ? (k, o[0] - originX, o[1] - originY) : (0, x, y)
        }
    }

    /// Seçim vurgusu: göreli (x1,y1,x2,y2) dörtlüleri
    func geometry(_ ids: [Int]) -> [Float] {
        locked {
            guard let h else { return [] }
            var out: [Float] = []
            for id in ids {
                var n: Int64 = 0
                guard let p = mk_entity_geometry(h, Int32(id), &n), n > 0 else { continue }
                for i in stride(from: 0, to: Int(n) - 1, by: 2) {
                    out.append(Float(Double(p[i]) - originX))
                    out.append(Float(Double(p[i + 1]) - originY))
                }
            }
            return out
        }
    }

    func selectRect(_ x0: Double, _ y0: Double, _ x1: Double, _ y1: Double, crossing: Bool) -> [Int] {
        locked {
            guard let h else { return [] }
            let ax = x0 + originX, ay = y0 + originY, bx = x1 + originX, by = y1 + originY
            let n = Int(mk_select_rect(h, ax, ay, bx, by, crossing ? 1 : 0, nil, 0))
            guard n > 0 else { return [] }
            var ids = [Int32](repeating: 0, count: n)
            mk_select_rect(h, ax, ay, bx, by, crossing ? 1 : 0, &ids, Int32(n))
            return ids.map { Int($0) }
        }
    }

    /// "TÜR|katman|handle[|blok]"
    func info(_ id: Int) -> String { locked { h.flatMap { mk_entity_info($0, Int32(id)) }.map { String(cString: $0) } ?? "" } }
    func text(_ id: Int) -> String { locked { h.flatMap { mk_entity_text($0, Int32(id)) }.map { String(cString: $0) } ?? "" } }

    private func withIds<T>(_ ids: [Int], _ f: (OpaquePointer, UnsafePointer<Int32>, Int32) -> T) -> T? {
        guard let h, !ids.isEmpty else { return nil }
        let a = ids.map { Int32($0) }
        return a.withUnsafeBufferPointer { f(h, $0.baseAddress!, Int32(a.count)) }
    }

    func move(_ ids: [Int], dx: Double, dy: Double) { locked { _ = withIds(ids) { mk_move($0, $1, $2, dx, dy) } } }
    func copy(_ ids: [Int], dx: Double, dy: Double) -> Int { locked { Int(withIds(ids) { mk_copy($0, $1, $2, dx, dy) } ?? -1) } }
    func delete(_ ids: [Int]) { locked { _ = withIds(ids) { mk_delete($0, $1, $2) } } }

    @discardableResult
    func addLine(_ layer: String, _ x1: Double, _ y1: Double, _ x2: Double, _ y2: Double) -> Int {
        locked { h.map { Int(mk_add_line($0, layer, x1 + originX, y1 + originY, x2 + originX, y2 + originY)) } ?? -1 }
    }

    @discardableResult
    func addText(_ layer: String, _ x: Double, _ y: Double, height: Double, rotation: Double, _ s: String) -> Int {
        locked { h.map { Int(mk_add_text($0, layer, x + originX, y + originY, height, rotation, s)) } ?? -1 }
    }

    @discardableResult
    func addCircle(_ layer: String, _ cx: Double, _ cy: Double, _ r: Double) -> Int {
        locked { h.map { Int(mk_add_circle($0, layer, cx + originX, cy + originY, r)) } ?? -1 }
    }

    @discardableResult
    func addPolyline(_ layer: String, _ pts: [(Double, Double)], closed: Bool) -> Int {
        locked {
            guard let h else { return -1 }
            var xy: [Double] = []
            for p in pts { xy.append(p.0 + originX); xy.append(p.1 + originY) }
            return Int(mk_add_polyline(h, layer, &xy, Int32(pts.count), closed ? 1 : 0))
        }
    }

    func offset(_ id: Int, _ d: Double, _ px: Double, _ py: Double) -> Int {
        locked { h.map { Int(mk_offset($0, Int32(id), d, px + originX, py + originY)) } ?? -1 }
    }

    @discardableResult
    func insertBlock(name: String, snippet: String, layer: String, _ x: Double, _ y: Double, scale: Double, rotation: Double) throws -> Int {
        try locked {
            let h = try handle()
            if mk_define_block(h, name, snippet) != 1 { throw CadError(message: Self.lastError) }
            return Int(mk_insert_block(h, layer, name, x + originX, y + originY, scale, rotation))
        }
    }

    /// Blok adı → yerleştirme sayısı
    func insertCounts() -> [(name: String, count: Int)] {
        locked {
            guard let h, let p = mk_insert_counts(h) else { return [] }
            return String(cString: p).split(separator: "\n").compactMap { l in
                let parts = l.split(separator: "\t", maxSplits: 1)
                guard parts.count == 2 else { return nil }
                return (String(parts[0]), Int(parts[1]) ?? 0)
            }
        }
    }

    func group(_ ids: [Int]) -> Int { locked { Int(withIds(ids) { mk_group($0, $1, $2) } ?? -1) } }

    func explode(_ ids: [Int]) -> [Int] {
        locked {
            var n: Int32 = 0
            let first = Int(withIds(ids) { mk_explode($0, $1, $2, &n) } ?? -1)
            return first < 0 ? [] : Array(first..<(first + Int(n)))
        }
    }

    func rotate(_ ids: [Int], _ cx: Double, _ cy: Double, _ deg: Double) { locked { _ = withIds(ids) { mk_rotate($0, $1, $2, cx + originX, cy + originY, deg) } } }
    func scale(_ ids: [Int], _ cx: Double, _ cy: Double, _ k: Double) { locked { _ = withIds(ids) { mk_scale($0, $1, $2, cx + originX, cy + originY, k) } } }
    func mirror(_ ids: [Int], _ x1: Double, _ y1: Double, _ x2: Double, _ y2: Double, keep: Bool) {
        locked { _ = withIds(ids) { mk_mirror($0, $1, $2, x1 + originX, y1 + originY, x2 + originX, y2 + originY, keep ? 1 : 0) } }
    }

    func setText(_ id: Int, _ s: String) -> Bool { locked { h.map { mk_set_text($0, Int32(id), s) == 1 } ?? false } }
    func setLayer(_ ids: [Int], _ layer: String) { locked { _ = withIds(ids) { mk_set_layer($0, $1, $2, layer) } } }
    /// aci: 1–255, 256 = katman rengi, 0 = bloktan
    func setColor(_ ids: [Int], _ aci: Int) { locked { _ = withIds(ids) { mk_set_color($0, $1, $2, Int32(aci)) } } }

    func undo() -> Bool { locked { h.map { mk_undo($0) == 1 } ?? false } }
    func redo() -> Bool { locked { h.map { mk_redo($0) == 1 } ?? false } }
    var undoCount: Int { locked { h.map { Int(mk_undo_count($0)) } ?? 0 } }
    var redoCount: Int { locked { h.map { Int(mk_redo_count($0)) } ?? 0 } }

    // ───────────────── sahne ─────────────────

    private func extract(_ start: Date) -> CadScene {
        let h = self.h!
        let ox = originX, oy = originY
        let nl = Int(mk_layer_count(h))
        var layers: [CadLayer] = []
        layers.reserveCapacity(nl)
        for i in 0..<nl {
            let n = mk_layer_name(h, Int32(i)).map { String(cString: $0) } ?? ""
            layers.append(CadLayer(name: n, rgb: mk_layer_rgb(h, Int32(i)), offInFile: (mk_layer_flags(h, Int32(i)) & 1) != 0,
                                   count: Int(mk_layer_entity_count(h, Int32(i)))))
        }

        let nv = Int(mk_vertex_float_count(h))
        var v = [Float](repeating: 0, count: nv)
        if nv > 1, let p = mk_vertices(h) {
            var i = 0
            while i + 1 < nv {
                v[i] = Float(Double(p[i]) - ox)
                v[i + 1] = Float(Double(p[i + 1]) - oy)
                i += 2
            }
        }

        var batches: [LineBatch] = []
        let nb = Int(mk_batch_count(h))
        if nb > 0, let bp = mk_batches(h) {
            for i in 0..<nb {
                let b = bp[i]
                batches.append(LineBatch(layer: Int(b.layer), rgb: b.rgb, first: Int(b.offset / 2), count: Int(b.count / 2),
                                         minX: Float(Double(b.minx) - ox), minY: Float(Double(b.miny) - oy),
                                         maxX: Float(Double(b.maxx) - ox), maxY: Float(Double(b.maxy) - oy)))
            }
        }

        var fills: [FillGroup] = []
        let np = Int(mk_poly_count(h))
        if np > 0, let pp = mk_polys(h) {
            var groups: [Int32: [Int]] = [:]
            var order: [Int32] = []
            for k in 0..<np {
                let g = pp[k].group
                if groups[g] == nil { groups[g] = []; order.append(g) }
                groups[g]!.append(k)
            }
            for g in order {
                let ks = groups[g]!
                let p0 = pp[ks[0]]
                fills.append(FillGroup(layer: Int(p0.layer), rgb: p0.rgb, alpha: p0.alpha,
                                       loopFirst: ks.map { Int(pp[$0].offset / 2) }, loopCount: ks.map { Int(pp[$0].npoints) }))
            }
        }

        var texts: [CadText] = []
        let nt = Int(mk_text_count(h))
        if nt > 0, let tp = mk_texts(h), let pool = mk_text_pool(h) {
            let raw = UnsafeRawPointer(pool)
            texts.reserveCapacity(nt)
            for i in 0..<nt {
                let t = tp[i]
                let s = t.utf8_len == 0 ? "" :
                    String(decoding: UnsafeRawBufferPointer(start: raw.advanced(by: Int(t.utf8_offset)), count: Int(t.utf8_len)), as: UTF8.self)
                texts.append(CadText(x: Float(Double(t.x) - ox), y: Float(Double(t.y) - oy), height: t.height, rotation: t.rotation,
                                     widthFactor: t.width_factor, boxWidth: t.box_width, layer: Int(t.layer), rgb: t.rgb,
                                     hAlign: Int(t.halign), vAlign: Int(t.valign), text: s))
            }
        }

        var ext = [Float](repeating: 0, count: 4)
        mk_extents(h, &ext)
        return CadScene(name: name, version: mk_dxf_version(h).map { String(cString: $0) } ?? "", entityCount: Int(mk_entity_count(h)),
                        layers: layers, vertices: v, batches: batches, fills: fills, texts: texts,
                        minX: Float(Double(ext[0]) - ox), minY: Float(Double(ext[1]) - oy),
                        maxX: Float(Double(ext[2]) - ox), maxY: Float(Double(ext[3]) - oy),
                        parseMs: Int(Date().timeIntervalSince(start) * 1000))
    }
}
