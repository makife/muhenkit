import SwiftUI

/// Tuvalin işaretçi olayları (görünüm koordinatı, Y aşağı)
struct CanvasInputHandlers {
    /// true: tek parmak / sol tuş sürüklemesi araca gider (kaydırmaz)
    var capturesDrag: () -> Bool = { false }
    var press: (CGPoint, Bool) -> Void = { _, _ in }
    var move: (CGPoint, Bool) -> Void = { _, _ in }
    var release: (CGPoint) -> Void = { _ in }
    var tap: (CGPoint) -> Void = { _ in }
    var box: (CGPoint, CGPoint, Bool) -> Void = { _, _, _ in }
    var cancel: () -> Void = {}
    var pan: (CGSize) -> Void = { _ in }
    var zoom: (CGPoint, Double) -> Void = { _, _ in }
    var rightClick: () -> Void = {}
    /// Klavye (macOS): işlendiyse true
    var key: (String, Bool) -> Bool = { _, _ in false }
}

#if os(iOS)
import UIKit

/// Dokunma: seçimde tek parmak kaydırır, uzun basıp sürüklemek pencere seçer; çizim araçlarında tek parmak araca gider.
/// İki parmak her zaman kaydır + yakınlaştır. iPad'de fare / kalem üzerinde gezinme önizleme verir.
struct CanvasInputView: UIViewRepresentable {
    let handlers: CanvasInputHandlers

    func makeUIView(context: Context) -> InputView {
        let v = InputView()
        v.handlers = handlers
        return v
    }

    func updateUIView(_ v: InputView, context: Context) { v.handlers = handlers }

    final class InputView: UIView, UIGestureRecognizerDelegate {
        var handlers = CanvasInputHandlers()
        private var boxStart: CGPoint?
        private var toolDrag = false
        private var lastPan = CGPoint.zero
        private var lastPinch: CGFloat = 1

        override init(frame: CGRect) {
            super.init(frame: frame)
            backgroundColor = .clear
            isMultipleTouchEnabled = true
            let one = UIPanGestureRecognizer(target: self, action: #selector(onePan(_:)))
            one.maximumNumberOfTouches = 1
            one.delegate = self
            addGestureRecognizer(one)
            let two = UIPanGestureRecognizer(target: self, action: #selector(twoPan(_:)))
            two.minimumNumberOfTouches = 2
            two.delegate = self
            addGestureRecognizer(two)
            let pinch = UIPinchGestureRecognizer(target: self, action: #selector(onPinch(_:)))
            pinch.delegate = self
            addGestureRecognizer(pinch)
            let tap = UITapGestureRecognizer(target: self, action: #selector(onTap(_:)))
            addGestureRecognizer(tap)
            let long = UILongPressGestureRecognizer(target: self, action: #selector(onLong(_:)))
            long.minimumPressDuration = 0.4
            long.delegate = self
            addGestureRecognizer(long)
            let hover = UIHoverGestureRecognizer(target: self, action: #selector(onHover(_:)))
            addGestureRecognizer(hover)
        }

        required init?(coder: NSCoder) { fatalError() }

        func gestureRecognizer(_ g: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith o: UIGestureRecognizer) -> Bool {
            // iki parmak kaydırma ile kıstırma birlikte
            (g is UIPinchGestureRecognizer && o is UIPanGestureRecognizer) || (o is UIPinchGestureRecognizer && g is UIPanGestureRecognizer)
        }

        override func gestureRecognizerShouldBegin(_ g: UIGestureRecognizer) -> Bool {
            // çizim aracında uzun basma pencere seçimi yapmasın
            if g is UILongPressGestureRecognizer { return !handlers.capturesDrag() }
            return true
        }

        @objc private func onePan(_ g: UIPanGestureRecognizer) {
            let p = g.location(in: self)
            switch g.state {
            case .began:
                toolDrag = handlers.capturesDrag()
                if toolDrag {
                    let t = g.translation(in: self)
                    let start = CGPoint(x: p.x - t.x, y: p.y - t.y)
                    handlers.move(start, false)
                    handlers.press(start, false)
                    handlers.move(p, true)
                } else {
                    lastPan = .zero
                    let t = g.translation(in: self)
                    handlers.pan(CGSize(width: t.x, height: t.y))
                    lastPan = t
                }
            case .changed:
                if toolDrag {
                    handlers.move(p, true)
                } else {
                    let t = g.translation(in: self)
                    handlers.pan(CGSize(width: t.x - lastPan.x, height: t.y - lastPan.y))
                    lastPan = t
                }
            case .ended:
                if toolDrag { handlers.release(p) }
                toolDrag = false
            case .cancelled, .failed:
                if toolDrag { handlers.cancel() }
                toolDrag = false
            default: break
            }
        }

        @objc private func twoPan(_ g: UIPanGestureRecognizer) {
            switch g.state {
            case .began:
                lastPan = g.translation(in: self)
            case .changed:
                let t = g.translation(in: self)
                handlers.pan(CGSize(width: t.x - lastPan.x, height: t.y - lastPan.y))
                lastPan = t
            default: break
            }
        }

        @objc private func onPinch(_ g: UIPinchGestureRecognizer) {
            switch g.state {
            case .began:
                lastPinch = 1
            case .changed:
                handlers.zoom(g.location(in: self), Double(g.scale / lastPinch))
                lastPinch = g.scale
            default: break
            }
        }

        @objc private func onTap(_ g: UITapGestureRecognizer) {
            handlers.tap(g.location(in: self))
        }

        @objc private func onLong(_ g: UILongPressGestureRecognizer) {
            let p = g.location(in: self)
            switch g.state {
            case .began:
                boxStart = p
                UIImpactFeedbackGenerator(style: .medium).impactOccurred()
                handlers.box(p, p, false)
            case .changed:
                if let s = boxStart { handlers.box(s, p, false) }
            case .ended:
                if let s = boxStart { handlers.box(s, p, true) }
                boxStart = nil
            case .cancelled, .failed:
                boxStart = nil
                handlers.cancel()
            default: break
            }
        }

        @objc private func onHover(_ g: UIHoverGestureRecognizer) {
            if g.state == .changed || g.state == .began { handlers.move(g.location(in: self), false) }
        }
    }
}

#else
import AppKit

/// Fare: sol tuş araç / seçim, sağ ya da orta tuşla sürükle kaydır, sağ tık (sürüklemeden) bitir/Esc,
/// teker yakınlaştır, izleme yüzeyinde iki parmak kaydır, kıstır yakınlaştır.
struct CanvasInputView: NSViewRepresentable {
    let handlers: CanvasInputHandlers

    func makeNSView(context: Context) -> InputView {
        let v = InputView()
        v.handlers = handlers
        return v
    }

    func updateNSView(_ v: InputView, context: Context) { v.handlers = handlers }

    final class InputView: NSView {
        var handlers = CanvasInputHandlers()
        private var panFrom: NSPoint?
        private var panMoved = false
        private var leftDown = false

        override var isFlipped: Bool { true }
        override var acceptsFirstResponder: Bool { true }

        override func updateTrackingAreas() {
            super.updateTrackingAreas()
            trackingAreas.forEach(removeTrackingArea)
            addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeInKeyWindow, .inVisibleRect, .mouseEnteredAndExited], owner: self))
        }

        private func loc(_ e: NSEvent) -> CGPoint { convert(e.locationInWindow, from: nil) }

        override func mouseDown(with e: NSEvent) {
            window?.makeFirstResponder(self)
            leftDown = true
            handlers.move(loc(e), false)
            handlers.press(loc(e), e.modifierFlags.contains(.shift))
        }

        override func mouseDragged(with e: NSEvent) { handlers.move(loc(e), true) }

        override func mouseUp(with e: NSEvent) {
            guard leftDown else { return }
            leftDown = false
            handlers.release(loc(e))
        }

        override func mouseMoved(with e: NSEvent) { handlers.move(loc(e), false) }

        override func rightMouseDown(with e: NSEvent) { panFrom = loc(e); panMoved = false }
        override func otherMouseDown(with e: NSEvent) { panFrom = loc(e); panMoved = false }

        override func rightMouseDragged(with e: NSEvent) { dragPan(e) }
        override func otherMouseDragged(with e: NSEvent) { dragPan(e) }

        private func dragPan(_ e: NSEvent) {
            let p = loc(e)
            guard let f = panFrom else { return }
            if abs(p.x - f.x) + abs(p.y - f.y) > 2 { panMoved = true }
            handlers.pan(CGSize(width: p.x - f.x, height: p.y - f.y))
            panFrom = p
        }

        override func rightMouseUp(with e: NSEvent) {
            if !panMoved { handlers.rightClick() }
            panFrom = nil
        }

        override func otherMouseUp(with e: NSEvent) { panFrom = nil }

        override func scrollWheel(with e: NSEvent) {
            if e.hasPreciseScrollingDeltas && !e.modifierFlags.contains(.command) {
                // izleme yüzeyi: iki parmak kaydırır
                handlers.pan(CGSize(width: e.scrollingDeltaX, height: e.scrollingDeltaY))
            } else {
                let d = e.hasPreciseScrollingDeltas ? e.scrollingDeltaY : e.scrollingDeltaY * 12
                handlers.zoom(loc(e), pow(1.0015, Double(d)))
            }
        }

        override func magnify(with e: NSEvent) { handlers.zoom(loc(e), 1 + Double(e.magnification)) }

        override func keyDown(with e: NSEvent) {
            let cmd = e.modifierFlags.contains(.command)
            var k = e.charactersIgnoringModifiers ?? ""
            switch e.keyCode {
            case 53: k = "esc"
            case 36, 76: k = "enter"
            case 51, 117: k = "delete"
            default: break
            }
            if !handlers.key(k, cmd) { super.keyDown(with: e) }
        }
    }
}
#endif
