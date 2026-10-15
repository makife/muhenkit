using System.Globalization;

namespace MuhenKit.Core;

public sealed record SymText(uint Rgb, float X, float Y, float H, float Rot, int HAlign, int VAlign, string Text);

/// <summary>
/// Sembol / hazır detay (core/content/symbols.dat; Android Symbols.kt ile aynı biçim).
/// Koordinatlar ekleme noktasına (sembol merkezi) göredir.
/// </summary>
public sealed class CadSymbol(string id, string name, string category, double width, double height, string snippet,
    List<(uint Rgb, float[] Pts)> lines, List<(uint Rgb, float Alpha, float[] Pts)> fills, List<SymText> texts)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Category { get; } = category;
    public double Width { get; } = width;
    public double Height { get; } = height;
    public string Snippet { get; } = snippet;
    public List<(uint Rgb, float[] Pts)> Lines { get; } = lines;
    public List<(uint Rgb, float Alpha, float[] Pts)> Fills { get; } = fills;
    public List<SymText> Texts { get; } = texts;
    public string BlockName => "MK_" + Id;

    /// <summary>Önizleme sınırları (x0, y0, x1, y1)</summary>
    public float[] Bounds => _bounds ??= ComputeBounds();
    private float[]? _bounds;

    private float[] ComputeBounds()
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        void Add(float[] a)
        {
            for (int i = 0; i + 1 < a.Length; i += 2)
            {
                x0 = Math.Min(x0, a[i]); x1 = Math.Max(x1, a[i]); y0 = Math.Min(y0, a[i + 1]); y1 = Math.Max(y1, a[i + 1]);
            }
        }
        foreach (var l in Lines) Add(l.Pts);
        foreach (var f in Fills) Add(f.Pts);
        foreach (var t in Texts) Add([t.X, t.Y, t.X, t.Y + t.H]);
        return x0 > x1 ? [-1, -1, 1, 1] : [x0, y0, x1, y1];
    }

    /// <summary>Yerleştirme önizlemesi: (x1,y1,x2,y2) parçaları</summary>
    public float[] Segments(double x, double y, double s, double rotDeg)
    {
        double r = rotDeg * Math.PI / 180, cs = Math.Cos(r) * s, sn = Math.Sin(r) * s;
        var out_ = new float[Lines.Sum(l => l.Pts.Length)];
        int k = 0;
        foreach (var (_, a) in Lines)
            for (int i = 0; i + 1 < a.Length; i += 2)
            {
                out_[k++] = (float)(x + a[i] * cs - a[i + 1] * sn);
                out_[k++] = (float)(y + a[i] * sn + a[i + 1] * cs);
            }
        return out_;
    }
}

public static class SymbolLibrary
{
    private static List<CadSymbol>? _all;
    private static Dictionary<string, CadSymbol> _byBlock = new();
    /// <summary>Örnek projenin ortanca yazı yüksekliği: hedef çizime ölçek referansı</summary>
    public static double RefTextHeight { get; private set; } = 10;

    public static List<CadSymbol> All => _all ??= Load();
    public static CadSymbol? ForBlock(string name) { _ = All; return _byBlock.GetValueOrDefault(name); }

    private static List<CadSymbol> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "content", "symbols.dat");
        var out_ = new List<CadSymbol>();
        if (!File.Exists(path)) return out_;
        var inv = CultureInfo.InvariantCulture;
        float F(string s) => float.TryParse(s, NumberStyles.Float, inv, out var v) ? v : 0;
        float[] Floats(string[] p, int from) => p.Skip(from).Select(F).ToArray();
        string[]? head = null;
        var lines = new List<(uint, float[])>();
        var fills = new List<(uint, float, float[])>();
        var texts = new List<SymText>();
        string snippet = "";
        foreach (var line in File.ReadLines(path, System.Text.Encoding.UTF8))
        {
            if (line.StartsWith("#ref_h ")) RefTextHeight = double.TryParse(line[7..].Trim(), NumberStyles.Float, inv, out var r) ? r : 10;
            else if (line.StartsWith("@SYM\t"))
            {
                head = line.Split('\t');
                lines = new(); fills = new(); texts = new(); snippet = "";
            }
            else if (line.StartsWith("L\t"))
            {
                var p = line[2..].Split(' ');
                lines.Add((Convert.ToUInt32(p[0], 16), Floats(p, 1)));
            }
            else if (line.StartsWith("F\t"))
            {
                var t = line.Split('\t');
                var p = t[2].Split(' ');
                fills.Add((Convert.ToUInt32(t[1], 16), F(p[0]), Floats(p, 1)));
            }
            else if (line.StartsWith("T\t"))
            {
                var t = line.Split('\t');
                var p = t[2].Split(' ');
                texts.Add(new SymText(Convert.ToUInt32(t[1], 16), F(p[0]), F(p[1]), F(p[2]), F(p[3]), int.Parse(p[4]), int.Parse(p[5]), t.Length > 3 ? t[3] : ""));
            }
            else if (line.StartsWith("S\t")) snippet = line[2..].Replace('\u001f', '\n').Replace('\u001e', '\n');
            else if (line == "@END")
            {
                if (head is { Length: >= 6 })
                    out_.Add(new CadSymbol(head[1], head[2], head[3], double.Parse(head[4], inv), double.Parse(head[5], inv), snippet, lines, fills, texts));
                head = null;
            }
        }
        _byBlock = out_.ToDictionary(s => s.BlockName);
        return out_;
    }
}
