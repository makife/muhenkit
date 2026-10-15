import Foundation
import CoreGraphics
import CoreText
import ImageIO
import UniformTypeIdentifiers

enum ExportFormat: Int, CaseIterable { case pdf, png }
enum PageOrientation: Int, CaseIterable { case auto, landscape, portrait }

struct Paper: Hashable {
    let name: String
    let w: CGFloat
    let h: CGFloat
    static let all = [Paper(name: "A4", w: 595, h: 842), Paper(name: "A3", w: 842, h: 1191), Paper(name: "A2", w: 1191, h: 1684),
                      Paper(name: "A1", w: 1684, h: 2384), Paper(name: "A0", w: 2384, h: 3370)]
}

struct ExportOptions {
    var format = ExportFormat.pdf
    var paper = Paper.all[0]
    var orientation = PageOrientation.auto
    var viewOnly = false
    var monochrome = true
}

/// Çizimi beyaz kâğıda PDF (vektör, gömülü yazı tipi) ya da PNG olarak çıkarır (Android/Windows ile aynı sayfa düzeni).
/// Sayfa birimi punto; CoreGraphics'te Y yukarı.
enum Exporter {
    private static let margin: CGFloat = 28 // ≈10 mm
    private static let footer: CGFloat = 14
    private static let pngDpi: CGFloat = 150

    /// Tüm çizimin dünya penceresi (%2 pay)
    static func extents(_ s: CadScene) -> CGRect {
        let px = CGFloat(s.maxX - s.minX) * 0.02, py = CGFloat(s.maxY - s.minY) * 0.02
        return CGRect(x: CGFloat(s.minX) - px, y: CGFloat(s.minY) - py, width: CGFloat(s.maxX - s.minX) + 2 * px, height: CGFloat(s.maxY - s.minY) + 2 * py)
    }

    static func render(_ s: CadScene, hidden: Set<Int>, window win: CGRect, _ o: ExportOptions, title: String) -> Data {
        let landscape: Bool
        switch o.orientation {
        case .landscape: landscape = true
        case .portrait: landscape = false
        case .auto: landscape = win.width >= win.height
        }
        let w = landscape ? o.paper.h : o.paper.w, h = landscape ? o.paper.w : o.paper.h
        if o.format == .pdf {
            let data = NSMutableData()
            var box = CGRect(x: 0, y: 0, width: w, height: h)
            guard let consumer = CGDataConsumer(data: data as CFMutableData),
                  let ctx = CGContext(consumer: consumer, mediaBox: &box, [kCGPDFContextCreator: "MühenKit", kCGPDFContextTitle: title] as CFDictionary)
            else { return Data() }
            ctx.beginPDFPage(nil)
            drawPage(ctx, w, h, s, hidden, win, o, title, lineWidth: 0.4)
            ctx.endPDFPage()
            ctx.closePDF()
            return data as Data
        }
        let k = min(pngDpi / 72, 9000 / max(w, h))
        let pw = Int((w * k).rounded()), ph = Int((h * k).rounded())
        guard let ctx = CGContext(data: nil, width: pw, height: ph, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return Data() }
        ctx.scaleBy(x: k, y: k)
        drawPage(ctx, w, h, s, hidden, win, o, title, lineWidth: 0.5)
        guard let img = ctx.makeImage() else { return Data() }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out as CFMutableData, UTType.png.identifier as CFString, 1, nil) else { return Data() }
        CGImageDestinationAddImage(dest, img, [kCGImagePropertyDPIWidth: pngDpi, kCGImagePropertyDPIHeight: pngDpi] as CFDictionary)
        CGImageDestinationFinalize(dest)
        return out as Data
    }

    private static func color(_ rgb: UInt32, _ mono: Bool, alpha: CGFloat = 1) -> CGColor {
        if mono { return CGColor(red: 0, green: 0, blue: 0, alpha: alpha) }
        let c = rgb & 0xFFFFFF
        let r = CGFloat((c >> 16) & 0xFF), g = CGFloat((c >> 8) & 0xFF), b = CGFloat(c & 0xFF)
        // beyaz kâğıtta beyaz/çok açık renkler görünmez: siyah (AutoCAD çizim davranışı)
        if r * 299 + g * 587 + b * 114 > 200_000 { return CGColor(red: 0, green: 0, blue: 0, alpha: alpha) }
        return CGColor(red: r / 255, green: g / 255, blue: b / 255, alpha: alpha)
    }

    private static func drawPage(_ c: CGContext, _ w: CGFloat, _ h: CGFloat, _ s: CadScene, _ hidden: Set<Int>, _ win: CGRect,
                                 _ o: ExportOptions, _ title: String, lineWidth: CGFloat) {
        c.textMatrix = .identity
        c.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        c.fill(CGRect(x: 0, y: 0, width: w, height: h))
        let target = CGRect(x: margin, y: margin + footer, width: w - 2 * margin, height: h - 2 * margin - footer)
        let ww = max(win.width, 1e-6), wh = max(win.height, 1e-6)
        let k = min(target.width / ww, target.height / wh)
        let ox = target.midX - win.midX * k, oy = target.midY - win.midY * k

        c.saveGState()
        c.clip(to: target)
        c.saveGState()
        c.translateBy(x: ox, y: oy)
        c.scaleBy(x: k, y: k)
        for (i, f) in s.fills.enumerated() where !hidden.contains(f.layer) {
            c.addPath(s.fillPaths[i])
            c.setFillColor(color(f.rgb, o.monochrome, alpha: CGFloat(min(max(f.alpha, 0), 1))))
            c.fillPath(using: .evenOdd)
        }
        c.setLineWidth(lineWidth / k)
        c.setLineCap(.round)
        c.setLineJoin(.round)
        for (i, b) in s.batches.enumerated() where !hidden.contains(b.layer) && b.count >= 2 {
            if CGFloat(b.maxX) < win.minX || CGFloat(b.minX) > win.maxX || CGFloat(b.maxY) < win.minY || CGFloat(b.minY) > win.maxY { continue }
            c.addPath(s.batchPaths[i])
            c.setStrokeColor(color(b.rgb, o.monochrome))
            c.strokePath()
        }
        c.restoreGState()

        // yazılar: sayfa koordinatında; Android/Windows ile aynı dikey hiza
        for t in s.texts where !hidden.contains(t.layer) {
            let cap = CGFloat(t.height) * k
            if cap < 0.3 { continue }
            let pad = CGFloat(t.height) * 60
            if CGFloat(t.x) < win.minX - pad || CGFloat(t.x) > win.maxX + pad || CGFloat(t.y) < win.minY - pad || CGFloat(t.y) > win.maxY + pad { continue }
            let font = CTFontCreateWithName("Helvetica" as CFString, 100, nil)
            let capRatio = CTFontGetCapHeight(font) / 100
            let size = cap / max(capRatio, 0.5)
            let f2 = CTFontCreateWithName("Helvetica" as CFString, size, nil)
            let wf = CGFloat(t.widthFactor > 0 ? t.widthFactor : 1)
            let lines = wrap(t, f2, wf, k)
            let n = lines.count
            let step = cap * 1.667
            for (i, line) in lines.enumerated() {
                let by: CGFloat
                switch t.vAlign {
                case 3: by = cap + CGFloat(i) * step
                case 2: by = -((cap + CGFloat(n - 1) * step) / 2 - cap) + CGFloat(i) * step
                case 1: by = -CGFloat(n - 1 - i) * step
                default: by = CGFloat(i) * step
                }
                let ct = ctLine(line, f2, color(t.rgb, o.monochrome))
                let lw = CGFloat(CTLineGetTypographicBounds(ct, nil, nil, nil)) * wf
                let ax: CGFloat = t.hAlign == 1 ? -lw / 2 : t.hAlign == 2 ? -lw : 0
                c.saveGState()
                c.translateBy(x: ox + CGFloat(t.x) * k, y: oy + CGFloat(t.y) * k)
                c.rotate(by: CGFloat(t.rotation) * .pi / 180)
                c.translateBy(x: ax, y: -by)
                c.scaleBy(x: wf, y: 1)
                c.textPosition = .zero
                CTLineDraw(ct, c)
                c.restoreGState()
            }
        }
        c.restoreGState()

        // çerçeve ve alt bilgi
        let black = CGColor(red: 0, green: 0, blue: 0, alpha: 1)
        c.setStrokeColor(black)
        c.setLineWidth(0.6)
        c.stroke(CGRect(x: margin, y: margin, width: w - 2 * margin, height: h - 2 * margin))
        c.move(to: CGPoint(x: margin, y: margin + footer))
        c.addLine(to: CGPoint(x: w - margin, y: margin + footer))
        c.strokePath()
        let small = CTFontCreateWithName("Helvetica" as CFString, 7, nil)
        let df = DateFormatter()
        df.locale = Locale(identifier: "tr_TR")
        df.dateFormat = "dd.MM.yyyy"
        let left = ctLine(title, small, black), right = ctLine("\(df.string(from: Date())) · MühenKit", small, black)
        c.textPosition = CGPoint(x: margin + 4, y: margin + 4)
        CTLineDraw(left, c)
        c.textPosition = CGPoint(x: w - margin - 4 - CGFloat(CTLineGetTypographicBounds(right, nil, nil, nil)), y: margin + 4)
        CTLineDraw(right, c)
    }

    private static func ctLine(_ s: String, _ font: CTFont, _ color: CGColor) -> CTLine {
        let attr = NSAttributedString(string: s, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font,
                                                               NSAttributedString.Key(kCTForegroundColorAttributeName as String): color])
        return CTLineCreateWithAttributedString(attr)
    }

    /// MTEXT kutu genişliğine göre kelime kaydırma
    private static func wrap(_ t: CadText, _ font: CTFont, _ wf: CGFloat, _ k: CGFloat) -> [String] {
        let raw = t.text.components(separatedBy: "\n")
        guard t.boxWidth > 0 else { return raw }
        let maxW = CGFloat(t.boxWidth) * k * 1.05
        let black = CGColor(red: 0, green: 0, blue: 0, alpha: 1)
        func width(_ s: String) -> CGFloat { CGFloat(CTLineGetTypographicBounds(ctLine(s, font, black), nil, nil, nil)) * wf }
        var out: [String] = []
        for para in raw {
            var cur = ""
            for word in para.components(separatedBy: " ") {
                let cand = cur.isEmpty ? word : cur + " " + word
                if !cur.isEmpty && width(cand) > maxW {
                    out.append(cur)
                    cur = word
                } else {
                    cur = cand
                }
            }
            out.append(cur)
        }
        return out
    }
}
