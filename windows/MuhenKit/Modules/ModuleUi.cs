using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MuhenKit.Core;
using Windows.UI;

namespace MuhenKit.Modules;

/// <summary>Sonuç satırı: değer sonradan yazılır</summary>
public sealed class ResultLine
{
    internal TextBlock Label = null!, Value = null!;
    internal FrameworkElement Root = null!;
    internal bool Big;

    public void Set(string value, string unit = "", bool? ok = null)
    {
        Value.Text = value + (unit.Length > 0 ? " " + unit : "");
        Value.Foreground = ok switch { true => ModuleUi.OkBrush, false => ModuleUi.BadBrush, null => ModuleUi.TextBrush };
    }

    public string LabelText { set => Label.Text = value; }
    public bool Visible { set => Root.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
}

/// <summary>Vurgu satırı değişebilen tablo</summary>
public sealed class TableView
{
    internal List<Border> Rows = new();

    public void Highlight(int row)
    {
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].Background = i == row ? new SolidColorBrush(Color.FromArgb(70, 0xF2, 0xB2, 0x66)) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}

/// <summary>
/// Hesap modüllerinin arayüz yapı taşları (Android modules/Common.kt karşılığı): kart, sayı alanı, seçim,
/// anahtar, sonuç satırı, tablo, not, görsel. Her girdi değişince <see cref="Recalc"/> çalışır.
/// </summary>
public sealed class ModuleUi(XamlRoot? root)
{
    public static readonly SolidColorBrush OkBrush = new(Color.FromArgb(255, 0x3D, 0xFF, 0x8E));
    public static readonly SolidColorBrush BadBrush = new(Color.FromArgb(255, 0xFF, 0x6B, 0x5E));
    public static readonly SolidColorBrush TextBrush = new(Color.FromArgb(255, 0xEE, 0xF1, 0xF5));
    public static readonly SolidColorBrush DimBrush = new(Color.FromArgb(255, 0x9F, 0xB6, 0xDC));
    public static readonly SolidColorBrush AccentBrush = new(Color.FromArgb(255, 0xF2, 0xB2, 0x66));
    private static readonly SolidColorBrush CardBrush = new(Color.FromArgb(255, 0x14, 0x17, 0x19));
    private static readonly SolidColorBrush EdgeBrush = new(Color.FromArgb(255, 0x2A, 0x2F, 0x33));

    public XamlRoot? Root { get; } = root;
    public StackPanel Left { get; } = new() { Spacing = 14 };
    public StackPanel Right { get; } = new() { Spacing = 14 };
    public string? Subtitle { get; set; }
    /// <summary>Girdiler değişince çağrılır (modül sonuçları yeniden hesaplar)</summary>
    public Action? Recalc { get; set; }
    private bool _busy;

    public void Changed()
    {
        if (_busy) return;
        _busy = true;
        try { Recalc?.Invoke(); }
        catch (Exception e)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "muhenkit_log.txt"), DateTime.Now + " modül hesap hatası: " + e + Environment.NewLine); } catch { }
        }
        finally { _busy = false; }
    }

    // ───────────────────────── kartlar ─────────────────────────

    /// <summary>Başlıklı kart; right: sonuç sütununa</summary>
    public StackPanel Card(string title, bool right = false)
    {
        var inner = new StackPanel { Spacing = 10 };
        var head = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = AccentBrush, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        inner.Children.Add(head);
        var b = new Border { Background = CardBrush, BorderBrush = EdgeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 14), Child = inner };
        (right ? Right : Left).Children.Add(b);
        inner.Tag = (head, b);
        return inner;
    }

    /// <summary>Kart başlığını sonradan değiştir</summary>
    public static void SetTitle(StackPanel card, string title) => (((TextBlock, Border))card.Tag).Item1.Text = title;

    /// <summary>Kartın kendisi (gizle/göster için)</summary>
    public static FrameworkElement Outer(StackPanel card) => (((TextBlock, Border))card.Tag).Item2;

    /// <summary>Yan yana alanlar (eşit genişlik)</summary>
    public static Grid Row(params FrameworkElement[] items)
    {
        var g = new Grid { ColumnSpacing = 10 };
        for (int i = 0; i < items.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(items[i], i);
            g.Children.Add(items[i]);
        }
        return g;
    }

    // ───────────────────────── girdiler ─────────────────────────

    /// <summary>Sayı alanı (metin tutulur; virgül de kabul). get: güncel metin</summary>
    public FrameworkElement Num(string label, string init, string? unit, out Func<string> get, Action<string>? onChange = null)
    {
        var box = new TextBox { Text = init, Header = unit != null ? $"{label} ({unit})" : label, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.TextChanged += (_, _) =>
        {
            var t = box.Text.Trim();
            box.BorderBrush = t.Length > 0 && Calc.Num(t) == null ? BadBrush : null;
            if (box.BorderBrush == null) box.ClearValue(Control.BorderBrushProperty);
            onChange?.Invoke(t);
            Changed();
        };
        get = () => box.Text.Trim().Replace(',', '.');
        return box;
    }

    /// <summary>Sayı alanı; değeri dışarıdan da yazılabilir (ör. hazır armatür seçilince)</summary>
    public FrameworkElement Num(string label, string init, string? unit, out Func<string> get, out Action<string> set)
    {
        var el = Num(label, init, unit, out get);
        var box = (TextBox)el;
        set = v => box.Text = v;
        return el;
    }

    /// <summary>Tek seçim: az seçenekte düğme grubu, çokta açılır liste</summary>
    public FrameworkElement Choice<T>(string? label, IList<T> options, T init, Func<T, string> text, out Func<T> get, Action<T>? onSelect = null)
    {
        var cur = init;
        get = () => cur;
        var sp = new StackPanel { Spacing = 4 };
        if (label != null) sp.Children.Add(new TextBlock { Text = label, Foreground = DimBrush, FontSize = 12 });
        if (options.Count <= 6 && options.Sum(o => text(o).Length) < 80)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var buttons = new List<ToggleButton>();
            foreach (var o in options)
            {
                var b = new ToggleButton { Content = text(o), IsChecked = EqualityComparer<T>.Default.Equals(o, init), Padding = new Thickness(12, 5, 12, 6) };
                b.Click += (_, _) =>
                {
                    cur = o;
                    foreach (var x in buttons) x.IsChecked = ReferenceEquals(x, b);
                    onSelect?.Invoke(o);
                    Changed();
                };
                buttons.Add(b);
                row.Children.Add(b);
            }
            sp.Children.Add(new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Disabled });
        }
        else
        {
            var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var o in options) cb.Items.Add(text(o));
            cb.SelectedIndex = Math.Max(0, options.IndexOf(init));
            cb.SelectionChanged += (_, _) =>
            {
                if (cb.SelectedIndex < 0) return;
                cur = options[cb.SelectedIndex];
                onSelect?.Invoke(cur);
                Changed();
            };
            sp.Children.Add(cb);
        }
        return sp;
    }

    /// <summary>Ad → değer listesinden seçim (katsayılar, armatürler, iletkenler)</summary>
    public FrameworkElement Named(string label, List<(string Name, double Value)> options, double init, out Func<double> get, Action<double>? onSelect = null)
    {
        double cur = init;
        get = () => cur;
        var cb = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (n, v) in options) cb.Items.Add($"{n}  ({Calc.Fmt(v, 3)})");
        cb.SelectedIndex = options.FindIndex(o => o.Value == init);
        if (cb.SelectedIndex < 0) cb.PlaceholderText = Calc.Fmt(init, 3);
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex < 0) return;
            cur = options[cb.SelectedIndex].Value;
            onSelect?.Invoke(cur);
            Changed();
        };
        return cb;
    }

    public FrameworkElement Toggle(string label, bool init, out Func<bool> get, Action<bool>? onChange = null)
    {
        var t = new ToggleSwitch { Header = null, IsOn = init, OnContent = label, OffContent = label };
        get = () => t.IsOn;
        t.Toggled += (_, _) => { onChange?.Invoke(t.IsOn); Changed(); };
        return t;
    }

    // ───────────────────────── çıktılar ─────────────────────────

    /// <summary>Sonuç satırı: etiket — değer birim; büyük: önemli sonuç</summary>
    public ResultLine Result(Panel p, string label, bool big = false)
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, big ? 4 : 1, 0, big ? 4 : 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new TextBlock { Text = label, Foreground = DimBrush, FontSize = big ? 15 : 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var v = new TextBlock { FontSize = big ? 21 : 15, FontWeight = big ? FontWeights.Bold : FontWeights.SemiBold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, IsTextSelectionEnabled = true };
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
        p.Children.Add(g);
        return new ResultLine { Label = l, Value = v, Root = g, Big = big };
    }

    public static TextBlock Note(string text) => new() { Text = text, Foreground = DimBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };

    public static TextBlock Text(string text, bool bold = false, double size = 13) =>
        new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    /// <summary>Basit tablo: başlık + satırlar; weights: sütun oranları</summary>
    public static (FrameworkElement View, TableView Table) Table(IList<string> headers, IEnumerable<IList<string>> rows, IList<double>? weights = null)
    {
        var tv = new TableView();
        var sp = new StackPanel();
        Grid MakeRow(IList<string> cells, bool head)
        {
            var g = new Grid { Padding = new Thickness(8, head ? 6 : 4, 8, head ? 6 : 4), ColumnSpacing = 8 };
            for (int i = 0; i < headers.Count; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weights?.ElementAtOrDefault(i) is > 0 and var w ? w : 1, GridUnitType.Star) });
            for (int i = 0; i < cells.Count && i < headers.Count; i++)
            {
                var t = new TextBlock { Text = cells[i], FontSize = head ? 11.5 : 12.5, FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Foreground = head ? DimBrush : TextBrush };
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
            return g;
        }
        sp.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), Child = MakeRow(headers, true) });
        foreach (var r in rows)
        {
            var b = new Border { Child = MakeRow(r, false), BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), BorderThickness = new Thickness(0, 1, 0, 0) };
            tv.Rows.Add(b);
            sp.Children.Add(b);
        }
        var outer = new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = sp };
        return (outer, tv);
    }

    /// <summary>Ortak içerikten görsel (core/content/img), beyaz zeminde</summary>
    public static FrameworkElement Image(string path)
    {
        var full = Path.Combine(AppContext.BaseDirectory, "content", path.Replace('/', Path.DirectorySeparatorChar));
        var img = new Microsoft.UI.Xaml.Controls.Image { Stretch = Stretch.Uniform, MaxHeight = 640 };
        if (File.Exists(full)) img.Source = new BitmapImage(new Uri(full));
        return new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.White), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Child = img };
    }
}
