namespace MuhenKit.Core;

/// <summary>Uygulama ekranları (Android Route ile aynı sıra ve metinler)</summary>
public enum RouteId { Cad, Projects, Lighting, Load, Drop, Diversity, Grounding, Lightning, Motor, Cable, Meter, Lux, Materials, Weak, Smm }

public sealed record RouteInfo(RouteId Id, string Title, string Subtitle, string Glyph);

public static class Routes
{
    // Segoe Fluent Icons glifleri
    public static readonly RouteInfo[] All =
    [
        new(RouteId.Cad, "DXF Çizim", "Son projene devam et · sembol, tablo, PDF", ""),
        new(RouteId.Projects, "Projelerim", "Kayıtlı çizimler, yeni proje, DXF aç", ""),
        new(RouteId.Lighting, "Aydınlatma", "Verim metodu ile armatür sayısı ve yerleşim", ""),
        new(RouteId.Load, "Yükleme Cetveli", "Linyeler, faz dağılımı, sigorta/kablo, %e", ""),
        new(RouteId.Drop, "Gerilim Düşümü", "Bir/üç faz %e, kesit önerisi, ısınma", ""),
        new(RouteId.Diversity, "Eşzamanlılık", "Daire %60/%40 ve bina katsayısı", ""),
        new(RouteId.Grounding, "Topraklama", "Şerit, çubuk, temel, gözlü · Wenner", ""),
        new(RouteId.Lightning, "Paratoner", "Risk analizi ve aktif paratoner yarıçapı", ""),
        new(RouteId.Motor, "Motor & TMŞ", "Termik röle, sigorta ve kesit", ""),
        new(RouteId.Cable, "Kablo Seçimi", "NYY tablosuna göre kesit", ""),
        new(RouteId.Meter, "Sayaç Panosu", "Pano ölçüleri ve enerji odası", ""),
        new(RouteId.Lux, "Aydınlık Düzeyleri", "Mekan türüne göre lüks tablosu", ""),
        new(RouteId.Materials, "Malzeme Keşfi", "Sigorta, KAKR, kompakt şalter fiyatları", ""),
        new(RouteId.Weak, "Zayıf Akım / Fiber", "Bina fiber altyapısı ve daire içi dağılım", ""),
        new(RouteId.Smm, "SMM Rehberi", "SMM olma adımları, evraklar, iş kolları", ""),
    ];
}
