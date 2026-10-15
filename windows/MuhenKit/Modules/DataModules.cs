using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MuhenKit.Core;
using static MuhenKit.Core.Calc;
using static MuhenKit.Modules.ModuleUi;

namespace MuhenKit.Modules;

/// <summary>Tablo ve rehber modülleri (Android modules/DataModules.kt ile aynı içerik)</summary>
public static class DataModules
{
    // ───────────────────────── Sayaç panosu ─────────────────────────

    public static void MeterPanel(ModuleUi ui)
    {
        ui.Subtitle = "Dolap tipi sayaç panosu ve enerji odası asgari ölçüleri (Çizelge 1)";
        var inp = ui.Card("Girdi");
        inp.Children.Add(ui.Num("Gereken sayaç adedi", "14", null, out var need, _ => Reset()));
        inp.Children.Add(ui.Choice(null, [false, true], false, t => t ? "Üç fazlı sayaç" : "Tek fazlı sayaç", out var three, _ => Reset()));

        var optCard = ui.Card("Seçenekler", right: true);
        var optPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        optCard.Children.Add(new ScrollViewer { Content = optPanel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Disabled });
        var none = Note("Bu adet için tablo dışında: birden fazla pano gerekir.");
        optCard.Children.Add(none);

        var res = ui.Card("Pano ve enerji odası", right: true);
        var r1 = ui.Result(res, "Bir kattaki sayaç");
        var r2 = ui.Result(res, "Kat sayısı");
        var r3 = ui.Result(res, "Toplam sayaç kapasitesi", big: true);
        var r4 = ui.Result(res, "Pano eni", big: true);
        var r5 = ui.Result(res, "Pano yüksekliği");
        var r6 = ui.Result(res, "Enerji odası en az eni (Şekil A)");
        var r7 = ui.Result(res, "Enerji odası en az derinliği (Şekil B)");
        var r8 = ui.Result(res, "Enerji odası yüksekliği");

        int choice = 0;
        string optKey = "";
        void Reset() => choice = 0;

        ui.Recalc = () =>
        {
            var r = Run("meter_panel", new Dictionary<string, object?> { ["need"] = need(), ["three"] = three() });
            int n = r.I("options");
            int sel = Math.Clamp(choice, 0, Math.Max(0, n - 1));
            var key = $"{n}|{need()}|{three()}|{sel}";
            if (key != optKey)
            {
                optKey = key;
                optPanel.Children.Clear();
                for (int i = 0; i < n; i++)
                {
                    int k = i;
                    var b = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
                    {
                        Content = $"{r.I($"option.{i}.floors")} katlı · {r[$"option.{i}.per_row"]}/kat", IsChecked = i == sel, Padding = new Thickness(12, 5, 12, 6),
                    };
                    b.Click += (_, _) => { choice = k; ui.Changed(); };
                    optPanel.Children.Add(b);
                }
            }
            none.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
            Outer(res).Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (n == 0) return;
            var p = $"option.{sel}.";
            r1.Set(r[p + "per_row"], "adet");
            r2.Set(r[p + "floors"]);
            r3.Set(r[p + "total"], "adet");
            r4.Set(Fmt(r.D(p + "width"), 1), "cm");
            r5.Set(Fmt(r.D(p + "h"), 0), "cm");
            r6.Set(Fmt(r.D(p + "room_a"), 1), "cm");
            r7.Set(Fmt(r.D(p + "room_b"), 1), "cm");
            r8.Set(Fmt(r.D(p + "room_h"), 0), "cm");
        };
    }

    // ───────────────────────── Aydınlık düzeyleri ─────────────────────────

    public static void Lux(ModuleUi ui)
    {
        ui.Subtitle = "Mekan türüne göre en az aydınlık düzeyi (genel / özel lüks)";
        var rows = Table("lux").Skip(1).ToList();
        var search = new TextBox { PlaceholderText = "Mekan ara (ör. mutfak, sınıf)", Header = "Ara" };
        var top = ui.Card("Aydınlık düzeyleri");
        top.Children.Add(search);
        var list = new StackPanel { Spacing = 14 };
        ui.Left.Children.Add(list);
        void Fill()
        {
            var q = search.Text.Trim();
            list.Children.Clear();
            var tr = new CultureInfo("tr-TR");
            var filtered = rows.Where(r => q.Length == 0 || tr.CompareInfo.IndexOf(r[1], q, CompareOptions.IgnoreCase) >= 0 || tr.CompareInfo.IndexOf(r[0], q, CompareOptions.IgnoreCase) >= 0);
            int count = 0;
            foreach (var g in filtered.GroupBy(r => r[0]))
            {
                var card = new StackPanel { Spacing = 8 };
                card.Children.Add(new TextBlock { Text = g.Key, Foreground = AccentBrush, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
                card.Children.Add(ModuleUi.Table(["Mekan", "Genel lx", "Özel lx"],
                    g.Select(r => (IList<string>)[r[1], r.Length > 2 && r[2].Length > 0 ? r[2] : "—", r.Length > 3 && r[3].Length > 0 ? r[3] : "—"]), [2.6, 1, 1]).View);
                list.Children.Add(new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x14, 0x17, 0x19)), CornerRadius = new CornerRadius(10),
                    BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2A, 0x2F, 0x33)), BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 12, 16, 14), Child = card,
                });
                count++;
            }
            if (count == 0) list.Children.Add(Note("Eşleşen mekan yok."));
        }
        search.TextChanged += (_, _) => Fill();
        Fill();
    }

    // ───────────────────────── Malzeme keşfi ─────────────────────────

    public static void Materials(ModuleUi ui)
    {
        ui.Subtitle = "Sigorta, kaçak akım rölesi, kompakt şalter (2018 liste fiyatları)";
        var rows = Table("prices").Skip(1).ToList();
        var qty = new Dictionary<string, int>();
        var unitTexts = new List<(TextBlock T, string[] Row)>();

        var tot = ui.Card("Toplam", right: true);
        tot.Children.Add(ui.Num("Güncelleme çarpanı (2018 → bugün)", "1", null, out var markup));
        var rCount = ui.Result(tot, "Seçilen kalem");
        var rTotal = ui.Result(tot, "Toplam", big: true);
        var copy = new Button { Content = "Keşfi panoya kopyala" };
        tot.Children.Add(copy);
        tot.Children.Add(Note("Fiyatlar Schneider Electric 26 Mayıs 2018 liste fiyatlarıdır; çarpanla güncelleyin."));
        var listCard = ui.Card("Seçilen kalemler", right: true);
        var chosenList = new StackPanel { Spacing = 2 };
        listCard.Children.Add(chosenList);

        double Price(string[] r) => double.Parse(r[3], CultureInfo.InvariantCulture);

        foreach (var g in rows.GroupBy(r => r[0]))
        {
            var card = ui.Card(g.Key);
            foreach (var r in g)
            {
                var row = new Grid { ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel();
                info.Children.Add(Text(r[1]));
                var unit = Note("");
                unitTexts.Add((unit, r));
                info.Children.Add(unit);
                row.Children.Add(info);
                var nb = new NumberBox { Value = 0, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 130, VerticalAlignment = VerticalAlignment.Center };
                var refCode = r[2];
                nb.ValueChanged += (_, e) => { qty[refCode] = double.IsNaN(e.NewValue) ? 0 : (int)e.NewValue; ui.Changed(); };
                Grid.SetColumn(nb, 1);
                row.Children.Add(nb);
                card.Children.Add(row);
            }
        }

        string summary = "";
        copy.Click += (_, _) =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(summary);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            copy.Content = "Kopyalandı ✓";
        };

        ui.Recalc = () =>
        {
            double m = Num(markup()) ?? 1;
            foreach (var (t, r) in unitTexts) t.Text = $"{r[2]} · {Fmt(Price(r) * m, 2)} TL";
            var chosen = rows.Where(r => qty.GetValueOrDefault(r[2]) > 0).ToList();
            double total = chosen.Sum(r => Price(r) * qty[r[2]] * m);
            rCount.Set(chosen.Count.ToString());
            rTotal.Set(Fmt(total, 2), "TL");
            chosenList.Children.Clear();
            var sb = new System.Text.StringBuilder("Malzeme keşfi\n");
            foreach (var c in chosen)
            {
                var line = $"{c[0]} {c[1]} ({c[2]}): {qty[c[2]]} adet × {Fmt(Price(c) * m, 2)} TL";
                sb.AppendLine(line);
                chosenList.Children.Add(Text(line, size: 12.5));
            }
            sb.Append($"TOPLAM: {Fmt(total, 2)} TL");
            summary = sb.ToString();
            if (chosen.Count == 0) chosenList.Children.Add(Note("Henüz kalem seçilmedi."));
            Outer(listCard).Visibility = Visibility.Visible;
            copy.IsEnabled = chosen.Count > 0;
            copy.Content = "Keşfi panoya kopyala";
        };
    }

    // ───────────────────────── rehberler (core/content/guides.json) ─────────────────────────

    private static JsonObject Guides() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "guides.json")))!.AsObject();

    private static IEnumerable<string> Strings(JsonNode? n) => n!.AsArray().Select(x => (string)x!);
    private static IEnumerable<(string, string)> Pairs(JsonNode? n) => n!.AsArray().Select(x => ((string)x![0]!, x[1]!.ToString()));

    public static void WeakCurrent(ModuleUi ui)
    {
        var g = Guides()["weak"]!;
        ui.Subtitle = (string?)g["subtitle"];
        var fiber = ui.Card("Bina fiber altyapısı (yeni uygulama)");
        int i = 1;
        foreach (var s in Strings(g["fiberSteps"])) fiber.Children.Add(Text($"{i++}. {s}"));
        fiber.Children.Add(Note((string?)g["fiberSource"] ?? ""));
        var flat = ui.Card("Daire içi dağılım");
        foreach (var s in Strings(g["flatNotes"])) flat.Children.Add(Text("• " + s));
        foreach (var (title, path) in Pairs(g["images"])) ui.Card(title, right: true).Children.Add(Image(path));
    }

    public static void Smm(ModuleUi ui)
    {
        var g = Guides()["smm"]!;
        ui.Subtitle = (string?)g["subtitle"];
        var steps = Pairs(g["steps"]).ToList();
        var docs = Strings(g["docs"]).ToList();
        // ilerleme bilgisayarda saklanır
        var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuhenKit", "smm.json");
        var done = new HashSet<string>();
        try { if (File.Exists(store)) foreach (var k in JsonNode.Parse(File.ReadAllText(store))!.AsArray()) done.Add((string)k!); } catch { }
        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(store)!);
                File.WriteAllText(store, new JsonArray(done.Select(d => (JsonNode)d).ToArray()).ToJsonString());
            }
            catch { }
        }

        var prog = ui.Card("İlerleme", right: true);
        var bar = new ProgressBar { Maximum = 1, Height = 6 };
        prog.Children.Add(bar);
        var rDone = ui.Result(prog, "Tamamlanan");

        CheckBox Check(string key, string text)
        {
            var cb = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = done.Contains(key) };
            cb.Click += (_, _) =>
            {
                if (cb.IsChecked == true) done.Add(key); else done.Remove(key);
                Save();
                ui.Changed();
            };
            return cb;
        }

        var sc = ui.Card("Adımlar ve maliyet");
        double total = 0;
        for (int i = 0; i < steps.Count; i++)
        {
            var (s, cost) = steps[i];
            double c = double.TryParse(cost, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            total += c;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Check("s" + i, s));
            var ct = Text($"{Fmt(c, 0)} TL");
            ct.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ct, 1);
            row.Children.Add(ct);
            sc.Children.Add(row);
        }
        ui.Result(sc, "Toplam başlangıç maliyeti", big: true).Set(Fmt(total, 0), "TL");
        sc.Children.Add(Note((string?)g["costNote"] ?? ""));

        var dc = ui.Card("Gerekli evraklar", right: true);
        for (int i = 0; i < docs.Count; i++) dc.Children.Add(Check("d" + i, docs[i]));
        dc.Children.Add(Text("Şirket / ortak / ücretli için ek", bold: true));
        foreach (var s in Strings(g["companyExtra"])) dc.Children.Add(Text("• " + s));

        var jc = ui.Card("SMM'ler ne iş yapar, nereden kazanır?");
        foreach (var (a, b) in Pairs(g["jobs"])) { jc.Children.Add(Text(a, bold: true)); jc.Children.Add(Note(b)); }
        var fc = ui.Card("Formlar", right: true);
        foreach (var (a, b) in Pairs(g["forms"])) { fc.Children.Add(Text(a, bold: true)); fc.Children.Add(Note(b)); }

        ui.Recalc = () =>
        {
            int n = steps.Count + docs.Count;
            int k = Enumerable.Range(0, steps.Count).Count(i => done.Contains("s" + i)) + Enumerable.Range(0, docs.Count).Count(i => done.Contains("d" + i));
            double p = n == 0 ? 0 : (double)k / n;
            bar.Value = p;
            rDone.Set($"%{(int)(p * 100)}");
        };
    }
}
