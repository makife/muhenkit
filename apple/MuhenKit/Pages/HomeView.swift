import SwiftUI

// ───────────────────────── Ana sayfa: mavi devre kartı ─────────────────────────
// Android Home.kt ile aynı tasarım: MCU (logo + arama) + modül çipleri + bakır yollar + akım darbeleri.

enum Pcb {
    static let board = Color(hex: 0x082152)
    static let board2 = Color(hex: 0x0C2E6E)
    static let maskDot = Color(hex: 0x3D7BD9)
    static let copper = Color(hex: 0xC08A3E)
    static let copperHi = Color(hex: 0xF2B266)
    static let gold = Color(hex: 0xE4C06A)
    static let silk = Color(hex: 0xEFF3FB)
    static let silkDim = Color(hex: 0x9FB6DC)
    static let chip = Color(hex: 0x0E141C)
    static let chip2 = Color(hex: 0x1A222D)
    static let chipEdge = Color(hex: 0x2C3A4E)
    static let led = [Color(hex: 0x3DFF8E), Color(hex: 0xFFB627), Color(hex: 0x5CC8FF), Color(hex: 0xFF6B5E)]
}

extension Color {
    init(hex: UInt32, alpha: Double = 1) {
        self.init(red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255, opacity: alpha)
    }
}

/// MühenKit logosu (uygulama simgesi)
struct AppLogo: View {
    let size: CGFloat
    var body: some View {
        Image("Logo").resizable().interpolation(.high).frame(width: size, height: size)
            .clipShape(RoundedRectangle(cornerRadius: size * 0.24))
    }
}

/// Belirli tohumla aynı diziyi veren basit rastgele sayı üreteci (arka plan yolları her açılışta aynı)
private struct Lcg {
    var s: UInt64
    mutating func next() -> Double {
        s = s &* 6364136223846793005 &+ 1442695040888963407
        return Double(s >> 11) / Double(1 << 53)
    }
}

/// Kartın sabit desenleri: lehim maskesi noktaları, silik bakır yollar, vialar
private struct PcbBoard: View {
    var body: some View {
        Canvas { ctx, size in
            ctx.fill(Path(CGRect(origin: .zero, size: size)),
                     with: .linearGradient(Gradient(colors: [Pcb.board2, Pcb.board]), startPoint: .zero, endPoint: CGPoint(x: 0, y: size.height)))
            let step: CGFloat = 18
            var y = step / 2
            while y < size.height {
                var x = step / 2
                while x < size.width {
                    ctx.fill(Path(ellipseIn: CGRect(x: x - 1.1, y: y - 1.1, width: 2.2, height: 2.2)), with: .color(Pcb.maskDot.opacity(0.16)))
                    x += step
                }
                y += step
            }
            var rnd = Lcg(s: 11)
            for _ in 0..<14 {
                let y0 = rnd.next() * size.height, x0 = rnd.next() * size.width * 0.6
                let len = size.width * (0.2 + rnd.next() * 0.4)
                let bend: CGFloat = rnd.next() < 0.5 ? 14 : -14
                var p = Path()
                p.move(to: CGPoint(x: x0, y: y0))
                p.addLine(to: CGPoint(x: x0 + len * 0.4, y: y0))
                p.addLine(to: CGPoint(x: x0 + len * 0.4 + abs(bend), y: y0 + bend))
                p.addLine(to: CGPoint(x: x0 + len, y: y0 + bend))
                ctx.stroke(p, with: .color(Pcb.copper.opacity(0.10)), lineWidth: 1.6)
                for pt in [CGPoint(x: x0, y: y0), CGPoint(x: x0 + len, y: y0 + bend)] {
                    ctx.fill(Path(ellipseIn: CGRect(x: pt.x - 3.5, y: pt.y - 3.5, width: 7, height: 7)), with: .color(Pcb.copper.opacity(0.18)))
                    ctx.fill(Path(ellipseIn: CGRect(x: pt.x - 1.4, y: pt.y - 1.4, width: 2.8, height: 2.8)), with: .color(Pcb.board))
                }
            }
        }
    }
}

/// MCU'dan her çipe bakır yol (45° kırılımlı)
private func tracePaths(_ mcu: CGRect, _ chips: [CGRect]) -> [[CGPoint]] {
    let n = chips.count
    return chips.enumerated().map { (i, r) -> [CGPoint] in
        let pinX = mcu.minX + 30 + (mcu.width - 60) * (n == 1 ? 0.5 : CGFloat(i) / CGFloat(n - 1))
        let busY = r.minY - 16 - CGFloat(i % 4) * 5
        let firstDrop = mcu.maxY + 18 + CGFloat(i % 6) * 5
        let endX = r.midX + CGFloat((i % 3) - 1) * 10
        let trunkX = r.minX - 13 + CGFloat(i % 3) * 4
        let dx = trunkX - pinX
        let diag = min(abs(dx), 14)
        let sg: CGFloat = dx < 0 ? -1 : 1
        return [
            CGPoint(x: pinX, y: mcu.maxY), CGPoint(x: pinX, y: firstDrop), CGPoint(x: pinX + diag * sg, y: firstDrop + diag),
            CGPoint(x: trunkX - diag * sg, y: firstDrop + diag), CGPoint(x: trunkX, y: firstDrop + diag * 2),
            CGPoint(x: trunkX, y: busY - 8), CGPoint(x: trunkX + 8, y: busY), CGPoint(x: endX - 8, y: busY),
            CGPoint(x: endX, y: busY + 8), CGPoint(x: endX, y: r.minY),
        ]
    }
}

/// Çoklu çizgi üzerinde oransal nokta
private func point(on pts: [CGPoint], at f: CGFloat) -> CGPoint {
    var lens: [CGFloat] = []
    var total: CGFloat = 0
    for i in 1..<pts.count {
        let l = hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y)
        lens.append(l)
        total += l
    }
    var d = total * f
    for i in 1..<pts.count {
        let l = lens[i - 1]
        if d <= l, l > 0 {
            let t = d / l
            return CGPoint(x: pts[i - 1].x + (pts[i].x - pts[i - 1].x) * t, y: pts[i - 1].y + (pts[i].y - pts[i - 1].y) * t)
        }
        d -= l
    }
    return pts.last ?? .zero
}

/// IC bacakları: üst/alt sıra ve yanlar
private func pins(_ ctx: GraphicsContext, _ r: CGRect, top: Int, bottom: Int, sides: Int) {
    let pin = GraphicsContext.Shading.linearGradient(Gradient(colors: [Color(hex: 0xE6E8EA), Color(hex: 0x8C9298)]),
                                                      startPoint: CGPoint(x: 0, y: r.minY), endPoint: CGPoint(x: 0, y: r.maxY))
    let pw: CGFloat = 5, ph: CGFloat = 8
    func row(_ n: Int, _ y: CGFloat) {
        guard n > 0 else { return }
        let span = r.width - 40
        for i in 0..<n {
            let x = r.minX + 20 + span * (CGFloat(i) + 0.5) / CGFloat(n)
            ctx.fill(Path(roundedRect: CGRect(x: x - pw / 2, y: y - ph / 2, width: pw, height: ph), cornerRadius: 1), with: pin)
        }
    }
    row(top, r.minY + 4)
    row(bottom, r.maxY - 4)
    guard sides > 0 else { return }
    let span = r.height - 30
    for i in 0..<sides {
        let y = r.minY + 15 + span * (CGFloat(i) + 0.5) / CGFloat(sides)
        ctx.fill(Path(roundedRect: CGRect(x: r.minX, y: y - pw / 2, width: ph, height: pw), cornerRadius: 1), with: pin)
        ctx.fill(Path(roundedRect: CGRect(x: r.maxX - ph, y: y - pw / 2, width: ph, height: pw), cornerRadius: 1), with: pin)
    }
}

struct HomeView: View {
    @EnvironmentObject private var nav: AppNav
    @State private var query = ""
    @State private var pressed: Int?
    @State private var showAbout = false

    private var list: [RouteInfo] {
        let q = query.trimmingCharacters(in: .whitespaces)
        return Routes.all.filter { q.isEmpty || "\($0.title) \($0.subtitle)".range(of: q, options: .caseInsensitive, locale: Locale(identifier: "tr_TR")) != nil }
    }

    var body: some View {
        GeometryReader { geo in
            let items = list
            let w = geo.size.width
            let cols = w > 1150 ? 4 : w > 820 ? 3 : 2
            let side: CGFloat = 14
            let gap: CGFloat = cols == 2 ? 22 : 34
            let chipW = (min(w, 1240) - side * 2 - gap * CGFloat(cols - 1)) / CGFloat(cols)
            let chipH: CGFloat = cols == 2 ? 128 : 148
            let rowGap: CGFloat = 46
            let left = (w - (chipW * CGFloat(cols) + gap * CGFloat(cols - 1))) / 2
            let mcuW = min(w - 28, 560)
            let mcuTop: CGFloat = 12, mcuH: CGFloat = 168
            let gridTop = mcuTop + mcuH + 64
            let rows = (items.count + cols - 1) / cols
            let totalH = max(gridTop + (chipH + rowGap) * CGFloat(rows) + 60, geo.size.height)
            let mcu = CGRect(x: (w - mcuW) / 2, y: mcuTop, width: mcuW, height: mcuH)
            let rects = items.indices.map { i in
                CGRect(x: left + (chipW + gap) * CGFloat(i % cols), y: gridTop + (chipH + rowGap) * CGFloat(i / cols), width: chipW, height: chipH)
            }
            let paths = tracePaths(mcu, rects)
            ZStack {
                PcbBoard().ignoresSafeArea()
                ScrollView {
                    ZStack(alignment: .topLeading) {
                        // yollar, vialar ve akım darbeleri (4 sn döngü)
                        TimelineView(.animation) { tl in
                            Canvas { ctx, _ in
                                let flow = CGFloat(tl.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 4) / 4)
                                for (i, pts) in paths.enumerated() {
                                    let hot = i == pressed
                                    var p = Path()
                                    p.addLines(pts)
                                    if hot { ctx.stroke(p, with: .color(Pcb.copperHi.opacity(0.22)), style: StrokeStyle(lineWidth: 9, lineCap: .round)) }
                                    ctx.stroke(p, with: .color(hot ? Pcb.copperHi : Pcb.copper.opacity(0.6)),
                                               style: StrokeStyle(lineWidth: hot ? 3.4 : 2.4, lineCap: .round, lineJoin: .round))
                                    let s = pts[0]
                                    ctx.fill(Path(ellipseIn: CGRect(x: s.x - 4.5, y: s.y - 4.5, width: 9, height: 9)), with: .color(Pcb.gold))
                                    ctx.fill(Path(ellipseIn: CGRect(x: s.x - 1.8, y: s.y - 1.8, width: 3.6, height: 3.6)), with: .color(Pcb.board))
                                    let e = pts[pts.count - 1]
                                    ctx.fill(Path(CGRect(x: e.x - 4.5, y: e.y - 2.5, width: 9, height: 5)), with: .color(Pcb.gold))
                                    for k in 0..<2 {
                                        let phase = (flow + CGFloat(i) * 0.137 + CGFloat(k) * 0.5).truncatingRemainder(dividingBy: 1)
                                        let q = point(on: pts, at: phase)
                                        let c = hot ? Color(hex: 0xFFB627) : Pcb.copperHi
                                        let r1: CGFloat = hot ? 9 : 7, r2: CGFloat = hot ? 5 : 4, r3: CGFloat = hot ? 2.6 : 1.9
                                        ctx.fill(Path(ellipseIn: CGRect(x: q.x - r1, y: q.y - r1, width: r1 * 2, height: r1 * 2)), with: .color(c.opacity(0.18)))
                                        ctx.fill(Path(ellipseIn: CGRect(x: q.x - r2, y: q.y - r2, width: r2 * 2, height: r2 * 2)), with: .color(c.opacity(0.35)))
                                        ctx.fill(Path(ellipseIn: CGRect(x: q.x - r3, y: q.y - r3, width: r3 * 2, height: r3 * 2)), with: .color(.white.opacity(0.92)))
                                    }
                                }
                                pins(ctx, mcu, top: 0, bottom: min(items.count, 22), sides: 6)
                                for r in rects { pins(ctx, r, top: 4, bottom: 4, sides: 0) }
                            }
                        }
                        .frame(width: w, height: totalH)
                        .allowsHitTesting(false)

                        McuChip(query: $query) { showAbout = true }
                            .frame(width: mcuW, height: mcuH)
                            .offset(x: mcu.minX, y: mcuTop)

                        ForEach(Array(items.enumerated()), id: \.element.id) { i, r in
                            Button { nav.open(r.id) } label: {
                                ModuleChip(index: Routes.all.firstIndex { $0.id == r.id } ?? i, info: r, lit: pressed == i)
                            }
                            .buttonStyle(ChipPressStyle(index: i, pressed: $pressed))
                            .frame(width: chipW, height: chipH)
                            .offset(x: rects[i].minX, y: rects[i].minY)
                        }

                        if items.isEmpty {
                            Text("EŞLEŞEN MODÜL YOK").font(.system(size: 13)).tracking(2).foregroundStyle(Pcb.silkDim)
                                .frame(width: w).offset(y: gridTop)
                        }
                        Text("MÜHENKİT REV 2.0 · ELEKTRİK · \(items.count) MODÜL").font(.system(size: 10)).tracking(1.5)
                            .foregroundStyle(Pcb.silkDim.opacity(0.7))
                            .frame(width: w).offset(y: totalH - 30)
                    }
                    .frame(width: w, height: totalH, alignment: .topLeading)
                }
            }
        }
        .background(Pcb.board.ignoresSafeArea())
        .sheet(isPresented: $showAbout) { AboutView() }
        #if os(iOS)
        .toolbar(.hidden, for: .navigationBar)
        #endif
    }
}

/// Basılı çip: üst görünüme hangi çipin basılı olduğunu bildirir (yol ve LED yansın)
private struct ChipPressStyle: ButtonStyle {
    let index: Int
    @Binding var pressed: Int?

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
            .onChange(of: configuration.isPressed) { p in
                if p { pressed = index } else if pressed == index { pressed = nil }
            }
    }
}

/// MCU: başlık + arama
private struct McuChip: View {
    @Binding var query: String
    let onAbout: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                AppLogo(size: 34)
                Text("MÜHENKİT").font(.system(size: 22, weight: .heavy)).tracking(4).foregroundStyle(Pcb.silk)
                Spacer()
                Circle().fill(Pcb.led[0]).frame(width: 8, height: 8)
                Text("PWR").font(.system(size: 9)).foregroundStyle(Pcb.silkDim)
                Button(action: onAbout) {
                    Image(systemName: "info.circle").font(.system(size: 17)).foregroundStyle(Pcb.silkDim).frame(width: 30, height: 30)
                }
                .buttonStyle(.plain)
            }
            Text("Elektrik proje ve hesap kiti — bir çipe dokun").font(.system(size: 11)).foregroundStyle(Pcb.silkDim).lineLimit(1)
                .padding(.top, 4)
            Spacer(minLength: 0)
            HStack(spacing: 8) {
                Image(systemName: "magnifyingglass").font(.system(size: 15)).foregroundStyle(Pcb.silkDim)
                ZStack(alignment: .leading) {
                    if query.isEmpty { Text("Modül ara… (ör. topraklama)").font(.system(size: 13)).foregroundStyle(Pcb.silkDim.opacity(0.6)) }
                    TextField("", text: $query).textFieldStyle(.plain).font(.system(size: 13)).foregroundStyle(Pcb.silk)
                }
                if !query.isEmpty {
                    Button { query = "" } label: { Image(systemName: "xmark").font(.system(size: 13)).foregroundStyle(Pcb.silkDim) }.buttonStyle(.plain)
                }
            }
            .padding(.horizontal, 10)
            .frame(height: 40)
            .background(Color(hex: 0x0A1018), in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Pcb.chipEdge, lineWidth: 1))
        }
        .padding(.horizontal, 16).padding(.vertical, 12)
        .background(LinearGradient(colors: [Pcb.chip2, Pcb.chip], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 6))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Pcb.chipEdge, lineWidth: 1.5))
        .shadow(color: .black.opacity(0.5), radius: 10, y: 4)
        .padding(.horizontal, 10).padding(.vertical, 8)
    }
}

private struct ModuleChip: View {
    let index: Int
    let info: RouteInfo
    let lit: Bool

    var body: some View {
        // çizim ve projeler öne çıksın: beyaz gövde, siyah yazı
        let white = info.id == .cad || info.id == .projects
        let bg: [Color] = white ? (lit ? [Color(hex: 0xFFF4E2), Color(hex: 0xE9DCC6)] : [.white, Color(hex: 0xE4EAF3)])
            : (lit ? [Color(hex: 0x243142), Color(hex: 0x151C26)] : [Pcb.chip2, Pcb.chip])
        let titleC = white ? Color(hex: 0x0B0F14) : Pcb.silk
        let subC = white ? Color(hex: 0x2B3542) : Pcb.silkDim
        let iconC = lit ? Pcb.copperHi : white ? Color(hex: 0x0A2C7A) : Pcb.gold
        let led = Pcb.led[index % Pcb.led.count]
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 8) {
                Image(systemName: info.symbol).font(.system(size: 17)).foregroundStyle(iconC)
                Text("U\(index + 1)").font(.system(size: 10)).foregroundStyle(subC)
                Spacer()
                Circle().fill(lit ? led : led.opacity(0.25)).frame(width: 7, height: 7)
            }
            Text(info.title.uppercased(with: Locale(identifier: "tr_TR"))).font(.system(size: 13, weight: .heavy)).tracking(1)
                .foregroundStyle(titleC).lineLimit(1).padding(.top, 6)
            Text(info.subtitle).font(.system(size: 11)).foregroundStyle(subC).lineLimit(2).padding(.top, 3)
            Spacer(minLength: 0)
            Text("MK-\(String(format: "%02d", index + 1))-\(info.id.rawValue.prefix(4).uppercased())").font(.system(size: 9)).tracking(1.5)
                .foregroundStyle(white ? Color(hex: 0x6A7689) : Color(hex: 0x5C6B82))
        }
        .padding(.leading, 12).padding(.trailing, 10).padding(.vertical, 8)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(LinearGradient(colors: bg, startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 5))
        .overlay(RoundedRectangle(cornerRadius: 5).stroke(lit ? Pcb.copperHi.opacity(0.85) : white ? Color(hex: 0xB8C4D6) : Pcb.chipEdge, lineWidth: 1.3))
        .shadow(color: .black.opacity(0.45), radius: 8, y: 3)
        .padding(.vertical, 7).padding(.horizontal, 2)
        .contentShape(Rectangle())
    }
}
