using System.Globalization;
using System.Text;

namespace MuhenKit.Core;

/// <summary>
/// Izgara tablo üretici (Android cad/TableBuilder.kt ile aynı): satır/sütun ölçüleri ve hücre yazılarından
/// DXF blok içeriği (LINE + TEXT) ve önizleme parçaları. Birimler çizim birimidir; ekleme noktası sol üst köşe.
/// </summary>
public sealed class TableBuilder(double textHeight)
{
    /// <summary>Align: 0 sol, 1 orta, 2 sağ</summary>
    private sealed record Cell(string Text, int Col, int Row, int ColSpan, int RowSpan, int Align, double Scale, int Color);

    private readonly List<double> _cols = [];
    private readonly List<double> _rows = [];
    private readonly List<Cell> _cells = [];

    public TableBuilder Columns(params double[] widthsInTextHeights)
    {
        foreach (var w in widthsInTextHeights) _cols.Add(w * textHeight);
        return this;
    }

    public int Row(double heightInTextHeights = 2.2)
    {
        _rows.Add(heightInTextHeights * textHeight);
        return _rows.Count - 1;
    }

    public TableBuilder Text(int row, int col, string text, int align = 1, int colSpan = 1, double scale = 1, int color = 7)
    {
        _cells.Add(new Cell(text, col, row, colSpan, 1, align, scale, color));
        return this;
    }

    public double Width => _cols.Sum();
    public double Height => _rows.Sum();

    private double X(int c) => _cols.Take(c).Sum();
    private double Y(int r) => -_rows.Take(r).Sum(); // aşağı doğru

    /// <summary>Birleşik hücrelerin iç çizgileri atlanarak çizilecek kenarlar (x1,y1,x2,y2)</summary>
    private List<double[]> Edges()
    {
        int nR = _rows.Count, nC = _cols.Count;
        var owner = new int[nR, nC];
        for (int r = 0; r < nR; r++) for (int c = 0; c < nC; c++) owner[r, c] = -1;
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            for (int r = c.Row; r < Math.Min(nR, c.Row + c.RowSpan); r++)
                for (int k = c.Col; k < Math.Min(nC, c.Col + c.ColSpan); k++) owner[r, k] = i;
        }
        bool Same(int r1, int c1, int r2, int c2)
        {
            if (r1 < 0 || r1 >= nR || r2 < 0 || r2 >= nR || c1 < 0 || c1 >= nC || c2 < 0 || c2 >= nC) return false;
            int a = owner[r1, c1];
            return a >= 0 && a == owner[r2, c2];
        }
        var out_ = new List<double[]>();
        for (int r = 0; r <= nR; r++)
            for (int c = 0; c < nC; c++)
            {
                if (r >= 1 && r < nR && Same(r - 1, c, r, c)) continue;
                out_.Add([X(c), Y(r), X(c + 1), Y(r)]);
            }
        for (int c = 0; c <= nC; c++)
            for (int r = 0; r < nR; r++)
            {
                if (c >= 1 && c < nC && Same(r, c - 1, r, c)) continue;
                out_.Add([X(c), Y(r), X(c), Y(r + 1)]);
            }
        return out_;
    }

    private static string Num(double v)
    {
        var s = v.ToString("F6", CultureInfo.InvariantCulture);
        return s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
    }

    /// <summary>Yazı hücreye sığmazsa küçültülür (STANDARD/txt.shx karakter genişliği ≈ 0,85·yükseklik)</summary>
    private double FitHeight(Cell c)
    {
        double w = X(c.Col + c.ColSpan) - X(c.Col) - textHeight * 0.6;
        double h = textHeight * c.Scale;
        double est = c.Text.Length * h * 0.85;
        if (est > w && w > 0) h *= w / est;
        return h;
    }

    public CadSymbol Build(string id, string name)
    {
        var sb = new StringBuilder();
        void T(int code, object v) => sb.Append(code).Append('\n').Append(v).Append('\n');
        var segs = new List<float>();
        foreach (var e in Edges())
        {
            T(0, "LINE"); T(100, "AcDbEntity"); T(8, "0"); T(62, 7); T(100, "AcDbLine");
            T(10, Num(e[0])); T(20, Num(e[1])); T(30, 0); T(11, Num(e[2])); T(21, Num(e[3])); T(31, 0);
            foreach (var v in e) segs.Add((float)v);
        }
        var texts = new List<SymText>();
        foreach (var c in _cells)
        {
            if (string.IsNullOrWhiteSpace(c.Text)) continue;
            double x0 = X(c.Col), x1 = X(c.Col + c.ColSpan), yTop = Y(c.Row), yBot = Y(c.Row + c.RowSpan);
            double th = FitHeight(c);
            double ax = c.Align switch { 0 => x0 + textHeight * 0.3, 2 => x1 - textHeight * 0.3, _ => (x0 + x1) / 2 };
            double ay = (yTop + yBot) / 2;
            // 72: yatay hiza, 73=2 dikey orta → hizalama noktası 11/21
            T(0, "TEXT"); T(100, "AcDbEntity"); T(8, "0"); T(62, c.Color); T(100, "AcDbText");
            T(10, Num(ax)); T(20, Num(ay)); T(30, 0); T(40, Num(th)); T(1, c.Text.Replace('\n', ' '));
            T(7, "STANDARD"); T(72, c.Align); T(11, Num(ax)); T(21, Num(ay)); T(31, 0); T(100, "AcDbText"); T(73, 2);
            texts.Add(new SymText(0xFFFFFF, (float)ax, (float)ay, (float)th, 0, c.Align, 2, c.Text));
        }
        return new CadSymbol(id, name, "Tablo", Width, Height, sb.ToString(), [(0xFFFFFFu, segs.ToArray())], [], texts);
    }
}

/// <summary>Proje tabloları: aydınlatma hesabı, toplam güç, firma anteti (Android cad/Tables.kt ile aynı)</summary>
public static class ProjectTables
{
    private static string Stamp() => (DateTime.Now.Ticks / 10_000 % 1_000_000).ToString();

    public static double? D(string? s) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

    /// <summary>Ondalık virgüllü; yalnız ondalık kısmın sondaki sıfırları atılır (9500 → "9500", 1,50 → "1,5")</summary>
    public static string F(double v, int digits = 2)
    {
        var s = v.ToString("F" + digits, CultureInfo.InvariantCulture);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s.Replace('.', ',');
    }

    // ───────────────────────── aydınlatma hesap tablosu ─────────────────────────

    /// <summary>Bir mahal. Boş bırakılan hesaplanabilir alanlar (k, η, Z, kullanılacak) otomatik doldurulur.</summary>
    public sealed class Room
    {
        public string Name = "", A = "", B = "", H = "3", Work = "0.85", Lux = "", Refl = "80-50-30",
            K = "", Eta = "", Power = "", Flux = "", Type = "", ZCalc = "", ZUsed = "";

        public Room Copy() => (Room)MemberwiseClone();
    }

    /// <summary>k = (a·b) / (h·(a+b)), h = H − çalışma düzlemi</summary>
    public static double? RoomIndex(Room r)
    {
        if (D(r.A) is not { } a || D(r.B) is not { } b || D(r.H) is not { } H) return null;
        double h = H - (D(r.Work) ?? 0);
        if (h <= 0 || a + b <= 0) return null;
        return a * b / (h * (a + b));
    }

    /// <summary>Doğrudan ışıklı armatür, 80-50-30 için tipik verim tablosundan doğrusal ara değer</summary>
    private static readonly (double K, double E)[] EtaTable =
        [(0.6, 0.27), (0.8, 0.32), (1.0, 0.36), (1.25, 0.41), (1.5, 0.45), (2.0, 0.51), (2.5, 0.55), (3.0, 0.58), (4.0, 0.62), (5.0, 0.65)];

    public static double SuggestEta(double k)
    {
        if (k <= EtaTable[0].K) return EtaTable[0].E;
        if (k >= EtaTable[^1].K) return EtaTable[^1].E;
        for (int i = 1; i < EtaTable.Length; i++)
        {
            var (k0, e0) = EtaTable[i - 1];
            var (k1, e1) = EtaTable[i];
            if (k <= k1) return e0 + (e1 - e0) * (k - k0) / (k1 - k0);
        }
        return EtaTable[^1].E;
    }

    /// <summary>Z = (E · a · b · 1,25) / (Φ · η) — 1,25 kirlenme (bakım) katsayısı</summary>
    public static double? LampCount(Room r, double? k)
    {
        if (D(r.Lux) is not { } e || D(r.A) is not { } a || D(r.B) is not { } b || D(r.Flux) is not { } phi) return null;
        double? eta = D(r.Eta) ?? (k is { } kk ? SuggestEta(kk) : null);
        if (eta is not { } et || phi <= 0 || et <= 0) return null;
        return e * a * b * 1.25 / (phi * et);
    }

    /// <summary>Formdaki ipuçları için hesaplanan değerler: (k, η, Z, kullanılacak)</summary>
    public static (double? K, double? Eta, double? Z, string Used) Computed(Room r)
    {
        double? k = D(r.K) ?? RoomIndex(r);
        double? eta = D(r.Eta) ?? (k is { } kk ? SuggestEta(kk) : null);
        var rc = r.Copy();
        rc.Eta = eta is { } e ? F(e) : r.Eta;
        double? z = D(r.ZCalc) ?? LampCount(rc, k);
        string used = string.IsNullOrWhiteSpace(r.ZUsed) ? (z is { } zz ? ((int)Math.Ceiling(zz - 1e-9)).ToString() : "") : r.ZUsed;
        return (k, eta, z, used);
    }

    public static List<string> Resolved(Room r)
    {
        var (k, eta, z, used) = Computed(r);
        var hTxt = string.Join(" - ", new[] { r.H, r.Work }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return
        [
            r.Name, r.A, r.B, r.H, hTxt, r.Lux, r.Refl,
            k is { } a ? F(a) : "", eta is { } b ? F(b) : "", r.Power, r.Flux, r.Type, z is { } c ? F(c) : "", used,
        ];
    }

    public static readonly string[] LightRows =
    [
        "MAHAL İSMİ",
        "Mahal eni ( a ) ( m )",
        "Mahal boyu ( b ) ( m )",
        "Mahal yüksekliği ( H ) ( m )",
        "Armatür yüksekliği ( h ) ( m )",
        "İstenilen aydınlık şiddeti ( E ) Lüx",
        "Tavan-duvar-yer yansıtma faktörleri",
        "Mahal endeksi ( k )  k = (a x b) / h x (a + b)",
        "Mahal verimi (cetvelden) ( η )",
        "Seçilen armatür gücü ( W )",
        "Seçilen armatür ışık akısı ( Ø )",
        "Seçilen armatür tipi",
        "Hesaplanan armatür sayısı  Z = (E x a x b x 1,25) / Ø x η",
        "Kullanılacak armatür sayısı",
    ];

    public static CadSymbol LightingTable(double th, IReadOnlyList<Room> rooms, string note)
    {
        var tb = new TableBuilder(th);
        tb.Columns([38.0, .. Enumerable.Repeat(9.0, rooms.Count)]);
        var values = rooms.Select(Resolved).ToList();
        for (int i = 0; i < LightRows.Length; i++)
        {
            int r = tb.Row(i == 0 ? 2.6 : 2.2);
            double sc = i == 0 ? 1.1 : 1.0;
            tb.Text(r, 0, LightRows[i], align: 0, scale: sc);
            for (int c = 0; c < values.Count; c++) tb.Text(r, c + 1, values[c][i], scale: sc);
        }
        if (!string.IsNullOrWhiteSpace(note))
        {
            int r = tb.Row(2.2);
            tb.Text(r, 0, note, align: 0, colSpan: rooms.Count + 1, scale: 0.85);
        }
        return tb.Build("TBL_AYD_" + Stamp(), "Aydınlatma hesap tablosu");
    }

    // ───────────────────────── toplam güç tablosu ─────────────────────────

    public sealed class Load
    {
        public string Name = "", Installed = "", DemandPct = "";
    }

    public static (double Installed, double Demand) PowerSums(IEnumerable<Load> loads)
    {
        double i = 0, d = 0;
        foreach (var l in loads)
        {
            double inst = D(l.Installed) ?? 0;
            i += inst;
            d += inst * (D(l.DemandPct) ?? 100) / 100;
        }
        return (i, d);
    }

    public static CadSymbol PowerTable(double th, string title, IReadOnlyList<Load> loads)
    {
        var tb = new TableBuilder(th);
        tb.Columns(4, 18, 11, 9, 11);
        int r0 = tb.Row(2.8);
        tb.Text(r0, 0, string.IsNullOrWhiteSpace(title) ? "TOPLAM GÜÇ TABLOSU" : title, colSpan: 5, scale: 1.25);
        int r1 = tb.Row(2.6);
        string[] heads = ["No", "Bölüm / Daire", "Kurulu güç (W)", "Eş zam. (%)", "Talep güç (W)"];
        for (int i = 0; i < heads.Length; i++) tb.Text(r1, i, heads[i], scale: 0.9);
        double sumI = 0, sumD = 0;
        int n = 0;
        foreach (var l in loads.Where(l => !string.IsNullOrWhiteSpace(l.Name) || !string.IsNullOrWhiteSpace(l.Installed)))
        {
            double inst = D(l.Installed) ?? 0, pct = D(l.DemandPct) ?? 100, dem = inst * pct / 100;
            sumI += inst;
            sumD += dem;
            int r = tb.Row();
            tb.Text(r, 0, (++n).ToString());
            tb.Text(r, 1, l.Name, align: 0);
            tb.Text(r, 2, F(inst, 0));
            tb.Text(r, 3, F(pct, 0));
            tb.Text(r, 4, F(dem, 0));
        }
        int rt = tb.Row(2.6);
        tb.Text(rt, 0, "TOPLAM", colSpan: 2, scale: 1.1);
        tb.Text(rt, 2, F(sumI, 0), scale: 1.1);
        tb.Text(rt, 3, sumI > 0 ? F(sumD / sumI * 100, 0) : "");
        tb.Text(rt, 4, F(sumD, 0), scale: 1.1);
        int rs = tb.Row(2.4);
        tb.Text(rs, 0, $"TOPLAM KURULU GÜÇ = {F(sumI, 0)} W     TOPLAM TALEP GÜÇ = {F(sumD, 0)} W", colSpan: 5, align: 0);
        return tb.Build("TBL_GUC_" + Stamp(), "Toplam güç tablosu");
    }

    // ───────────────────────── firma anteti ─────────────────────────

    /// <summary>Antet alanları: anahtar, etiket, varsayılan (formdaki sıra)</summary>
    public static readonly (string Key, string Label, string Default)[] TitleFields =
    [
        ("firma", "Firma adı", ""),
        ("firmaAlt", "Firma alt yazısı (ör. elektrik – otomasyon)", ""),
        ("proje", "Proje adı", "ELEKTRİK İÇ TESİSAT PROJESİ"),
        ("sorumlu", "Proje sorumlusu adı soyadı", ""),
        ("smm", "S.M.M. no", ""),
        ("et", "E.T. (oda sicil) no", ""),
        ("yaptiran", "Projeyi yaptıran adı soyadı", ""),
        ("yaptiranAdres", "Projeyi yaptıran adresi", ""),
        ("vergi", "Vergi dairesi / no", ""),
        ("yapiSahibi", "Yapı sahibi", ""),
        ("il", "İl", ""),
        ("ilce", "İlçe", ""),
        ("adres", "Yapı adresi", ""),
        ("pafta", "Pafta", ""),
        ("ada", "Ada", ""),
        ("parsel", "Parsel", ""),
        ("sinif", "Yapı sınıfı", ""),
        ("kat", "Kat sayısı", ""),
        ("alan", "Toplam alan (m²)", ""),
        ("bagimsiz", "Bağımsız bölüm sayısı", ""),
        ("amac", "Kullanım amacı", "KONUT"),
        ("kurulu", "Toplam kurulu güç (W)", ""),
        ("talepFaktor", "Talep faktörü (%)", ""),
        ("talep", "Toplam talep güç (W)", ""),
        ("cizen", "Çizen", ""),
        ("tarih", "Çizim tarihi", ""),
        ("olcek", "Ölçek", "1/50"),
        ("projeNo", "Proje no / pafta", ""),
    ];

    public static CadSymbol TitleBlock(double th, IReadOnlyDictionary<string, string> v)
    {
        string G(string k) => v.TryGetValue(k, out var s) ? s : "";
        var tb = new TableBuilder(th);
        tb.Columns(10, 10, 10, 10, 10, 10);
        void Pair(string label, string value, int c, int span, int row)
        {
            tb.Text(row, c, label, align: 0, scale: 0.75);
            tb.Text(row, c + 1, value, align: 0, colSpan: span);
        }
        int r = tb.Row(5.5);
        tb.Text(r, 0, G("firma"), colSpan: 6, scale: 2.4);
        if (!string.IsNullOrWhiteSpace(G("firmaAlt")))
        {
            r = tb.Row(2.4); tb.Text(r, 0, G("firmaAlt"), colSpan: 6, scale: 1.1);
        }
        r = tb.Row(3.6); tb.Text(r, 0, G("proje"), colSpan: 6, scale: 1.6);
        r = tb.Row(2.2);
        tb.Text(r, 0, "PROJE SORUMLUSU", colSpan: 2, scale: 0.85);
        tb.Text(r, 2, "S.M.M. NO", scale: 0.85); tb.Text(r, 3, "E.T. NO", scale: 0.85); tb.Text(r, 4, "İMZA", colSpan: 2, scale: 0.85);
        r = tb.Row(3.0);
        tb.Text(r, 0, G("sorumlu"), colSpan: 2); tb.Text(r, 2, G("smm")); tb.Text(r, 3, G("et")); tb.Text(r, 4, "", colSpan: 2);
        r = tb.Row(2.2); tb.Text(r, 0, "PROJEYİ YAPTIRAN", colSpan: 6, scale: 0.85);
        r = tb.Row(); Pair("ADI SOYADI", G("yaptiran"), 0, 5, r);
        r = tb.Row(); Pair("ADRESİ", G("yaptiranAdres"), 0, 5, r);
        r = tb.Row(); Pair("VERGİ D./NO", G("vergi"), 0, 2, r); Pair("YAPI SAHİBİ", G("yapiSahibi"), 3, 2, r);
        r = tb.Row(2.2); tb.Text(r, 0, "ARSANIN ÖZELLİKLERİ", colSpan: 6, scale: 0.85);
        r = tb.Row(2.0);
        string[] h1 = ["İL", "İLÇE", "ADRES", "PAFTA", "ADA", "PARSEL"];
        for (int i = 0; i < 6; i++) tb.Text(r, i, h1[i], scale: 0.75);
        r = tb.Row();
        string[] k1 = ["il", "ilce", "adres", "pafta", "ada", "parsel"];
        for (int i = 0; i < 6; i++) tb.Text(r, i, G(k1[i]));
        r = tb.Row(2.2); tb.Text(r, 0, "YAPININ ÖZELLİKLERİ", colSpan: 6, scale: 0.85);
        r = tb.Row(2.0);
        string[] h2 = ["SINIFI", "KAT SAYISI", "TOP. ALAN (m²)", "BAĞ. BÖL. SAY.", "KULLANIM AMACI", "TALEP FAK. (%)"];
        for (int i = 0; i < 6; i++) tb.Text(r, i, h2[i], scale: 0.75);
        r = tb.Row();
        string[] k2 = ["sinif", "kat", "alan", "bagimsiz", "amac", "talepFaktor"];
        for (int i = 0; i < 6; i++) tb.Text(r, i, G(k2[i]));
        r = tb.Row(2.6);
        tb.Text(r, 0, $"TOPLAM KURULU GÜÇ = {G("kurulu")} W", colSpan: 3, align: 0);
        tb.Text(r, 3, $"TOPLAM TALEP GÜÇ = {G("talep")} W", colSpan: 3, align: 0);
        r = tb.Row(2.0);
        string[] h3 = ["ÇİZEN", "", "ÇİZİM TARİHİ", "", "ÖLÇEK", "PROJE NO"];
        for (int i = 0; i < 6; i++) if (h3[i].Length > 0) tb.Text(r, i, h3[i], scale: 0.75);
        r = tb.Row();
        tb.Text(r, 0, G("cizen"), colSpan: 2); tb.Text(r, 2, G("tarih"), colSpan: 2); tb.Text(r, 4, G("olcek")); tb.Text(r, 5, G("projeNo"));
        return tb.Build("TBL_ANTET_" + Stamp(), "Firma anteti");
    }
}
