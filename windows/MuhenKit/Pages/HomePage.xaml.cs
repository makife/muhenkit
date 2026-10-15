using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MuhenKit.Core;
using Windows.Foundation;
using Windows.UI;

namespace MuhenKit.Pages;

/// <summary>
/// Ana sayfa: mavi devre kartı. MCU çipi (logo + arama) ve modül çipleri; MCU'dan çiplere bakır
/// yollar ve üzerlerinde akan akım darbeleri (Android ana sayfasıyla aynı tasarım).
/// </summary>
public sealed partial class HomePage : Page
{
    private static readonly Color Board = C(0x08, 0x21, 0x52), Board2 = C(0x0C, 0x2E, 0x6E), MaskDot = C(0x3D, 0x7B, 0xD9),
        Copper = C(0xC0, 0x8A, 0x3E), CopperHi = C(0xF2, 0xB2, 0x66), Gold = C(0xE4, 0xC0, 0x6A), Silk = C(0xEF, 0xF3, 0xFB),
        SilkDim = C(0x9F, 0xB6, 0xDC), Chip = C(0x0E, 0x14, 0x1C), Chip2 = C(0x1A, 0x22, 0x2D), ChipEdge = C(0x2C, 0x3A, 0x4E);
    private static readonly Color[] Leds = [C(0x3D, 0xFF, 0x8E), C(0xFF, 0xB6, 0x27), C(0x5C, 0xC8, 0xFF), C(0xFF, 0x6B, 0x5E)];
    private static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private readonly CanvasControl _canvas = new();
    private readonly Canvas _layer = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Grid _content = new();
    private readonly TextBox _search = new();
    private List<RouteInfo> _list = Routes.All.ToList();
    private Rect _mcu;
    private List<Rect> _chips = new();
    private List<Vector2[]> _paths = new();
    private int _hot = -1;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public HomePage()
    {
        InitializeComponent();
        Background = new SolidColorBrush(Board);
        _content.Children.Add(_canvas);
        _content.Children.Add(_layer);
        _scroll.Content = _content;
        Content = _scroll;
        _canvas.Draw += OnDraw;
        _search.PlaceholderText = "Modül ara… (ör. topraklama)";
        _search.TextChanged += (_, _) =>
        {
            var q = _search.Text.Trim();
            _list = Routes.All.Where(r => q.Length == 0 || (r.Title + " " + r.Subtitle).Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            Layout();
        };
        SizeChanged += (_, _) => Layout();
        // akım darbeleri: her karede yeniden çiz
        CompositionTarget.Rendering += OnFrame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
        Loaded += (_, _) => { CompositionTarget.Rendering -= OnFrame; CompositionTarget.Rendering += OnFrame; };
    }

    private void OnFrame(object? sender, object e) => _canvas.Invalidate();

    // ───────────────────────── yerleşim ─────────────────────────

    private void Layout()
    {
        double w = ActualWidth;
        if (w <= 0) return;
        int cols = w > 1150 ? 4 : w > 820 ? 3 : 2;
        double side = 20, gap = 34, chipH = 148, rowGap = 46;
        double chipW = (Math.Min(w, 1240) - side * 2 - gap * (cols - 1)) / cols;
        double left = (w - (chipW * cols + gap * (cols - 1))) / 2;
        double mcuW = Math.Min(w - 40, 560), mcuTop = 16, mcuH = 176;
        double gridTop = mcuTop + mcuH + 70;
        int rows = (_list.Count + cols - 1) / cols;
        double totalH = Math.Max(gridTop + rows * (chipH + rowGap) + 80, ActualHeight);
        _mcu = new Rect((w - mcuW) / 2, mcuTop, mcuW, mcuH);
        _chips = _list.Select((_, i) => new Rect(left + (i % cols) * (chipW + gap), gridTop + (i / cols) * (chipH + rowGap), chipW, chipH)).ToList();
        _paths = BuildPaths();
        _content.Height = totalH;
        _canvas.Height = totalH;
        _layer.Children.Clear();
        Place(_mcuChip ??= McuChip(), _mcu, inset: new Thickness(10, 8, 10, 8));
        for (int i = 0; i < _list.Count; i++) Place(ModuleChip(i, _list[i]), _chips[i], inset: new Thickness(2, 7, 2, 7));
        if (_list.Count == 0)
            Place(new TextBlock { Text = "EŞLEŞEN MODÜL YOK", Foreground = new SolidColorBrush(SilkDim), CharacterSpacing = 200, HorizontalAlignment = HorizontalAlignment.Center },
                new Rect(0, gridTop, w, 30), default);
        var foot = new TextBlock { Text = $"MÜHENKİT REV 2.0 · ELEKTRİK · {_list.Count} MODÜL", FontSize = 10, CharacterSpacing = 150, Foreground = new SolidColorBrush(Color.FromArgb(180, SilkDim.R, SilkDim.G, SilkDim.B)), TextAlignment = TextAlignment.Center };
        Place(foot, new Rect(0, totalH - 36, w, 20), default);
        _canvas.Invalidate();
    }

    private void Place(FrameworkElement e, Rect r, Thickness inset)
    {
        e.Width = Math.Max(0, r.Width - inset.Left - inset.Right);
        e.Height = Math.Max(0, r.Height - inset.Top - inset.Bottom);
        Canvas.SetLeft(e, r.X + inset.Left);
        Canvas.SetTop(e, r.Y + inset.Top);
        _layer.Children.Add(e);
    }

    /// <summary>MCU'dan her çipe 45° kırılımlı bakır yol</summary>
    private List<Vector2[]> BuildPaths()
    {
        int n = _chips.Count;
        var list = new List<Vector2[]>();
        for (int i = 0; i < n; i++)
        {
            var r = _chips[i];
            float pinX = (float)(_mcu.X + 30 + (_mcu.Width - 60) * (n == 1 ? 0.5 : (double)i / (n - 1)));
            float bot = (float)(_mcu.Y + _mcu.Height);
            float busY = (float)(r.Y - 16 - (i % 4) * 5);
            float firstDrop = bot + 18 + (i % 6) * 5;
            float endX = (float)(r.X + r.Width / 2 + ((i % 3) - 1) * 10);
            float trunkX = (float)(r.X - 17 + (i % 3) * 4);
            float dx = trunkX - pinX;
            float diag = Math.Min(Math.Abs(dx), 14);
            float sg = dx < 0 ? -1 : 1;
            list.Add([
                new(pinX, bot), new(pinX, firstDrop), new(pinX + diag * sg, firstDrop + diag), new(trunkX - diag * sg, firstDrop + diag),
                new(trunkX, firstDrop + diag * 2), new(trunkX, busY - 8), new(trunkX + 8, busY), new(endX - 8, busY), new(endX, busY + 8), new(endX, (float)r.Y),
            ]);
        }
        return list;
    }

    private static Vector2 PointAt(Vector2[] p, float t)
    {
        float total = 0;
        for (int i = 1; i < p.Length; i++) total += Vector2.Distance(p[i - 1], p[i]);
        float d = total * t;
        for (int i = 1; i < p.Length; i++)
        {
            float seg = Vector2.Distance(p[i - 1], p[i]);
            if (d <= seg) return Vector2.Lerp(p[i - 1], p[i], seg == 0 ? 0 : d / seg);
            d -= seg;
        }
        return p[^1];
    }

    // ───────────────────────── çizim ─────────────────────────

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float w = (float)sender.ActualWidth, h = (float)sender.ActualHeight;
        using (var grad = new Microsoft.Graphics.Canvas.Brushes.CanvasLinearGradientBrush(sender, Board2, Board) { StartPoint = new(0, 0), EndPoint = new(0, h) })
            ds.FillRectangle(0, 0, w, h, grad);
        // lehim maskesi noktaları
        var dot = Color.FromArgb(40, MaskDot.R, MaskDot.G, MaskDot.B);
        for (float y = 9; y < h; y += 18)
            for (float x = 9; x < w; x += 18) ds.FillCircle(x, y, 1.1f, dot);
        // silik arka plan yolları
        var rnd = new Random(11);
        for (int k = 0; k < 14; k++)
        {
            float y0 = (float)rnd.NextDouble() * h, x0 = (float)rnd.NextDouble() * w * 0.6f, len = w * (0.2f + (float)rnd.NextDouble() * 0.4f);
            float bend = 14 * (rnd.Next(2) == 0 ? 1 : -1);
            var c = Color.FromArgb(26, Copper.R, Copper.G, Copper.B);
            ds.DrawLine(x0, y0, x0 + len * 0.4f, y0, c, 1.6f);
            ds.DrawLine(x0 + len * 0.4f, y0, x0 + len * 0.4f + Math.Abs(bend), y0 + bend, c, 1.6f);
            ds.DrawLine(x0 + len * 0.4f + Math.Abs(bend), y0 + bend, x0 + len, y0 + bend, c, 1.6f);
            foreach (var pt in new[] { new Vector2(x0, y0), new Vector2(x0 + len, y0 + bend) })
            {
                ds.FillCircle(pt, 3.5f, Color.FromArgb(46, Copper.R, Copper.G, Copper.B));
                ds.FillCircle(pt, 1.4f, Board);
            }
        }
        // bakır yollar, vialar, akım darbeleri
        var round = new CanvasStrokeStyle { LineJoin = CanvasLineJoin.Round, StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
        float flow = (float)(_clock.Elapsed.TotalSeconds / 4.0 % 1.0);
        for (int i = 0; i < _paths.Count; i++)
        {
            var p = _paths[i];
            bool hot = i == _hot;
            using var pb = new CanvasPathBuilder(sender);
            pb.BeginFigure(p[0]);
            for (int k = 1; k < p.Length; k++) pb.AddLine(p[k]);
            pb.EndFigure(CanvasFigureLoop.Open);
            using var path = CanvasGeometry.CreatePath(pb);
            if (hot) ds.DrawGeometry(path, Color.FromArgb(56, CopperHi.R, CopperHi.G, CopperHi.B), 9, round);
            ds.DrawGeometry(path, hot ? CopperHi : Color.FromArgb(153, Copper.R, Copper.G, Copper.B), hot ? 3.4f : 2.4f, round);
            ds.FillCircle(p[0], 4.5f, Gold);
            ds.FillCircle(p[0], 1.8f, Board);
            ds.FillRectangle(p[^1].X - 4.5f, p[^1].Y - 2.5f, 9, 5, Gold);
            for (int k = 0; k < 2; k++)
            {
                float ph = (flow + i * 0.137f + k * 0.5f) % 1f;
                var pos = PointAt(p, ph);
                var c = hot ? C(0xFF, 0xB6, 0x27) : CopperHi;
                ds.FillCircle(pos, hot ? 9 : 7, Color.FromArgb(46, c.R, c.G, c.B));
                ds.FillCircle(pos, hot ? 5 : 4, Color.FromArgb(90, c.R, c.G, c.B));
                ds.FillCircle(pos, hot ? 2.6f : 1.9f, Color.FromArgb(235, 255, 255, 255));
            }
        }
        // IC bacakları
        Pins(ds, _mcu, 0, Math.Min(_chips.Count, 22), 6);
        foreach (var r in _chips) Pins(ds, r, 4, 4, 0);
    }

    private static void Pins(CanvasDrawingSession ds, Rect r, int top, int bottom, int sides)
    {
        var pin = C(0xC9, 0xCD, 0xD2);
        void Row(int n, double y)
        {
            double span = r.Width - 40;
            for (int i = 0; i < n; i++)
            {
                double x = r.X + 20 + span * (i + 0.5) / n;
                ds.FillRoundedRectangle((float)x - 2.5f, (float)y - 4, 5, 8, 1, 1, pin);
            }
        }
        Row(top, r.Y + 4);
        Row(bottom, r.Y + r.Height - 4);
        double sp = r.Height - 30;
        for (int i = 0; i < sides; i++)
        {
            double y = r.Y + 15 + sp * (i + 0.5) / sides;
            ds.FillRoundedRectangle((float)r.X, (float)y - 2.5f, 8, 5, 1, 1, pin);
            ds.FillRoundedRectangle((float)(r.X + r.Width - 8), (float)y - 2.5f, 8, 5, 1, 1, pin);
        }
    }

    // ───────────────────────── çipler ─────────────────────────

    private FrameworkElement? _mcuChip;

    /// <summary>Bir kez kurulur (arama kutusu yazarken odağını kaybetmesin)</summary>
    private FrameworkElement McuChip()
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(6), BorderBrush = new SolidColorBrush(ChipEdge), BorderThickness = new Thickness(1.5),
            Background = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1), GradientStops = { new GradientStop { Color = Chip2, Offset = 0 }, new GradientStop { Color = Chip, Offset = 1 } } },
            Padding = new Thickness(18, 12, 18, 12),
        };
        var g = new Grid { RowSpacing = 4 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(new Image { Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "content", "brand", "icon_preview.png"))), Width = 36, Height = 36 });
        head.Children.Add(new TextBlock { Text = "MÜHENKİT", FontSize = 24, FontWeight = FontWeights.ExtraBold, CharacterSpacing = 300, Foreground = new SolidColorBrush(Silk), VerticalAlignment = VerticalAlignment.Center });
        var pwr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        pwr.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Leds[0]) });
        pwr.Children.Add(new TextBlock { Text = "PWR", FontSize = 9, Foreground = new SolidColorBrush(SilkDim) });
        var info = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14, Foreground = new SolidColorBrush(SilkDim) },
            Padding = new Thickness(6), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(info, "Hakkında");
        info.Click += (_, _) => ShowAbout();
        pwr.Children.Add(info);
        var headRow = new Grid();
        headRow.Children.Add(head);
        headRow.Children.Add(pwr);
        g.Children.Add(headRow);
        var sub = new TextBlock { Text = "Elektrik proje ve hesap kiti — bir çipe tıkla", FontSize = 12, Foreground = new SolidColorBrush(SilkDim) };
        Grid.SetRow(sub, 1);
        g.Children.Add(sub);
        Grid.SetRow(_search, 3);
        g.Children.Add(_search);
        b.Child = g;
        return b;
    }

    private FrameworkElement ModuleChip(int i, RouteInfo r)
    {
        bool white = r.Id is RouteId.Cad or RouteId.Projects;
        Color title = white ? C(0x0B, 0x0F, 0x14) : Silk, sub = white ? C(0x2B, 0x35, 0x42) : SilkDim;
        LinearGradientBrush Bg(bool lit) => new()
        {
            StartPoint = new(0, 0), EndPoint = new(1, 1),
            GradientStops =
            {
                new GradientStop { Color = white ? (lit ? C(0xFF, 0xF4, 0xE2) : C(0xFF, 0xFF, 0xFF)) : (lit ? C(0x24, 0x31, 0x42) : Chip2), Offset = 0 },
                new GradientStop { Color = white ? (lit ? C(0xE9, 0xDC, 0xC6) : C(0xE4, 0xEA, 0xF3)) : (lit ? C(0x15, 0x1C, 0x26) : Chip), Offset = 1 },
            },
        };
        var border = new Border
        {
            CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.3), Padding = new Thickness(14, 10, 12, 10),
            BorderBrush = new SolidColorBrush(white ? C(0xB8, 0xC4, 0xD6) : ChipEdge), Background = Bg(false),
        };
        var led = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromArgb(64, Leds[i % 4].R, Leds[i % 4].G, Leds[i % 4].B)) };
        var icon = new FontIcon { Glyph = r.Glyph, FontSize = 20, Foreground = new SolidColorBrush(white ? C(0x0A, 0x2C, 0x7A) : Gold) };
        var g = new Grid { RowSpacing = 4 };
        for (int k = 0; k < 4; k++) g.RowDefinitions.Add(new RowDefinition { Height = k == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var head = new Grid();
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        left.Children.Add(icon);
        left.Children.Add(new TextBlock { Text = $"U{i + 1}", FontSize = 11, Foreground = new SolidColorBrush(sub), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(left);
        led.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(led);
        g.Children.Add(head);
        var t = new TextBlock { Text = r.Title.ToUpper(new System.Globalization.CultureInfo("tr-TR")), FontSize = 15, FontWeight = FontWeights.ExtraBold, CharacterSpacing = 60, Foreground = new SolidColorBrush(title), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(t, 1);
        g.Children.Add(t);
        var s = new TextBlock { Text = r.Subtitle, FontSize = 12, Foreground = new SolidColorBrush(sub), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(s, 2);
        g.Children.Add(s);
        var part = new TextBlock { Text = $"MK-{i + 1:00}-{r.Id.ToString().ToUpperInvariant()[..Math.Min(4, r.Id.ToString().Length)]}", FontSize = 9, CharacterSpacing = 150, Foreground = new SolidColorBrush(white ? C(0x6A, 0x76, 0x89) : C(0x5C, 0x6B, 0x82)) };
        Grid.SetRow(part, 3);
        g.Children.Add(part);
        border.Child = g;
        // üzerine gelince yol parlar, çip aydınlanır (masaüstünde fare var)
        border.PointerEntered += (_, _) =>
        {
            _hot = i;
            border.Background = Bg(true);
            border.BorderBrush = new SolidColorBrush(CopperHi);
            icon.Foreground = new SolidColorBrush(CopperHi);
            led.Fill = new SolidColorBrush(Leds[i % 4]);
        };
        border.PointerExited += (_, _) =>
        {
            if (_hot == i) _hot = -1;
            border.Background = Bg(false);
            border.BorderBrush = new SolidColorBrush(white ? C(0xB8, 0xC4, 0xD6) : ChipEdge);
            icon.Foreground = new SolidColorBrush(white ? C(0x0A, 0x2C, 0x7A) : Gold);
            led.Fill = new SolidColorBrush(Color.FromArgb(64, Leds[i % 4].R, Leds[i % 4].G, Leds[i % 4].B));
        };
        border.Tapped += (_, _) => (Application.Current as App)?.Navigate(r.Id);
        return border;
    }

    public const string PrivacyUrl = "https://makife.github.io/muhenkit/gizlilik.html";

    /// <summary>Hakkında: sürüm, sorumluluk notu, gizlilik</summary>
    private async void ShowAbout()
    {
        var v = typeof(HomePage).Assembly.GetName().Version;
        var sp = new StackPanel { Spacing = 10, MaxWidth = 460 };
        sp.Children.Add(new TextBlock { Text = $"Sürüm {v?.Major}.{v?.Minor}.{v?.Build}", FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = "Elektrik proje çizimi (DXF) ve mühendislik hesapları. İnternet gerektirmez; projeleriniz yalnızca bu bilgisayarda saklanır, kişisel veri toplanmaz.", TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock
        {
            Text = "Sorumluluk notu: Hesaplar ve tablolar ön tasarım ve kontrol amaçlıdır. Projenin yönetmeliklere uygunluğu ve sonuçların doğruluğu projeyi hazırlayan mühendisin sorumluluğundadır.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x9F, 0xB6, 0xDC)),
        });
        sp.Children.Add(new HyperlinkButton { Content = "Destek: makifergan@gmail.com", NavigateUri = new Uri("mailto:makifergan@gmail.com"), Padding = new Thickness(0) });
        sp.Children.Add(new HyperlinkButton { Content = "Gizlilik politikası", NavigateUri = new Uri(PrivacyUrl), Padding = new Thickness(0) });
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "MühenKit Elektrik", Content = sp, CloseButtonText = "Kapat", RequestedTheme = ElementTheme.Dark,
        };
        await dlg.ShowAsync();
    }
}
