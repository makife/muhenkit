using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MuhenKit.Core;
using Windows.UI;
using static MuhenKit.Core.ProjectTables;

namespace MuhenKit.Pages;

/// <summary>Proje tabloları formları (Android ui/TableForms.kt): doldur → tablo bloğu üret → çizimde yerleştir</summary>
public static class TableForms
{
    public enum Kind { Lighting, Power, Title }

    public static readonly (Kind Kind, string Title, string Hint, string Glyph)[] Kinds =
    [
        (Kind.Lighting, "Aydınlatma hesap tablosu", "Mahal bazında ölçüler, lüks, verim, armatür seçimi ve sayısı", ""),
        (Kind.Power, "Toplam güç tablosu", "Bölüm/daire bazında kurulu güç, eşzamanlılık ve talep gücü", ""),
        (Kind.Title, "Firma anteti", "Projeyi yapan firma ve proje bilgileri (bilgiler hatırlanır)", ""),
    ];

    private static readonly Brush Accent = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xB2, 0x66));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(255, 0x9F, 0xB6, 0xDC));
    private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    /// <summary>Formu gösterir; "Çizime ekle" ile üretilen tablo döner, vazgeçilirse null</summary>
    public static Task<CadSymbol?> Show(Kind kind, XamlRoot root, double textHeight) => kind switch
    {
        Kind.Lighting => Lighting(root, textHeight),
        Kind.Power => Power(root, textHeight),
        _ => Title(root, textHeight),
    };

    private static ContentDialog Dialog(XamlRoot root, string title, UIElement content, double maxWidth)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = root, Title = title, Content = content, PrimaryButtonText = "Çizime ekle", CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Primary, RequestedTheme = ElementTheme.Dark,
        };
        dlg.Resources["ContentDialogMaxWidth"] = maxWidth;
        dlg.Resources["ContentDialogMaxHeight"] = 900.0;
        return dlg;
    }

    private static TextBlock Hint(string s) => new() { Text = s, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap };

    // ───────────────────────── aydınlatma ─────────────────────────

    /// <summary>Satır: etiket, okuma, yazma, hesaplanan ipucu (boşsa hesaplanır)</summary>
    private static readonly (string Label, Func<Room, string> Get, Action<Room, string> Set, Func<Room, string?>? Calc)[] RoomFields =
    [
        ("Mahal ismi", r => r.Name, (r, v) => r.Name = v, null),
        ("En a (m)", r => r.A, (r, v) => r.A = v, null),
        ("Boy b (m)", r => r.B, (r, v) => r.B = v, null),
        ("Yükseklik H (m)", r => r.H, (r, v) => r.H = v, null),
        ("Çalışma düzlemi (m)", r => r.Work, (r, v) => r.Work = v, null),
        ("Aydınlık E (lx)", r => r.Lux, (r, v) => r.Lux = v, null),
        ("Yansıtma (tavan-duvar-yer)", r => r.Refl, (r, v) => r.Refl = v, null),
        ("Mahal endeksi k", r => r.K, (r, v) => r.K = v, r => Computed(r).K is { } k ? F(k) : null),
        ("Verim η", r => r.Eta, (r, v) => r.Eta = v, r => Computed(r).Eta is { } e ? F(e) : null),
        ("Armatür gücü (W)", r => r.Power, (r, v) => r.Power = v, null),
        ("Işık akısı Ø (lm)", r => r.Flux, (r, v) => r.Flux = v, null),
        ("Armatür tipi", r => r.Type, (r, v) => r.Type = v, null),
        ("Hesaplanan Z", r => r.ZCalc, (r, v) => r.ZCalc = v, r => Computed(r).Z is { } z ? F(z) : null),
        ("Kullanılacak sayı", r => r.ZUsed, (r, v) => r.ZUsed = v, r => Computed(r).Used is { Length: > 0 } u ? u : null),
    ];

    private static async Task<CadSymbol?> Lighting(XamlRoot root, double th)
    {
        var rooms = new List<Room> { new() { Name = "SALON" } };
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        var note = new TextBox { Header = "Tablo altı not", Text = "Not: Tasarruflu ampül kullanılması tercih edilerek, armatür seçimi yapılmıştır." };
        var add = new Button { Content = "+ Mahal ekle" };

        void Rebuild()
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            foreach (var _ in rooms) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            for (int i = 0; i <= RoomFields.Length; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int f = 0; f < RoomFields.Length; f++)
            {
                var lb = new TextBlock { Text = RoomFields[f].Label, VerticalAlignment = VerticalAlignment.Center };
                if (RoomFields[f].Calc != null) lb.Foreground = Accent;
                Grid.SetRow(lb, f + 1);
                grid.Children.Add(lb);
            }
            for (int c = 0; c < rooms.Count; c++)
            {
                var room = rooms[c];
                int idx = c;
                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(new TextBlock { Text = $"Mahal {c + 1}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                if (rooms.Count > 1)
                {
                    var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 }, Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
                    ToolTipService.SetToolTip(del, "Mahali sil");
                    del.Click += (_, _) => { rooms.RemoveAt(idx); Rebuild(); };
                    Grid.SetColumn(del, 1);
                    head.Children.Add(del);
                }
                Grid.SetColumn(head, c + 1);
                grid.Children.Add(head);
                var calcBoxes = new List<(TextBox Box, Func<Room, string?> Calc)>();
                void Hints()
                {
                    foreach (var (box, calc) in calcBoxes) box.PlaceholderText = calc(room) ?? "";
                }
                for (int f = 0; f < RoomFields.Length; f++)
                {
                    var fd = RoomFields[f];
                    var tb = new TextBox { Text = fd.Get(room) };
                    tb.TextChanged += (_, _) => { fd.Set(room, tb.Text); Hints(); };
                    if (fd.Calc != null) calcBoxes.Add((tb, fd.Calc));
                    Grid.SetRow(tb, f + 1);
                    Grid.SetColumn(tb, c + 1);
                    grid.Children.Add(tb);
                }
                Hints();
            }
        }
        add.Click += (_, _) =>
        {
            var last = rooms[^1];
            rooms.Add(new Room { H = last.H, Work = last.Work, Refl = last.Refl, Power = last.Power, Flux = last.Flux, Type = last.Type });
            Rebuild();
        };
        Rebuild();

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Hint("Turuncu satırlar boş bırakılırsa hesaplanır (gri ipucu): k = (a·b)/(h·(a+b)), h = H − çalışma düzlemi; " +
            "Z = (E·a·b·1,25)/(Ø·η). η boşsa 80-50-30 için tipik değer önerilir — cetvelden gireceğin değer geçerlidir."));
        panel.Children.Add(new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 12),
        });
        panel.Children.Add(add);
        panel.Children.Add(note);
        var dlg = Dialog(root, "Aydınlatma hesap tablosu", new ScrollViewer { Content = panel, Padding = new Thickness(0, 0, 16, 0) }, 1200);
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? LightingTable(th, rooms, note.Text) : null;
    }

    // ───────────────────────── toplam güç ─────────────────────────

    private static async Task<CadSymbol?> Power(XamlRoot root, double th)
    {
        var loads = new List<Load> { new() { Name = "1. KAT DAİRE", DemandPct = "60" }, new() { Name = "2. KAT DAİRE", DemandPct = "60" } };
        var title = new TextBox { Header = "Tablo başlığı", Text = "TOPLAM GÜÇ TABLOSU" };
        var rows = new StackPanel { Spacing = 6 };
        var sum = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Accent };
        void Sum()
        {
            var (i, d) = PowerSums(loads);
            sum.Text = $"Toplam kurulu güç: {F(i, 0)} W   ·   Toplam talep güç: {F(d, 0)} W";
        }
        void Rebuild()
        {
            rows.Children.Clear();
            for (int k = 0; k < loads.Count; k++)
            {
                var l = loads[k];
                int idx = k;
                var g = new Grid { ColumnSpacing = 8 };
                foreach (var w in new[] { 2.0, 1.0, 0.8 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBox Box(int col, string header, string val, Action<string> set)
                {
                    var t = new TextBox { Header = k == 0 ? header : null, Text = val };
                    t.TextChanged += (_, _) => { set(t.Text); Sum(); };
                    Grid.SetColumn(t, col);
                    g.Children.Add(t);
                    return t;
                }
                Box(0, "Bölüm / daire", l.Name, v => l.Name = v);
                Box(1, "Kurulu güç (W)", l.Installed, v => l.Installed = v);
                Box(2, "Eş zam. (%)", l.DemandPct, v => l.DemandPct = v);
                var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
                ToolTipService.SetToolTip(del, "Satırı sil");
                del.Click += (_, _) => { loads.RemoveAt(idx); Rebuild(); };
                Grid.SetColumn(del, 3);
                g.Children.Add(del);
                rows.Children.Add(g);
            }
            Sum();
        }
        Rebuild();
        var add = new Button { Content = "+ Satır ekle" };
        add.Click += (_, _) => { loads.Add(new Load { DemandPct = loads.Count > 0 ? loads[^1].DemandPct : "100" }); Rebuild(); };

        var panel = new StackPanel { Spacing = 12, MinWidth = 560 };
        panel.Children.Add(title);
        panel.Children.Add(rows);
        panel.Children.Add(add);
        panel.Children.Add(new Border { Height = 1, Background = Line });
        panel.Children.Add(sum);
        var dlg = Dialog(root, "Toplam güç tablosu", new ScrollViewer { Content = panel, Padding = new Thickness(0, 0, 16, 0) }, 760);
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? PowerTable(th, title.Text, loads) : null;
    }

    // ───────────────────────── antet ─────────────────────────

    private static string AntetPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuhenKit", "antet.json");

    private static Dictionary<string, string> LoadAntet()
    {
        try
        {
            if (File.Exists(AntetPath)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AntetPath)) ?? [];
        }
        catch (Exception) { }
        return [];
    }

    private static void SaveAntet(Dictionary<string, string> v)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AntetPath)!);
            File.WriteAllText(AntetPath, JsonSerializer.Serialize(v));
        }
        catch (Exception) { }
    }

    private static async Task<CadSymbol?> Title(XamlRoot root, double th)
    {
        var saved = LoadAntet();
        var values = TitleFields.ToDictionary(f => f.Key, f => saved.TryGetValue(f.Key, out var s) ? s : f.Default);
        // iki sütun: alanlar soldan sağa, satır satır
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < TitleFields.Length; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (key, label, _) = TitleFields[i];
            var tb = new TextBox { Header = label, Text = values[key] };
            tb.TextChanged += (_, _) => values[key] = tb.Text;
            Grid.SetRow(tb, i / 2);
            Grid.SetColumn(tb, i % 2);
            grid.Children.Add(tb);
        }
        var panel = new StackPanel { Spacing = 12, MinWidth = 640 };
        panel.Children.Add(Hint("Bilgiler bu bilgisayarda saklanır; sonraki projede hazır gelir."));
        panel.Children.Add(grid);
        var dlg = Dialog(root, "Firma anteti", new ScrollViewer { Content = panel, Padding = new Thickness(0, 0, 16, 0) }, 820);
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return null;
        SaveAntet(values);
        return TitleBlock(th, values);
    }
}
