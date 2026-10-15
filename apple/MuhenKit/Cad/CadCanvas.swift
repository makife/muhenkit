import SwiftUI
#if os(macOS)
import AppKit
#endif

/// Görünüm: dünya merkezi ve yakınlaştırma (piksel / çizim birimi)
struct ViewState: Equatable {
    var cx: Double
    var cy: Double
    var zoom: Double
}

enum CadColors {
    static let background = Color(red: 11 / 255, green: 14 / 255, blue: 16 / 255)

    /// Koyu zeminde siyah görünmez: beyaza çevir
    static func adapt(_ rgb: UInt32, alpha: Double = 1) -> Color {
        let r = Double((rgb >> 16) & 0xFF), g = Double((rgb >> 8) & 0xFF), b = Double(rgb & 0xFF)
        if r * 299 + g * 587 + b * 114 < 40_000 { return Color(white: 1, opacity: alpha) }
        return Color(red: r / 255, green: g / 255, blue: b / 255, opacity: alpha)
    }
}

/// DXF sahnesini çizen tuval: kaydırma (sürükle), yakınlaştırma (iki parmak / fare tekeri / izleme yüzeyi)
struct CadCanvas: View {
    let scene: CadScene
    @Binding var view: ViewState
    var hidden: Set<Int>
    /// Değiştikçe tuval yeniden çizilir (sahne yenilenince artır)
    var revision: Int = 0
    /// Düzenleme katmanı (seçim, lastik, yakalama) — ekran koordinatına dönüştürülmüş çizim
    var overlay: ((inout GraphicsContext, CGAffineTransform) -> Void)?

    @State private var dragStart: ViewState?
    @State private var pinchStart: ViewState?
    @State private var size: CGSize = .zero

    var body: some View {
        GeometryReader { geo in
            Canvas(rendersAsynchronously: false) { ctx, size in
                draw(&ctx, size)
            }
            .background(CadColors.background)
            .contentShape(Rectangle())
            .gesture(panGesture.simultaneously(with: pinchGesture))
            .onAppear { size = geo.size }
            .onChange(of: geo.size) { size = $0 }
            #if os(macOS)
            .background(ScrollWheelCatcher { delta, location in
                zoom(at: location, factor: pow(1.0015, delta))
            })
            #endif
        }
    }

    private var panGesture: some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { g in
                let s = dragStart ?? view
                if dragStart == nil { dragStart = view }
                view.cx = s.cx - g.translation.width / s.zoom
                view.cy = s.cy + g.translation.height / s.zoom
            }
            .onEnded { _ in dragStart = nil }
    }

    private var pinchGesture: some Gesture {
        MagnificationGesture()
            .onChanged { k in
                let s = pinchStart ?? view
                if pinchStart == nil { pinchStart = view }
                view.zoom = max(1e-9, s.zoom * k)
            }
            .onEnded { _ in pinchStart = nil }
    }

    /// İmleç altındaki dünya noktası sabit kalacak şekilde yakınlaştır
    private func zoom(at p: CGPoint, factor: Double) {
        let (wx, wy) = toWorld(p)
        let z = max(1e-9, view.zoom * factor)
        view = ViewState(cx: wx - (p.x - size.width / 2) / z, cy: wy + (p.y - size.height / 2) / z, zoom: z)
    }

    func toWorld(_ p: CGPoint) -> (Double, Double) {
        (view.cx + (p.x - size.width / 2) / view.zoom, view.cy - (p.y - size.height / 2) / view.zoom)
    }

    static func fit(_ s: CadScene, in size: CGSize) -> ViewState {
        let w = max(Double(s.maxX - s.minX), 1e-6), h = max(Double(s.maxY - s.minY), 1e-6)
        let z = min(max(size.width, 100) / w, max(size.height, 100) / h) * 0.92
        return ViewState(cx: Double(s.minX + s.maxX) / 2, cy: Double(s.minY + s.maxY) / 2, zoom: z)
    }

    private func draw(_ ctx: inout GraphicsContext, _ size: CGSize) {
        let v = view
        let z = v.zoom
        let m = CGAffineTransform(translationX: size.width / 2, y: size.height / 2).scaledBy(x: z, y: -z).translatedBy(x: -v.cx, y: -v.cy)
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
        overlay?(&ctx, m)
    }
}

#if os(macOS)
/// Fare tekeri / izleme yüzeyi kaydırmasıyla yakınlaştırma (SwiftUI macOS 13'te teker olayı vermiyor)
struct ScrollWheelCatcher: NSViewRepresentable {
    let onScroll: (Double, CGPoint) -> Void

    func makeNSView(context: Context) -> NSView {
        let v = WheelView()
        v.onScroll = onScroll
        return v
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        (nsView as? WheelView)?.onScroll = onScroll
    }

    final class WheelView: NSView {
        var onScroll: ((Double, CGPoint) -> Void)?
        private var monitor: Any?

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            if let monitor { NSEvent.removeMonitor(monitor) }
            monitor = nil
            guard window != nil else { return }
            monitor = NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak self] e in
                guard let self, e.window == self.window else { return e }
                let p = self.convert(e.locationInWindow, from: nil)
                guard self.bounds.contains(p) else { return e }
                // AppKit Y yukarı → SwiftUI Y aşağı
                let d = e.hasPreciseScrollingDeltas ? e.scrollingDeltaY : e.scrollingDeltaY * 12
                self.onScroll?(d, CGPoint(x: p.x, y: self.bounds.height - p.y))
                return nil
            }
        }

        deinit {
            if let monitor { NSEvent.removeMonitor(monitor) }
        }
    }
}
#endif
