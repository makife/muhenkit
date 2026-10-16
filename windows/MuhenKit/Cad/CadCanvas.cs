using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
/// DXF çizim tuvali (Win2D / Direct2D). Sahne bir kez geometriye çevrilir; her karede yalnızca
/// dönüşüm değişir. Çizgiler "hairline": her yakınlaştırmada 1 piksel.
/// Fare: tekerlek = imleç noktasında yakınlaştır, orta/sağ tuş sürükle = kaydır, sol tuş = düzenleme (Input).
/// Dokunmatik: iki parmakla kaydır-yakınlaştır.
/// </summary>
public sealed class CadCanvas : Grid
{
    private readonly CanvasControl _canvas = new() { ClearColor = Color.FromArgb(255, 11, 14, 16) };
    private CadScene? _scene;
    private CanvasGeometry?[] _batchGeo = [];
    private CanvasGeometry?[] _fillGeo = [];
    private float[] _textRefWidth = [];
    private readonly CanvasStrokeStyle _hairline = new() { TransformBehavior = CanvasStrokeTransformBehavior.Hairline };
    private readonly CanvasStrokeStyle _fixed = new() { TransformBehavior = CanvasStrokeTransformBehavior.Fixed };
    private readonly CanvasStrokeStyle _dotted = new() { CustomDashStyle = [4f, 3f] };
    private Microsoft.Graphics.Canvas.Text.CanvasTextFormat? _labelFmt;
    private CanvasTextFormat? _measure;
    private const float CapRatio = 0.70f; // Segoe UI büyük harf yüksekliği / yazı boyutu

    private Overlay _overlay = Overlay.Empty;
    private CanvasGeometry? _selGeo;
    private float[]? _selGeoSrc;
    private bool _leftDown;

    public ViewState View { get; private set; } = new(0, 0, 1);
    public HashSet<int> Hidden { get; } = new();
    public event Action<ViewState>? ViewChanged;
    /// <summary>İmleç dünya koordinatı (göreli) — durum çubuğu için</summary>
    public event Action<double, double>? PointerMovedWorld;
    public ICanvasInput? Input { get; set; }

    public CadCanvas()
    {
        Children.Add(_canvas);
        _canvas.Draw += OnDraw;
        _canvas.CreateResources += (_, _) => BuildGeometry();
        PointerWheelChanged += OnWheel;
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) =>
        {
            _panning = false;
            if (_leftDown) { _leftDown = false; Input?.Cancel(); }
        };
        DoubleTapped += (_, e) => { if (e.PointerDeviceType == PointerDeviceType.Mouse) return; Fit(); };
        ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale;
        ManipulationDelta += OnManipulation;
        SizeChanged += (_, e) => { if (e.PreviousSize.Width == 0) Fit(); };
        Unloaded += (_, _) => _canvas.RemoveFromVisualTree();
        IsTabStop = true;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
    }

    public void SetScene(CadScene s, ViewState? initial = null)
    {
        _scene = s;
        _overlay = Overlay.Empty;
        BuildGeometry();
        if (initial is { Zoom: > 0 } v) SetView(v);
        else Fit();
    }

    /// <summary>Düzenlemeden sonra: görünüm korunur</summary>
    public void UpdateScene(CadScene s)
    {
        _scene = s;
        BuildGeometry();
    }

    public void SetOverlay(Overlay o)
    {
        _overlay = o;
        _canvas.Invalidate();
    }

    public void Invalidate() => _canvas.Invalidate();

    /// <summary>Yakalama/seçim toleransı: 10 piksel</summary>
    public double Tolerance => 10 / View.Zoom;

    /// <summary>Ekrandaki dünya penceresi (göreli)</summary>
    public (double X0, double Y0, double X1, double Y1) ViewWindow()
    {
        var v = View;
        double hw = ActualWidth / 2 / v.Zoom, hh = ActualHeight / 2 / v.Zoom;
        return (v.Cx - hw, v.Cy - hh, v.Cx + hw, v.Cy + hh);
    }

    /// <summary>
    /// Tuvalin grafik aygıtı; henüz oluşmadıysa null (CanvasControl.Device bu durumda null değil ArgumentException veriyor:
    /// proje tuval yüklenmeden hızlı açılınca "Dosya açılamadı: Parametre hatalı"). Aygıt gelince CreateResources geometriyi kurar.
    /// </summary>
    private CanvasDevice? Device
    {
        get
        {
            try { return _canvas.Device; }
            catch (ArgumentException) { return null; }
        }
    }

    private void BuildGeometry()
    {
        var s = _scene;
        var dev = Device;
        if (s == null || dev == null) return;
        var v = s.Vertices;
        foreach (var g in _batchGeo) g?.Dispose();
        foreach (var g in _fillGeo) g?.Dispose();
        // her parti tek yol: parçalar ayrı figür
        _batchGeo = s.Batches.Select(b =>
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
        _fillGeo = s.Fills.Select(f =>
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
        _textRefWidth = s.Texts.Select(t =>
        {
            using var l = new CanvasTextLayout(dev, t.Text, _measure, 100000, 1000);
            return (float)l.LayoutBounds.Width;
        }).ToArray();
        _canvas.Invalidate();
    }

    private static Color Adapt(uint rgb)
    {
        var r = (byte)(rgb >> 16);
        var g = (byte)(rgb >> 8);
        var b = (byte)rgb;
        // koyu zeminde siyah görünmez: beyaza çevir
        return r * 299 + g * 587 + b * 114 < 40_000 ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, r, g, b);
    }

    private Matrix3x2 WorldToScreen()
    {
        var v = View;
        float w = (float)ActualWidth, h = (float)ActualHeight;
        // ekran = (dünya − merkez)·zoom ; Y yukarı → aşağı
        return Matrix3x2.CreateTranslation((float)-v.Cx, (float)-v.Cy) * Matrix3x2.CreateScale((float)v.Zoom, (float)-v.Zoom)
             * Matrix3x2.CreateTranslation(w / 2, h / 2);
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var s = _scene;
        if (s == null) return;
        var ds = args.DrawingSession;
        var m = WorldToScreen();
        ds.Transform = m;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;
        for (int i = 0; i < _fillGeo.Length && i < s.Fills.Count; i++)
        {
            var f = s.Fills[i];
            if (Hidden.Contains(f.Layer) || _fillGeo[i] == null) continue;
            var c = Adapt(f.Rgb);
            c.A = (byte)(255 * f.Alpha);
            ds.FillGeometry(_fillGeo[i], c);
        }
        // görünür pencere (göreli dünya) — parti sınırlarıyla eleme
        var (vx0, vy0, vx1, vy1) = ViewWindow();
        for (int i = 0; i < _batchGeo.Length && i < s.Batches.Count; i++)
        {
            var b = s.Batches[i];
            if (Hidden.Contains(b.Layer) || _batchGeo[i] == null) continue;
            if (b.MaxX < vx0 || b.MinX > vx1 || b.MaxY < vy0 || b.MinY > vy1) continue;
            ds.DrawGeometry(_batchGeo[i], Adapt(b.Rgb), 1f, _hairline);
        }
        // yazılar: ekran uzayında çizilir (küçük yazı tipi boyutlarında DirectWrite bozulmasın)
        ds.Transform = Matrix3x2.Identity;
        var v = View;
        using var fmt = new CanvasTextFormat { FontFamily = "Segoe UI", WordWrapping = CanvasWordWrapping.NoWrap };
        for (int i = 0; i < s.Texts.Count && i < _textRefWidth.Length; i++)
        {
            var t = s.Texts[i];
            if (Hidden.Contains(t.Layer)) continue;
            float capPx = (float)(t.Height * v.Zoom);
            if (capPx < 3f) continue;
            if (t.X < vx0 - t.Height * 40 || t.X > vx1 + t.Height * 40 || t.Y < vy0 - t.Height * 40 || t.Y > vy1 + t.Height * 40) continue;
            float size = capPx / CapRatio;
            fmt.FontSize = size;
            float wf = t.WidthFactor > 0 ? t.WidthFactor : 1f;
            float width = _textRefWidth[i] * size / 100f;
            float ax = t.HAlign == 1 ? -width / 2 : t.HAlign == 2 ? -width : 0;
            // satır kutusunun üstünden taban çizgisine ~0,93·boyut (Segoe UI); dikey hiza TextMesh ile aynı
            float baseline = size * 0.93f;
            float ay = t.VAlign switch { 3 => capPx, 2 => capPx / 2, _ => 0 };
            var p = Vector2.Transform(new Vector2(t.X, t.Y), m);
            ds.Transform = Matrix3x2.CreateTranslation(ax, ay - baseline) * Matrix3x2.CreateScale(wf, 1)
                * Matrix3x2.CreateRotation((float)(-t.Rotation * Math.PI / 180)) * Matrix3x2.CreateTranslation(p);
            ds.DrawText(t.Text.Replace('\n', ' '), 0, 0, Adapt(t.Rgb), fmt);
        }
        ds.Transform = Matrix3x2.Identity;
        DrawOverlay(ds, m);
    }

    // ───────────────────────── düzenleme katmanı ─────────────────────────

    private static readonly Color SelColor = Color.FromArgb(255, 0x4F, 0xC3, 0xF7), GhostColor = Color.FromArgb(255, 0xFF, 0xB6, 0x27),
        RubberColor = Color.FromArgb(255, 0x3D, 0xFF, 0x8E), SnapColor = Color.FromArgb(255, 0xFF, 0xD5, 0x4F);

    private CanvasGeometry? Segs(float[] a)
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

    private void DrawOverlay(CanvasDrawingSession ds, Matrix3x2 m)
    {
        var o = _overlay;
        if (!ReferenceEquals(_selGeoSrc, o.Selection))
        {
            _selGeo?.Dispose();
            _selGeo = Segs(o.Selection);
            _selGeoSrc = o.Selection;
        }
        if (_selGeo != null)
        {
            ds.Transform = m;
            ds.DrawGeometry(_selGeo, Color.FromArgb(60, SelColor.R, SelColor.G, SelColor.B), 6f, _fixed);
            ds.DrawGeometry(_selGeo, SelColor, 1.5f, _fixed);
            if (o.Ghost is { } g)
            {
                ds.Transform = g * m;
                ds.DrawGeometry(_selGeo, GhostColor, 1.3f, _fixed);
            }
        }
        ds.Transform = m;
        if (o.Rubber is { Length: >= 4 } r)
        {
            using var rg = Segs(r);
            if (rg != null) ds.DrawGeometry(rg, RubberColor, 1.3f, _fixed);
        }
        ds.Transform = Matrix3x2.Identity;
        if (o.Guides is { Length: >= 4 } gd)
        {
            var gc = Color.FromArgb(220, 0x7C, 0xE8, 0xB0);
            for (int i = 0; i + 3 < gd.Length; i += 4)
                ds.DrawLine(Vector2.Transform(new(gd[i], gd[i + 1]), m), Vector2.Transform(new(gd[i + 2], gd[i + 3]), m), gc, 1f, _dotted);
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
                _labelFmt ??= new Microsoft.Graphics.Canvas.Text.CanvasTextFormat { FontFamily = "Segoe UI", FontSize = 12 };
                using var tl = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(ds, o.Label, _labelFmt, 400, 40);
                var lb = tl.LayoutBounds;
                float lx = p.X + 14, ly = p.Y + 12;
                ds.FillRoundedRectangle(lx - 5, ly - 2, (float)lb.Width + 10, (float)lb.Height + 4, 4, 4, Color.FromArgb(225, 0x10, 0x22, 0x1A));
                ds.DrawTextLayout(tl, lx, ly, Color.FromArgb(255, 0x7C, 0xE8, 0xB0));
            }
        }
    }

    // ───────────────────────── görünüm ─────────────────────────

    public void SetView(ViewState v)
    {
        View = v;
        _canvas.Invalidate();
        ViewChanged?.Invoke(v);
    }

    public void Fit()
    {
        var s = _scene;
        if (s == null || ActualWidth <= 0) return;
        bool empty = s.EntityCount == 0;
        double w = empty ? 1000 : Math.Max(s.MaxX - s.MinX, 1e-6), h = empty ? 700 : Math.Max(s.MaxY - s.MinY, 1e-6);
        double z = Math.Min(ActualWidth / w, ActualHeight / h) * 0.92;
        SetView(empty ? new ViewState(500, 350, z) : new ViewState((s.MinX + s.MaxX) / 2.0, (s.MinY + s.MaxY) / 2.0, z));
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    public (double X, double Y) ToWorld(Point p)
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
    /// <summary>Sağ tık (sürüklemeden): AutoCAD gibi komutu bitir / vazgeç</summary>
    public event Action? RightClick;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch) return; // dokunma: manipülasyon
        Focus(FocusState.Pointer);
        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            _panning = true;
            _last = pt.Position;
            _rightDown = pt.Properties.IsRightButtonPressed;
            _rightStart = pt.Position;
            CapturePointer(e.Pointer);
            e.Handled = true;
        }
        else if (pt.Properties.IsLeftButtonPressed && Input != null)
        {
            _leftDown = true;
            CapturePointer(e.Pointer);
            var (wx, wy) = ToWorld(pt.Position);
            bool shift = (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0;
            Input.Press(wx, wy, Tolerance, shift);
            e.Handled = true;
        }
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Position;
        var (wx, wy) = ToWorld(p);
        PointerMovedWorld?.Invoke(wx, wy);
        if (!_panning)
        {
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch) Input?.Move(wx, wy, Tolerance, _leftDown);
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
            if (_rightDown && Math.Abs(p.X - _rightStart.X) + Math.Abs(p.Y - _rightStart.Y) < 5) RightClick?.Invoke();
            _rightDown = false;
        }
        else if (_leftDown)
        {
            _leftDown = false;
            var (wx, wy) = ToWorld(e.GetCurrentPoint(this).Position);
            ReleasePointerCapture(e.Pointer);
            Input?.Release(wx, wy, Tolerance);
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
