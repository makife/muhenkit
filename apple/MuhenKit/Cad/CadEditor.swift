import Foundation
import CoreGraphics
import SwiftUI

enum CadTool: String, CaseIterable, Identifiable {
    case select, move, copy, line, pline, circle, rect, offset, symbol, text, measure, rotate, scale, mirror, dimension
    var id: String { rawValue }

    var label: String {
        switch self {
        case .select: return "Seç"
        case .move: return "Taşı"
        case .copy: return "Kopyala"
        case .line: return "Çizgi"
        case .pline: return "Çoklu çizgi"
        case .circle: return "Daire"
        case .rect: return "Dikdörtgen"
        case .offset: return "Ötele"
        case .symbol: return "Sembol"
        case .text: return "Yazı"
        case .measure: return "Ölç"
        case .rotate: return "Döndür"
        case .scale: return "Ölçekle"
        case .mirror: return "Ayna"
        case .dimension: return "Ölçü"
        }
    }

    /// SF Symbols
    var symbol: String {
        switch self {
        case .select: return "cursorarrow"
        case .move: return "arrow.up.and.down.and.arrow.left.and.right"
        case .copy: return "plus.square.on.square"
        case .line: return "line.diagonal"
        case .pline: return "point.topleft.down.curvedto.point.bottomright.up"
        case .circle: return "circle"
        case .rect: return "rectangle"
        case .offset: return "arrow.left.and.right.square"
        case .symbol: return "square.grid.3x3.square"
        case .text: return "textformat"
        case .measure: return "ruler"
        case .rotate: return "rotate.right"
        case .scale: return "arrow.up.left.and.arrow.down.right"
        case .mirror: return "arrow.left.and.right.righttriangle.left.righttriangle.right"
        case .dimension: return "arrow.left.and.right"
        }
    }
}

/// Tuvalin düzenleme katmanı (dünya koordinatı)
struct EditOverlay {
    var selection: [Float] = []
    var ghost: CGAffineTransform?
    var rubber: [Float]?
    var snapType = 0
    var snap = CGPoint.zero
    var cursor: CGPoint?
    var box: [Float]?
    var boxCrossing = false
    var guides: [Float]?
    var marks: [Float]?
    var label: String?
}

typealias Pt = (x: Double, y: Double)
typealias SnapPt = (kind: Int, x: Double, y: Double)

/// DXF düzenleyicisi (Windows CadEditor ile aynı mantık): araçlar dokun-dokun / tıkla-tıkla çalışır,
/// basılı sürükleme de olur. Koordinatlar göreli (köken çıkarılmış).
/// Ana iş parçacığında kullanılır; çekirdek işleri arka planda yapılıp sonuç ana iş parçacığına döner.
final class CadEditor: ObservableObject {
    private static let typeNames = ["LINE": "Çizgi", "LWPOLYLINE": "Çoklu çizgi", "POLYLINE": "Çoklu çizgi", "CIRCLE": "Daire", "ARC": "Yay",
                                    "ELLIPSE": "Elips", "TEXT": "Yazı", "MTEXT": "Çok satırlı yazı", "INSERT": "Blok", "HATCH": "Tarama",
                                    "SOLID": "Dolgu", "SPLINE": "Eğri", "DIMENSION": "Ölçü", "POINT": "Nokta"]
    private static let snapNames = ["", "Uç nokta", "Orta nokta", "Merkez", "Ekleme noktası"]

    @Published private(set) var doc: CadDocument?
    @Published private(set) var scene: CadScene?
    /// Sahne her yenilendiğinde artar (tuval yeniden çizilsin)
    @Published private(set) var revision = 0
    @Published private(set) var tool: CadTool = .select
    @Published private(set) var selection: Set<Int> = []
    @Published private(set) var selectionInfo: String?
    @Published private(set) var busy = false
    @Published var activeLayer = "0"
    @Published var ortho = false
    @Published var snapOn = true
    @Published var angleSnap = true
    @Published var tracking = true
    @Published var mirrorKeep = true
    @Published var textHeight = 2.5
    @Published var offsetDist = 1.0
    @Published private(set) var symbol: CadSymbol?
    @Published var symbolScale = 1.0
    @Published var symbolRot = 0.0
    @Published private(set) var undoCount = 0
    @Published private(set) var redoCount = 0
    @Published private(set) var dirty = false
    /// Canlı ölçü / ipucu
    @Published private(set) var live: String?
    @Published private(set) var overlay = EditOverlay()
    /// Kısa bildirim
    @Published var message: String?
    /// Yazı aracı: bu noktaya yazı girilsin
    @Published var textRequest: CGPoint?
    /// Çizim değişti (otomatik kayıt için)
    var onEdited: (() -> Void)?
    /// "Sığdır" komutu (komut satırından Z)
    var onFit: (() -> Void)?

    private var fixedScale: Double?
    private var placeScale: Double { fixedScale ?? symbolScale }
    private var selGeom: [Float] = []
    private var anchor: Pt?
    private var pivot: Pt?
    private var scaleRef: Pt?
    private var dimP2: Pt?
    private var acq: [Pt] = []
    private var acqSeg: (a: Pt, b: Pt)?
    private var lastAcq: Pt?
    private var guides: [Float]?
    private var trackLabel: String?
    private var tol = 1.0
    private var pline: [Pt] = []
    private var offsetTarget: Int?
    private var press: SnapPt = (0, 0, 0)
    private var hover: Pt = (0, 0)
    private var dragging = false
    private var boxStart: [Float]?
    private var shift = false

    func attach(_ d: CadDocument) {
        doc = d
        let s = d.scene!
        scene = s
        activeLayer = s.layers.contains { $0.name == "0" } ? "0" : s.layers.first?.name ?? "0"
        let hs = s.texts.map { Double($0.height) }.filter { $0 > 0 }.sorted()
        textHeight = hs.isEmpty ? 10 : (hs[hs.count / 2] * 100).rounded() / 100
        let k = textHeight / SymbolLibrary.refTextHeight
        symbolScale = k.isFinite && k > 0 ? (k * 1000).rounded() / 1000 : 1
        offsetDist = (textHeight * 2 * 100).rounded() / 100
        selection = []
        selGeom = []
        selectionInfo = nil
        tool = .select
        resetSteps()
        refresh()
        revision += 1
        setOverlay()
    }

    // ───────────────────────── araç seçimi ─────────────────────────

    static func needsSelection(_ t: CadTool) -> Bool { [.move, .copy, .rotate, .scale, .mirror].contains(t) }

    /// Dokunmatikte tek parmak sürüklemesi araca mı gider (değilse kaydırır)
    var capturesDrag: Bool { tool != .select && tool != .text && tool != .offset }

    var hint: String {
        switch tool {
        case .select: return "Nesneye dokunun / tıklayın · basılı tutup sürükleyin: pencere seçimi (soldan sağa içindekiler, sağdan sola değenler)"
        case .move, .copy: return anchor == nil ? "Taban noktasını verin" : "Hedef noktayı verin ya da @dx,dy / uzaklık yazın" + (tool == .copy ? " · Esc: bitir" : "")
        case .line: return anchor == nil ? "İlk noktayı verin" : "Sonraki nokta ya da uzunluk, uzunluk<açı, @dx,dy · Bitir: Enter / sağ tık"
        case .pline: return pline.isEmpty ? "İlk noktayı verin" : "Sonraki nokta · Enter: bitir · K: kapat"
        case .circle: return anchor == nil ? "Merkezi verin" : "Yarıçap noktasını verin ya da yarıçap yazın"
        case .rect: return anchor == nil ? "İlk köşeyi verin" : "Karşı köşeyi verin ya da genişlik,yükseklik yazın"
        case .offset: return offsetTarget == nil ? "Ötelenecek nesneye dokunun · uzaklık \(Self.fmt(offsetDist))" : "Hangi tarafa? Dokunun"
        case .symbol: return "\(symbol?.name ?? ""): yerleştirmek için dokunun · açı \(Int(symbolRot))°"
        case .text: return "Yazının yerleşeceği noktaya dokunun"
        case .measure: return anchor == nil ? "İlk noktayı verin" : "İkinci noktayı verin"
        case .dimension: return anchor == nil ? "Ölçülecek ilk nokta (uçlara yakalar)" : dimP2 == nil ? "İkinci nokta" : "Ölçü çizgisinin yeri ya da uzaklık yazın"
        case .rotate: return pivot == nil ? "Dönme merkezini verin" : "Açıyı verin ya da derece yazın"
        case .scale: return pivot == nil ? "Ölçekleme merkezini verin" : scaleRef == nil ? "Katsayı yazın ya da referans noktası verin" : "Yeni noktayı verin"
        case .mirror: return anchor == nil ? "Ayna ekseninin ilk noktası" : "İkinci nokta"
        }
    }

    func selectTool(_ t0: CadTool) {
        var t = t0
        if Self.needsSelection(t) && selection.isEmpty {
            message = "Önce \(t.label.lowercased(with: Locale(identifier: "tr_TR"))) için nesne seçin."
            t = .select
        }
        if t == .symbol && symbol == nil {
            message = "Sembol listesinden bir sembol seçin."
            return
        }
        tool = t
        resetSteps()
        if t == .offset && !selection.isEmpty { clearSelectionSilently() }
        setOverlay()
    }

    func startSymbol(_ s: CadSymbol) {
        symbol = s
        fixedScale = nil
        selectTool(.symbol)
    }

    /// Tablo / antet: sol üst köşesinden, ölçeksiz ve döndürmesiz
    func startTable(_ s: CadSymbol) {
        symbol = s
        symbolRot = 0
        fixedScale = 1
        selectTool(.symbol)
    }

    private func resetSteps() {
        anchor = nil
        pivot = nil
        scaleRef = nil
        pline = []
        offsetTarget = nil
        dimP2 = nil
        acq = []
        acqSeg = nil
        lastAcq = nil
        guides = nil
        trackLabel = nil
        boxStart = nil
        dragging = false
        live = nil
    }

    /// Esc: önce yarım adımı, sonra aracı, en son seçimi bırak
    func escape() {
        if tool == .pline && pline.count >= 2 { finishPolyline(closed: false); return }
        if anchor != nil || pivot != nil || !pline.isEmpty || offsetTarget != nil || dimP2 != nil {
            let copyChain = tool == .copy && anchor != nil
            let keep = acq, seg = acqSeg
            resetSteps()
            acq = keep
            acqSeg = seg
            if copyChain { tool = .select }
        } else if tool != .select {
            resetSteps()
            tool = .select
        } else {
            clearSelectionSilently()
        }
        setOverlay()
    }

    /// Sağ tık: çizgi zinciri / çoklu çizgi sürüyorsa bitir, değilse Esc
    func rightClick() {
        if (tool == .line && anchor != nil) || (tool == .pline && !pline.isEmpty) { enter() } else { escape() }
    }

    /// Enter / Bitir: çizgi zincirini / çoklu çizgiyi bitir
    func enter() {
        if tool == .pline { finishPolyline(closed: false); return }
        if tool == .line {
            anchor = nil
            live = nil
            setOverlay()
        }
    }

    var canFinish: Bool { (tool == .line && anchor != nil) || (tool == .pline && !pline.isEmpty) }

    func selectAll() {
        guard let d = doc, let s = scene else { return }
        let ids = d.selectRect(Double(s.minX) - 1, Double(s.minY) - 1, Double(s.maxX) + 1, Double(s.maxY) + 1, crossing: false)
        setSelection(ids, "\(ids.count) nesne seçili")
    }

    private func clearSelectionSilently() {
        selection = []
        selGeom = []
        selectionInfo = nil
    }

    func clearSelection() {
        clearSelectionSilently()
        if Self.needsSelection(tool) { tool = .select }
        resetSteps()
        setOverlay()
    }

    private func setSelection(_ ids: [Int], _ info: String?) {
        guard let d = doc else { return }
        selection = Set(ids)
        selGeom = d.geometry(Array(selection))
        selectionInfo = selection.isEmpty ? nil : info ?? "\(selection.count) nesne seçili"
        setOverlay()
    }

    // ───────────────────────── seçim üzerindeki işlemler ─────────────────────────

    func deleteSelection() {
        let ids = Array(selection)
        guard !ids.isEmpty else { return }
        clearSelectionSilently()
        if Self.needsSelection(tool) { tool = .select }
        resetSteps()
        edit { $0.delete(ids) }
    }

    func undo() { edit { _ = $0.undo() } }
    func redo() { edit { _ = $0.redo() } }

    func groupSelection() {
        let ids = Array(selection)
        guard ids.count >= 2 else { message = "Gruplamak için en az iki nesne seçin."; return }
        var newId = -1
        var err = ""
        edit({ d in
            newId = d.group(ids)
            if newId < 0 { err = CadDocument.lastError }
        }, after: { [weak self] in
            if newId >= 0 { self?.setSelection([newId], "1 grup (\(ids.count) nesne)") } else { self?.message = err.isEmpty ? "Gruplanamadı." : err }
        })
    }

    func explodeSelection() {
        let ids = Array(selection)
        guard !ids.isEmpty else { return }
        var parts: [Int] = []
        edit({ d in parts = d.explode(ids) }, after: { [weak self] in
            if parts.isEmpty { self?.message = "Seçimde çözülecek grup ya da blok yok." } else { self?.setSelection(parts, "\(parts.count) parça") }
        })
    }

    func applyLayer(_ layer: String) {
        let ids = Array(selection)
        if !ids.isEmpty { edit { $0.setLayer(ids, layer) } }
    }

    func applyColor(_ aci: Int) {
        let ids = Array(selection)
        if !ids.isEmpty { edit { $0.setColor(ids, aci) } }
    }

    /// Tek bir yazı seçiliyse (kimlik, içerik)
    func selectedText() -> (id: Int, text: String)? {
        guard let d = doc, selection.count == 1, let id = selection.first else { return nil }
        let info = d.info(id)
        return info.hasPrefix("TEXT|") || info.hasPrefix("MTEXT|") ? (id, d.text(id)) : nil
    }

    func setText(_ id: Int, _ text: String) { edit { _ = $0.setText(id, text) } }

    func addText(_ x: Double, _ y: Double, _ text: String, height: Double) {
        guard !text.trimmingCharacters(in: .whitespaces).isEmpty, height > 0 else { return }
        textHeight = height
        let layer = activeLayer
        edit { $0.addText(layer, x, y, height: height, rotation: 0, text) }
    }

    func finishPolyline(closed: Bool) {
        let pts = pline
        pline = []
        anchor = nil
        live = nil
        if pts.count < 2 || (closed && pts.count < 3) { setOverlay(); return }
        let layer = activeLayer
        edit { $0.addPolyline(layer, pts.map { ($0.x, $0.y) }, closed: closed) }
    }

    /// Keşif: (görünen ad, adet, kütüphane sembolü mü)
    func counts() -> [(name: String, count: Int, library: Bool)] {
        guard let d = doc else { return [] }
        return d.insertCounts().map { c -> (name: String, count: Int, library: Bool) in
            let sym = SymbolLibrary.forBlock(c.name)
            return (sym?.name ?? c.name, c.count, sym != nil)
        }.sorted { a, b in a.library != b.library ? a.library : a.name < b.name }
    }

    // ───────────────────────── çekirdekte düzenle ─────────────────────────

    /// Çekirdekte düzenle → sahneyi yeniden kur (arka planda) → tuvale yükle
    private func edit(_ op: @escaping (CadDocument) -> Void, after: (() -> Void)? = nil) {
        guard let d = doc, !busy else { return }
        busy = true
        let sel = Array(selection)
        DispatchQueue.global(qos: .userInitiated).async {
            op(d)
            let s = d.rebuild()
            let g = d.geometry(sel)
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.selGeom = g
                if g.isEmpty && !self.selection.isEmpty {
                    self.selection = []
                    self.selectionInfo = nil
                    if Self.needsSelection(self.tool) { self.tool = .select }
                }
                self.scene = s
                self.revision += 1
                self.refresh()
                self.setOverlay()
                self.busy = false
                after?()
                self.onEdited?()
            }
        }
    }

    private func refresh() {
        guard let d = doc else { return }
        undoCount = d.undoCount
        redoCount = d.redoCount
        dirty = d.dirty
    }

    // ───────────────────────── yakalama ve kısıtlar ─────────────────────────

    private let angleStep = 15.0, angleTol = 3.0

    private func snapPoint(_ x: Double, _ y: Double, _ tol: Double) -> SnapPt {
        guard snapOn, let d = doc else { return (0, x, y) }
        return d.snap(x, y, tol: tol)
    }

    private var from: Pt? {
        switch tool {
        case .line, .rect, .move, .copy, .mirror, .measure: return anchor
        case .dimension: return dimP2 == nil ? anchor : nil
        case .pline: return pline.last
        default: return nil
        }
    }

    private func constrain(_ p: SnapPt) -> SnapPt {
        guides = nil
        trackLabel = nil
        if p.kind > 0 || tool == .select { return p }
        let f = from
        if ortho, let f0 = f {
            let dx = p.x - f0.x, dy = p.y - f0.y
            return abs(dx) >= abs(dy) ? (0, p.x, f0.y) : (0, f0.x, p.y)
        }
        return track(p, f)
    }

    private struct Guide { let ox, oy, dx, dy: Double; let label: String; let polar: Bool }

    private func track(_ p: SnapPt, _ from: Pt?) -> SnapPt {
        let tol = self.tol
        var lines: [Guide] = []
        var segDx = 0.0, segDy = 0.0, segLen = 0.0
        if let sg = acqSeg {
            segDx = sg.b.x - sg.a.x; segDy = sg.b.y - sg.a.y
            segLen = (segDx * segDx + segDy * segDy).squareRoot()
            if segLen > 1e-12 { segDx /= segLen; segDy /= segLen } else { segLen = 0 }
        }
        if let f = from, tool != .rect {
            let dx = p.x - f.x, dy = p.y - f.y
            if angleSnap && dx * dx + dy * dy > 1e-18 {
                let ang = atan2(dy, dx) * 180 / .pi, ta = (ang / angleStep).rounded() * angleStep
                if abs(ang - ta) <= angleTol {
                    let r = ta * .pi / 180
                    let tn = (ta.truncatingRemainder(dividingBy: 360) + 360).truncatingRemainder(dividingBy: 360)
                    lines.append(Guide(ox: f.x, oy: f.y, dx: cos(r), dy: sin(r), label: "Kutupsal \(Int(tn))°", polar: true))
                }
            }
            if tracking && segLen > 0 {
                lines.append(Guide(ox: f.x, oy: f.y, dx: segDx, dy: segDy, label: "Paralel", polar: false))
                lines.append(Guide(ox: f.x, oy: f.y, dx: -segDy, dy: segDx, label: "Dik", polar: false))
            }
        }
        if tracking {
            for q in acq {
                if let ff = from, abs(ff.x - q.x) + abs(ff.y - q.y) < 1e-9 { continue }
                lines.append(Guide(ox: q.x, oy: q.y, dx: 1, dy: 0, label: "Yatay hiza", polar: false))
                lines.append(Guide(ox: q.x, oy: q.y, dx: 0, dy: 1, label: "Dikey hiza", polar: false))
                if segLen > 0 && abs(segDx) > 1e-6 && abs(segDy) > 1e-6 {
                    lines.append(Guide(ox: q.x, oy: q.y, dx: segDx, dy: segDy, label: "Paralel hiza", polar: false))
                    lines.append(Guide(ox: q.x, oy: q.y, dx: -segDy, dy: segDx, label: "Dik hiza", polar: false))
                }
            }
        }
        if lines.isEmpty { return p }
        func dist(_ g: Guide) -> Double { abs((p.x - g.ox) * g.dy - (p.y - g.oy) * g.dx) }
        let cands = lines.map { ($0, dist($0)) }.filter { $0.1 < tol || $0.0.polar }
            .sorted { ($0.0.polar ? $0.1 * 0.5 : $0.1) < ($1.0.polar ? $1.1 * 0.5 : $1.1) }
        guard let g1 = cands.first?.0 else { return p }
        // iki kılavuzun kesişimi
        for (g2, d2) in cands.dropFirst() {
            let cr = g1.dx * g2.dy - g1.dy * g2.dx
            if abs(cr) < 0.05 { continue }
            if d2 > tol * 1.5 && !g2.polar { continue }
            let t1 = ((g2.ox - g1.ox) * g2.dy - (g2.oy - g1.oy) * g2.dx) / cr
            let ix = g1.ox + g1.dx * t1, iy = g1.oy + g1.dy * t1
            if abs(ix - p.x) + abs(iy - p.y) > tol * 3 { continue }
            setGuides([g1, g2], ix, iy)
            trackLabel = "\(g1.label) + \(g2.label)"
            return (0, ix, iy)
        }
        var t = (p.x - g1.ox) * g1.dx + (p.y - g1.oy) * g1.dy
        if from != nil && !g1.polar && g1.label == "Paralel" && segLen > 0 && abs(abs(t) - segLen) < tol {
            t = (t < 0 ? -1 : 1) * segLen
            trackLabel = "Paralel · = \(Self.fmt(segLen)) (referansla eşit)"
        } else {
            trackLabel = g1.label
        }
        let x = g1.ox + g1.dx * t, y = g1.oy + g1.dy * t
        setGuides([g1], x, y)
        return (0, x, y)
    }

    private func setGuides(_ gs: [Guide], _ x: Double, _ y: Double) {
        let ext = tol * 4
        var o: [Float] = []
        for g in gs {
            let t = (x - g.ox) * g.dx + (y - g.oy) * g.dy
            let s: Double = t >= 0 ? 1 : -1
            o += [Float(g.ox), Float(g.oy), Float(x + g.dx * ext * s), Float(y + g.dy * ext * s)]
        }
        guides = o
    }

    /// Üzerinden geçilen yakalama noktasını izlemeye al; uç/orta ise o çizgi parçasını referans yap
    private func acquire(_ s: SnapPt, _ tol: Double) {
        guard tracking, let d = doc else { return }
        let q: Pt = (s.x, s.y)
        if let l = lastAcq, abs(l.x - q.x) + abs(l.y - q.y) < 1e-9 { return }
        lastAcq = q
        acq.removeAll { abs($0.x - q.x) + abs($0.y - q.y) < 1e-9 }
        acq.append(q)
        if acq.count > 5 { acq.removeFirst() }
        if s.kind == 1 || s.kind == 2 {
            let id = d.pick(q.x, q.y, tol: tol)
            guard id >= 0 else { return }
            let g = d.geometry([id])
            var best = Double.greatestFiniteMagnitude
            var i = 0
            while i + 3 < g.count {
                let ax = Double(g[i]), ay = Double(g[i + 1]), bx = Double(g[i + 2]), by = Double(g[i + 3])
                let vx = bx - ax, vy = by - ay, len2 = vx * vx + vy * vy
                if len2 >= 1e-18 {
                    let t = min(max(((q.x - ax) * vx + (q.y - ay) * vy) / len2, 0), 1)
                    let ddx = ax + vx * t - q.x, ddy = ay + vy * t - q.y, dd = ddx * ddx + ddy * ddy
                    if dd < best { best = dd; acqSeg = ((ax, ay), (bx, by)) }
                }
                i += 4
            }
        }
    }

    private func snapAngle(_ deg0: Double) -> Double {
        var deg = deg0
        while deg > 180 { deg -= 360 }
        while deg <= -180 { deg += 360 }
        if ortho { return (deg / 15).rounded() * 15 }
        if angleSnap {
            let t = (deg / angleStep).rounded() * angleStep
            if abs(deg - t) <= angleTol { return t }
        }
        return deg
    }

    // ───────────────────────── işaretçi girişi ─────────────────────────

    func pointerPress(_ x: Double, _ y: Double, tol: Double, shift: Bool = false) {
        guard doc != nil, !busy else { return }
        self.shift = shift
        self.tol = tol
        dragging = false
        press = tool == .select ? (0, x, y) : constrain(snapPoint(x, y, tol))
        if tool == .select { boxStart = [Float(x), Float(y)] }
    }

    func pointerMove(_ x: Double, _ y: Double, tol: Double, pressed: Bool) {
        guard doc != nil, !busy else { return }
        self.tol = tol
        if pressed && !dragging && abs(x - press.x) + abs(y - press.y) > tol * 0.6 { dragging = true }
        if tool == .select {
            if pressed && dragging, let b = boxStart {
                setOverlay(box: [b[0], b[1], Float(x), Float(y)], crossing: x < Double(b[0]))
            }
            return
        }
        let raw = snapPoint(x, y, tol)
        if raw.kind > 0 && !pressed { acquire(raw, tol) }
        if raw.kind > 0 && pressed && dragging { acquire(raw, tol) }
        let p = constrain(raw)
        hover = (p.x, p.y)
        if pressed && dragging && needsFirstPoint { preview(p, firstOverride: (press.x, press.y)) } else { preview(p, firstOverride: nil) }
    }

    func pointerRelease(_ x: Double, _ y: Double, tol: Double) {
        guard doc != nil, !busy else { return }
        self.tol = tol
        if tool == .select {
            if dragging, let b = boxStart { boxSelect(Double(b[0]), Double(b[1]), x, y) } else { pickAt(x, y, tol) }
            boxStart = nil
            dragging = false
            return
        }
        var p = constrain(snapPoint(x, y, tol))
        if dragging && needsFirstPoint {
            point((press.x, press.y), tol)
            p = constrain(snapPoint(x, y, tol))
        }
        dragging = false
        point((p.x, p.y), tol)
    }

    /// Dokunmatik: uzun basıp sürükleyerek pencere seçimi
    func box(_ x0: Double, _ y0: Double, _ x1: Double, _ y1: Double, done: Bool) {
        guard doc != nil, !busy else { return }
        if tool != .select { selectTool(.select) }
        if done {
            shift = false
            boxSelect(x0, y0, x1, y1)
        } else {
            setOverlay(box: [Float(x0), Float(y0), Float(x1), Float(y1)], crossing: x1 < x0)
        }
    }

    func cancelPointer() {
        dragging = false
        boxStart = nil
        setOverlay()
    }

    private var needsFirstPoint: Bool {
        switch tool {
        case .line, .circle, .rect, .measure, .mirror, .move, .copy, .dimension: return anchor == nil
        case .pline: return pline.isEmpty
        default: return false
        }
    }

    private func pickAt(_ x: Double, _ y: Double, _ tol: Double) {
        guard let d = doc else { return }
        let id = d.pick(x, y, tol: tol)
        if id < 0 {
            if !selection.isEmpty && !shift { clearSelection() }
            return
        }
        var sel = selection
        let added: Bool
        if shift || sel.contains(id) { sel.remove(id); added = false } else { sel.insert(id); added = true }
        setSelection(Array(sel), describe(d.info(id), added, sel.count))
    }

    private func boxSelect(_ x0: Double, _ y0: Double, _ x1: Double, _ y1: Double) {
        guard let d = doc else { return }
        let ids = d.selectRect(x0, y0, x1, y1, crossing: x1 < x0)
        var sel = selection
        if shift { sel.subtract(ids) } else { sel.formUnion(ids) }
        setSelection(Array(sel), nil)
    }

    private func describe(_ info: String, _ added: Bool, _ count: Int) -> String {
        let parts = info.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
        let type = Self.typeNames[parts.first ?? ""] ?? parts.first ?? ""
        let head = count > 1 ? "\(count) nesne seçili" : "1 nesne seçili"
        return added ? "\(head) · \(type) · \(parts.count > 1 ? parts[1] : "")" : head
    }

    // ───────────────────────── nokta adımları ─────────────────────────

    private func point(_ p: Pt, _ tol: Double) {
        guard let d = doc else { return }
        if tracking && ![.select, .offset, .text].contains(tool) {
            acq.removeAll { abs($0.x - p.x) + abs($0.y - p.y) < 1e-9 }
            acq.append(p)
            if acq.count > 6 { acq.removeFirst() }
        }
        let layer = activeLayer
        switch tool {
        case .line:
            if let a = anchor {
                if near(a, p, tol) { break }
                anchor = p
                edit { $0.addLine(layer, a.x, a.y, p.x, p.y) }
            } else { anchor = p }
        case .pline:
            if let l = pline.last, near(l, p, tol) { break }
            pline.append(p)
            anchor = p
        case .circle:
            if let c = anchor {
                let r = dist(c, p)
                if r < tol * 0.05 { break }
                anchor = nil
                edit { $0.addCircle(layer, c.x, c.y, r) }
            } else { anchor = p }
        case .rect:
            if let r0 = anchor {
                if abs(r0.x - p.x) < 1e-9 || abs(r0.y - p.y) < 1e-9 { break }
                anchor = nil
                edit { $0.addPolyline(layer, [(r0.x, r0.y), (p.x, r0.y), (p.x, p.y), (r0.x, p.y)], closed: true) }
            } else { anchor = p }
        case .measure:
            if let m0 = anchor {
                live = Self.measureText(m0, p)
                anchor = nil
                setOverlay(rubber: [Float(m0.x), Float(m0.y), Float(p.x), Float(p.y)])
                return
            }
            anchor = p
        case .dimension:
            if let d1 = anchor {
                if let d2 = dimP2 {
                    let dim = buildDimension(d1, d2, p)
                    anchor = nil
                    dimP2 = nil
                    edit { dd in
                        _ = try? dd.insertBlock(name: "MK_OLCU_" + String(UInt64(Date().timeIntervalSince1970 * 1e7), radix: 16), snippet: dim.snippet,
                                                layer: layer, d1.x, d1.y, scale: 1, rotation: 0)
                    }
                } else if !near(d1, p, tol) { dimP2 = p }
            } else { anchor = p }
        case .move, .copy:
            if let b = anchor {
                let dx = p.x - b.x, dy = p.y - b.y
                if abs(dx) + abs(dy) < 1e-12 { break }
                doMoveCopy(dx, dy)
            } else { anchor = p }
        case .mirror:
            if let m1 = anchor {
                if near(m1, p, tol) { break }
                let ids = Array(selection), keep = mirrorKeep
                anchor = nil
                tool = .select
                edit { $0.mirror(ids, m1.x, m1.y, p.x, p.y, keep: keep) }
            } else { anchor = p }
        case .rotate:
            if let pv = pivot {
                if near(pv, p, tol) { break }
                rotateBy(snapAngle(atan2(p.y - pv.y, p.x - pv.x) * 180 / .pi))
            } else { pivot = p }
        case .scale:
            if let sp = pivot {
                if let sr = scaleRef {
                    var k = dist(sp, p) / max(dist(sp, sr), 1e-12)
                    if ortho { k = max(0.25, (k * 4).rounded() / 4) }
                    scaleBy(k)
                } else if !near(sp, p, tol) { scaleRef = p }
            } else { pivot = p }
        case .offset:
            if let target = offsetTarget {
                let dd = offsetDist
                offsetTarget = nil
                clearSelectionSilently()
                var result = 0
                edit({ result = $0.offset(target, dd, p.x, p.y) }, after: { [weak self] in
                    if result < 0 { self?.message = "Bu nesne ötelenemiyor. Desteklenenler: çizgi, daire, yay ve düz parçalı çoklu çizgi." }
                })
            } else {
                let id = d.pick(p.x, p.y, tol: tol)
                if id < 0 { break }
                offsetTarget = id
                selection = [id]
                selGeom = d.geometry([id])
            }
        case .text:
            textRequest = CGPoint(x: p.x, y: p.y)
        case .symbol:
            if let sym = symbol {
                let sc = placeScale, rot = symbolRot
                edit { _ = try? $0.insertBlock(name: sym.blockName, snippet: sym.snippet, layer: layer, p.x, p.y, scale: sc, rotation: rot) }
            }
        case .select:
            break
        }
        live = nil
        preview((0, hover.x, hover.y), firstOverride: nil)
    }

    private func doMoveCopy(_ dx: Double, _ dy: Double) {
        let ids = Array(selection)
        if tool == .move {
            anchor = nil
            tool = .select
            edit { $0.move(ids, dx: dx, dy: dy) }
        } else {
            edit { _ = $0.copy(ids, dx: dx, dy: dy) } // art arda kopya: taban aynı kalır
        }
    }

    func rotateBy(_ deg: Double) {
        guard let c = pivot, !selection.isEmpty, deg != 0 else { return }
        let ids = Array(selection)
        pivot = nil
        tool = .select
        edit { $0.rotate(ids, c.x, c.y, deg) }
    }

    func scaleBy(_ k: Double) {
        guard let c = pivot, !selection.isEmpty, k > 0, k != 1 else { return }
        let ids = Array(selection)
        pivot = nil
        scaleRef = nil
        tool = .select
        edit { $0.scale(ids, c.x, c.y, k) }
    }

    /// Sembolü 90° döndür
    func rotateSymbol() {
        symbolRot = (symbolRot + 90).truncatingRemainder(dividingBy: 360)
        preview((0, hover.x, hover.y), firstOverride: nil)
    }

    private func near(_ a: Pt, _ b: Pt, _ tol: Double) -> Bool { abs(a.x - b.x) + abs(a.y - b.y) < tol * 0.05 }
    private func dist(_ a: Pt, _ b: Pt) -> Double { ((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y)).squareRoot() }

    static func fmt(_ v: Double) -> String {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "tr_TR")
        f.maximumFractionDigits = 3
        f.minimumFractionDigits = 0
        f.usesGroupingSeparator = false
        return f.string(from: NSNumber(value: v)) ?? "\(v)"
    }

    private static func measureText(_ a: Pt, _ b: Pt) -> String {
        let dx = b.x - a.x, dy = b.y - a.y
        var ang = atan2(dy, dx) * 180 / .pi
        if ang < 0 { ang += 360 }
        return "Uzunluk \(fmt((dx * dx + dy * dy).squareRoot())) · Açı \(String(format: "%.1f", ang))° · ΔX \(fmt(dx)) · ΔY \(fmt(dy))"
    }

    // ───────────────────────── önizleme ─────────────────────────

    private func seg(_ a: Pt, _ b: Pt) -> [Float] { [Float(a.x), Float(a.y), Float(b.x), Float(b.y)] }

    private func preview(_ p: SnapPt, firstOverride: Pt?) {
        let cur: Pt = (p.x, p.y)
        let a = firstOverride ?? anchor
        var rubber: [Float]?
        var ghost: CGAffineTransform?
        var lv: String?
        switch tool {
        case .line:
            if let l = a { rubber = seg(l, cur); lv = lineLive(l, cur) }
        case .pline:
            var pts = pline
            if let f = firstOverride, pts.isEmpty { pts.append(f) }
            if let last = pts.last {
                var list: [Float] = []
                for i in pts.indices.dropFirst() { list += seg(pts[i - 1], pts[i]) }
                list += seg(last, cur)
                rubber = list
                lv = lineLive(last, cur)
            }
        case .circle:
            if let c = a {
                let r = dist(c, cur)
                let n = 96
                var o: [Float] = []
                for i in 0..<n {
                    let a0 = 2 * Double.pi * Double(i) / Double(n), a1 = 2 * Double.pi * Double(i + 1) / Double(n)
                    o += [Float(c.x + r * cos(a0)), Float(c.y + r * sin(a0)), Float(c.x + r * cos(a1)), Float(c.y + r * sin(a1))]
                }
                o += seg(c, cur)
                rubber = o
                lv = "Yarıçap \(Self.fmt(r)) · Çap \(Self.fmt(2 * r))"
            }
        case .rect:
            if let r0 = a {
                let c1: Pt = (cur.x, r0.y), c3: Pt = (r0.x, cur.y)
                rubber = seg(r0, c1) + seg(c1, cur) + seg(cur, c3) + seg(c3, r0)
                lv = "\(Self.fmt(abs(cur.x - r0.x))) × \(Self.fmt(abs(cur.y - r0.y)))"
            }
        case .measure:
            if let m0 = a { rubber = seg(m0, cur); lv = Self.measureText(m0, cur) }
        case .move, .copy:
            if let b = a {
                ghost = CGAffineTransform(translationX: cur.x - b.x, y: cur.y - b.y)
                rubber = seg(b, cur)
                lv = "ΔX \(Self.fmt(cur.x - b.x)) · ΔY \(Self.fmt(cur.y - b.y)) · Uzaklık \(Self.fmt(dist(b, cur)))"
            }
        case .mirror:
            if let m1 = a {
                rubber = seg(m1, cur)
                if dist(m1, cur) > 1e-9 {
                    let phi = atan2(cur.y - m1.y, cur.x - m1.x), ca = cos(2 * phi), sb = sin(2 * phi)
                    ghost = CGAffineTransform(a: ca, b: sb, c: sb, d: -ca, tx: m1.x - (ca * m1.x + sb * m1.y), ty: m1.y - (sb * m1.x - ca * m1.y))
                }
            }
        case .rotate:
            if let pv = pivot {
                let deg = snapAngle(atan2(cur.y - pv.y, cur.x - pv.x) * 180 / .pi)
                ghost = CGAffineTransform(translationX: pv.x, y: pv.y).rotated(by: deg * .pi / 180).translatedBy(x: -pv.x, y: -pv.y)
                rubber = seg(pv, cur)
                lv = "Açı \(String(format: "%.1f", deg))°"
            }
        case .scale:
            if let sp = pivot {
                if let sr = scaleRef {
                    var k = dist(sp, cur) / max(dist(sp, sr), 1e-12)
                    if ortho { k = max(0.25, (k * 4).rounded() / 4) }
                    ghost = CGAffineTransform(translationX: sp.x, y: sp.y).scaledBy(x: k, y: k).translatedBy(x: -sp.x, y: -sp.y)
                    rubber = seg(sp, sr) + seg(sp, cur)
                    lv = "Ölçek ×\(Self.fmt(k))"
                } else {
                    rubber = seg(sp, cur)
                }
            }
        case .symbol:
            if let sym = symbol { rubber = sym.segments(cur.x, cur.y, scale: placeScale, rot: symbolRot) }
        case .dimension:
            if let d1 = a {
                if let d2 = dimP2 {
                    let dim = buildDimension(d1, d2, cur)
                    rubber = dim.preview
                    lv = "Ölçü \(dim.text)"
                } else {
                    rubber = seg(d1, cur)
                    lv = "Ölçü \(Self.fmt(dist(d1, cur)))"
                }
            }
        default:
            break
        }
        if let tl = trackLabel { lv = lv.map { "\(tl) · \($0)" } ?? tl }
        if lv != nil || tool != .measure { live = lv }
        setOverlay(ghost: ghost, rubber: rubber, snap: p.kind > 0 ? p : nil, cursor: tool == .select ? nil : cur,
                   label: trackLabel ?? (p.kind > 0 ? Self.snapNames[p.kind] : nil))
    }

    private func lineLive(_ a: Pt, _ b: Pt) -> String {
        var ang = atan2(b.y - a.y, b.x - a.x) * 180 / .pi
        if ang < 0 { ang += 360 }
        return "Uzunluk \(Self.fmt(dist(a, b))) · Açı \(String(format: "%.1f", ang))°"
    }

    private func setOverlay(ghost: CGAffineTransform? = nil, rubber: [Float]? = nil, snap: SnapPt? = nil, cursor: Pt? = nil,
                            box: [Float]? = nil, crossing: Bool = false, label: String? = nil) {
        let showLive = cursor != nil && tool != .select
        var o = EditOverlay()
        o.selection = selGeom
        o.ghost = ghost
        o.rubber = rubber
        o.snapType = snap?.kind ?? 0
        if let s = snap { o.snap = CGPoint(x: s.x, y: s.y) }
        if let c = cursor { o.cursor = CGPoint(x: c.x, y: c.y) }
        o.box = box
        o.boxCrossing = crossing
        o.guides = showLive ? guides : nil
        o.marks = showLive && !acq.isEmpty ? acq.flatMap { [Float($0.x), Float($0.y)] } : nil
        o.label = showLive ? label : nil
        overlay = o
    }

    // ───────────────────────── ölçü (hizalı) ─────────────────────────

    struct DimGeom { let snippet: String; let preview: [Float]; let text: String }

    /// Hizalı ölçü: p1–p2 arası, ölçü çizgisi q'dan geçer; tek blok (koordinatlar p1'e göre)
    func buildDimension(_ p1: Pt, _ p2: Pt, _ q: Pt) -> DimGeom {
        let h = textHeight > 0 ? textHeight : 2.5
        var ux = p2.x - p1.x, uy = p2.y - p1.y
        let len = (ux * ux + uy * uy).squareRoot()
        if len < 1e-12 { ux = 1; uy = 0 } else { ux /= len; uy /= len }
        let nx = -uy, ny = ux
        let off = (q.x - p1.x) * nx + (q.y - p1.y) * ny
        let sg: Double = off >= 0 ? 1 : -1
        let gap = h * 0.4, ext = h * 0.6, tick = h * 0.5
        let d1: Pt = (p1.x + nx * off, p1.y + ny * off)
        let d2: Pt = (p2.x + nx * off, p2.y + ny * off)
        var segs: [(Pt, Pt)] = []
        if abs(off) > gap {
            segs.append(((p1.x + nx * gap * sg, p1.y + ny * gap * sg), (d1.x + nx * ext * sg, d1.y + ny * ext * sg)))
            segs.append(((p2.x + nx * gap * sg, p2.y + ny * gap * sg), (d2.x + nx * ext * sg, d2.y + ny * ext * sg)))
        }
        segs.append(((d1.x - ux * tick * 0.6, d1.y - uy * tick * 0.6), (d2.x + ux * tick * 0.6, d2.y + uy * tick * 0.6)))
        let tx = (ux + nx) / 2.0.squareRoot() * tick, ty = (uy + ny) / 2.0.squareRoot() * tick
        segs.append(((d1.x - tx, d1.y - ty), (d1.x + tx, d1.y + ty)))
        segs.append(((d2.x - tx, d2.y - ty), (d2.x + tx, d2.y + ty)))
        var ang = atan2(uy, ux) * 180 / .pi
        if ang > 90.0001 || ang <= -90 { ang += ang > 0 ? -180 : 180 }
        let ar = ang * .pi / 180, upx = -sin(ar), upy = cos(ar)
        let mx = (d1.x + d2.x) / 2 + upx * h * 0.35, my = (d1.y + d2.y) / 2 + upy * h * 0.35
        let text = Self.fmt(len)

        func n(_ v: Double) -> String {
            var s = String(format: "%.8f", locale: Locale(identifier: "en_US_POSIX"), v)
            if s.contains(".") {
                while s.hasSuffix("0") { s.removeLast() }
                if s.hasSuffix(".") { s.removeLast() }
            }
            return s
        }
        var sb = ""
        for (a, b) in segs {
            sb += "0\nLINE\n100\nAcDbEntity\n8\n0\n62\n0\n100\nAcDbLine\n10\n\(n(a.x - p1.x))\n20\n\(n(a.y - p1.y))\n30\n0.0\n11\n\(n(b.x - p1.x))\n21\n\(n(b.y - p1.y))\n31\n0.0\n"
        }
        let lx = mx - p1.x, ly = my - p1.y
        sb += "0\nTEXT\n100\nAcDbEntity\n8\n0\n62\n0\n100\nAcDbText\n10\n\(n(lx))\n20\n\(n(ly))\n30\n0.0\n40\n\(n(h))\n1\n\(text)\n50\n\(n(ang))\n72\n1\n11\n\(n(lx))\n21\n\(n(ly))\n31\n0.0\n100\nAcDbText\n73\n0\n"

        var prev: [Float] = []
        for (a, b) in segs { prev += [Float(a.x), Float(a.y), Float(b.x), Float(b.y)] }
        let tw = Double(text.count) * h * 0.3
        prev += [Float(mx - ux * tw), Float(my - uy * tw), Float(mx + ux * tw), Float(my + uy * tw)]
        return DimGeom(snippet: sb, preview: prev, text: text)
    }

    // ───────────────────────── komut satırı ─────────────────────────

    private static let aliases: [String: CadTool] = [
        "L": .line, "LINE": .line, "ÇİZGİ": .line, "PL": .pline, "PLINE": .pline, "C": .circle, "CIRCLE": .circle, "DAİRE": .circle,
        "REC": .rect, "RECTANG": .rect, "M": .move, "MOVE": .move, "TAŞI": .move, "CO": .copy, "CP": .copy, "COPY": .copy,
        "RO": .rotate, "ROTATE": .rotate, "SC": .scale, "SCALE": .scale, "MI": .mirror, "MIRROR": .mirror, "O": .offset, "OFFSET": .offset,
        "T": .text, "DT": .text, "TEXT": .text, "MT": .text, "DIM": .dimension, "DAL": .dimension, "DLI": .dimension, "ÖLÇÜ": .dimension,
        "DI": .measure, "DIST": .measure, "ÖLÇ": .measure,
    ]

    /// Komut satırı: kısaltma (L, PL, C, M, CO, RO…) ya da sayısal giriş. Anlaşılmazsa false.
    @discardableResult
    func command(_ raw: String) -> Bool {
        let s = raw.trimmingCharacters(in: .whitespaces)
        if s.isEmpty { enter(); return true }
        let up = s.uppercased(with: Locale(identifier: "tr_TR"))
        if let t = Self.aliases[up] ?? Self.aliases[s.uppercased()] { selectTool(t); return true }
        switch up {
        case "E", "ERASE", "SİL", "DEL": deleteSelection(); return true
        case "U", "UNDO": undo(); return true
        case "REDO": redo(); return true
        case "Z", "ZE", "ZOOM": onFit?(); return true
        case "G", "GROUP": groupSelection(); return true
        case "X", "EXPLODE": explodeSelection(); return true
        case "K", "CL", "CLOSE" where tool == .pline: finishPolyline(closed: true); return true
        case "R" where tool == .symbol: rotateSymbol(); return true
        default: break
        }
        return numeric(s)
    }

    private func numeric(_ s0: String) -> Bool {
        guard let d = doc else { return false }
        var s = s0
        let rel = s.hasPrefix("@")
        if rel { s.removeFirst() }
        func num(_ x: String) -> Double? { Double(x.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".")) }
        let tol = self.tol
        let fromPt: Pt? = tool == .pline && !pline.isEmpty ? pline.last : anchor

        if s.contains("<") {
            let p = s.split(separator: "<").map(String.init)
            guard p.count == 2, let len = num(p[0]), let ang = num(p[1]), let f = fromPt else { return false }
            let r = ang * .pi / 180
            point((f.x + len * cos(r), f.y + len * sin(r)), tol)
            return true
        }
        let parts = s.split(whereSeparator: { $0 == ";" || $0 == " " || $0 == "," }).map(String.init)
        if parts.count == 2, let x = num(parts[0]), let y = num(parts[1]) {
            if tool == .rect, let r0 = fromPt { point((r0.x + x, r0.y + y), tol); return true }
            if let f = fromPt, tool != .rotate, tool != .scale { point((f.x + x, f.y + y), tol); return true }
            point(rel ? (hover.x + x, hover.y + y) : (x - d.originX, y - d.originY), tol)
            return true
        }
        guard parts.count == 1, let v = num(parts[0]) else { return false }
        switch tool {
        case .rotate where pivot != nil: rotateBy(v); return true
        case .scale where pivot != nil: scaleBy(v); return true
        case .offset: if v > 0 { offsetDist = v }; return true
        case .circle:
            if let c = anchor { if v > 0 { point((c.x + v, c.y), tol) }; return true }
        case .dimension:
            if let a1 = anchor, let a2 = dimP2 {
                let ux = a2.x - a1.x, uy = a2.y - a1.y, l = (ux * ux + uy * uy).squareRoot()
                guard l >= 1e-12 else { return false }
                let nx = -uy / l, ny = ux / l
                let side: Double = (hover.x - a1.x) * nx + (hover.y - a1.y) * ny >= 0 ? 1 : -1
                point((a1.x + nx * v * side, a1.y + ny * v * side), tol)
                return true
            }
        case .symbol:
            if v > 0 { if fixedScale != nil { fixedScale = v } else { symbolScale = v } }
            return true
        default: break
        }
        guard let f = fromPt, v != 0 else { return false }
        var dx = hover.x - f.x, dy = hover.y - f.y, l = (dx * dx + dy * dy).squareRoot()
        if l < 1e-12 { dx = 1; dy = 0; l = 1 }
        point((f.x + dx / l * v, f.y + dy / l * v), tol)
        return true
    }
}
