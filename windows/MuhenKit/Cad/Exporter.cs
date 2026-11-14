using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using MuhenKit.Core;
using Windows.UI;

namespace MuhenKit.Cad;

public enum ExportFormat { Pdf, Png }
public enum PageOrientation { Auto, Landscape, Portrait }

public sealed record Paper(string Name, float W, float H)
{
    public static readonly Paper[] All = [new("A4", 595, 842), new("A3", 842, 1191), new("A2", 1191, 1684), new("A1", 1684, 2384), new("A0", 2384, 3370)];
}

/// <summary>
/// Scale: ölçek paydası (50 → 1/50), 0 = kâğıda sığdır. UnitMm: bir çizim biriminin mm karşılığı (cm çizimde 10).
/// DefaultLw: kalınlığı tanımsız çizgilerin kalınlığı (mm). TitleBlock: firma antetini sağ alt köşeye al. Footer: alt bilgi şeridi.
/// </summary>
public sealed record ExportOptions(ExportFormat Format, Paper Paper, PageOrientation Orientation, bool ViewOnly, bool Monochrome,
    int Scale = 0, double UnitMm = 10, float DefaultLw = 0.18f, bool TitleBlock = true, bool Footer = false);

/// <summary>Sayfa yerleşimi (punto, Y yukarı). K: punto / çizim birimi; Ka: antet için aynısı.</summary>
public sealed record PageLayout(float W, float H, bool Landscape, double K, double Ox, double Oy, (float X0, float Y0, float X1, float Y1) Area,
    bool HasTitle, double Ka, double Ax, double Ay, bool Fits, double EffScale, double NeedWmm, double NeedHmm);

/// <summary>
/// Çizimi beyaz kâğıda PDF (kendi vektör yazıcımız) ya da PNG (Win2D) olarak çıkarır (Android ui/Exporter.kt ile aynı sayfa düzeni).
/// Pafta TS EN ISO 5457: solda 20 mm cilt payı, diğer kenarlar 10 mm, 0,7 mm çerçeve; antet sağ alt köşede en çok 180 mm (ISO 7200).
/// Sayfa birimi punto, Y yukarı. Herhangi bir iş parçacığından çağrılabilir.
/// </summary>
public static class Exporter
{
    private const float Mm = 72f / 25.4f;
    private const float FrameLeft = 20 * Mm, FrameSide = 10 * Mm, FrameLine = 0.7f * Mm, FooterH = 5 * Mm, TitleMaxW = 180 * Mm;
    private const float PngDpi = 150;
    /// <summary>Helvetica / Arial büyük harf yüksekliği / yazı boyutu</summary>
    private const float CapRatio = 0.716f;
    /// <summary>Ölçek paydaları; 0 = kâğıda sığdır</summary>
    public static readonly int[] Scales = [0, 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000];
    private static readonly System.Text.RegularExpressions.Regex ScaleText = new(@"^\s*1\s*[/:]\s*\d+([.,]\d+)?\s*$");

    /// <summary>$INSUNITS → bir birimin mm karşılığı; belirtilmemişse cm (mimari altlıkların çoğu)</summary>
    public static double UnitMm(int insunits) => insunits switch { 1 => 25.4, 2 => 304.8, 4 => 1, 5 => 10, 6 => 1000, _ => 10 };

    /// <summary>Tüm çizimin dünya penceresi (%2 pay); exclude ≥ 0 ise o nesne (ör. sağ alta alınan antet) hariç</summary>
    public static (double X0, double Y0, double X1, double Y1) Extents(CadScene s, int exclude = -1)
    {
        double x0 = s.MinX, y0 = s.MinY, x1 = s.MaxX, y1 = s.MaxY;
        if (exclude >= 0 && Bounds(s, o => o != exclude) is { } b) (x0, y0, x1, y1) = b;
        double px = (x1 - x0) * 0.02, py = (y1 - y0) * 0.02;
        return (x0 - px, y0 - py, x1 + px, y1 + py);
    }

    /// <summary>Sahibi koşula uyan ilkellerin sınırları (yazılar dayanak noktasıyla)</summary>
    public static (double X0, double Y0, double X1, double Y1)? Bounds(CadScene s, Func<int, bool> owner)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void G(float x, float y) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        var v = s.Vertices;
        foreach (var b in s.Batches)
            for (int i = b.First; i + 1 < b.First + b.Count; i += 2)
                if (owner(Own(s, i))) { G(v[i * 2], v[i * 2 + 1]); G(v[i * 2 + 2], v[i * 2 + 3]); }
        foreach (var f in s.Fills)
            if (owner(f.Owner))
                for (int q = 0; q < f.LoopFirst.Length; q++)
                    for (int i = 0; i < f.LoopCount[q]; i++) G(v[(f.LoopFirst[q] + i) * 2], v[(f.LoopFirst[q] + i) * 2 + 1]);
        foreach (var t in s.Texts)
            if (owner(t.Owner)) { G(t.X, t.Y); G(t.X, t.Y + t.Height); }
        return x0 <= x1 ? (x0, y0, x1, y1) : null;
    }

    /// <summary>Köşe indeksinin ait olduğu çizgi parçasının sahibi</summary>
    private static int Own(CadScene s, int vertex) => vertex / 2 < s.SegOwners.Length ? s.SegOwners[vertex / 2] : -1;

    /// <summary>Sağ alta alınacak antetin sınırları; seçenek kapalıysa ya da antet yoksa null</summary>
    public static (double X0, double Y0, double X1, double Y1)? TitleBounds(CadScene s, ExportOptions o) =>
        o.TitleBlock && s.TitleBlock >= 0 ? Bounds(s, w => w == s.TitleBlock) : null;

    public static PageLayout Layout((double X0, double Y0, double X1, double Y1) win, (double X0, double Y0, double X1, double Y1)? title, ExportOptions o) =>
        Layout(win, title, o, o.Paper, o.Orientation);

    private static PageLayout Layout((double X0, double Y0, double X1, double Y1) win, (double X0, double Y0, double X1, double Y1)? title,
        ExportOptions o, Paper paper, PageOrientation orientation)
    {
        double ww = Math.Max(win.X1 - win.X0, 1e-6), wh = Math.Max(win.Y1 - win.Y0, 1e-6);
        bool landscape = orientation switch { PageOrientation.Landscape => true, PageOrientation.Portrait => false, _ => ww >= wh };
        float w = landscape ? paper.H : paper.W, h = landscape ? paper.W : paper.H;
        float fx0 = FrameLeft, fx1 = w - FrameSide, fy1 = h - FrameSide, iy0 = FrameSide + (o.Footer ? FooterH : 0);
        var cands = new List<(float X0, float Y0, float X1, float Y1)>();
        double ka = 0, ax = 0, ay = 0;
        if (title is { } t && t.X1 > t.X0 && t.Y1 > t.Y0)
        {
            double tw = t.X1 - t.X0, th = t.Y1 - t.Y0;
            ka = Math.Min(Math.Min(TitleMaxW, fx1 - fx0) / tw, (fy1 - iy0) * 0.6 / th);
            float pw = (float)(tw * ka), ph = (float)(th * ka);
            ax = fx1 - pw - t.X0 * ka;
            ay = iy0 - t.Y0 * ka;
            cands.Add((fx0, iy0, fx1 - pw, fy1)); // antetin solu (tam yükseklik)
            cands.Add((fx0, iy0 + ph, fx1, fy1)); // antetin üstü (tam genişlik)
        }
        else cands.Add((fx0, iy0, fx1, fy1));

        double Fit((float X0, float Y0, float X1, float Y1) c) => Math.Max(0, Math.Min((c.X1 - c.X0) / ww, (c.Y1 - c.Y0) / wh));
        var best = cands.OrderByDescending(Fit).First();
        double k = Fit(best);
        bool fits = true;
        if (o.Scale > 0)
        {
            k = o.UnitMm / o.Scale * Mm;
            var fitting = cands.Where(c => Fit(c) >= k * 0.999).ToList();
            fits = fitting.Count > 0;
            if (fits) best = fitting.OrderByDescending(Fit).First();
        }
        double ox = (best.X0 + best.X1) / 2 - (win.X0 + ww / 2) * k, oy = (best.Y0 + best.Y1) / 2 - (win.Y0 + wh / 2) * k;
        return new PageLayout(w, h, landscape, k, ox, oy, best, ka > 0, ka, ax, ay, fits, o.UnitMm * Mm / k, ww * k / Mm, wh * k / Mm);
    }

    /// <summary>Seçilen ölçekte çizimin sığdığı en küçük kâğıt (yön sabitse yalnız o yön)</summary>
    public static (Paper Paper, PageOrientation Orientation)? SuggestPaper((double X0, double Y0, double X1, double Y1) win,
        (double X0, double Y0, double X1, double Y1)? title, ExportOptions o)
    {
        PageOrientation[] dirs = o.Orientation == PageOrientation.Auto ? [PageOrientation.Landscape, PageOrientation.Portrait] : [o.Orientation];
        foreach (var p in Paper.All)
            foreach (var d in dirs)
                if (Layout(win, title, o, p, d).Fits) return (p, d);
        return null;
    }

    public static byte[] Render(CadScene s, ICollection<int> hidden, (double X0, double Y0, double X1, double Y1) win, ExportOptions o, string title)
    {
        var L = Layout(win, TitleBounds(s, o), o);
        if (o.Format == ExportFormat.Pdf)
        {
            var pdf = new PdfSink(L.W, L.H);
            DrawPage(pdf, L, s, hidden, win, o, title);
            return pdf.Finish();
        }
        float k = PngDpi / 72f;
        // çok büyük kâğıtta bellek sınırı: uzun kenar en çok 9000 piksel
        k = Math.Min(k, 9000 / Math.Max(L.W, L.H));
        using var png = new PngSink(L.W, L.H, k);
        DrawPage(png, L, s, hidden, win, o, title);
        return png.Finish();
    }

    // ───────────────────────── sayfa ─────────────────────────

    private static void DrawPage(ISink c, PageLayout L, CadScene s, ICollection<int> hidden, (double X0, double Y0, double X1, double Y1) win,
        ExportOptions o, string title)
    {
        int tb = L.HasTitle ? s.TitleBlock : -1;
        var a = L.Area;
        c.PushClip(a.X0, a.Y0, a.X1, a.Y1);
        DrawScene(c, s, hidden, o, L.K, L.Ox, L.Oy, win, tb < 0 ? null : w => w != tb, null);
        c.PopClip();
        if (tb >= 0)
            DrawScene(c, s, hidden, o, L.Ka, L.Ax, L.Ay, null, w => w == tb, o.Scale > 0 ? $"1/{o.Scale}" : null);

        // çerçeve (ISO 5457) ve alt bilgi
        float fx0 = FrameLeft, fy0 = FrameSide, fx1 = L.W - FrameSide, fy1 = L.H - FrameSide;
        c.Lines([fx0, fy0, fx1, fy0, fx1, fy0, fx1, fy1, fx1, fy1, fx0, fy1, fx0, fy1, fx0, fy0], 0, FrameLine);
        if (o.Footer)
        {
            c.Lines([fx0, fy0 + FooterH, fx1, fy0 + FooterH], 0, 0.25f * Mm);
            float fs = 7;
            c.Text(title, fx0 + 4, fy0 + 4, 0, 0, fs, 1, 0, 0);
            var right = (o.Scale > 0 ? $"Ölçek 1/{o.Scale}" : $"Ölçeksiz (yaklaşık 1/{Math.Round(L.EffScale)})") + " · " +
                        DateTime.Now.ToString("dd.MM.yyyy", new CultureInfo("tr-TR"));
            c.Text(right, fx1 - 4, fy0 + 4, -TextMetrics.Width(right, fs), 0, fs, 1, 0, 0);
        }
    }

    /// <summary>Sahneyi (x·k + ox, y·k + oy) dönüşümüyle çizer. cull: görünmeyen partileri atlamak için dünya penceresi;
    /// owner: ilkelin sahip nesnesine göre süzgeç; scale: antetteki "1/50" gibi ölçek yazılarının yerine yazılacak değer.</summary>
    private static void DrawScene(ISink c, CadScene s, ICollection<int> hidden, ExportOptions o, double k, double ox, double oy,
        (double X0, double Y0, double X1, double Y1)? cull, Func<int, bool>? owner, string? scale)
    {
        uint Col(uint rgb)
        {
            if (o.Monochrome) return 0;
            rgb &= 0xFFFFFF;
            uint r = rgb >> 16 & 0xFF, g = rgb >> 8 & 0xFF, b = rgb & 0xFF;
            // beyaz kâğıtta beyaz/çok açık renkler görünmez: siyah (AutoCAD çizim davranışı)
            return r * 299 + g * 587 + b * 114 > 200_000 ? 0 : rgb;
        }
        float X(float x) => (float)(ox + x * k);
        float Y(float y) => (float)(oy + y * k);
        var v = s.Vertices;

        foreach (var f in s.Fills)
        {
            if (hidden.Contains(f.Layer) || (owner != null && !owner(f.Owner))) continue;
            var loops = new List<float[]>();
            for (int q = 0; q < f.LoopFirst.Length; q++)
            {
                int a = f.LoopFirst[q], n = f.LoopCount[q];
                var pts = new float[n * 2];
                for (int i = 0; i < n; i++) { pts[i * 2] = X(v[(a + i) * 2]); pts[i * 2 + 1] = Y(v[(a + i) * 2 + 1]); }
                loops.Add(pts);
            }
            c.Fill(loops, Col(f.Rgb), Math.Clamp(f.Alpha, 0, 1));
        }
        // görünmeyen partileri atla; kalınlık: DXF'teki (1/100 mm) ya da tanımsızsa varsayılan
        foreach (var b in s.Batches)
        {
            if (hidden.Contains(b.Layer) || b.Count < 2) continue;
            if (cull is { } w && (b.MaxX < w.X0 || b.MinX > w.X1 || b.MaxY < w.Y0 || b.MinY > w.Y1)) continue;
            var seg = new List<float>(b.Count * 2);
            for (int i = b.First; i + 1 < b.First + b.Count; i += 2)
            {
                if (owner != null && !owner(Own(s, i))) continue;
                seg.Add(X(v[i * 2])); seg.Add(Y(v[i * 2 + 1])); seg.Add(X(v[i * 2 + 2])); seg.Add(Y(v[i * 2 + 3]));
            }
            if (seg.Count > 0) c.Lines([.. seg], Col(b.Rgb), (b.LWeight >= 0 ? b.LWeight / 100f : o.DefaultLw) * Mm);
        }
        foreach (var t in s.Texts)
        {
            if (hidden.Contains(t.Layer) || (owner != null && !owner(t.Owner))) continue;
            float cap = (float)(t.Height * k);
            if (cap < 0.3f) continue;
            if (cull is { } w && (t.X < w.X0 - t.Height * 60 || t.X > w.X1 + t.Height * 60 || t.Y < w.Y0 - t.Height * 60 || t.Y > w.Y1 + t.Height * 60)) continue;
            var tt = scale != null && ScaleText.IsMatch(t.Text) ? t with { Text = scale } : t;
            float size = cap / CapRatio, wf = t.WidthFactor > 0 ? t.WidthFactor : 1;
            var lines = Wrap(tt, size, wf, k);
            int n = lines.Count;
            float step = cap * 1.667f;
            for (int i = 0; i < n; i++)
            {
                // taban çizgisi (aşağı +): Android DrawingPainter ile aynı dikey hiza
                float by = t.VAlign switch
                {
                    3 => cap + i * step,
                    2 => -((cap + (n - 1) * step) / 2 - cap) + i * step,
                    1 => -(n - 1 - i) * step,
                    _ => i * step,
                };
                float lw = TextMetrics.Width(lines[i], size) * wf;
                float ax = t.HAlign switch { 1 => -lw / 2, 2 => -lw, _ => 0 };
                c.Text(lines[i], X(t.X), Y(t.Y), ax, -by, size, wf, t.Rotation, Col(t.Rgb));
            }
        }
    }

    /// <summary>MTEXT kutu genişliğine göre kelime kaydırma</summary>
    private static List<string> Wrap(CadText t, float size, float wf, double k)
    {
        var raw = t.Text.Split('\n');
        if (t.BoxWidth <= 0) return [.. raw];
        double max = t.BoxWidth * k * 1.05;
        var out_ = new List<string>();
        foreach (var para in raw)
        {
            var sb = new StringBuilder();
            foreach (var word in para.Split(' '))
            {
                var cand = sb.Length == 0 ? word : sb + " " + word;
                if (sb.Length > 0 && TextMetrics.Width(cand, size) * wf > max)
                {
                    out_.Add(sb.ToString());
                    sb.Clear().Append(word);
                }
                else sb.Clear().Append(cand);
            }
            out_.Add(sb.ToString());
        }
        return out_;
    }

    // ───────────────────────── çizim hedefleri ─────────────────────────

    private interface ISink
    {
        void PushClip(float x0, float y0, float x1, float y1);
        void PopClip();
        /// <summary>(x1,y1,x2,y2) parçaları</summary>
        void Lines(float[] seg, uint rgb, float width);
        void Fill(List<float[]> loops, uint rgb, float alpha);
        /// <summary>Yazı: (x,y) dayanak noktası; (dx,dy) yazı ekseninde taban çizgisi başlangıcı</summary>
        void Text(string s, float x, float y, float dx, float dy, float size, float wf, float rotDeg, uint rgb);
    }

    /// <summary>Yazı genişliği: Arial (Helvetica ile aynı ölçüler), 1000 birimlik referansla</summary>
    private static class TextMetrics
    {
        private static readonly Dictionary<string, float> Cache = new();
        private static CanvasTextFormat? _fmt;

        public static float Width(string s, float size)
        {
            s = PdfSink.Printable(s);
            lock (Cache)
            {
                if (!Cache.TryGetValue(s, out var w))
                {
                    _fmt ??= new CanvasTextFormat { FontFamily = "Arial", FontSize = 1000, WordWrapping = CanvasWordWrapping.NoWrap };
                    using var l = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), s, _fmt, 1e7f, 2000);
                    w = (float)l.LayoutBoundsIncludingTrailingWhitespace.Width;
                    if (Cache.Count > 50_000) Cache.Clear();
                    Cache[s] = w;
                }
                return w * size / 1000;
            }
        }
    }

    /// <summary>Tek sayfalık vektör PDF. Yazılar Helvetica (gömülmez); Türkçe harfler WinAnsi + Differences ile.</summary>
    private sealed class PdfSink(float w, float h) : ISink
    {
        private readonly StringBuilder _c = new();
        private readonly List<float> _alphas = [];
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string N(float v)
        {
            var s = v.ToString("F2", Inv);
            return s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
        }

        private static string Rgb(uint c) => $"{N((c >> 16 & 0xFF) / 255f)} {N((c >> 8 & 0xFF) / 255f)} {N((c & 0xFF) / 255f)}";

        public void PushClip(float x0, float y0, float x1, float y1) => _c.Append($"q {N(x0)} {N(y0)} {N(x1 - x0)} {N(y1 - y0)} re W n\n");
        public void PopClip() => _c.Append("Q\n");

        public void Lines(float[] seg, uint rgb, float width)
        {
            _c.Append(Rgb(rgb)).Append(" RG ").Append(N(width)).Append(" w 1 J 1 j\n");
            float lx = float.NaN, ly = float.NaN;
            int n = 0;
            for (int i = 0; i + 3 < seg.Length; i += 4)
            {
                // birbirine bağlı parçaları tek yol olarak sürdür (dosya küçülür). NaN karşılaştırması false döner:
                // ilk parçada da "m" yazılsın diye koşul tersten kurulu (yoksa her grubun ilk çizgisi kayboluyordu)
                if (!(Math.Abs(seg[i] - lx) <= 0.005f && Math.Abs(seg[i + 1] - ly) <= 0.005f))
                    _c.Append(N(seg[i])).Append(' ').Append(N(seg[i + 1])).Append(" m ");
                _c.Append(N(seg[i + 2])).Append(' ').Append(N(seg[i + 3])).Append(" l\n");
                lx = seg[i + 2]; ly = seg[i + 3];
                if (++n % 2000 == 0) { _c.Append("S\n"); lx = float.NaN; }
            }
            _c.Append("S\n");
        }

        public void Fill(List<float[]> loops, uint rgb, float alpha)
        {
            int gi = _alphas.FindIndex(a => Math.Abs(a - alpha) < 0.01f);
            if (gi < 0) { _alphas.Add(alpha); gi = _alphas.Count - 1; }
            _c.Append($"q /GS{gi} gs {Rgb(rgb)} rg\n");
            foreach (var p in loops)
            {
                if (p.Length < 6) continue;
                _c.Append(N(p[0])).Append(' ').Append(N(p[1])).Append(" m ");
                for (int i = 2; i + 1 < p.Length; i += 2) _c.Append(N(p[i])).Append(' ').Append(N(p[i + 1])).Append(" l ");
                _c.Append("h\n");
            }
            _c.Append("f* Q\n");
        }

        public void Text(string s, float x, float y, float dx, float dy, float size, float wf, float rotDeg, uint rgb)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            double r = rotDeg * Math.PI / 180;
            float cs = (float)Math.Cos(r), sn = (float)Math.Sin(r);
            float px = x + dx * cs - dy * sn, py = y + dx * sn + dy * cs;
            _c.Append($"BT /F1 {N(size)} Tf {N(wf * 100)} Tz {Rgb(rgb)} rg {cs.ToString("F4", Inv)} {sn.ToString("F4", Inv)} {(-sn).ToString("F4", Inv)} {cs.ToString("F4", Inv)} {N(px)} {N(py)} Tm (");
            foreach (var b in Encode(s))
            {
                if (b is (byte)'(' or (byte)')' or (byte)'\\') _c.Append('\\').Append((char)b);
                else if (b < 32 || b > 126) _c.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                else _c.Append((char)b);
            }
            _c.Append(") Tj ET\n");
        }

        // WinAnsi'de olmayan Türkçe harfler, ISO-8859-9'daki yerlerine (Ð Ý Þ ð ý þ) Differences ile konur
        private static readonly Dictionary<char, byte> Special = new()
        {
            ['Ğ'] = 0xD0, ['İ'] = 0xDD, ['Ş'] = 0xDE, ['ğ'] = 0xF0, ['ı'] = 0xFD, ['ş'] = 0xFE,
            ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['‰'] = 0x89, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['‘'] = 0x91, ['’'] = 0x92,
            ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['™'] = 0x99, ['›'] = 0x9B, ['œ'] = 0x9C,
        };
        private static readonly Dictionary<char, string> Translit = new()
        {
            ['η'] = "n", ['φ'] = "f", ['Φ'] = "Ø", ['∅'] = "Ø", ['Ω'] = "Ohm", ['μ'] = "µ", ['Δ'] = "D", ['π'] = "pi", ['≤'] = "<=", ['≥'] = ">=",
            ['√'] = "v", ['α'] = "a", ['β'] = "b", ['λ'] = "l", ['ρ'] = "p", ['σ'] = "s", ['θ'] = "0", ['×'] = "x", ['⌀'] = "Ø",
        };

        /// <summary>Yazının PDF'te basılabilir hali (ölçüm de bununla yapılır)</summary>
        public static string Printable(string s)
        {
            if (s.All(ch => ch < 0x80)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch < 0x80 || Special.ContainsKey(ch) || (ch >= 0xA0 && ch <= 0xFF && ch is not ('Ð' or 'Ý' or 'Þ' or 'ð' or 'ý' or 'þ'))) sb.Append(ch);
                else if (Translit.TryGetValue(ch, out var t)) sb.Append(t);
                else sb.Append('?');
            }
            return sb.ToString();
        }

        private static IEnumerable<byte> Encode(string s)
        {
            foreach (var ch in Printable(s))
                yield return ch < 0x100 && !Special.ContainsKey(ch) ? (byte)ch : Special.TryGetValue(ch, out var b) ? b : (byte)'?';
        }

        public byte[] Finish()
        {
            byte[] content;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
                {
                    var raw = Encoding.ASCII.GetBytes(_c.ToString());
                    z.Write(raw, 0, raw.Length);
                }
                content = ms.ToArray();
            }
            var gs = new StringBuilder();
            for (int i = 0; i < _alphas.Count; i++) gs.Append($"/GS{i} << /Type /ExtGState /ca {N(_alphas[i])} /CA {N(_alphas[i])} >> ");
            var objs = new List<byte[]>
            {
                A("<< /Type /Catalog /Pages 2 0 R >>"),
                A("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                A($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(w)} {N(h)}] /Resources << /Font << /F1 5 0 R >> /ExtGState << {gs}>> >> /Contents 4 0 R >>"),
                A($"<< /Length {content.Length} /Filter /FlateDecode >>\nstream\n").Concat(content).Concat(A("\nendstream")).ToArray(),
                A("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding " +
                  "/Differences [208 /Gbreve 221 /Idotaccent 222 /Scedilla 240 /gbreve 253 /dotlessi 254 /scedilla] >> >>"),
                A($"<< /Producer (MuhenKit) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>"),
            };
            using var o = new MemoryStream();
            void W(byte[] b) => o.Write(b, 0, b.Length);
            W(A("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
            var offs = new List<long>();
            for (int i = 0; i < objs.Count; i++)
            {
                offs.Add(o.Position);
                W(A($"{i + 1} 0 obj\n"));
                W(objs[i]);
                W(A("\nendobj\n"));
            }
            long xref = o.Position;
            var x = new StringBuilder($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
            foreach (var off in offs) x.Append(off.ToString("D10")).Append(" 00000 n \n");
            x.Append($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R /Info {objs.Count} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            W(A(x.ToString()));
            return o.ToArray();
        }

        private static byte[] A(string s) => Encoding.Latin1.GetBytes(s);
    }

    /// <summary>PNG: aynı sayfa Win2D ile (punto → piksel, Y aşağı)</summary>
    private sealed class PngSink : ISink, IDisposable
    {
        private readonly CanvasRenderTarget _rt;
        private readonly CanvasDrawingSession _ds;
        private readonly float _h, _k;
        private readonly Stack<CanvasActiveLayer> _clips = new();

        public PngSink(float w, float h, float k)
        {
            _h = h;
            _k = k;
            _rt = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), (int)Math.Round(w * k), (int)Math.Round(h * k), 96);
            _ds = _rt.CreateDrawingSession();
            _ds.Clear(Color.FromArgb(255, 255, 255, 255));
            _ds.Antialiasing = CanvasAntialiasing.Antialiased;
            _ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
        }

        private Vector2 P(float x, float y) => new(x * _k, (_h - y) * _k);
        private static Color C(uint c, float a = 1) => Color.FromArgb((byte)(a * 255), (byte)(c >> 16), (byte)(c >> 8), (byte)c);

        public void PushClip(float x0, float y0, float x1, float y1)
        {
            var a = P(x0, y1);
            _clips.Push(_ds.CreateLayer(1, new Windows.Foundation.Rect(a.X, a.Y, (x1 - x0) * _k, (y1 - y0) * _k)));
        }

        public void PopClip() => _clips.Pop().Dispose();

        public void Lines(float[] seg, uint rgb, float width)
        {
            using var pb = new CanvasPathBuilder(_ds);
            for (int i = 0; i + 3 < seg.Length; i += 4)
            {
                pb.BeginFigure(P(seg[i], seg[i + 1]));
                pb.AddLine(P(seg[i + 2], seg[i + 3]));
                pb.EndFigure(CanvasFigureLoop.Open);
            }
            using var g = CanvasGeometry.CreatePath(pb);
            using var st = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            _ds.DrawGeometry(g, C(rgb), Math.Max(1f, width * _k), st);
        }

        public void Fill(List<float[]> loops, uint rgb, float alpha)
        {
            using var pb = new CanvasPathBuilder(_ds);
            pb.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Alternate);
            foreach (var p in loops)
            {
                if (p.Length < 6) continue;
                pb.BeginFigure(P(p[0], p[1]));
                for (int i = 2; i + 1 < p.Length; i += 2) pb.AddLine(P(p[i], p[i + 1]));
                pb.EndFigure(CanvasFigureLoop.Closed);
            }
            using var g = CanvasGeometry.CreatePath(pb);
            _ds.FillGeometry(g, C(rgb, alpha));
        }

        public void Text(string s, float x, float y, float dx, float dy, float size, float wf, float rotDeg, uint rgb)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            float px = size * _k;
            if (px < 1) return;
            using var fmt = new CanvasTextFormat { FontFamily = "Arial", FontSize = px, WordWrapping = CanvasWordWrapping.NoWrap };
            using var l = new CanvasTextLayout(_ds, s, fmt, 1e6f, px * 3);
            float baseline = l.LineMetrics.Length > 0 ? l.LineMetrics[0].Baseline : px * 0.9f;
            // yerel (Y aşağı) → döndür → dayanak noktası
            _ds.Transform = Matrix3x2.CreateTranslation(0, -baseline) * Matrix3x2.CreateScale(wf, 1)
                * Matrix3x2.CreateTranslation(dx * _k, -dy * _k)
                * Matrix3x2.CreateRotation((float)(-rotDeg * Math.PI / 180)) * Matrix3x2.CreateTranslation(P(x, y));
            _ds.DrawTextLayout(l, 0, 0, C(rgb));
            _ds.Transform = Matrix3x2.Identity;
        }

        public byte[] Finish()
        {
            _ds.Dispose();
            using var ms = new MemoryStream();
            _rt.SaveAsync(ms.AsRandomAccessStream(), CanvasBitmapFileFormat.Png).AsTask().GetAwaiter().GetResult();
            return ms.ToArray();
        }

        public void Dispose()
        {
            _rt.Dispose();
        }
    }
}
