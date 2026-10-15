import Foundation

/// Uygulama ekranları (Android Route / Windows RouteId ile aynı sıra ve metinler)
enum RouteId: String, CaseIterable, Hashable {
    case cad, projects, lighting, load, drop, diversity, grounding, lightning, motor, cable, meter, lux, materials, weak, smm
}

struct RouteInfo {
    let id: RouteId
    let title: String
    let subtitle: String
    /// SF Symbols adı
    let symbol: String
}

enum Routes {
    static let all: [RouteInfo] = [
        RouteInfo(id: .cad, title: "DXF Çizim", subtitle: "Son projene devam et · sembol, tablo, PDF", symbol: "pencil.and.ruler"),
        RouteInfo(id: .projects, title: "Projelerim", subtitle: "Kayıtlı çizimler, yeni proje, DXF aç", symbol: "folder"),
        RouteInfo(id: .lighting, title: "Aydınlatma", subtitle: "Verim metodu ile armatür sayısı ve yerleşim", symbol: "lightbulb"),
        RouteInfo(id: .load, title: "Yükleme Cetveli", subtitle: "Linyeler, faz dağılımı, sigorta/kablo, %e", symbol: "list.bullet.rectangle"),
        RouteInfo(id: .drop, title: "Gerilim Düşümü", subtitle: "Bir/üç faz %e, kesit önerisi, ısınma", symbol: "chart.line.downtrend.xyaxis"),
        RouteInfo(id: .diversity, title: "Eşzamanlılık", subtitle: "Daire %60/%40 ve bina katsayısı", symbol: "building.2"),
        RouteInfo(id: .grounding, title: "Topraklama", subtitle: "Şerit, çubuk, temel, gözlü · Wenner", symbol: "arrow.down.to.line"),
        RouteInfo(id: .lightning, title: "Paratoner", subtitle: "Risk analizi ve aktif paratoner yarıçapı", symbol: "cloud.bolt"),
        RouteInfo(id: .motor, title: "Motor & TMŞ", subtitle: "Termik röle, sigorta ve kesit", symbol: "gearshape.2"),
        RouteInfo(id: .cable, title: "Kablo Seçimi", subtitle: "NYY tablosuna göre kesit", symbol: "cable.connector"),
        RouteInfo(id: .meter, title: "Sayaç Panosu", subtitle: "Pano ölçüleri ve enerji odası", symbol: "gauge"),
        RouteInfo(id: .lux, title: "Aydınlık Düzeyleri", subtitle: "Mekan türüne göre lüks tablosu", symbol: "sun.max"),
        RouteInfo(id: .materials, title: "Malzeme Keşfi", subtitle: "Sigorta, KAKR, kompakt şalter fiyatları", symbol: "cart"),
        RouteInfo(id: .weak, title: "Zayıf Akım / Fiber", subtitle: "Bina fiber altyapısı ve daire içi dağılım", symbol: "network"),
        RouteInfo(id: .smm, title: "SMM Rehberi", subtitle: "SMM olma adımları, evraklar, iş kolları", symbol: "graduationcap"),
    ]

    static func info(_ id: RouteId) -> RouteInfo { all.first { $0.id == id }! }
}

/// Uygulama içi paylaşılan içerik (core/content): rehberler, semboller, görseller
enum Content {
    static var root: URL { Bundle.main.resourceURL!.appendingPathComponent("content") }
    static func url(_ rel: String) -> URL { root.appendingPathComponent(rel) }
}
