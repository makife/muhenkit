using System.Globalization;
using System.Numerics;
using MuhenKit.Core;

namespace MuhenKit.Cad;

public enum CadTool { Select, Move, Copy, Line, PLine, Circle, Rect, Offset, Symbol, Text, Measure, Rotate, Scale, Mirror, Dimension }

/// <summary>
/// DXF düzenleyicisi (masaüstü): araçlar tıkla-tıkla çalışır, imleç hareketinde canlı önizleme,
/// basılı sürükleme de olur. Komut satırı AutoCAD kısaltmalarını ve sayısal girişi anlar.
/// Android CadEditor ile aynı çekirdek çağrıları; koordinatlar göreli (köken çıkarılmış).
/// </summary>
public sealed class CadEditor : ICanvasInput
{
    public static readonly Dictionary<CadTool, (string Label, string Glyph)> ToolInfo = new()
    {
        [CadTool.Select] = ("Seç", ""),
        [CadTool.Move] = ("Taşı", ""),
        [CadTool.Copy] = ("Kopyala", ""),
        [CadTool.Line] = ("Çizgi", ""),
        [CadTool.PLine] = ("Çoklu çizgi", ""),
        [CadTool.Circle] = ("Daire", ""),
        [CadTool.Rect] = ("Dikdörtgen", ""),
        [CadTool.Offset] = ("Ötele", ""),
        [CadTool.Symbol] = ("Sembol", ""),
        [CadTool.Text] = ("Yazı", ""),
        [CadTool.Measure] = ("Ölç", ""),
        [CadTool.Rotate] = ("Döndür", ""),
        [CadTool.Scale] = ("Ölçekle", ""),
        [CadTool.Dimension] = ("Ölçü", "\uE1D9"),
        [CadTool.Mirror] = ("Ayna", ""),
    };

    private static readonly Dictionary<string, string> TypeNames = new()
    {
        ["LINE"] = "Çizgi", ["LWPOLYLINE"] = "Çoklu çizgi", ["POLYLINE"] = "Çoklu çizgi", ["CIRCLE"] = "Daire", ["ARC"] = "Yay",
        ["ELLIPSE"] = "Elips", ["TEXT"] = "Yazı", ["MTEXT"] = "Çok satırlı yazı", ["INSERT"] = "Blok", ["HATCH"] = "Tarama",
        ["SOLID"] = "Dolgu", ["SPLINE"] = "Eğri", ["DIMENSION"] = "Ölçü", ["POINT"] = "Nokta",
    };

    private readonly CadCanvas _canvas;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui;

    public CadDocument? Doc { get; private set; }
    public CadTool Tool { get; private set; } = CadTool.Select;
    public HashSet<int> Selection { get; private set; } = new();
    public string? SelectionInfo { get; private set; }
    public bool Busy { get; private set; }
    public string ActiveLayer { get; set; } = "0";
    public bool Ortho { get; set; }
    public bool SnapOn { get; set; } = true;
    public bool AngleSnap { get; set; } = true;
    public bool MirrorKeep { get; set; } = true;
    public double TextHeight { get; set; } = 2.5;
    public double OffsetDist { get; set; } = 1;
    public CadSymbol? Symbol { get; private set; }
    public double SymbolScale { get; set; } = 1;
    /// <summary>Tablolar çizim biriminde üretilir: ölçek 1 sabit</summary>
    private double? _fixedScale;
    private double PlaceScale => _fixedScale ?? SymbolScale;
    public double SymbolRot { get; set; }
    public int UndoCount { get; private set; }
    public int RedoCount { get; private set; }
    public bool Dirty { get; private set; }
    /// <summary>Canlı ölçü / ipucu (durum çubuğu)</summary>
    public string? Live { get; private set; }

    /// <summary>Durum değişti (araç, seçim, sayaçlar): arayüz yenilensin</summary>
    public event Action? Changed;
    /// <summary>Kısa bildirim</summary>
    public event Action<string>? Message;
    /// <summary>Sahne yeniden kuruldu (katman listesi vb.)</summary>
    public event Action<CadScene>? SceneChanged;
    /// <summary>Yazı aracı: bu noktaya yazı girilsin</summary>
    public event Action<double, double>? TextRequested;

    private float[] _selGeom = [];
    private (double X, double Y)? _anchor;      // çizim ilk noktası, taşı/kopya tabanı, ayna/ölç ilk nokta
    private (double X, double Y)? _pivot;       // döndür/ölçekle merkezi
    private (double X, double Y)? _scaleRef;    // ölçekle referans noktası
    private (double X, double Y)? _dimP2;       // ölçü: ikinci nokta
    // hizalama izleri (AutoCAD "nesne yakalama izleme"): üzerinden geçilen yakalama noktaları ve son çizgi parçası
    private readonly List<(double X, double Y)> _acq = new();
    private ((double X, double Y) A, (double X, double Y) B)? _acqSeg;
    private (double X, double Y)? _lastAcq;
    private float[]? _guides;
    private string? _trackLabel;
    private double _tol = 1;
    public bool Tracking { get; set; } = true;
    private readonly List<(double X, double Y)> _pline = new();
    private int? _offsetTarget;
    private (int Kind, double X, double Y) _press;
    private (double X, double Y) _hover;
    private bool _dragging;
    private float[]? _boxStart;

    public CadEditor(CadCanvas canvas)
    {
        _canvas = canvas;
        _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        canvas.Input = this;
    }

    public void Attach(CadDocument doc)
    {
        Doc = doc;
        var s = doc.Scene;
        ActiveLayer = s.Layers.Any(l => l.Name == "0") ? "0" : s.Layers.FirstOrDefault()?.Name ?? "0";
        var hs = s.Texts.Select(t => (double)t.Height).Where(h => h > 0).Order().ToList();
        TextHeight = hs.Count == 0 ? 10 : Math.Round(hs[hs.Count / 2], 2);
        // semboller örnek projenin biriminde: yazı yüksekliği oranıyla bu çizime uydur
        SymbolScale = TextHeight / SymbolLibrary.RefTextHeight is var k && double.IsFinite(k) && k > 0 ? Math.Round(k, 3) : 1;
        OffsetDist = Math.Round(TextHeight * 2, 2);
        Selection = new();
        _selGeom = [];
        SelectionInfo = null;
        Tool = CadTool.Select;
        ResetSteps();
        Refresh();
        Overlay();
        Changed?.Invoke();
    }

    // ───────────────────────── araç seçimi ─────────────────────────

    public static bool NeedsSelection(CadTool t) => t is CadTool.Move or CadTool.Copy or CadTool.Rotate or CadTool.Scale or CadTool.Mirror;

    public string Hint => Tool switch
    {
        CadTool.Select => "Nesneye tıklayın (Shift ile çıkarın) · boşlukta sürükleyin: soldan sağa pencere, sağdan sola kesişen",
        CadTool.Move or CadTool.Copy => _anchor == null ? "Taban noktasını tıklayın" : "Hedef noktayı tıklayın ya da @dx,dy / uzaklık yazın" + (Tool == CadTool.Copy ? " · Esc: bitir" : ""),
        CadTool.Line => _anchor == null ? "İlk noktayı tıklayın" : "Sonraki nokta: tıklayın ya da uzunluk, uzunluk<açı, @dx,dy yazın · Enter/Esc: bitir",
        CadTool.PLine => _pline.Count == 0 ? "İlk noktayı tıklayın" : "Sonraki nokta · Enter: bitir · K yazın: kapat",
        CadTool.Circle => _anchor == null ? "Merkezi tıklayın" : "Yarıçap noktasını tıklayın ya da yarıçap yazın",
        CadTool.Rect => _anchor == null ? "İlk köşeyi tıklayın" : "Karşı köşeyi tıklayın ya da genişlik,yükseklik yazın",
        CadTool.Offset => _offsetTarget == null ? $"Ötelenecek nesneye tıklayın · uzaklık {Fmt(OffsetDist)} (yazarak değiştirin)" : "Hangi tarafa? Tıklayın",
        CadTool.Symbol => $"{Symbol?.Name}: yerleştirmek için tıklayın · açı {SymbolRot:0}° (R ile 90° döndürün) · Esc: bitir",
        CadTool.Text => "Yazının yerleşeceği noktaya tıklayın",
        CadTool.Measure => _anchor == null ? "İlk noktayı tıklayın" : "İkinci noktayı tıklayın",
        CadTool.Dimension => _anchor == null ? "Ölçülecek ilk noktayı tıklayın (uçlara yakalar)" : _dimP2 == null ? "İkinci noktayı tıklayın" : "Ölçü çizgisinin yerini tıklayın ya da uzaklık yazın",
        CadTool.Rotate => _pivot == null ? "Dönme merkezini tıklayın" : "Açıyı tıklayın ya da derece yazın",
        CadTool.Scale => _pivot == null ? "Ölçekleme merkezini tıklayın" : _scaleRef == null ? "Ölçek katsayısı yazın ya da referans noktasını tıklayın" : "Yeni noktayı tıklayın",
        CadTool.Mirror => _anchor == null ? "Ayna ekseninin ilk noktasını tıklayın" : "İkinci noktayı tıklayın",
        _ => "",
    };

    public void SelectTool(CadTool t)
    {
        if (NeedsSelection(t) && Selection.Count == 0)
        {
            Message?.Invoke($"Önce {ToolInfo[t].Label.ToLower(new CultureInfo("tr-TR"))} için nesne seçin.");
            t = CadTool.Select;
        }
        if (t == CadTool.Symbol && Symbol == null)
        {
            Message?.Invoke("Sağ panelden bir sembol seçin.");
            return;
        }
        Tool = t;
        ResetSteps();
        if (t == CadTool.Offset && Selection.Count > 0) ClearSelectionSilently();
        Overlay();
        Changed?.Invoke();
    }

    public void StartSymbol(CadSymbol s)
    {
        Symbol = s;
        _fixedScale = null;
        SelectTool(CadTool.Symbol);
    }

    /// <summary>Tablo / antet: sol üst köşesinden, ölçeksiz ve döndürmesiz yerleştirilir</summary>
    public void StartTable(CadSymbol s)
    {
        Symbol = s;
        SymbolRot = 0;
        _fixedScale = 1;
        SelectTool(CadTool.Symbol);
    }

    private void ResetSteps()
    {
        _anchor = null;
        _pivot = null;
        _scaleRef = null;
        _pline.Clear();
        _offsetTarget = null;
        _dimP2 = null;
        _acq.Clear();
        _acqSeg = null;
        _lastAcq = null;
        _guides = null;
        _trackLabel = null;
        _boxStart = null;
        _dragging = false;
        Live = null;
    }

    /// <summary>Esc: önce yarım adımı, sonra aracı, en son seçimi bırak</summary>
    public void Escape()
    {
        if (Tool == CadTool.PLine && _pline.Count >= 2) { FinishPolyline(false); return; }
        if (_anchor != null || _pivot != null || _pline.Count > 0 || _offsetTarget != null || _dimP2 != null)
        {
            bool copyChain = Tool == CadTool.Copy && _anchor != null;
            // yarım adımı bırak ama hizalama referanslarını koru (aynı araçla devam edilebilir)
            var keep = _acq.ToList();
            var seg = _acqSeg;
            ResetSteps();
            _acq.AddRange(keep);
            _acqSeg = seg;
            if (copyChain) Tool = CadTool.Select;
        }
        else if (Tool != CadTool.Select) { ResetSteps(); Tool = CadTool.Select; }
        else ClearSelectionSilently();
        Overlay();
        Changed?.Invoke();
    }

    /// <summary>Sağ tık: çizgi zinciri / çoklu çizgi sürüyorsa bitir (Enter), değilse Esc</summary>
    public void RightClick()
    {
        if ((Tool == CadTool.Line && _anchor != null) || (Tool == CadTool.PLine && _pline.Count > 0)) Enter();
        else Escape();
    }

    /// <summary>Enter: çizgi zincirini / çoklu çizgiyi bitir</summary>
    public void Enter()
    {
        if (Tool == CadTool.PLine) { FinishPolyline(false); return; }
        if (Tool == CadTool.Line) { _anchor = null; Live = null; Overlay(); Changed?.Invoke(); }
    }

    public void SelectAll()
    {
        var d = Doc;
        if (d == null) return;
        var s = d.Scene;
        var ids = d.SelectRect(s.MinX - 1, s.MinY - 1, s.MaxX + 1, s.MaxY + 1, false);
        SetSelection(ids, $"{ids.Length} nesne seçili");
    }

    private void ClearSelectionSilently()
    {
        Selection = new();
        _selGeom = [];
        SelectionInfo = null;
    }

    public void ClearSelection()
    {
        ClearSelectionSilently();
        if (NeedsSelection(Tool)) Tool = CadTool.Select;
        ResetSteps();
        Overlay();
        Changed?.Invoke();
    }

    private void SetSelection(IEnumerable<int> ids, string? info)
    {
        var d = Doc;
        if (d == null) return;
        Selection = ids.ToHashSet();
        _selGeom = d.Geometry(Selection);
        SelectionInfo = Selection.Count == 0 ? null : info ?? $"{Selection.Count} nesne seçili";
        Overlay();
        Changed?.Invoke();
    }

    // ───────────────────────── seçim üzerindeki işlemler ─────────────────────────

    public void DeleteSelection()
    {
        var ids = Selection.ToList();
        if (ids.Count == 0) return;
        ClearSelectionSilently();
        if (NeedsSelection(Tool)) Tool = CadTool.Select;
        ResetSteps();
        Edit(d => d.Delete(ids));
    }

    public void Undo() => Edit(d => d.Undo(), reselect: true);
    public void Redo() => Edit(d => d.Redo(), reselect: true);

    public void GroupSelection()
    {
        var ids = Selection.ToList();
        if (ids.Count < 2) { Message?.Invoke("Gruplamak için en az iki nesne seçin."); return; }
        int newId = -1;
        string err = "";
        Edit(d => { newId = d.Group(ids); if (newId < 0) err = d.LastError; }, after: () =>
        {
            if (newId >= 0) SetSelection([newId], $"1 grup ({ids.Count} nesne)");
            else Message?.Invoke(err.Length > 0 ? err : "Gruplanamadı.");
        });
    }

    public void ExplodeSelection()
    {
        var ids = Selection.ToList();
        if (ids.Count == 0) return;
        List<int> parts = [];
        Edit(d => parts = d.Explode(ids), after: () =>
        {
            if (parts.Count == 0) Message?.Invoke("Seçimde çözülecek grup ya da blok yok.");
            else SetSelection(parts, $"{parts.Count} parça");
        });
    }

    public void ApplyLayer(string layer)
    {
        var ids = Selection.ToList();
        if (ids.Count > 0) Edit(d => d.SetLayer(ids, layer));
    }

    public void ApplyColor(int aci)
    {
        var ids = Selection.ToList();
        if (ids.Count > 0) Edit(d => d.SetColor(ids, aci));
    }

    /// <summary>Tek bir yazı seçiliyse (kimlik, içerik)</summary>
    public (int Id, string Text)? SelectedText()
    {
        var d = Doc;
        if (d == null || Selection.Count != 1) return null;
        int id = Selection.First();
        var info = d.Info(id);
        return info.StartsWith("TEXT|") || info.StartsWith("MTEXT|") ? (id, d.Text(id)) : null;
    }

    public void SetText(int id, string text) => Edit(d => d.SetText(id, text));

    public void AddText(double x, double y, string text, double height)
    {
        if (string.IsNullOrWhiteSpace(text) || height <= 0) return;
        TextHeight = height;
        var layer = ActiveLayer;
        Edit(d => d.AddText(layer, x, y, height, 0, text));
    }

    public void FinishPolyline(bool closed)
    {
        var pts = _pline.ToList();
        _pline.Clear();
        _anchor = null;
        Live = null;
        if (pts.Count < 2 || (closed && pts.Count < 3)) { Overlay(); Changed?.Invoke(); return; }
        var layer = ActiveLayer;
        Edit(d => d.AddPolyline(layer, pts, closed));
    }

    /// <summary>Keşif: (görünen ad, adet, kütüphane sembolü mü)</summary>
    public List<(string Name, int Count, bool Library)> Counts()
    {
        var d = Doc;
        if (d == null) return [];
        return d.InsertCounts().Select(c =>
        {
            var sym = SymbolLibrary.ForBlock(c.Name);
            return (sym?.Name ?? c.Name, c.Count, sym != null);
        }).OrderBy(t => !t.Item3).ThenBy(t => t.Item1).ToList();
    }

    // ───────────────────────── çekirdekte düzenle ─────────────────────────

    /// <summary>Çekirdekte düzenle → sahneyi yeniden kur → tuvale yükle. Görünüm korunur.</summary>
    private async void Edit(Action<CadDocument> op, bool reselect = false, Action? after = null)
    {
        var d = Doc;
        if (d == null || Busy) return;
        Busy = true;
        Changed?.Invoke();
        try
        {
            var sel = Selection.ToList();
            var (s, geom) = await Task.Run(() =>
            {
                op(d);
                var s = d.Rebuild();
                return (s, d.Geometry(sel));
            });
            _selGeom = geom;
            if (geom.Length == 0 && Selection.Count > 0)
            {
                // nesneler silindi / geri alındı
                Selection = new();
                SelectionInfo = null;
                if (NeedsSelection(Tool)) Tool = CadTool.Select;
            }
            Refresh();
            _canvas.UpdateScene(s);
            SceneChanged?.Invoke(s);
            Overlay();
            after?.Invoke();
        }
        catch (Exception e)
        {
            Message?.Invoke("İşlem yapılamadı: " + e.Message);
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private void Refresh()
    {
        var d = Doc;
        if (d == null) return;
        UndoCount = d.UndoCount;
        RedoCount = d.RedoCount;
        Dirty = d.Dirty;
    }

    // ───────────────────────── yakalama ve kısıtlar ─────────────────────────

    private const double AngleStep = 15, AngleTol = 3;

    private (int Kind, double X, double Y) SnapPoint(double x, double y, double tol)
    {
        if (!SnapOn || Doc == null) return (0, x, y);
        return Doc.Snap(x, y, tol);
    }

    /// <summary>Çizimde "nereden" ölçülüyor: çizgi/taşı tabanı, çoklu çizginin son noktası</summary>
    private (double X, double Y)? From => Tool switch
    {
        CadTool.Line or CadTool.Rect or CadTool.Move or CadTool.Copy or CadTool.Mirror or CadTool.Measure => _anchor,
        CadTool.Dimension => _dimP2 == null ? _anchor : null,
        CadTool.PLine => _pline.Count > 0 ? _pline[^1] : null,
        _ => null,
    };

    /// <summary>Orto, 15° kutupsal ve hizalama izleri (yatay/dikey/paralel/dik kılavuzlar, eşit uzunluk)</summary>
    private (int Kind, double X, double Y) Constrain((int Kind, double X, double Y) p)
    {
        _guides = null;
        _trackLabel = null;
        if (p.Kind > 0 || Tool == CadTool.Select) return p;
        var from = From;
        if (Ortho && from is { } f0)
        {
            double dx = p.X - f0.X, dy = p.Y - f0.Y;
            return Math.Abs(dx) >= Math.Abs(dy) ? (0, p.X, f0.Y) : (0, f0.X, p.Y);
        }
        return Track(p, from);
    }

    private readonly record struct Guide(double Ox, double Oy, double Dx, double Dy, string Label, bool Polar);

    private (int Kind, double X, double Y) Track((int Kind, double X, double Y) p, (double X, double Y)? from)
    {
        double tol = _tol;
        var lines = new List<Guide>();
        double segDx = 0, segDy = 0, segLen = 0;
        if (_acqSeg is { } sg)
        {
            segDx = sg.B.X - sg.A.X; segDy = sg.B.Y - sg.A.Y;
            segLen = Math.Sqrt(segDx * segDx + segDy * segDy);
            if (segLen > 1e-12) { segDx /= segLen; segDy /= segLen; } else segLen = 0;
        }
        if (from is { } f && Tool != CadTool.Rect)
        {
            // kutupsal: imlece en yakın 15° doğrultusu (±3°)
            double dx = p.X - f.X, dy = p.Y - f.Y;
            if (AngleSnap && dx * dx + dy * dy > 1e-18)
            {
                double ang = Math.Atan2(dy, dx) * 180 / Math.PI, ta = Math.Round(ang / AngleStep) * AngleStep;
                if (Math.Abs(ang - ta) <= AngleTol)
                {
                    double r = ta * Math.PI / 180;
                    double tn = ((ta % 360) + 360) % 360;
                    lines.Add(new Guide(f.X, f.Y, Math.Cos(r), Math.Sin(r), $"Kutupsal {tn:0}°", true));
                }
            }
            if (Tracking && segLen > 0)
            {
                lines.Add(new Guide(f.X, f.Y, segDx, segDy, "Paralel", false));
                lines.Add(new Guide(f.X, f.Y, -segDy, segDx, "Dik", false));
            }
        }
        if (Tracking)
            foreach (var q in _acq)
            {
                if (from is { } ff && Math.Abs(ff.X - q.X) + Math.Abs(ff.Y - q.Y) < 1e-9) continue;
                lines.Add(new Guide(q.X, q.Y, 1, 0, "Yatay hiza", false));
                lines.Add(new Guide(q.X, q.Y, 0, 1, "Dikey hiza", false));
                if (segLen > 0 && Math.Abs(segDx) > 1e-6 && Math.Abs(segDy) > 1e-6)
                {
                    lines.Add(new Guide(q.X, q.Y, segDx, segDy, "Paralel hiza", false));
                    lines.Add(new Guide(q.X, q.Y, -segDy, segDx, "Dik hiza", false));
                }
            }
        if (lines.Count == 0) return p;
        double Dist(Guide g) => Math.Abs((p.X - g.Ox) * g.Dy - (p.Y - g.Oy) * g.Dx);
        var cands = lines.Select(g => (g, d: Dist(g))).Where(c => c.d < tol || c.g.Polar).OrderBy(c => c.g.Polar ? c.d * 0.5 : c.d).ToList();
        if (cands.Count == 0) return p;
        var g1 = cands[0].g;
        // iki kılavuzun kesişimi: ör. paralel çizgi + referansın ucundan dik iz → eşit boy
        foreach (var (g2, d2) in cands.Skip(1))
        {
            double cr = g1.Dx * g2.Dy - g1.Dy * g2.Dx;
            if (Math.Abs(cr) < 0.05) continue;
            if (d2 > tol * 1.5 && !g2.Polar) continue;
            double t1 = ((g2.Ox - g1.Ox) * g2.Dy - (g2.Oy - g1.Oy) * g2.Dx) / cr;
            double ix = g1.Ox + g1.Dx * t1, iy = g1.Oy + g1.Dy * t1;
            if (Math.Abs(ix - p.X) + Math.Abs(iy - p.Y) > tol * 3) continue;
            SetGuides([g1, g2], ix, iy);
            _trackLabel = $"{g1.Label} + {g2.Label}";
            return (0, ix, iy);
        }
        double t = (p.X - g1.Ox) * g1.Dx + (p.Y - g1.Oy) * g1.Dy;
        // paralel çizerken referans parçayla eşit uzunluk
        if (from != null && !g1.Polar && g1.Label == "Paralel" && segLen > 0 && Math.Abs(Math.Abs(t) - segLen) < tol)
        {
            t = Math.Sign(t) * segLen;
            _trackLabel = $"Paralel · = {Fmt(segLen)} (referansla eşit)";
        }
        else _trackLabel = g1.Label;
        double x = g1.Ox + g1.Dx * t, y = g1.Oy + g1.Dy * t;
        SetGuides([g1], x, y);
        return (0, x, y);
    }

    private void SetGuides(Guide[] gs, double x, double y)
    {
        double ext = _tol * 4;
        var o = new List<float>();
        foreach (var g in gs)
        {
            // kılavuz: kaynaktan imlecin biraz ötesine
            double t = (x - g.Ox) * g.Dx + (y - g.Oy) * g.Dy;
            double s = t >= 0 ? 1 : -1;
            o.AddRange([(float)g.Ox, (float)g.Oy, (float)(x + g.Dx * ext * s), (float)(y + g.Dy * ext * s)]);
        }
        _guides = o.ToArray();
    }

    /// <summary>Üzerinden geçilen yakalama noktasını izlemeye al; uç/orta ise o çizgi parçasını referans yap</summary>
    private void Acquire((int Kind, double X, double Y) s, double tol)
    {
        if (!Tracking || Doc == null) return;
        var q = (s.X, s.Y);
        if (_lastAcq is { } l && Math.Abs(l.X - q.X) + Math.Abs(l.Y - q.Y) < 1e-9) return;
        _lastAcq = q;
        _acq.RemoveAll(a => Math.Abs(a.X - q.X) + Math.Abs(a.Y - q.Y) < 1e-9);
        _acq.Add(q);
        if (_acq.Count > 5) _acq.RemoveAt(0);
        if (s.Kind is 1 or 2)
        {
            int id = Doc.Pick(q.X, q.Y, tol);
            if (id < 0) return;
            var g = Doc.Geometry(id);
            double best = double.MaxValue;
            for (int i = 0; i + 3 < g.Length; i += 4)
            {
                double ax = g[i], ay = g[i + 1], bx = g[i + 2], by = g[i + 3];
                double vx = bx - ax, vy = by - ay, len2 = vx * vx + vy * vy;
                if (len2 < 1e-18) continue;
                double t = Math.Clamp(((q.X - ax) * vx + (q.Y - ay) * vy) / len2, 0, 1);
                double ddx = ax + vx * t - q.X, ddy = ay + vy * t - q.Y, d = ddx * ddx + ddy * ddy;
                if (d < best) { best = d; _acqSeg = ((ax, ay), (bx, by)); }
            }
        }
    }

    private double SnapAngle(double deg)
    {
        while (deg > 180) deg -= 360;
        while (deg <= -180) deg += 360;
        if (Ortho) return Math.Round(deg / 15) * 15;
        if (AngleSnap)
        {
            double t = Math.Round(deg / AngleStep) * AngleStep;
            if (Math.Abs(deg - t) <= AngleTol) return t;
        }
        return deg;
    }

    // ───────────────────────── fare girişi ─────────────────────────

    public void Press(double x, double y, double tol, bool shift)
    {
        if (Doc == null || Busy) return;
        _shift = shift;
        _tol = tol;
        _dragging = false;
        _press = Tool == CadTool.Select ? (0, x, y) : Constrain(SnapPoint(x, y, tol));
        if (Tool == CadTool.Select) _boxStart = [(float)x, (float)y];
    }

    private bool _shift;

    public void Move(double x, double y, double tol, bool pressed)
    {
        if (Doc == null || Busy) return;
        _tol = tol;
        if (pressed && !_dragging && Math.Abs(x - _press.X) + Math.Abs(y - _press.Y) > tol * 0.6) _dragging = true;
        if (Tool == CadTool.Select)
        {
            if (pressed && _dragging && _boxStart != null)
            {
                Overlay(box: [_boxStart[0], _boxStart[1], (float)x, (float)y], crossing: x < _boxStart[0]);
            }
            return;
        }
        var raw = SnapPoint(x, y, tol);
        if (raw.Kind > 0 && !pressed) Acquire(raw, tol);
        var p = Constrain(raw);
        _hover = (p.X, p.Y);
        // sürüklerken ilk nokta basılan yerdir
        if (pressed && _dragging && NeedsFirstPoint()) Preview(p, (_press.X, _press.Y));
        else Preview(p, null);
    }

    public void Release(double x, double y, double tol)
    {
        if (Doc == null || Busy) return;
        _tol = tol;
        if (Tool == CadTool.Select)
        {
            if (_dragging && _boxStart != null) BoxSelect(_boxStart[0], _boxStart[1], x, y);
            else PickAt(x, y, tol);
            _boxStart = null;
            _dragging = false;
            return;
        }
        var p = Constrain(SnapPoint(x, y, tol));
        if (_dragging && NeedsFirstPoint())
        {
            // bas-sürükle-bırak: basılan nokta ilk nokta, bırakılan ikinci
            Point((_press.X, _press.Y), tol);
            p = Constrain(SnapPoint(x, y, tol));
        }
        _dragging = false;
        Point((p.X, p.Y), tol);
    }

    public void Cancel()
    {
        _dragging = false;
        _boxStart = null;
        Overlay();
    }

    /// <summary>Araç henüz ilk noktasını bekliyor mu (sürüklemede basılan nokta ilk nokta olur)</summary>
    private bool NeedsFirstPoint() => Tool switch
    {
        CadTool.Line or CadTool.Circle or CadTool.Rect or CadTool.Measure or CadTool.Mirror or CadTool.Move or CadTool.Copy or CadTool.Dimension => _anchor == null,
        CadTool.PLine => _pline.Count == 0,
        _ => false,
    };

    private void PickAt(double x, double y, double tol)
    {
        var d = Doc!;
        int id = d.Pick(x, y, tol);
        if (id < 0)
        {
            if (Selection.Count > 0 && !_shift) ClearSelection();
            return;
        }
        var sel = Selection.ToHashSet();
        bool added;
        if (_shift || sel.Contains(id)) { sel.Remove(id); added = false; }
        else { sel.Add(id); added = true; }
        SetSelection(sel, Describe(d.Info(id), added, sel.Count));
    }

    private void BoxSelect(double x0, double y0, double x1, double y1)
    {
        var d = Doc!;
        bool crossing = x1 < x0; // sağdan sola: değenler
        var ids = d.SelectRect(x0, y0, x1, y1, crossing);
        var sel = Selection.ToHashSet();
        if (_shift) sel.ExceptWith(ids); else sel.UnionWith(ids);
        SetSelection(sel, null);
    }

    private string Describe(string info, bool added, int count)
    {
        var parts = info.Split('|');
        var type = TypeNames.GetValueOrDefault(parts[0], parts[0]);
        var head = count > 1 ? $"{count} nesne seçili" : "1 nesne seçili";
        return added ? $"{head} · {type} · {(parts.Length > 1 ? parts[1] : "")}" : head;
    }

    // ───────────────────────── nokta adımları ─────────────────────────

    /// <summary>Bir nokta verildi (tıklama ya da komut satırı)</summary>
    private void Point((double X, double Y) p, double tol)
    {
        var d = Doc!;
        // tıklanan her nokta çizim bitene kadar hizalama referansı olur
        if (Tracking && Tool is not (CadTool.Select or CadTool.Offset or CadTool.Text))
        {
            _acq.RemoveAll(a => Math.Abs(a.X - p.X) + Math.Abs(a.Y - p.Y) < 1e-9);
            _acq.Add(p);
            if (_acq.Count > 6) _acq.RemoveAt(0);
        }
        var layer = ActiveLayer;
        switch (Tool)
        {
            case CadTool.Line:
                if (_anchor is { } a)
                {
                    if (Near(a, p, tol)) break;
                    _anchor = p;
                    Edit(dd => dd.AddLine(layer, a.X, a.Y, p.X, p.Y));
                }
                else _anchor = p;
                break;
            case CadTool.PLine:
                if (_pline.Count > 0 && Near(_pline[^1], p, tol)) break;
                _pline.Add(p);
                _anchor = p;
                break;
            case CadTool.Circle:
                if (_anchor is { } c)
                {
                    double r = Dist(c, p);
                    if (r < tol * 0.05) break;
                    _anchor = null;
                    Edit(dd => dd.AddCircle(layer, c.X, c.Y, r));
                }
                else _anchor = p;
                break;
            case CadTool.Rect:
                if (_anchor is { } r0)
                {
                    if (Math.Abs(r0.X - p.X) < 1e-9 || Math.Abs(r0.Y - p.Y) < 1e-9) break;
                    _anchor = null;
                    Edit(dd => dd.AddPolyline(layer, [r0, (p.X, r0.Y), p, (r0.X, p.Y)], true));
                }
                else _anchor = p;
                break;
            case CadTool.Measure:
                if (_anchor is { } m0)
                {
                    Live = MeasureText(m0, p);
                    _anchor = null;
                    Overlay(rubber: [(float)m0.X, (float)m0.Y, (float)p.X, (float)p.Y]);
                    Changed?.Invoke();
                    return;
                }
                _anchor = p;
                break;
            case CadTool.Dimension:
                if (_anchor is not { } d1) _anchor = p;
                else if (_dimP2 is not { } d2) { if (!Near(d1, p, tol)) _dimP2 = p; }
                else
                {
                    var dim = BuildDimension(d1, d2, p);
                    _anchor = null;
                    _dimP2 = null;
                    var dl = ActiveLayer;
                    Edit(dd => dd.InsertDimension(dl, d1.X, d1.Y, dim.Snippet));
                }
                break;
            case CadTool.Move:
            case CadTool.Copy:
                if (_anchor is { } b)
                {
                    double dx = p.X - b.X, dy = p.Y - b.Y;
                    if (Math.Abs(dx) + Math.Abs(dy) < 1e-12) break;
                    DoMoveCopy(dx, dy);
                }
                else _anchor = p;
                break;
            case CadTool.Mirror:
                if (_anchor is { } m1)
                {
                    if (Near(m1, p, tol)) break;
                    var ids = Selection.ToList();
                    bool keep = MirrorKeep;
                    _anchor = null;
                    Tool = CadTool.Select;
                    Edit(dd => dd.Mirror(ids, m1.X, m1.Y, p.X, p.Y, keep));
                }
                else _anchor = p;
                break;
            case CadTool.Rotate:
                if (_pivot is { } pv)
                {
                    if (Near(pv, p, tol)) break;
                    RotateBy(SnapAngle(Math.Atan2(p.Y - pv.Y, p.X - pv.X) * 180 / Math.PI));
                }
                else _pivot = p;
                break;
            case CadTool.Scale:
                if (_pivot is not { } sp) _pivot = p;
                else if (_scaleRef is not { } sr) { if (!Near(sp, p, tol)) _scaleRef = p; }
                else
                {
                    double k = Dist(sp, p) / Math.Max(Dist(sp, sr), 1e-12);
                    if (Ortho) k = Math.Max(0.25, Math.Round(k * 4) / 4);
                    ScaleBy(k);
                }
                break;
            case CadTool.Offset:
                if (_offsetTarget is { } target)
                {
                    double dist = OffsetDist;
                    _offsetTarget = null;
                    ClearSelectionSilently();
                    int result = 0;
                    Edit(dd => result = dd.Offset(target, dist, p.X, p.Y),
                        after: () => { if (result < 0) Message?.Invoke("Bu nesne ötelenemiyor. Desteklenenler: çizgi, daire, yay ve düz parçalı çoklu çizgi."); });
                }
                else
                {
                    int id = d.Pick(p.X, p.Y, tol);
                    if (id < 0) break;
                    _offsetTarget = id;
                    Selection = [id];
                    _selGeom = d.Geometry(Selection);
                }
                break;
            case CadTool.Text:
                TextRequested?.Invoke(p.X, p.Y);
                break;
            case CadTool.Symbol:
                if (Symbol is { } sym)
                {
                    double sc = PlaceScale, rot = SymbolRot;
                    Edit(dd => dd.InsertSymbol(sym, layer, p.X, p.Y, sc, rot));
                }
                break;
        }
        Live = null;
        Preview((0, _hover.X, _hover.Y), null);
        Changed?.Invoke();
    }

    private void DoMoveCopy(double dx, double dy)
    {
        var ids = Selection.ToList();
        if (Tool == CadTool.Move)
        {
            _anchor = null;
            Tool = CadTool.Select;
            Edit(d => d.Move(ids, dx, dy));
        }
        else Edit(d => d.Copy(ids, dx, dy)); // art arda kopya: taban aynı kalır
    }

    public void RotateBy(double deg)
    {
        if (_pivot is not { } c || Selection.Count == 0 || deg == 0) return;
        var ids = Selection.ToList();
        _pivot = null;
        Tool = CadTool.Select;
        Edit(d => d.Rotate(ids, c.X, c.Y, deg));
    }

    public void ScaleBy(double k)
    {
        if (_pivot is not { } c || Selection.Count == 0 || !(k > 0) || k == 1) return;
        var ids = Selection.ToList();
        _pivot = null;
        _scaleRef = null;
        Tool = CadTool.Select;
        Edit(d => d.Scale(ids, c.X, c.Y, k));
    }

    /// <summary>Sembolü 90° döndür (R tuşu)</summary>
    public void RotateSymbol()
    {
        SymbolRot = (SymbolRot + 90) % 360;
        Preview((0, _hover.X, _hover.Y), null);
        Changed?.Invoke();
    }

    private static bool Near((double X, double Y) a, (double X, double Y) b, double tol) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < tol * 0.05;
    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static string Fmt(double v) => v.ToString("0.###", new CultureInfo("tr-TR"));

    private static string MeasureText((double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, ang = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (ang < 0) ang += 360;
        return $"Uzunluk {Fmt(Math.Sqrt(dx * dx + dy * dy))} · Açı {ang:0.0}° · ΔX {Fmt(dx)} · ΔY {Fmt(dy)}";
    }

    // ───────────────────────── önizleme ─────────────────────────

    private static float[] Seg((double X, double Y) a, (double X, double Y) b) => [(float)a.X, (float)a.Y, (float)b.X, (float)b.Y];

    /// <summary>İmleç p'deyken araç önizlemesi; firstOverride: sürüklemede henüz konmamış ilk nokta</summary>
    private void Preview((int Kind, double X, double Y) p, (double X, double Y)? firstOverride)
    {
        var cur = (p.X, p.Y);
        var a = firstOverride ?? _anchor;
        float[]? rubber = null;
        Matrix3x2? ghost = null;
        string? live = null;
        switch (Tool)
        {
            case CadTool.Line when a is { } l:
                rubber = Seg(l, cur);
                live = LineLive(l, cur);
                break;
            case CadTool.PLine:
                {
                    var pts = _pline.ToList();
                    if (firstOverride is { } f && pts.Count == 0) pts.Add(f);
                    if (pts.Count > 0)
                    {
                        var list = new List<float>();
                        for (int i = 1; i < pts.Count; i++) list.AddRange(Seg(pts[i - 1], pts[i]));
                        list.AddRange(Seg(pts[^1], cur));
                        rubber = list.ToArray();
                        live = LineLive(pts[^1], cur);
                    }
                    break;
                }
            case CadTool.Circle when a is { } c:
                {
                    double r = Dist(c, cur);
                    const int n = 96;
                    var o = new float[n * 4 + 4];
                    for (int i = 0; i < n; i++)
                    {
                        double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                        o[i * 4] = (float)(c.X + r * Math.Cos(a0));
                        o[i * 4 + 1] = (float)(c.Y + r * Math.Sin(a0));
                        o[i * 4 + 2] = (float)(c.X + r * Math.Cos(a1));
                        o[i * 4 + 3] = (float)(c.Y + r * Math.Sin(a1));
                    }
                    Seg(c, cur).CopyTo(o, n * 4);
                    rubber = o;
                    live = $"Yarıçap {Fmt(r)} · Çap {Fmt(2 * r)}";
                    break;
                }
            case CadTool.Rect when a is { } r0:
                {
                    (double X, double Y) c1 = (cur.X, r0.Y), c3 = (r0.X, cur.Y);
                    rubber = [.. Seg(r0, c1), .. Seg(c1, cur), .. Seg(cur, c3), .. Seg(c3, r0)];
                    live = $"{Fmt(Math.Abs(cur.X - r0.X))} × {Fmt(Math.Abs(cur.Y - r0.Y))}";
                    break;
                }
            case CadTool.Measure when a is { } m0:
                rubber = Seg(m0, cur);
                live = MeasureText(m0, cur);
                break;
            case CadTool.Move or CadTool.Copy when a is { } b:
                ghost = Matrix3x2.CreateTranslation((float)(cur.X - b.X), (float)(cur.Y - b.Y));
                rubber = Seg(b, cur);
                live = $"ΔX {Fmt(cur.X - b.X)} · ΔY {Fmt(cur.Y - b.Y)} · Uzaklık {Fmt(Dist(b, cur))}";
                break;
            case CadTool.Mirror when a is { } m1:
                rubber = Seg(m1, cur);
                if (Dist(m1, cur) > 1e-9)
                {
                    double phi = Math.Atan2(cur.Y - m1.Y, cur.X - m1.X), ca = Math.Cos(2 * phi), sb = Math.Sin(2 * phi);
                    // x' = a·x + b·y + e ; y' = b·x − a·y + f
                    ghost = new Matrix3x2((float)ca, (float)sb, (float)sb, (float)-ca,
                        (float)(m1.X - (ca * m1.X + sb * m1.Y)), (float)(m1.Y - (sb * m1.X - ca * m1.Y)));
                }
                break;
            case CadTool.Rotate when _pivot is { } pv:
                {
                    double deg = SnapAngle(Math.Atan2(cur.Y - pv.Y, cur.X - pv.X) * 180 / Math.PI);
                    ghost = Matrix3x2.CreateRotation((float)(deg * Math.PI / 180), new Vector2((float)pv.X, (float)pv.Y));
                    rubber = Seg(pv, cur);
                    live = $"Açı {deg:0.0}°";
                    break;
                }
            case CadTool.Scale when _pivot is { } sp:
                if (_scaleRef is { } sr)
                {
                    double k = Dist(sp, cur) / Math.Max(Dist(sp, sr), 1e-12);
                    if (Ortho) k = Math.Max(0.25, Math.Round(k * 4) / 4);
                    ghost = Matrix3x2.CreateScale((float)k, new Vector2((float)sp.X, (float)sp.Y));
                    rubber = [.. Seg(sp, sr), .. Seg(sp, cur)];
                    live = $"Ölçek ×{Fmt(k)}";
                }
                else rubber = Seg(sp, cur);
                break;
            case CadTool.Symbol when Symbol is { } sym:
                rubber = sym.Segments(cur.X, cur.Y, PlaceScale, SymbolRot);
                break;
            case CadTool.Dimension when a is { } d1:
                if (_dimP2 is { } d2)
                {
                    var dim = BuildDimension(d1, d2, cur);
                    rubber = dim.Preview;
                    live = $"Ölçü {dim.Text}";
                }
                else
                {
                    rubber = Seg(d1, cur);
                    live = $"Ölçü {Fmt(Dist(d1, cur))}";
                }
                break;
        }
        if (_trackLabel != null) live = live == null ? _trackLabel : $"{_trackLabel} · {live}";
        if (live != null || Tool != CadTool.Measure) Live = live;
        Overlay(ghost: ghost, rubber: rubber, snap: p.Kind > 0 ? p : null, cursor: Tool == CadTool.Select ? null : cur,
            label: _trackLabel ?? (p.Kind > 0 ? SnapNames[p.Kind] : null));
        LiveChanged?.Invoke();
    }

    /// <summary>Yalnız canlı ölçü değişti (araç çubuğu yenilenmesin)</summary>
    public event Action? LiveChanged;

    private static string LineLive((double X, double Y) a, (double X, double Y) b)
    {
        double ang = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
        if (ang < 0) ang += 360;
        return $"Uzunluk {Fmt(Dist(a, b))} · Açı {ang:0.0}°";
    }

    private static readonly string[] SnapNames = ["", "Uç nokta", "Orta nokta", "Merkez", "Ekleme noktası"];

    private void Overlay(Matrix3x2? ghost = null, float[]? rubber = null, (int Kind, double X, double Y)? snap = null,
        (double X, double Y)? cursor = null, float[]? box = null, bool crossing = false, string? label = null)
    {
        bool live = cursor != null && Tool != CadTool.Select;
        _canvas.SetOverlay(new Overlay
        {
            Selection = _selGeom,
            Ghost = ghost,
            Rubber = rubber,
            SnapType = snap?.Kind ?? 0,
            Snap = snap is { } s ? new Vector2((float)s.X, (float)s.Y) : default,
            Cursor = cursor is { } c ? new Vector2((float)c.X, (float)c.Y) : null,
            Box = box,
            BoxCrossing = crossing,
            Guides = live ? _guides : null,
            Marks = live && _acq.Count > 0 ? _acq.SelectMany(q => new[] { (float)q.X, (float)q.Y }).ToArray() : null,
            Label = live ? label : null,
        });
    }

    // ───────────────────────── ölçü (hizalı) ─────────────────────────

    public sealed record DimGeom(string Snippet, float[] Preview, string Text);

    /// <summary>
    /// Hizalı ölçü: p1–p2 arası, ölçü çizgisi q noktasından geçer. Uzatma çizgileri, eğik tikler ve ortalanmış yazı;
    /// tek blok olarak eklenir (taşınır, silinir, Çöz ile parçalanır). Blok koordinatları p1'e göredir.
    /// </summary>
    public DimGeom BuildDimension((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) q)
    {
        double h = TextHeight > 0 ? TextHeight : 2.5;
        double ux = p2.X - p1.X, uy = p2.Y - p1.Y, len = Math.Sqrt(ux * ux + uy * uy);
        if (len < 1e-12) { ux = 1; uy = 0; } else { ux /= len; uy /= len; }
        double nx = -uy, ny = ux;
        double off = (q.X - p1.X) * nx + (q.Y - p1.Y) * ny;
        double sg = off >= 0 ? 1 : -1;
        double gap = h * 0.4, ext = h * 0.6, tick = h * 0.5;
        (double X, double Y) P(double x, double y) => (x, y);
        var d1 = P(p1.X + nx * off, p1.Y + ny * off);
        var d2 = P(p2.X + nx * off, p2.Y + ny * off);
        var segs = new List<((double X, double Y) A, (double X, double Y) B)>();
        // uzatma çizgileri (nesneden küçük boşlukla başlar, ölçü çizgisini biraz geçer)
        if (Math.Abs(off) > gap)
        {
            segs.Add((P(p1.X + nx * gap * sg, p1.Y + ny * gap * sg), P(d1.X + nx * ext * sg, d1.Y + ny * ext * sg)));
            segs.Add((P(p2.X + nx * gap * sg, p2.Y + ny * gap * sg), P(d2.X + nx * ext * sg, d2.Y + ny * ext * sg)));
        }
        // ölçü çizgisi uçlardan biraz taşar (mimari tik stili)
        segs.Add((P(d1.X - ux * tick * 0.6, d1.Y - uy * tick * 0.6), P(d2.X + ux * tick * 0.6, d2.Y + uy * tick * 0.6)));
        // 45° eğik tikler
        double tx = (ux + nx) / Math.Sqrt(2) * tick, ty = (uy + ny) / Math.Sqrt(2) * tick;
        segs.Add((P(d1.X - tx, d1.Y - ty), P(d1.X + tx, d1.Y + ty)));
        segs.Add((P(d2.X - tx, d2.Y - ty), P(d2.X + tx, d2.Y + ty)));
        // yazı: ölçü çizgisinin üstünde (dışa doğru), okunur açıda
        double ang = Math.Atan2(uy, ux) * 180 / Math.PI;
        if (ang > 90.0001 || ang <= -90) ang += ang > 0 ? -180 : 180;
        double ar = ang * Math.PI / 180, upx = -Math.Sin(ar), upy = Math.Cos(ar);
        double mx = (d1.X + d2.X) / 2 + upx * h * 0.35, my = (d1.Y + d2.Y) / 2 + upy * h * 0.35;
        string text = Fmt(len);

        var inv = CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.########", inv);
        var sb = new System.Text.StringBuilder();
        foreach (var (a, b) in segs)
            sb.Append($"0\nLINE\n100\nAcDbEntity\n8\n0\n62\n0\n100\nAcDbLine\n10\n{N(a.X - p1.X)}\n20\n{N(a.Y - p1.Y)}\n30\n0.0\n11\n{N(b.X - p1.X)}\n21\n{N(b.Y - p1.Y)}\n31\n0.0\n");
        double lx = mx - p1.X, ly = my - p1.Y;
        sb.Append($"0\nTEXT\n100\nAcDbEntity\n8\n0\n62\n0\n100\nAcDbText\n10\n{N(lx)}\n20\n{N(ly)}\n30\n0.0\n40\n{N(h)}\n1\n{text}\n50\n{N(ang)}\n72\n1\n11\n{N(lx)}\n21\n{N(ly)}\n31\n0.0\n100\nAcDbText\n73\n0\n");

        var prev = new List<float>();
        foreach (var (a, b) in segs) prev.AddRange([(float)a.X, (float)a.Y, (float)b.X, (float)b.Y]);
        // yazının yerini gösteren kısa çizgi (önizleme)
        double tw = text.Length * h * 0.3;
        prev.AddRange([(float)(mx - ux * tw), (float)(my - uy * tw), (float)(mx + ux * tw), (float)(my + uy * tw)]);
        return new DimGeom(sb.ToString(), prev.ToArray(), text);
    }

    // ───────────────────────── komut satırı ─────────────────────────

    private static readonly Dictionary<string, CadTool> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L"] = CadTool.Line, ["LINE"] = CadTool.Line, ["ÇİZGİ"] = CadTool.Line,
        ["PL"] = CadTool.PLine, ["PLINE"] = CadTool.PLine,
        ["C"] = CadTool.Circle, ["CIRCLE"] = CadTool.Circle, ["DAİRE"] = CadTool.Circle,
        ["REC"] = CadTool.Rect, ["RECTANG"] = CadTool.Rect,
        ["M"] = CadTool.Move, ["MOVE"] = CadTool.Move, ["TAŞI"] = CadTool.Move,
        ["CO"] = CadTool.Copy, ["CP"] = CadTool.Copy, ["COPY"] = CadTool.Copy,
        ["RO"] = CadTool.Rotate, ["ROTATE"] = CadTool.Rotate,
        ["SC"] = CadTool.Scale, ["SCALE"] = CadTool.Scale,
        ["MI"] = CadTool.Mirror, ["MIRROR"] = CadTool.Mirror,
        ["O"] = CadTool.Offset, ["OFFSET"] = CadTool.Offset,
        ["T"] = CadTool.Text, ["DT"] = CadTool.Text, ["TEXT"] = CadTool.Text, ["MT"] = CadTool.Text,
        ["DIM"] = CadTool.Dimension, ["DAL"] = CadTool.Dimension, ["DLI"] = CadTool.Dimension, ["ÖLÇÜ"] = CadTool.Dimension,
        ["DI"] = CadTool.Measure, ["DIST"] = CadTool.Measure, ["ÖLÇ"] = CadTool.Measure,
    };

    /// <summary>Komut satırı: kısaltma (L, PL, C, M, CO, RO…) ya da sayısal giriş. Anlaşılmazsa false.</summary>
    public bool Command(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0) { Enter(); return true; }
        var up = s.ToUpper(new CultureInfo("tr-TR"));
        if (Aliases.TryGetValue(s, out var t) || Aliases.TryGetValue(up, out t)) { SelectTool(t); return true; }
        switch (up)
        {
            case "E" or "ERASE" or "SİL" or "DEL": DeleteSelection(); return true;
            case "U" or "UNDO": Undo(); return true;
            case "REDO": Redo(); return true;
            case "Z" or "ZE" or "ZOOM": _canvas.Fit(); return true;
            case "G" or "GROUP": GroupSelection(); return true;
            case "X" or "EXPLODE": ExplodeSelection(); return true;
            case "K" or "CL" or "CLOSE" when Tool == CadTool.PLine: FinishPolyline(true); return true;
            case "R" when Tool == CadTool.Symbol: RotateSymbol(); return true;
        }
        return Numeric(s);
    }

    private bool Numeric(string s)
    {
        var d = Doc;
        if (d == null) return false;
        var inv = CultureInfo.InvariantCulture;
        bool rel = s.StartsWith('@');
        if (rel) s = s[1..];
        bool Num(string x, out double v) => double.TryParse(x.Trim().Replace(',', '.'), NumberStyles.Float, inv, out v);
        double tol = _canvas.Tolerance;

        // "a<b": uzunluk<açı
        if (s.Contains('<'))
        {
            var p = s.Split('<');
            if (p.Length != 2 || !Num(p[0], out var len) || !Num(p[1], out var ang)) return false;
            var from = Tool == CadTool.PLine && _pline.Count > 0 ? _pline[^1] : _anchor;
            if (from is not { } f) return false;
            double r = ang * Math.PI / 180;
            Point((f.X + len * Math.Cos(r), f.Y + len * Math.Sin(r)), tol);
            return true;
        }
        // "x;y" ya da "x y" ya da "x,y" (iki sayı) — ondalık için nokta
        var parts = s.Split([';', ' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && Num(parts[0], out var x) && Num(parts[1], out var y))
        {
            var from = Tool == CadTool.PLine && _pline.Count > 0 ? _pline[^1] : _anchor;
            if (Tool == CadTool.Rect && from is { } r0) { Point((r0.X + x, r0.Y + y), tol); return true; }
            // ilk nokta konduysa göreli (@ isteğe bağlı)
            if (from is { } f && Tool is not CadTool.Rotate and not CadTool.Scale)
            {
                Point((f.X + x, f.Y + y), tol);
                return true;
            }
            // ilk nokta: mutlak koordinat (durum çubuğundakiyle aynı)
            Point(rel ? (_hover.X + x, _hover.Y + y) : (x - d.OriginX, y - d.OriginY), tol);
            return true;
        }
        if (parts.Length != 1 || !Num(parts[0], out var v)) return false;
        switch (Tool)
        {
            case CadTool.Rotate when _pivot != null: RotateBy(v); return true;
            case CadTool.Scale when _pivot != null: ScaleBy(v); return true;
            case CadTool.Offset: if (v > 0) { OffsetDist = v; Changed?.Invoke(); } return true;
            case CadTool.Circle when _anchor is { } c: if (v > 0) Point((c.X + v, c.Y), tol); return true;
            case CadTool.Dimension when _anchor is { } a1 && _dimP2 is { } a2:
                {
                    // ölçü çizgisinin nesneye uzaklığı: imlecin olduğu tarafa
                    double ux = a2.X - a1.X, uy = a2.Y - a1.Y, l = Math.Sqrt(ux * ux + uy * uy);
                    if (l < 1e-12) return false;
                    double nx = -uy / l, ny = ux / l;
                    double side = (_hover.X - a1.X) * nx + (_hover.Y - a1.Y) * ny >= 0 ? 1 : -1;
                    Point((a1.X + nx * v * side, a1.Y + ny * v * side), tol);
                    return true;
                }
            case CadTool.Symbol: if (v > 0) { if (_fixedScale != null) _fixedScale = v; else SymbolScale = v; Changed?.Invoke(); } return true;
        }
        // doğrudan uzaklık: imleç yönünde
        {
            var from = Tool == CadTool.PLine && _pline.Count > 0 ? _pline[^1] : _anchor;
            if (from is not { } f || v == 0) return false;
            double dx = _hover.X - f.X, dy = _hover.Y - f.Y, l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-12) { dx = 1; dy = 0; l = 1; }
            Point((f.X + dx / l * v, f.Y + dy / l * v), tol);
            return true;
        }
    }
}
