using System.Globalization;

namespace MuhenKit.Core;

/// <summary>Hesap sonucu: "anahtar=değer" satırları</summary>
public sealed class CalcResult(Dictionary<string, string> kv)
{
    public string this[string k] => kv.GetValueOrDefault(k, "");
    public double D(string k) => double.TryParse(kv.GetValueOrDefault(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    public int I(string k) => double.IsNaN(D(k)) ? 0 : (int)D(k);
    public bool B(string k) => kv.GetValueOrDefault(k) == "1";
    public string? Error => kv.GetValueOrDefault("error");
}

/// <summary>
/// C++ hesap çekirdeği (core/mkcalc). Android, Windows, iOS ve macOS aynı formülleri çalıştırır
/// (Android Calc.kt ile aynı arayüz).
/// </summary>
public static class Calc
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, List<string[]>> TableCache = new();

    public static CalcResult Run(string module, IEnumerable<KeyValuePair<string, object?>> input)
    {
        var text = string.Join("\n", input.Select(kv => kv.Key + "=" + kv.Value switch
        {
            null => "",
            bool b => b ? "1" : "0",
            double d => double.IsFinite(d) ? d.ToString("G10", CultureInfo.InvariantCulture) : "",
            int i => i.ToString(CultureInfo.InvariantCulture),
            var o => o.ToString()!.Trim().Replace(',', '.'),
        }));
        string outText;
        lock (Lock) outText = Native.Str(Native.mc_calc(Native.Z(module), Native.Z(text)));
        var kv = new Dictionary<string, string>();
        foreach (var line in outText.Split('\n'))
        {
            int i = line.IndexOf('=');
            if (i > 0) kv[line[..i]] = line[(i + 1)..].TrimEnd('\r');
        }
        return new CalcResult(kv);
    }

    /// <summary>Sekme ayrılmış tablo: ilk satır başlıklar</summary>
    public static List<string[]> Table(string name)
    {
        lock (Lock)
        {
            if (!TableCache.TryGetValue(name, out var t))
            {
                t = Native.Str(Native.mc_table(Native.Z(name))).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();
                TableCache[name] = t;
            }
            return t;
        }
    }

    /// <summary>Ad → değer tablosu (soiling, luminaires, conductors, lp_c1…)</summary>
    public static List<(string Name, double Value)> Named(string name) =>
        Table(name).Skip(1).Select(r => (r[0], r.Length > 1 && double.TryParse(r[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)).ToList();

    /// <summary>Türkçe sayı biçimi: ondalık virgül, sondaki sıfırlar atılır</summary>
    public static string Fmt(double v, int digits = 2)
    {
        if (!double.IsFinite(v)) return "—";
        var s = v.ToString("F" + digits, CultureInfo.InvariantCulture);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s.Replace('.', ',');
    }

    /// <summary>Alan metni → sayı (virgül de kabul)</summary>
    public static double? Num(string s) =>
        double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Sayı → alan metni (nokta ondalıklı)</summary>
    public static string Field(double v)
    {
        var s = v.ToString("F6", CultureInfo.InvariantCulture);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s;
    }
}
