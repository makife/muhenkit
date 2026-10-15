import SwiftUI

/// Görünüm: dünya merkezi ve yakınlaştırma (nokta / çizim birimi)
struct ViewState: Equatable {
    var cx: Double
    var cy: Double
    var zoom: Double
}

enum CadColors {
    static let background = Color(red: 11 / 255, green: 14 / 255, blue: 16 / 255)
    static let sel = Color(red: 0x4F / 255, green: 0xC3 / 255, blue: 0xF7 / 255)
    static let ghost = Color(red: 1, green: 0xB6 / 255, blue: 0x27 / 255)
    static let rubber = Color(red: 0x3D / 255, green: 1, blue: 0x8E / 255)
    static let snap = Color(red: 1, green: 0xD5 / 255, blue: 0x4F / 255)
    static let guide = Color(red: 0x7C / 255, green: 0xE8 / 255, blue: 0xB0 / 255)

    /// Koyu zeminde siyah görünmez: beyaza çevir
    static func adapt(_ rgb: UInt32, alpha: Double = 1) -> Color {
        let r = Double((rgb >> 16) & 0xFF), g = Double((rgb >> 8) & 0xFF), b = Double(rgb & 0xFF)
        if r * 299 + g * 587 + b * 114 < 40_000 { return Color(white: 1, opacity: alpha) }
        return Color(red: r / 255, green: g / 255, blue: b / 255, opacity: alpha)
    }
}

/// DXF sahnesi + düzenleme katmanı. Girdi: dokunma (iOS) / fare-klavye (macOS)
struct CadCanvas: View {
    let scene: CadScene
    @Binding var view: ViewState
    var hidden: Set<Int>
    var revision: Int = 0
    var overlay: EditOverlay?
    /// Düzenleyici: işaretçi olayları dünya koordinatında buna gider (nil: yalnız görüntüleme)
    var editor: CadEditor?
    /// Klavye (macOS): işlendiyse true
    var onKey: ((String, Bool) -> Bool)?

    var body: some View {
        GeometryReader { geo in
            Canvas(rendersAsynchronously: false) { ctx, size in
                draw(&ctx, size)
            }
            .background(CadColors.background)
            .overlay(CanvasInputView(handlers: wrapped(geo.size)))
        }
    }

    // ───────────────────────── görünüm dönüşümleri ─────────────────────────

    static func transform(_ v: ViewState, _ size: CGSize) -> CGAffineTransform {
        CGAffineTransform(translationX: size.width / 2, y: size.height / 2).scaledBy(x: v.zoom, y: -v.zoom).translatedBy(x: -v.cx, y: -v.cy)
    }

    static func toWorld(_ p: CGPoint, _ v: ViewState, _ size: CGSize) -> (Double, Double) {
        (v.cx + (p.x - size.width / 2) / v.zoom, v.cy - (p.y - size.height / 2) / v.zoom)
    }

    static func fit(_ s: CadScene, in size: CGSize) -> ViewState {
        let w = max(Double(s.maxX - s.minX), 1e-6), h = max(Double(s.maxY - s.minY), 1e-6)
        let z = min(max(size.width, 100) / w, max(size.height, 100) / h) * 0.92
        return ViewState(cx: Double(s.minX + s.maxX) / 2, cy: Double(s.minY + s.maxY) / 2, zoom: z)
    }

    #if os(iOS)
    /// Parmak için yakalama/seçim toleransı (nokta)
    static let touchTol = 16.0
    #else
    static let touchTol = 10.0
    #endif

    /// Kaydırma / yakınlaştırma tuvalde; işaretçi olayları dünya koordinatına çevrilip düzenleyiciye verilir
    private func wrapped(_ size: CGSize) -> CanvasInputHandlers {
        var h = CanvasInputHandlers()
        let binding = $view
        if let ed = editor {
            func w(_ p: CGPoint) -> (Double, Double) { Self.toWorld(p, binding.wrappedValue, size) }
            func tol() -> Double { Self.touchTol / binding.wrappedValue.zoom }
            h.capturesDrag = { ed.capturesDrag }
            h.press = { p, shift in let (x, y) = w(p); ed.pointerPress(x, y, tol: tol(), shift: shift) }
            h.move = { p, pressed in let (x, y) = w(p); ed.pointerMove(x, y, tol: tol(), pressed: pressed) }
            h.release = { p in let (x, y) = w(p); ed.pointerRelease(x, y, tol: tol()) }
            h.tap = { p in
                let (x, y) = w(p)
                ed.pointerMove(x, y, tol: tol(), pressed: false)
                ed.pointerPress(x, y, tol: tol())
                ed.pointerRelease(x, y, tol: tol())
            }
            h.box = { a, b, done in
                let (x0, y0) = w(a), (x1, y1) = w(b)
                ed.box(x0, y0, x1, y1, done: done)
            }
            h.cancel = { ed.cancelPointer() }
            h.rightClick = { ed.rightClick() }
        }
        if let k = onKey { h.key = k }
        h.pan = { d in
            var v = binding.wrappedValue
            v.cx -= d.width / v.zoom
            v.cy += d.height / v.zoom
            binding.wrappedValue = v
        }
        h.zoom = { p, k in
            let v = binding.wrappedValue
            let (wx, wy) = Self.toWorld(p, v, size)
            let z = min(max(v.zoom * k, 1e-9), 1e9)
            binding.wrappedValue = ViewState(cx: wx - (p.x - size.width / 2) / z, cy: wy + (p.y - size.height / 2) / z, zoom: z)
        }
        return h
    }

    // ───────────────────────── çizim ─────────────────────────

    private func draw(_ ctx: inout GraphicsContext, _ size: CGSize) {
        let v = view
        let z = v.zoom
        let m = Self.transform(v, size)
        let hw = size.width / 2 / z, hh = size.height / 2 / z
        let x0 = v.cx - hw, x1 = v.cx + hw, y0 = v.cy - hh, y1 = v.cy + hh

        var world = ctx
        world.concatenate(m)
        let fills = scene.fillPaths
        for (i, f) in scene.fills.enumerated() where !hidden.contains(f.layer) {
            world.fill(Path(fills[i]), with: .color(CadColors.adapt(f.rgb, alpha: Double(f.alpha))), style: FillStyle(eoFill: true))
        }
        let paths = scene.batchPaths
        let lw = 1 / z
        for (i, b) in scene.batches.enumerated() where !hidden.contains(b.layer) {
            if Double(b.maxX) < x0 || Double(b.minX) > x1 || Double(b.maxY) < y0 || Double(b.minY) > y1 { continue }
            world.stroke(Path(paths[i]), with: .color(CadColors.adapt(b.rgb)), lineWidth: lw)
        }

        // yazılar: ekran koordinatında, okunur yönde
        for t in scene.texts where !hidden.contains(t.layer) {
            let cap = Double(t.height) * z
            if cap < 3 { continue }
            let pad = Double(t.height) * 40
            if Double(t.x) < x0 - pad || Double(t.x) > x1 + pad || Double(t.y) < y0 - pad || Double(t.y) > y1 + pad { continue }
            let p = CGPoint(x: Double(t.x), y: Double(t.y)).applying(m)
            var c = ctx
            c.translateBy(x: p.x, y: p.y)
            c.rotate(by: .degrees(-Double(t.rotation)))
            c.scaleBy(x: CGFloat(t.widthFactor > 0 ? t.widthFactor : 1), y: 1)
            let text = c.resolve(Text(t.text.replacingOccurrences(of: "\n", with: " "))
                .font(.system(size: cap / 0.7)).foregroundColor(CadColors.adapt(t.rgb)))
            let ax: CGFloat = t.hAlign == 1 ? 0.5 : t.hAlign == 2 ? 1 : 0
            // taban çizgisi ≈ satır kutusunun %78'i; üst hizada büyük harf üstü ≈ %22
            let ay: CGFloat = t.vAlign == 3 ? 0.22 : t.vAlign == 2 ? 0.5 : 0.78
            c.draw(text, at: .zero, anchor: UnitPoint(x: ax, y: ay))
        }
        if let o = overlay { drawOverlay(&ctx, o, m) }
    }

    private static func segPath(_ a: [Float], _ t: CGAffineTransform) -> Path {
        var p = Path()
        var i = 0
        while i + 3 < a.count {
            p.move(to: CGPoint(x: Double(a[i]), y: Double(a[i + 1])).applying(t))
            p.addLine(to: CGPoint(x: Double(a[i + 2]), y: Double(a[i + 3])).applying(t))
            i += 4
        }
        return p
    }

    private func drawOverlay(_ ctx: inout GraphicsContext, _ o: EditOverlay, _ m: CGAffineTransform) {
        if o.selection.count >= 4 {
            let sp = Self.segPath(o.selection, m)
            ctx.stroke(sp, with: .color(CadColors.sel.opacity(0.24)), lineWidth: 6)
            ctx.stroke(sp, with: .color(CadColors.sel), lineWidth: 1.5)
            if let g = o.ghost {
                ctx.stroke(Self.segPath(o.selection, g.concatenating(m)), with: .color(CadColors.ghost), lineWidth: 1.3)
            }
        }
        if let r = o.rubber, r.count >= 4 {
            ctx.stroke(Self.segPath(r, m), with: .color(CadColors.rubber), lineWidth: 1.3)
        }
        if let g = o.guides, g.count >= 4 {
            ctx.stroke(Self.segPath(g, m), with: .color(CadColors.guide.opacity(0.86)), style: StrokeStyle(lineWidth: 1, dash: [2, 4]))
        }
        if let mk = o.marks {
            var i = 0
            var p = Path()
            while i + 1 < mk.count {
                let q = CGPoint(x: Double(mk[i]), y: Double(mk[i + 1])).applying(m)
                p.move(to: CGPoint(x: q.x - 4, y: q.y)); p.addLine(to: CGPoint(x: q.x + 4, y: q.y))
                p.move(to: CGPoint(x: q.x, y: q.y - 4)); p.addLine(to: CGPoint(x: q.x, y: q.y + 4))
                i += 2
            }
            ctx.stroke(p, with: .color(CadColors.guide), lineWidth: 1.2)
        }
        if let b = o.box, b.count == 4 {
            let p0 = CGPoint(x: Double(b[0]), y: Double(b[1])).applying(m)
            let p1 = CGPoint(x: Double(b[2]), y: Double(b[3])).applying(m)
            let rect = CGRect(x: min(p0.x, p1.x), y: min(p0.y, p1.y), width: abs(p1.x - p0.x), height: abs(p1.y - p0.y))
            // AutoCAD gibi: pencere mavi, kesişen yeşil ve kesikli
            let c = o.boxCrossing ? Color(red: 0x3D / 255, green: 0xD6 / 255, blue: 0x6E / 255) : Color(red: 0x4F / 255, green: 0x8B / 255, blue: 0xF7 / 255)
            ctx.fill(Path(rect), with: .color(c.opacity(0.16)))
            ctx.stroke(Path(rect), with: .color(c), style: StrokeStyle(lineWidth: 1, dash: o.boxCrossing ? [6, 4] : []))
        }
        if o.snapType > 0 {
            let p = o.snap.applying(m)
            let k: CGFloat = 7
            var s = Path()
            switch o.snapType {
            case 2:
                s.move(to: CGPoint(x: p.x - k, y: p.y + k)); s.addLine(to: CGPoint(x: p.x + k, y: p.y + k)); s.addLine(to: CGPoint(x: p.x, y: p.y - k)); s.closeSubpath()
            case 3:
                s.addEllipse(in: CGRect(x: p.x - k, y: p.y - k, width: 2 * k, height: 2 * k))
            case 4:
                s.move(to: CGPoint(x: p.x - k, y: p.y - k)); s.addLine(to: CGPoint(x: p.x + k, y: p.y + k))
                s.move(to: CGPoint(x: p.x - k, y: p.y + k)); s.addLine(to: CGPoint(x: p.x + k, y: p.y - k))
            default:
                s.addRect(CGRect(x: p.x - k, y: p.y - k, width: 2 * k, height: 2 * k))
            }
            ctx.stroke(s, with: .color(CadColors.snap), lineWidth: 2)
        }
        if let c = o.cursor {
            let p = c.applying(m)
            ctx.stroke(Path(ellipseIn: CGRect(x: p.x - 3.5, y: p.y - 3.5, width: 7, height: 7)), with: .color(CadColors.rubber), lineWidth: 1.5)
            if let label = o.label, !label.isEmpty {
                let t = ctx.resolve(Text(label).font(.system(size: 12)).foregroundColor(CadColors.guide))
                let sz = t.measure(in: CGSize(width: 400, height: 40))
                let r = CGRect(x: p.x + 9, y: p.y + 10, width: sz.width + 10, height: sz.height + 4)
                ctx.fill(Path(roundedRect: r, cornerRadius: 4), with: .color(Color(red: 0x10 / 255, green: 0x22 / 255, blue: 0x1A / 255).opacity(0.88)))
                ctx.draw(t, at: CGPoint(x: r.minX + 5, y: r.minY + 2), anchor: .topLeading)
            }
        }
    }
}
