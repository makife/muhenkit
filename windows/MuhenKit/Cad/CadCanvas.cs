using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MuhenKit.Core;
using Windows.Foundation;
using Windows.UI;

namespace MuhenKit.Cad;

/// <summary>Görünüm: ekran ortasındaki dünya noktası (göreli) ve piksel/birim</summary>
public readonly record struct ViewState(double Cx, double Cy, double Zoom);

/// <summary>Düzenleme katmanı (göreli dünya koordinatları)</summary>
public sealed class Overlay
{
    public static readonly Overlay Empty = new();
    /// <summary>Seçili nesnelerin (x1,y1,x2,y2) parçaları</summary>
    public float[] Selection = [];
    /// <summary>Seçimin önizleme dönüşümü (taşı, döndür, ayna…)</summary>
    public Matrix3x2? Ghost;
    /// <summary>Lastik bant: çizgi, daire, dikdörtgen, sembol önizlemesi</summary>
    public float[]? Rubber;
    public int SnapType;
    public Vector2 Snap;
    public Vector2? Cursor;
    /// <summary>Pencere seçimi (x0,y0,x1,y1); Crossing: sağdan sola</summary>
    public float[]? Box;
    public bool BoxCrossing;
    /// <summary>Hizalama izleri: kesikli kılavuz çizgileri (x1,y1,x2,y2)</summary>
    public float[]? Guides;
    /// <summary>Yakalanmış (izlenen) noktalar: küçük artı işaretleri (x,y)</summary>
    public float[]? Marks;
    /// <summary>İmleç yanındaki kısa etiket (ör. "Paralel", "= 250")</summary>
    public string? Label;
}

/// <summary>Sol tuş / kalem girişi (göreli dünya; tol = birkaç pikselin dünya karşılığı)</summary>
public interface ICanvasInput
{
    void Press(double x, double y, double tol, bool shift);
    void Move(double x, double y, double tol, bool pressed);
    void Release(double x, double y, double tol);
    void Cancel();
}

/// <summary>
/// DXF çizim tuvali (Win2D / Direct2D), AutoCAD gibi görünüm alanlı: tek görünüm ya da 2×2 dört görünüm.
/// Dört bölme aynı çizimi kendi kaydırma/yakınlaştırmasıyla gösterir; sahne geometrisi bir kez kurulur ve
/// bölmeler arasında paylaşılır (Win2D'nin ortak aygıtı). Etkin bölme: son tıklanan (mavi çerçeve); Sığdır,
/// yakınlaştır, komut satırı ölçüsü ona uygulanır. Bir komut bir bölmede başlayıp ötekinde bitebilir.
/// Bölme köşesindeki düğme: dört görünümde o bölmeyi tek ekrana büyütür, tek görünümde dörde böler.
/// Fare: tekerlek = imleç noktasında yakınlaştır, orta/sağ tuş sürükle = kaydır, sol tuş = düzenleme (Input).
/// Dokunmatik: iki parmakla kaydır-yakınlaştır.
/// </summary>
public sealed class CadCanvas : Grid
{
    internal CadScene? Scene { get; private set; }
    internal CanvasGeometry?[] BatchGeo = [];
    internal CanvasGeometry?[] FillGeo = [];
    internal float[] TextRefWidth = [];
    internal readonly CanvasStrokeStyle Hairline = new() { TransformBehavior = CanvasStrokeTransformBehavior.Hairline };
    internal readonly CanvasStrokeStyle FixedStroke = new() { TransformBehavior = CanvasStrokeTransformBehavior.Fixed };
    internal readonly CanvasStrokeStyle Dotted = new() { CustomDashStyle = [4f, 3f] };
    internal CanvasTextFormat? LabelFmt;
    private CanvasTextFormat? _measure;
    internal const float CapRatio = 0.70f; // Segoe UI büyük harf yüksekliği / yazı boyutu

    internal Overlay Overlay = Overlay.Empty;
    private CanvasGeometry? _selGeo;
    private float[]? _selGeoSrc;

    private readonly ViewPane[] _panes = new ViewPane[4];
    private readonly SplitHandle _vSplit, _hSplit, _cSplit;
    private int _active;
    private bool _quad;
    private double _fx = 0.5, _fy = 0.5;
    /// <summary>İmlecin üzerinde olduğu bölme (imleç halkası ve etiketi yalnız orada çizilir)</summary>
    internal int Hover = -1;

    public HashSet<int> Hidden { get; } = new();
    /// <summary>Herhangi bir bölmenin görünümü değişti</summary>
    public event Action<ViewState>? ViewChanged;
    /// <summary>Bölme düzeni değişti (dört/tek, etkin bölme, bölme oranları)</summary>
    public event Action? LayoutChanged;
    /// <summary>İmleç dünya koordinatı (göreli) — durum çubuğu için</summary>
    public event Action<double, double>? PointerMovedWorld;
    /// <summary>Sağ tık (sürüklemeden): AutoCAD gibi komutu bitir / vazgeç</summary>
    public event Action? RightClick;
    public ICanvasInput? Input { get; set; }

    private bool _arrow;
    /// <summary>Seç aracında ok imleci; çizim/düzenleme araçlarında artı</summary>
    public bool ArrowCursor
    {
        get => _arrow;
        set { if (_arrow == value) return; _arrow = value; foreach (var p in _panes) p.UpdateCursor(); }
    }

    /// <summary>Bölme adları (köşede küçük yazı)</summary>
    public static readonly string[] PaneNames = ["Görünüm 1", "Görünüm 2", "Görünüm 3", "Görünüm 4"];

    public CadCanvas()
    {
        Background = new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x2F, 0x33)); // bölme araları
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++)
        {
            var p = new ViewPane(this, i);
            SetRow(p, i / 2);
            SetColumn(p, i % 2);
            _panes[i] = p;
            Children.Add(p);
        }
        // bölme çizgileri: sürükleyerek oran değişir (ortadaki kare ikisini birden)
        _vSplit = Splitter(true, false);
        _hSplit = Splitter(false, true);
        _cSplit = Splitter(true, true);
        IsTabStop = true;
        ApplyLayout();
    }

    // ───────────────────────── bölme düzeni ─────────────────────────

    /// <summary>Dört görünüm açık mı</summary>
    public bool Quad
    {
        get => _quad;
        set { if (_quad == value) return; _quad = value; ApplyLayout(); LayoutChanged?.Invoke(); }
    }

    /// <summary>Etkin bölme (0 sol üst, 1 sağ üst, 2 sol alt, 3 sağ alt)</summary>
    public int Active
    {
        get => _active;
        set
        {
            value = Math.Clamp(value, 0, 3);
            if (_active == value) return;
            _active = value;
            if (!_quad) ApplyLayout();
            foreach (var p in _panes) p.UpdateChrome();
            LayoutChanged?.Invoke();
        }
    }

    /// <summary>Dikey ve yatay bölme oranları (0,15…0,85)</summary>
    public (double Fx, double Fy) Split
    {
        get => (_fx, _fy);
        set
        {
            _fx = Math.Clamp(value.Fx, 0.15, 0.85);
            _fy = Math.Clamp(value.Fy, 0.15, 0.85);
            ApplyLayout();
        }
    }

    /// <summary>Dört bölmenin görünümleri (hiç ayarlanmamış bölme null)</summary>
    public ViewState?[] PaneViews => _panes.Select(p => p.HasView ? p.View : (ViewState?)null).ToArray();

    public void SetPaneView(int i, ViewState v) => _panes[i].SetView(v);

    /// <summary>Etkin bölmeyi tek ekrana büyüt ya da dörde böl</summary>
    public void ToggleQuad() => Quad = !Quad;

    /// <summary>Bölmeye özgü düğme: dört görünümde o bölmeyi büyüt, tek görünümde dörde böl</summary>
    internal void PaneButton(int i)
    {
        if (_quad) { _active = i; Quad = false; foreach (var p in _panes) p.UpdateChrome(); }
        else Quad = true;
        Focus(FocusState.Programmatic);
    }

    /// <summary>Sıradaki bölmeyi etkin yap (Ctrl+R, AutoCAD gibi)</summary>
    public void CycleActive() { if (_quad) Active = (_active + 1) % 4; }

    private void ApplyLayout()
    {
        ColumnDefinitions[0].Width = new GridLength(_quad ? _fx : 1, GridUnitType.Star);
        ColumnDefinitions[1].Width = new GridLength(_quad ? 1 - _fx : 0, GridUnitType.Star);
        RowDefinitions[0].Height = new GridLength(_quad ? _fy : 1, GridUnitType.Star);
        RowDefinitions[1].Height = new GridLength(_quad ? 1 - _fy : 0, GridUnitType.Star);
        for (int i = 0; i < 4; i++)
        {
            var p = _panes[i];
            bool shown = _quad || i == _active;
            p.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            SetRow(p, _quad ? i / 2 : 0);
            SetColumn(p, _quad ? i % 2 : 0);
            p.Margin = _quad ? new Thickness(i % 2 == 1 ? 1 : 0, i >= 2 ? 1 : 0, i % 2 == 0 ? 1 : 0, i < 2 ? 1 : 0) : new Thickness(0);
            p.UpdateChrome();
        }
        foreach (var s in new[] { _vSplit, _hSplit, _cSplit }) s.Visibility = _quad ? Visibility.Visible : Visibility.Collapsed;
        PlaceSplitters();
    }

    private SplitHandle Splitter(bool vertical, bool horizontal)
    {
        bool both = vertical && horizontal;
        var b = new SplitHandle(both ? InputSystemCursorShape.SizeAll : vertical ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
        SetRowSpan(b, 2);
        SetColumnSpan(b, 2);
        Canvas.SetZIndex(b, 5);
        bool drag = false;
        b.PointerPressed += (_, e) => { drag = true; b.CapturePointer(e.Pointer); e.Handled = true; };
        b.PointerMoved += (_, e) =>
        {
            if (!drag || ActualWidth <= 0 || ActualHeight <= 0) return;
            var p = e.GetCurrentPoint(this).Position;
            double fx = vertical ? p.X / ActualWidth : _fx, fy = horizontal ? p.Y / ActualHeight : _fy;
            Split = (fx, fy);
            e.Handled = true;
        };
        b.PointerReleased += (_, e) => { if (drag) { drag = false; b.ReleasePointerCapture(e.Pointer); LayoutChanged?.Invoke(); } e.Handled = true; };
        b.PointerCaptureLost += (_, _) => drag = false;
        b.DoubleTapped += (_, e) => { Split = (0.5, 0.5); LayoutChanged?.Invoke(); e.Handled = true; };
        Children.Add(b);
        SizeChanged += (_, _) => PlaceSplitters();
        return b;
    }

    private void PlaceSplitters()
    {
        if (_vSplit == null || _hSplit == null || _cSplit == null) return;
        double w = ActualWidth, h = ActualHeight, x = w * _fx, y = h * _fy;
        const double t = 8; // tutma kalınlığı
        _vSplit.HorizontalAlignment = HorizontalAlignment.Left; _vSplit.VerticalAlignment = VerticalAlignment.Stretch;
        _vSplit.Width = t; _vSplit.Margin = new Thickness(x - t / 2, 0, 0, 0);
        _hSplit.HorizontalAlignment = HorizontalAlignment.Stretch; _hSplit.VerticalAlignment = VerticalAlignment.Top;
        _hSplit.Height = t; _hSplit.Margin = new Thickness(0, y - t / 2, 0, 0);
        _cSplit.HorizontalAlignment = HorizontalAlignment.Left; _cSplit.VerticalAlignment = VerticalAlignment.Top;
        _cSplit.Width = _cSplit.Height = 18; _cSplit.Margin = new Thickness(x - 9, y - 9, 0, 0);
    }

    internal void Activate(int i)
    {
        if (_active == i) return;
        _active = i;
        foreach (var p in _panes) p.UpdateChrome();
        LayoutChanged?.Invoke();
    }

    private ViewPane A => _panes[_active];

    // ───────────────────────── sahne ─────────────────────────

    public void SetScene(CadScene s, ViewState? initial = null)
    {
        Scene = s;
        Overlay = Overlay.Empty;
        BuildGeometry();
        foreach (var p in _panes) p.ClearView();
        if (initial is { Zoom: > 0 } v) A.SetView(v);
        foreach (var p in _panes) if (!p.HasView) p.Fit();
    }

    /// <summary>Düzenlemeden sonra: görünümler korunur</summary>
    public void UpdateScene(CadScene s)
    {
        Scene = s;
        BuildGeometry();
    }

    public void SetOverlay(Overlay o)
    {
        Overlay = o;
        InvalidateAll();
    }

    public void Invalidate() => InvalidateAll();

    private void InvalidateAll()
    {
        foreach (var p in _panes) if (p.Visibility == Visibility.Visible) p.Invalidate();
    }

    internal void PaneDeviceReady() { if (BatchGeo.Length == 0 && Scene != null) BuildGeometry(); }

    internal CanvasDevice? Device
    {
        get
        {
            foreach (var p in _panes) if (p.Device is { } d) return d;
            return null;
        }
    }

    private void BuildGeometry()
    {
        var s = Scene;
        var dev = Device;
        if (s == null || dev == null) return;
        var v = s.Vertices;
        foreach (var g in BatchGeo) g?.Dispose();
        foreach (var g in FillGeo) g?.Dispose();
        // her parti tek yol: parçalar ayrı figür
        BatchGeo = s.Batches.Select(b =>
        {
            if (b.Count < 2) return null;
            using var pb = new CanvasPathBuilder(dev);
            for (int i = b.First; i + 1 < b.First + b.Count; i += 2)
            {
                pb.BeginFigure(v[i * 2], v[i * 2 + 1]);
                pb.AddLine(v[i * 2 + 2], v[i * 2 + 3]);
                pb.EndFigure(CanvasFigureLoop.Open);
            }
            return CanvasGeometry.CreatePath(pb);
        }).ToArray();
        // dolgular: çift-tek kuralı
        FillGeo = s.Fills.Select(f =>
        {
            using var pb = new CanvasPathBuilder(dev);
            pb.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Alternate);
            for (int k = 0; k < f.LoopFirst.Length; k++)
            {
                int a = f.LoopFirst[k];
                pb.BeginFigure(v[a * 2], v[a * 2 + 1]);
                for (int q = 1; q < f.LoopCount[k]; q++) pb.AddLine(v[(a + q) * 2], v[(a + q) * 2 + 1]);
                pb.EndFigure(CanvasFigureLoop.Closed);
            }
            return (CanvasGeometry?)CanvasGeometry.CreatePath(pb);
        }).ToArray();
        // yazı ölçüleri: 100 DIP'lik referans yazıyla (genişlik boyutla doğrusal)
        _measure ??= new CanvasTextFormat { FontFamily = "Segoe UI", FontSize = 100, WordWrapping = CanvasWordWrapping.NoWrap };
        TextRefWidth = s.Texts.Select(t =>
        {
            using var l = new CanvasTextLayout(dev, t.Text, _measure, 100000, 1000);
            return (float)l.LayoutBounds.Width;
        }).ToArray();
        InvalidateAll();
    }

    internal static Color Adapt(uint rgb)
    {
        var r = (byte)(rgb >> 16);
        var g = (byte)(rgb >> 8);
        var b = (byte)rgb;
        // koyu zeminde siyah görünmez: beyaza çevir
        return r * 299 + g * 587 + b * 114 < 40_000 ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, r, g, b);
    }

    internal CanvasGeometry? SelectionGeometry()
    {
        var o = Overlay;
        if (!ReferenceEquals(_selGeoSrc, o.Selection))
        {
            _selGeo?.Dispose();
            _selGeo = Segs(o.Selection);
            _selGeoSrc = o.Selection;
        }
        return _selGeo;
    }

    internal CanvasGeometry? Segs(float[] a)
    {
        var dev = Device;
        if (a.Length < 4 || dev == null) return null;
        using var pb = new CanvasPathBuilder(dev);
        for (int i = 0; i + 3 < a.Length; i += 4)
        {
            pb.BeginFigure(a[i], a[i + 1]);
            pb.AddLine(a[i + 2], a[i + 3]);
            pb.EndFigure(CanvasFigureLoop.Open);
        }
        return CanvasGeometry.CreatePath(pb);
    }

    // ───────────────────────── etkin bölmenin görünümü ─────────────────────────

    public ViewState View => A.View;
    /// <summary>Yakalama/seçim toleransı: 10 piksel (etkin bölme)</summary>
    public double Tolerance => 10 / A.View.Zoom;
    public (double X0, double Y0, double X1, double Y1) ViewWindow() => A.ViewWindow();
    public void SetView(ViewState v) => A.SetView(v);
    public void Fit() => A.Fit();
    public void ZoomBy(double factor) => A.ZoomBy(factor);

    // bölmelerden gelen olaylar
    internal void RaiseView(ViewState v) => ViewChanged?.Invoke(v);
    internal void RaisePointer(double x, double y) => PointerMovedWorld?.Invoke(x, y);
    internal void RaiseRightClick() => RightClick?.Invoke();
}

/// <summary>Bölme çizgisi tutamağı (imleç yalnız alt sınıftan ayarlanabiliyor)</summary>
internal sealed class SplitHandle : Grid
{
    public SplitHandle(InputSystemCursorShape shape)
    {
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ProtectedCursor = InputSystemCursor.Create(shape);
    }
}

/// <summary>Tek görünüm alanı: kendi kaydırma/yakınlaştırması olan çizim yüzeyi</summary>
internal sealed class ViewPane : Grid
{
    private static readonly Color SelColor = Color.FromArgb(255, 0x4F, 0xC3, 0xF7), GhostColor = Color.FromArgb(255, 0xFF, 0xB6, 0x27),
        RubberColor = Color.FromArgb(255, 0x3D, 0xFF, 0x8E), SnapColor = Color.FromArgb(255, 0xFF, 0xD5, 0x4F);

    private readonly CadCanvas _host;
    private readonly int _index;
    private readonly CanvasControl _canvas = new() { ClearColor = Color.FromArgb(255, 11, 14, 16) };
    private readonly Border _frame;
    private readonly Button _button;
    private readonly FontIcon _buttonIcon;
    private readonly TextBlock _name;
    private bool _leftDown;

    public ViewState View { get; private set; } = new(0, 0, 1);
    public bool HasView { get; private set; }

    public ViewPane(CadCanvas host, int index)
    {
        _host = host;
        _index = index;
        Children.Add(_canvas);
        _frame = new Border { BorderThickness = new Thickness(2), IsHitTestVisible = false, BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x4F, 0x8B, 0xF7)) };
        Children.Add(_frame);
        _name = new TextBlock
        {
            Text = CadCanvas.PaneNames[index], FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0x9F, 0xB6, 0xDC)),
        };
        // çizimin üstünde okunsun: yarı saydam koyu zemin
        Children.Add(new Border
        {
            Child = _name, Padding = new Thickness(5, 1, 5, 2), Margin = new Thickness(4), CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0B, 0x0E, 0x10)), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        });
        _buttonIcon = new FontIcon { FontSize = 11 };
        _button = new Button
        {
            Content = _buttonIcon, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(4), MinHeight = 0, MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x14, 0x17, 0x19)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0x9F, 0xB6, 0xDC)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x2F, 0x33)),
        };
        _button.Click += (_, _) => _host.PaneButton(_index);
        Children.Add(_button);

        _canvas.Draw += OnDraw;
        _canvas.CreateResources += (_, _) => _host.PaneDeviceReady();
        PointerWheelChanged += OnWheel;
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerEntered += (_, _) => { _host.Hover = _index; };
        PointerExited += (_, _) => { if (_host.Hover == _index) { _host.Hover = -1; Invalidate(); } };
        PointerCaptureLost += (_, _) =>
        {
            _panning = false;
            if (_leftDown) { _leftDown = false; _host.Input?.Cancel(); }
        };
        DoubleTapped += (_, e) => { if (e.PointerDeviceType == PointerDeviceType.Mouse) return; Fit(); };
        ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale;
        ManipulationDelta += OnManipulation;
        SizeChanged += (_, e) => { if (e.PreviousSize.Width == 0 && !HasView) Fit(); };
        Unloaded += (_, _) => _canvas.RemoveFromVisualTree();
        UpdateCursor();
    }

    public void UpdateCursor() =>
        ProtectedCursor = InputSystemCursor.Create(_host.ArrowCursor ? InputSystemCursorShape.Arrow : InputSystemCursorShape.Cross);

    /// <summary>Çerçeve (etkin bölme) ve köşe düğmesi (büyüt / dörde böl)</summary>
    public void UpdateChrome()
    {
        bool quad = _host.Quad;
        _frame.Visibility = quad && _host.Active == _index ? Visibility.Visible : Visibility.Collapsed;
        _buttonIcon.Glyph = quad ? "" : ""; // tam ekran / ızgara
        bool active = _host.Active == _index;
        _name.Foreground = new SolidColorBrush(quad && active ? Color.FromArgb(255, 0x7F, 0xA8, 0xF8) : Color.FromArgb(0xC0, 0x9F, 0xB6, 0xDC));
        _name.FontWeight = quad && active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        ToolTipService.SetToolTip(_button, quad ? $"Görünüm {_index + 1}: tek ekrana büyüt" : "Dört görünüme böl (AutoCAD görünüm alanları)");
    }

    public CanvasDevice? Device
    {
        get
        {
            // CanvasControl.Device aygıt yokken ArgumentException veriyor (tuval yüklenmeden proje açılınca)
            try { return _canvas.Device; }
            catch (ArgumentException) { return null; }
        }
    }

    public void Invalidate() => _canvas.Invalidate();

    public void ClearView() { HasView = false; View = new(0, 0, 1); }

    public void SetView(ViewState v)
    {
        View = v;
        HasView = true;
        _canvas.Invalidate();
        _host.RaiseView(v);
    }

    public void Fit()
    {
        var s = _host.Scene;
        if (s == null || ActualWidth <= 0) return;
        bool empty = s.EntityCount == 0;
        double w = empty ? 1000 : Math.Max(s.MaxX - s.MinX, 1e-6), h = empty ? 700 : Math.Max(s.MaxY - s.MinY, 1e-6);
        double z = Math.Min(ActualWidth / w, ActualHeight / h) * 0.92;
        SetView(empty ? new ViewState(500, 350, z) : new ViewState((s.MinX + s.MaxX) / 2.0, (s.MinY + s.MaxY) / 2.0, z));
    }

    public (double X0, double Y0, double X1, double Y1) ViewWindow()
    {
        var v = View;
        double hw = ActualWidth / 2 / v.Zoom, hh = ActualHeight / 2 / v.Zoom;
        return (v.Cx - hw, v.Cy - hh, v.Cx + hw, v.Cy + hh);
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    private double Tol => 10 / View.Zoom;

    private (double X, double Y) ToWorld(Point p)
    {
        var v = View;
        return (v.Cx + (p.X - ActualWidth / 2) / v.Zoom, v.Cy - (p.Y - ActualHeight / 2) / v.Zoom);
    }

    private void ZoomAt(Point p, double factor)
    {
        var v = View;
        double nz = Math.Clamp(v.Zoom * factor, 1e-9, 1e7);
        var (wx, wy) = ToWorld(p);
        SetView(new ViewState(wx - (p.X - ActualWidth / 2) / nz, wy + (p.Y - ActualHeight / 2) / nz, nz));
    }

    private Matrix3x2 WorldToScreen()
    {
        var v = View;
        float w = (float)ActualWidth, h = (float)ActualHeight;
        // ekran = (dünya − merkez)·zoom ; Y yukarı → aşağı
        return Matrix3x2.CreateTranslation((float)-v.Cx, (float)-v.Cy) * Matrix3x2.CreateScale((float)v.Zoom, (float)-v.Zoom)
             * Matrix3x2.CreateTranslation(w / 2, h / 2);
    }

    // ───────────────────────── çizim ─────────────────────────

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var h = _host;
        var s = h.Scene;
        if (s == null) return;
        var ds = args.DrawingSession;
        var m = WorldToScreen();
        ds.Transform = m;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;
        var fills = h.FillGeo;
        for (int i = 0; i < fills.Length && i < s.Fills.Count; i++)
        {
            var f = s.Fills[i];
            if (h.Hidden.Contains(f.Layer) || fills[i] == null) continue;
            var c = CadCanvas.Adapt(f.Rgb);
            c.A = (byte)(255 * f.Alpha);
            ds.FillGeometry(fills[i], c);
        }
        // görünür pencere (göreli dünya) — parti sınırlarıyla eleme
        var (vx0, vy0, vx1, vy1) = ViewWindow();
        var batches = h.BatchGeo;
        for (int i = 0; i < batches.Length && i < s.Batches.Count; i++)
        {
            var b = s.Batches[i];
            if (h.Hidden.Contains(b.Layer) || batches[i] == null) continue;
            if (b.MaxX < vx0 || b.MinX > vx1 || b.MaxY < vy0 || b.MinY > vy1) continue;
            ds.DrawGeometry(batches[i], CadCanvas.Adapt(b.Rgb), 1f, h.Hairline);
        }
        // yazılar: ekran uzayında çizilir (küçük yazı tipi boyutlarında DirectWrite bozulmasın)
        ds.Transform = Matrix3x2.Identity;
        var v = View;
        var widths = h.TextRefWidth;
        using var fmt = new CanvasTextFormat { FontFamily = "Segoe UI", WordWrapping = CanvasWordWrapping.NoWrap };
        for (int i = 0; i < s.Texts.Count && i < widths.Length; i++)
        {
            var t = s.Texts[i];
            if (h.Hidden.Contains(t.Layer)) continue;
            float capPx = (float)(t.Height * v.Zoom);
            if (capPx < 3f) continue;
            if (t.X < vx0 - t.Height * 40 || t.X > vx1 + t.Height * 40 || t.Y < vy0 - t.Height * 40 || t.Y > vy1 + t.Height * 40) continue;
            float size = capPx / CadCanvas.CapRatio;
            fmt.FontSize = size;
            float wf = t.WidthFactor > 0 ? t.WidthFactor : 1f;
            float width = widths[i] * size / 100f;
            float ax = t.HAlign == 1 ? -width / 2 : t.HAlign == 2 ? -width : 0;
            // satır kutusunun üstünden taban çizgisine ~0,93·boyut (Segoe UI); dikey hiza TextMesh ile aynı
            float baseline = size * 0.93f;
            float ay = t.VAlign switch { 3 => capPx, 2 => capPx / 2, _ => 0 };
            var p = Vector2.Transform(new Vector2(t.X, t.Y), m);
            ds.Transform = Matrix3x2.CreateTranslation(ax, ay - baseline) * Matrix3x2.CreateScale(wf, 1)
                * Matrix3x2.CreateRotation((float)(-t.Rotation * Math.PI / 180)) * Matrix3x2.CreateTranslation(p);
            ds.DrawText(t.Text.Replace('\n', ' '), 0, 0, CadCanvas.Adapt(t.Rgb), fmt);
        }
        ds.Transform = Matrix3x2.Identity;
        DrawOverlay(ds, m);
    }

    private void DrawOverlay(CanvasDrawingSession ds, Matrix3x2 m)
    {
        var h = _host;
        var o = h.Overlay;
        var sel = h.SelectionGeometry();
        if (sel != null)
        {
            ds.Transform = m;
            ds.DrawGeometry(sel, Color.FromArgb(60, SelColor.R, SelColor.G, SelColor.B), 6f, h.FixedStroke);
            ds.DrawGeometry(sel, SelColor, 1.5f, h.FixedStroke);
            if (o.Ghost is { } g)
            {
                ds.Transform = g * m;
                ds.DrawGeometry(sel, GhostColor, 1.3f, h.FixedStroke);
            }
        }
        ds.Transform = m;
        if (o.Rubber is { Length: >= 4 } r)
        {
            using var rg = h.Segs(r);
            if (rg != null) ds.DrawGeometry(rg, RubberColor, 1.3f, h.FixedStroke);
        }
        ds.Transform = Matrix3x2.Identity;
        if (o.Guides is { Length: >= 4 } gd)
        {
            var gc = Color.FromArgb(220, 0x7C, 0xE8, 0xB0);
            for (int i = 0; i + 3 < gd.Length; i += 4)
                ds.DrawLine(Vector2.Transform(new(gd[i], gd[i + 1]), m), Vector2.Transform(new(gd[i + 2], gd[i + 3]), m), gc, 1f, h.Dotted);
        }
        if (o.Marks is { Length: >= 2 } mk)
        {
            var mc = Color.FromArgb(255, 0x7C, 0xE8, 0xB0);
            for (int i = 0; i + 1 < mk.Length; i += 2)
            {
                var p = Vector2.Transform(new(mk[i], mk[i + 1]), m);
                ds.DrawLine(p.X - 4, p.Y, p.X + 4, p.Y, mc, 1.2f);
                ds.DrawLine(p.X, p.Y - 4, p.X, p.Y + 4, mc, 1.2f);
            }
        }
        if (o.Box is { Length: 4 } b)
        {
            var p0 = Vector2.Transform(new(b[0], b[1]), m);
            var p1 = Vector2.Transform(new(b[2], b[3]), m);
            var rect = new Rect(Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y), Math.Abs(p1.X - p0.X), Math.Abs(p1.Y - p0.Y));
            // AutoCAD gibi: pencere mavi, kesişen yeşil ve kesikli
            var c = o.BoxCrossing ? Color.FromArgb(255, 0x3D, 0xD6, 0x6E) : Color.FromArgb(255, 0x4F, 0x8B, 0xF7);
            ds.FillRectangle(rect, Color.FromArgb(40, c.R, c.G, c.B));
            using var dash = new CanvasStrokeStyle { DashStyle = o.BoxCrossing ? CanvasDashStyle.Dash : CanvasDashStyle.Solid };
            ds.DrawRectangle(rect, c, 1f, dash);
        }
        // yakalama işareti, imleç ve etiketi yalnız imlecin olduğu bölmede (tek görünümde her zaman)
        bool here = !h.Quad || h.Hover == _index || (h.Hover < 0 && h.Active == _index);
        if (!here) return;
        if (o.SnapType > 0)
        {
            var p = Vector2.Transform(o.Snap, m);
            float k = 7;
            switch (o.SnapType)
            {
                case 2: // orta: üçgen
                    ds.DrawLine(p.X - k, p.Y + k, p.X + k, p.Y + k, SnapColor, 2);
                    ds.DrawLine(p.X + k, p.Y + k, p.X, p.Y - k, SnapColor, 2);
                    ds.DrawLine(p.X, p.Y - k, p.X - k, p.Y + k, SnapColor, 2);
                    break;
                case 3: ds.DrawCircle(p, k, SnapColor, 2); break; // merkez
                case 4: // ekleme noktası: çarpı
                    ds.DrawLine(p.X - k, p.Y - k, p.X + k, p.Y + k, SnapColor, 2);
                    ds.DrawLine(p.X - k, p.Y + k, p.X + k, p.Y - k, SnapColor, 2);
                    break;
                default: ds.DrawRectangle(p.X - k, p.Y - k, 2 * k, 2 * k, SnapColor, 2); break; // uç
            }
        }
        if (o.Cursor is { } cur)
        {
            var p = Vector2.Transform(cur, m);
            ds.DrawCircle(p, 3.5f, RubberColor, 1.5f);
            if (!string.IsNullOrEmpty(o.Label))
            {
                h.LabelFmt ??= new CanvasTextFormat { FontFamily = "Segoe UI", FontSize = 12 };
                using var tl = new CanvasTextLayout(ds, o.Label, h.LabelFmt, 400, 40);
                var lb = tl.LayoutBounds;
                float lx = p.X + 14, ly = p.Y + 12;
                ds.FillRoundedRectangle(lx - 5, ly - 2, (float)lb.Width + 10, (float)lb.Height + 4, 4, 4, Color.FromArgb(225, 0x10, 0x22, 0x1A));
                ds.DrawTextLayout(tl, lx, ly, Color.FromArgb(255, 0x7C, 0xE8, 0xB0));
            }
        }
    }

    // ───────────────────────── giriş ─────────────────────────

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        int delta = pt.Properties.MouseWheelDelta;
        ZoomAt(pt.Position, Math.Pow(1.0015, delta)); // 120 birim ≈ %20
        e.Handled = true;
    }

    private bool _panning;
    private Point _last;
    /// <summary>Sağ tuş basıldı: sürüklenmezse "tık" sayılır (Esc/Enter), sürüklenirse kaydırma</summary>
    private bool _rightDown;
    private Point _rightStart;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInButton(d)) return;
        var pt = e.GetCurrentPoint(this);
        _host.Activate(_index);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch) return; // dokunma: manipülasyon
        _host.Focus(FocusState.Pointer);
        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            _panning = true;
            _last = pt.Position;
            _rightDown = pt.Properties.IsRightButtonPressed;
            _rightStart = pt.Position;
            CapturePointer(e.Pointer);
            e.Handled = true;
        }
        else if (pt.Properties.IsLeftButtonPressed && _host.Input != null)
        {
            _leftDown = true;
            CapturePointer(e.Pointer);
            var (wx, wy) = ToWorld(pt.Position);
            bool shift = (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0;
            _host.Input.Press(wx, wy, Tol, shift);
            e.Handled = true;
        }
    }

    private bool IsInButton(DependencyObject d)
    {
        for (var x = d; x != null && x != this; x = VisualTreeHelper.GetParent(x))
            if (x == _button) return true;
        return false;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Position;
        var (wx, wy) = ToWorld(p);
        _host.Hover = _index;
        _host.RaisePointer(wx, wy);
        if (!_panning)
        {
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch) _host.Input?.Move(wx, wy, Tol, _leftDown);
            return;
        }
        var v = View;
        SetView(v with { Cx = v.Cx - (p.X - _last.X) / v.Zoom, Cy = v.Cy + (p.Y - _last.Y) / v.Zoom });
        _last = p;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            ReleasePointerCapture(e.Pointer);
            var p = e.GetCurrentPoint(this).Position;
            if (_rightDown && Math.Abs(p.X - _rightStart.X) + Math.Abs(p.Y - _rightStart.Y) < 5) _host.RaiseRightClick();
            _rightDown = false;
        }
        else if (_leftDown)
        {
            _leftDown = false;
            var (wx, wy) = ToWorld(e.GetCurrentPoint(this).Position);
            ReleasePointerCapture(e.Pointer);
            _host.Input?.Release(wx, wy, Tol);
        }
    }

    private void OnManipulation(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (e.PointerDeviceType != PointerDeviceType.Touch) return;
        var d = e.Delta;
        var v = View;
        SetView(v with { Cx = v.Cx - d.Translation.X / v.Zoom, Cy = v.Cy + d.Translation.Y / v.Zoom });
        if (Math.Abs(d.Scale - 1) > 1e-4) ZoomAt(e.Position, d.Scale);
    }
}
