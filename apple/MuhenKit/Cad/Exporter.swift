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
    /// Ölçek paydası (50 → 1/50); 0 = kâğıda sığdır
    var scale = 0
    /// Bir çizim biriminin mm karşılığı (cm çizimde 10)
    var unitMm: Double = 10
    /// Kalınlığı tanımsız çizgilerin kalınlığı (mm)
    var defaultLw: CGFloat = 0.18
    /// Firma antetini sağ alt köşeye al
    var titleBlock = true
    /// Alt bilgi şeridi (dosya adı, ölçek, tarih)
    var footer = false
}

/// Sayfa yerleşimi (punto, Y yukarı). Çizim: x' = ox + x·k, y' = oy + y·k; antet aynı biçimde ka/ax/ay ile.
struct PageLayout {
    let w: CGFloat, h: CGFloat, landscape: Bool
    let k: CGFloat, ox: CGFloat, oy: CGFloat, area: CGRect
    let hasTitle: Bool, ka: CGFloat, ax: CGFloat, ay: CGFloat
    let fits: Bool, effScale: Double, needWmm: Double, needHmm: Double
}

/// Çizimi beyaz kâğıda PDF (vektör, gömülü yazı tipi) ya da PNG olarak çıkarır (Android/Windows ile aynı sayfa düzeni).
/// Pafta TS EN ISO 5457: solda 20 mm cilt payı, diğer kenarlar 10 mm, 0,7 mm çerçeve; antet sağ alt köşede en çok 180 mm (ISO 7200).
/// Sayfa birimi punto; CoreGraphics'te Y yukarı.
enum Exporter {
    private static let mm: CGFloat = 72 / 25.4
    private static let frameLeft = 20 * mm, frameSide = 10 * mm, frameLine = 0.7 * mm, footerH = 5 * mm, titleMaxW = 180 * mm
    private static let pngDpi: CGFloat = 150
    /// Ölçek paydaları; 0 = kâğıda sığdır
    static let scales = [0, 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000]
    private static let scaleTextRe = try! NSRegularExpression(pattern: #"^\s*1\s*[/:]\s*\d+([.,]\d+)?\s*$"#)

    /// INSUNITS → bir birimin mm karşılığı; belirtilmemişse cm (mimari altlıkların çoğu)
    static func unitMm(_ insunits: Int) -> Double {
        switch insunits { case 1: return 25.4; case 2: return 304.8; case 4: return 1; case 5: return 10; case 6: return 1000; default: return 10 }
    }

    /// Tüm çizimin dünya penceresi (%2 pay); exclude ≥ 0 ise o nesne (sağ alta alınan antet) hariç
    static func extents(_ s: CadScene, exclude: Int = -1) -> CGRect {
        var r = CGRect(x: CGFloat(s.minX), y: CGFloat(s.minY), width: CGFloat(s.maxX - s.minX), height: CGFloat(s.maxY - s.minY))
        if exclude >= 0, let b = bounds(s, { $0 != exclude }) { r = b }
        let px = r.width * 0.02, py = r.height * 0.02
        return r.insetBy(dx: -px, dy: -py)
    }

    /// Sahibi koşula uyan ilkellerin dünya sınırları (yazılar dayanak noktasıyla)
    static func bounds(_ s: CadScene, _ owner: (Int) -> Bool) -> CGRect? {
        var x0 = Float.greatestFiniteMagnitude, y0 = x0, x1 = -x0, y1 = -x0
        func g(_ x: Float, _ y: Float) { x0 = min(x0, x); x1 = max(x1, x); y0 = min(y0, y); y1 = max(y1, y) }
        let v = s.vertices
        for b in s.batches {
            var i = b.first
            while i + 1 < b.first + b.count {
                if owner(s.owner(ofVertex: i)) { g(v[i * 2], v[i * 2 + 1]); g(v[i * 2 + 2], v[i * 2 + 3]) }
                i += 2
            }
        }
        for f in s.fills where owner(f.owner) {
            for q in 0..<f.loopFirst.count { for i in 0..<f.loopCount[q] { let a = f.loopFirst[q] + i; g(v[a * 2], v[a * 2 + 1]) } }
        }
        for t in s.texts where owner(t.owner) { g(t.x, t.y); g(t.x, t.y + t.height) }
        return x0 <= x1 ? CGRect(x: CGFloat(x0), y: CGFloat(y0), width: CGFloat(x1 - x0), height: CGFloat(y1 - y0)) : nil
    }

    /// Sağ alta alınacak antetin sınırları; seçenek kapalıysa ya da antet yoksa nil
    static func titleBounds(_ s: CadScene, _ o: ExportOptions) -> CGRect? {
        o.titleBlock && s.titleBlock >= 0 ? bounds(s, { $0 == s.titleBlock }) : nil
    }

    static func layout(_ win: CGRect, _ title: CGRect?, _ o: ExportOptions, paper: Paper? = nil, orientation: PageOrientation? = nil) -> PageLayout {
        let paper = paper ?? o.paper, orientation = orientation ?? o.orientation
        let ww = max(win.width, 1e-6), wh = max(win.height, 1e-6)
        let landscape: Bool
        switch orientation {
        case .landscape: landscape = true
        case .portrait: landscape = false
        case .auto: landscape = ww >= wh
        }
        let w = landscape ? paper.h : paper.w, h = landscape ? paper.w : paper.h
        let fx0 = frameLeft, fx1 = w - frameSide, fy1 = h - frameSide, iy0 = frameSide + (o.footer ? footerH : 0)
        var cands: [CGRect] = []
        var ka: CGFloat = 0, ax: CGFloat = 0, ay: CGFloat = 0
        if let t = title, t.width > 0, t.height > 0 {
            ka = min(min(titleMaxW, fx1 - fx0) / t.width, (fy1 - iy0) * 0.6 / t.height)
            let pw = t.width * ka, ph = t.height * ka
            ax = fx1 - pw - t.minX * ka
            ay = iy0 - t.minY * ka
            cands.append(CGRect(x: fx0, y: iy0, width: fx1 - pw - fx0, height: fy1 - iy0)) // antetin solu (tam yükseklik)
            cands.append(CGRect(x: fx0, y: iy0 + ph, width: fx1 - fx0, height: fy1 - iy0 - ph)) // antetin üstü (tam genişlik)
        } else {
            cands.append(CGRect(x: fx0, y: iy0, width: fx1 - fx0, height: fy1 - iy0))
        }
        func fit(_ c: CGRect) -> CGFloat { max(0, min(c.width / ww, c.height / wh)) }
        var best = cands.max { fit($0) < fit($1) }!
        var k = fit(best)
        var fits = true
        if o.scale > 0 {
            k = CGFloat(o.unitMm / Double(o.scale)) * mm
            let fitting = cands.filter { fit($0) >= k * 0.999 }
            fits = !fitting.isEmpty
            if fits { best = fitting.max { fit($0) < fit($1) }! }
        }
        let ox = best.midX - win.midX * k, oy = best.midY - win.midY * k
        return PageLayout(w: w, h: h, landscape: landscape, k: k, ox: ox, oy: oy, area: best, hasTitle: ka > 0, ka: ka, ax: ax, ay: ay,
                          fits: fits, effScale: o.unitMm * Double(mm / k), needWmm: Double(ww * k / mm), needHmm: Double(wh * k / mm))
    }

    /// Seçilen ölçekte çizimin sığdığı en küçük kâğıt (yön sabitse yalnız o yön)
    static func suggestPaper(_ win: CGRect, _ title: CGRect?, _ o: ExportOptions) -> (Paper, PageOrientation)? {
        let dirs: [PageOrientation] = o.orientation == .auto ? [.landscape, .portrait] : [o.orientation]
        for p in Paper.all { for d in dirs where layout(win, title, o, paper: p, orientation: d).fits { return (p, d) } }
        return nil
    }

    static func render(_ s: CadScene, hidden: Set<Int>, window win: CGRect, _ o: ExportOptions, title: String) -> Data {
        let L = layout(win, titleBounds(s, o), o)
        if o.format == .pdf {
            let data = NSMutableData()
            var box = CGRect(x: 0, y: 0, width: L.w, height: L.h)
            guard let consumer = CGDataConsumer(data: data as CFMutableData),
                  let ctx = CGContext(consumer: consumer, mediaBox: &box, [kCGPDFContextCreator: "MühenKit", kCGPDFContextTitle: title] as CFDictionary)
            else { return Data() }
            ctx.beginPDFPage(nil)
            drawPage(ctx, L, s, hidden, win, o, title)
            ctx.endPDFPage()
            ctx.closePDF()
            return data as Data
        }
        let k = min(pngDpi / 72, 9000 / max(L.w, L.h))
        let pw = Int((L.w * k).rounded()), ph = Int((L.h * k).rounded())
        guard let ctx = CGContext(data: nil, width: pw, height: ph, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return Data() }
        ctx.scaleBy(x: k, y: k)
        drawPage(ctx, L, s, hidden, win, o, title)
        guard let img = ctx.makeImage() else { return Data() }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out as CFMutableData, UTType.png.identifier as CFString, 1, nil) else { return Data() }
        CGImageDestinationAddImage(dest, img, [kCGImagePropertyDPIWidth: pngDpi, kCGImagePropertyDPIHeight: pngDpi] as CFDictionary)
        CGImageDestinationFinalize(dest)
        return out as Data
    }

    /// Projelerim önizlemesi: tüm çizim, beyaz zemin, renkli (Android writeThumb ile aynı: 480×300)
    static func thumbnail(_ s: CadScene, hidden: Set<Int>, width w: Int = 480, height h: Int = 300) -> Data? {
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
        ctx.textMatrix = .identity
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        let pad = CGFloat(max(s.maxX - s.minX, 1)) * 0.03
        let win = CGRect(x: CGFloat(s.minX) - pad, y: CGFloat(s.minY) - pad,
                         width: CGFloat(s.maxX - s.minX) + 2 * pad, height: CGFloat(s.maxY - s.minY) + 2 * pad)
        let target = CGRect(x: 4, y: 4, width: w - 8, height: h - 8)
        let k = min(target.width / max(win.width, 1e-6), target.height / max(win.height, 1e-6))
        ctx.saveGState()
        ctx.clip(to: target)
        drawContent(ctx, k: k, ox: target.midX - win.midX * k, oy: target.midY - win.midY * k, s, hidden, cull: win, owner: nil,
                    scaleText: nil, mono: false) { _ in 0.8 }
        ctx.restoreGState()
        guard let img = ctx.makeImage() else { return nil }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out as CFMutableData, UTType.png.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, img, nil)
        return CGImageDestinationFinalize(dest) ? out as Data : nil
    }

    private static func color(_ rgb: UInt32, _ mono: Bool, alpha: CGFloat = 1) -> CGColor {
        if mono { return CGColor(red: 0, green: 0, blue: 0, alpha: alpha) }
        let c = rgb & 0xFFFFFF
        let r = CGFloat((c >> 16) & 0xFF), g = CGFloat((c >> 8) & 0xFF), b = CGFloat(c & 0xFF)
        // beyaz kâğıtta beyaz/çok açık renkler görünmez: siyah (AutoCAD çizim davranışı)
        if r * 299 + g * 587 + b * 114 > 200_000 { return CGColor(red: 0, green: 0, blue: 0, alpha: alpha) }
        return CGColor(red: r / 255, green: g / 255, blue: b / 255, alpha: alpha)
    }

    private static func drawPage(_ c: CGContext, _ L: PageLayout, _ s: CadScene, _ hidden: Set<Int>, _ win: CGRect,
                                 _ o: ExportOptions, _ title: String) {
        c.textMatrix = .identity
        c.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        c.fill(CGRect(x: 0, y: 0, width: L.w, height: L.h))
        let tb = L.hasTitle ? s.titleBlock : -1
        let lw: (Int) -> CGFloat = { $0 >= 0 ? CGFloat($0) / 100 * mm : o.defaultLw * mm }
        c.saveGState()
        c.clip(to: L.area)
        drawContent(c, k: L.k, ox: L.ox, oy: L.oy, s, hidden, cull: win, owner: tb < 0 ? nil : { $0 != tb }, scaleText: nil,
                    mono: o.monochrome, lw: lw)
        c.restoreGState()
        if tb >= 0 {
            drawContent(c, k: L.ka, ox: L.ax, oy: L.ay, s, hidden, cull: nil, owner: { $0 == tb },
                        scaleText: o.scale > 0 ? "1/\(o.scale)" : nil, mono: o.monochrome, lw: lw)
        }

        // çerçeve (ISO 5457) ve alt bilgi
        let black = CGColor(red: 0, green: 0, blue: 0, alpha: 1)
        let fx0 = frameLeft, fy0 = frameSide, fx1 = L.w - frameSide, fy1 = L.h - frameSide
        c.setStrokeColor(black)
        c.setLineWidth(frameLine)
        c.stroke(CGRect(x: fx0, y: fy0, width: fx1 - fx0, height: fy1 - fy0))
        guard o.footer else { return }
        c.setLineWidth(0.25 * mm)
        c.move(to: CGPoint(x: fx0, y: fy0 + footerH))
        c.addLine(to: CGPoint(x: fx1, y: fy0 + footerH))
        c.strokePath()
        let small = CTFontCreateWithName("Helvetica" as CFString, 7, nil)
        let df = DateFormatter()
        df.locale = Locale(identifier: "tr_TR")
        df.dateFormat = "dd.MM.yyyy"
        let sc = o.scale > 0 ? "Ölçek 1/\(o.scale)" : "Ölçeksiz (yaklaşık 1/\(Int(L.effScale.rounded())))"
        let left = ctLine(title, small, black), right = ctLine("\(sc) · \(df.string(from: Date()))", small, black)
        c.textPosition = CGPoint(x: fx0 + 4, y: fy0 + 4)
        CTLineDraw(left, c)
        c.textPosition = CGPoint(x: fx1 - 4 - CGFloat(CTLineGetTypographicBounds(right, nil, nil, nil)), y: fy0 + 4)
        CTLineDraw(right, c)
    }

    /// Sahneyi x' = ox + x·k, y' = oy + y·k dönüşümüyle çizer. cull: görünmeyen partileri atlamak için dünya penceresi;
    /// owner: ilkelin sahip nesnesine göre süzgeç; scaleText: antetteki "1/50" gibi ölçek yazılarının yerine yazılacak değer;
    /// lw: DXF kalınlığından (1/100 mm, -3 tanımsız) sayfa birimine çizgi kalınlığı.
    private static func drawContent(_ c: CGContext, k: CGFloat, ox: CGFloat, oy: CGFloat, _ s: CadScene, _ hidden: Set<Int>,
                                    cull: CGRect?, owner: ((Int) -> Bool)?, scaleText: String?, mono: Bool,
                                    lw: (Int) -> CGFloat) {
        let v = s.vertices
        c.saveGState()
        c.translateBy(x: ox, y: oy)
        c.scaleBy(x: k, y: k)
        for (i, f) in s.fills.enumerated() where !hidden.contains(f.layer) && (owner?(f.owner) ?? true) {
            c.addPath(s.fillPaths[i])
            c.setFillColor(color(f.rgb, mono, alpha: CGFloat(min(max(f.alpha, 0), 1))))
            c.fillPath(using: .evenOdd)
        }
        c.setLineCap(.round)
        c.setLineJoin(.round)
        for (i, b) in s.batches.enumerated() where !hidden.contains(b.layer) && b.count >= 2 {
            if let w = cull, CGFloat(b.maxX) < w.minX || CGFloat(b.minX) > w.maxX || CGFloat(b.maxY) < w.minY || CGFloat(b.minY) > w.maxY { continue }
            if let own = owner {
                // süzgeçli: yalnız uyan parçalar
                let p = CGMutablePath()
                var j = b.first
                while j + 1 < b.first + b.count {
                    if own(s.owner(ofVertex: j)) {
                        p.move(to: CGPoint(x: CGFloat(v[j * 2]), y: CGFloat(v[j * 2 + 1])))
                        p.addLine(to: CGPoint(x: CGFloat(v[j * 2 + 2]), y: CGFloat(v[j * 2 + 3])))
                    }
                    j += 2
                }
                if p.isEmpty { continue }
                c.addPath(p)
            } else {
                c.addPath(s.batchPaths[i])
            }
            c.setLineWidth(lw(b.lweight) / k)
            c.setStrokeColor(color(b.rgb, mono))
            c.strokePath()
        }
        c.restoreGState()

        // yazılar: sayfa koordinatında; Android/Windows ile aynı dikey hiza
        for t0 in s.texts where !hidden.contains(t0.layer) && (owner?(t0.owner) ?? true) {
            var t = t0
            if let st = scaleText, scaleTextRe.firstMatch(in: t0.text, range: NSRange(t0.text.startIndex..., in: t0.text)) != nil { t.text = st }
            let cap = CGFloat(t.height) * k
            if cap < 0.3 { continue }
            if let w = cull {
                let pad = CGFloat(t.height) * 60
                if CGFloat(t.x) < w.minX - pad || CGFloat(t.x) > w.maxX + pad || CGFloat(t.y) < w.minY - pad || CGFloat(t.y) > w.maxY + pad { continue }
            }
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
                let ct = ctLine(line, f2, color(t.rgb, mono))
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
