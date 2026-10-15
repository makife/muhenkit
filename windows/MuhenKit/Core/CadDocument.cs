using System.Runtime.InteropServices;

namespace MuhenKit.Core;

public sealed record CadLayer(string Name, uint Rgb, bool OffInFile, int Count);
public sealed record LineBatch(int Layer, uint Rgb, int First, int Count, float MinX, float MinY, float MaxX, float MaxY);
public sealed record FillGroup(int Layer, uint Rgb, float Alpha, int[] LoopFirst, int[] LoopCount);
public sealed record CadText(float X, float Y, float Height, float Rotation, float WidthFactor, float BoxWidth,
    int Layer, uint Rgb, int HAlign, int VAlign, string Text);

/// <summary>
/// Çizilmeye hazır sahne. Koordinatlardan köken (OriginX/Y) çıkarılmıştır: büyük (UTM) koordinatlarda
/// float hassasiyeti kaybolmasın (Android sürümüyle aynı yaklaşım).
/// </summary>
public sealed class CadScene
{
    public required string Name;
    public required string Version;
    public required int EntityCount;
    public required List<CadLayer> Layers;
    public required float[] Vertices;
    public required List<LineBatch> Batches;
    public required List<FillGroup> Fills;
    public required List<CadText> Texts;
    public float MinX, MinY, MaxX, MaxY;
    public long ParseMs;
}

public sealed class CadException(string msg) : Exception(msg);

/// <summary>Açık DXF belgesi (çekirdekte açık kalır). Tüm çağrılar kilitli: çekirdek tek iş parçacıklıdır.</summary>
public sealed class CadDocument : IDisposable
{
    private nint _h;
    private readonly object _lock = new();
    public string Name { get; }
    public double OriginX { get; }
    public double OriginY { get; }
    public CadScene Scene { get; private set; } = null!;

    private CadDocument(nint h, string name, double ox, double oy)
    {
        _h = h;
        Name = name;
        OriginX = ox;
        OriginY = oy;
    }

    private nint H => _h != 0 ? _h : throw new CadException("Çizim kapatıldı.");

    public static CadDocument Open(string name, byte[] data)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var h = Native.mk_open(data, data.LongLength);
        if (h == 0) throw new CadException(Native.Str(Native.mk_last_error()) is { Length: > 0 } e ? e : "DXF okunamadı.");
        if (Native.mk_build_scene(h) != 0)
        {
            var e = Native.Str(Native.mk_last_error());
            Native.mk_close(h);
            throw new CadException(e);
        }
        var ext = new float[4];
        Native.mk_extents(h, ext);
        var doc = new CadDocument(h, name, (ext[0] + (double)ext[2]) / 2, (ext[1] + (double)ext[3]) / 2);
        doc.Scene = doc.Extract(sw);
        return doc;
    }

    public CadScene Rebuild()
    {
        lock (_lock)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Native.mk_build_scene(H);
            Scene = Extract(sw);
            return Scene;
        }
    }

    public bool Dirty { get { lock (_lock) return Native.mk_is_dirty(H) == 1; } }

    public byte[] Save()
    {
        lock (_lock)
        {
            var p = Native.mk_save(H, out var n);
            var b = new byte[n];
            Marshal.Copy(p, b, 0, (int)n);
            return b;
        }
    }

    public byte[] SaveCopy()
    {
        lock (_lock)
        {
            var p = Native.mk_save_copy(H, out var n);
            var b = new byte[n];
            Marshal.Copy(p, b, 0, (int)n);
            return b;
        }
    }

    public void SetDirty(bool d) { lock (_lock) Native.mk_set_dirty(H, d ? 1 : 0); }

    // ───────────────── düzenleme: tüm koordinatlar göreli (köken çıkarılmış) ─────────────────

    private static byte[] Z(string s) => Native.Z(s);
    private static int[] A(IReadOnlyCollection<int> ids) => ids.ToArray();

    public int Pick(double x, double y, double tol) { lock (_lock) return Native.mk_pick(H, x + OriginX, y + OriginY, tol); }

    /// <summary>(tür, x, y) göreli; tür 0: yakalama yok. 1 uç, 2 orta, 3 merkez, 4 ekleme noktası</summary>
    public (int Kind, double X, double Y) Snap(double x, double y, double tol)
    {
        lock (_lock)
        {
            var o = new double[2];
            int k = Native.mk_snap(H, x + OriginX, y + OriginY, tol, o);
            return k > 0 ? (k, o[0] - OriginX, o[1] - OriginY) : (0, x, y);
        }
    }

    /// <summary>Seçim vurgusu: göreli (x1,y1,x2,y2) dörtlüleri</summary>
    public float[] Geometry(int id)
    {
        lock (_lock)
        {
            var p = Native.mk_entity_geometry(H, id, out var n);
            var g = new float[n];
            if (n > 0) Marshal.Copy(p, g, 0, (int)n);
            for (long i = 0; i + 1 < n; i += 2)
            {
                g[i] = (float)(g[i] - OriginX);
                g[i + 1] = (float)(g[i + 1] - OriginY);
            }
            return g;
        }
    }

    public float[] Geometry(IEnumerable<int> ids) => ids.SelectMany(Geometry).ToArray();

    public int[] SelectRect(double x0, double y0, double x1, double y1, bool crossing)
    {
        lock (_lock)
        {
            double ax = x0 + OriginX, ay = y0 + OriginY, bx = x1 + OriginX, by = y1 + OriginY;
            int n = Native.mk_select_rect(H, ax, ay, bx, by, crossing ? 1 : 0, null, 0);
            if (n <= 0) return [];
            var ids = new int[n];
            Native.mk_select_rect(H, ax, ay, bx, by, crossing ? 1 : 0, ids, n);
            return ids;
        }
    }

    /// <summary>"TÜR|katman|handle[|blok]"</summary>
    public string Info(int id) { lock (_lock) return Native.Str(Native.mk_entity_info(H, id)); }
    public string Text(int id) { lock (_lock) return Native.Str(Native.mk_entity_text(H, id)); }
    public string LastError => Native.Str(Native.mk_last_error());

    public void Move(IReadOnlyCollection<int> ids, double dx, double dy) { lock (_lock) Native.mk_move(H, A(ids), ids.Count, dx, dy); }
    public int Copy(IReadOnlyCollection<int> ids, double dx, double dy) { lock (_lock) return Native.mk_copy(H, A(ids), ids.Count, dx, dy); }
    public void Delete(IReadOnlyCollection<int> ids) { lock (_lock) Native.mk_delete(H, A(ids), ids.Count); }

    public int AddLine(string layer, double x1, double y1, double x2, double y2)
    { lock (_lock) return Native.mk_add_line(H, Z(layer), x1 + OriginX, y1 + OriginY, x2 + OriginX, y2 + OriginY); }

    public int AddText(string layer, double x, double y, double h, double rot, string text)
    { lock (_lock) return Native.mk_add_text(H, Z(layer), x + OriginX, y + OriginY, h, rot, Z(text)); }

    public int AddCircle(string layer, double cx, double cy, double r)
    { lock (_lock) return Native.mk_add_circle(H, Z(layer), cx + OriginX, cy + OriginY, r); }

    public int AddPolyline(string layer, IReadOnlyList<(double X, double Y)> pts, bool closed)
    {
        var xy = new double[pts.Count * 2];
        for (int i = 0; i < pts.Count; i++)
        {
            xy[i * 2] = pts[i].X + OriginX;
            xy[i * 2 + 1] = pts[i].Y + OriginY;
        }
        lock (_lock) return Native.mk_add_polyline(H, Z(layer), xy, pts.Count, closed ? 1 : 0);
    }

    public int Offset(int id, double d, double px, double py) { lock (_lock) return Native.mk_offset(H, id, d, px + OriginX, py + OriginY); }

    /// <summary>Sembolü yerleştir; blok tanımı yoksa önce eklenir</summary>
    public int InsertSymbol(CadSymbol sym, string layer, double x, double y, double scale, double rot)
    {
        lock (_lock)
        {
            if (Native.mk_define_block(H, Z(sym.BlockName), Z(sym.Snippet)) != 1) throw new CadException(LastError);
            return Native.mk_insert_block(H, Z(layer), Z(sym.BlockName), x + OriginX, y + OriginY, scale, rot);
        }
    }

    /// <summary>Ölçü: tek blok (MK_OLCU_n) olarak, p1 noktasına yerleştirilir</summary>
    public int InsertDimension(string layer, double x, double y, string snippet)
    {
        lock (_lock)
        {
            var name = "MK_OLCU_" + DateTime.UtcNow.Ticks.ToString("x");
            if (Native.mk_define_block(H, Z(name), Z(snippet)) != 1) throw new CadException(LastError);
            return Native.mk_insert_block(H, Z(layer), Z(name), x + OriginX, y + OriginY, 1, 0);
        }
    }

    /// <summary>Blok adı → yerleştirme sayısı</summary>
    public List<(string Name, int Count)> InsertCounts()
    {
        lock (_lock)
            return Native.Str(Native.mk_insert_counts(H)).Split('\n').Where(l => l.Contains('\t'))
                .Select(l => { var p = l.Split('\t', 2); return (p[0], int.TryParse(p[1], out var c) ? c : 0); }).ToList();
    }

    public int Group(IReadOnlyCollection<int> ids) { lock (_lock) return Native.mk_group(H, A(ids), ids.Count); }

    public List<int> Explode(IReadOnlyCollection<int> ids)
    {
        lock (_lock)
        {
            int first = Native.mk_explode(H, A(ids), ids.Count, out var n);
            return first < 0 ? [] : Enumerable.Range(first, n).ToList();
        }
    }

    public void Rotate(IReadOnlyCollection<int> ids, double cx, double cy, double deg) { lock (_lock) Native.mk_rotate(H, A(ids), ids.Count, cx + OriginX, cy + OriginY, deg); }
    public void Scale(IReadOnlyCollection<int> ids, double cx, double cy, double k) { lock (_lock) Native.mk_scale(H, A(ids), ids.Count, cx + OriginX, cy + OriginY, k); }

    public void Mirror(IReadOnlyCollection<int> ids, double x1, double y1, double x2, double y2, bool keep)
    { lock (_lock) Native.mk_mirror(H, A(ids), ids.Count, x1 + OriginX, y1 + OriginY, x2 + OriginX, y2 + OriginY, keep ? 1 : 0); }

    public bool SetText(int id, string text) { lock (_lock) return Native.mk_set_text(H, id, Z(text)) == 1; }
    public void SetLayer(IReadOnlyCollection<int> ids, string layer) { lock (_lock) Native.mk_set_layer(H, A(ids), ids.Count, Z(layer)); }
    /// <summary>aci: 1–255, 256 = katman rengi, 0 = bloktan</summary>
    public void SetColor(IReadOnlyCollection<int> ids, int aci) { lock (_lock) Native.mk_set_color(H, A(ids), ids.Count, aci); }

    public bool Undo() { lock (_lock) return Native.mk_undo(H) == 1; }
    public bool Redo() { lock (_lock) return Native.mk_redo(H) == 1; }
    public int UndoCount { get { lock (_lock) return Native.mk_undo_count(H); } }
    public int RedoCount { get { lock (_lock) return Native.mk_redo_count(H); } }

    private unsafe CadScene Extract(System.Diagnostics.Stopwatch sw)
    {
        var h = H;
        int nl = Native.mk_layer_count(h);
        var layers = new List<CadLayer>(nl);
        for (int i = 0; i < nl; i++)
            layers.Add(new CadLayer(Native.Str(Native.mk_layer_name(h, i)), Native.mk_layer_rgb(h, i),
                (Native.mk_layer_flags(h, i) & 1) != 0, Native.mk_layer_entity_count(h, i)));

        long nv = Native.mk_vertex_float_count(h);
        var v = new float[nv];
        if (nv > 0) Marshal.Copy(Native.mk_vertices(h), v, 0, (int)nv);
        for (long i = 0; i + 1 < nv; i += 2)
        {
            v[i] = (float)(v[i] - OriginX);
            v[i + 1] = (float)(v[i + 1] - OriginY);
        }

        int nb = Native.mk_batch_count(h);
        var bp = (Native.MkBatch*)Native.mk_batches(h);
        var batches = new List<LineBatch>(nb);
        for (int i = 0; i < nb; i++)
        {
            var b = bp[i];
            batches.Add(new LineBatch(b.Layer, b.Rgb, (int)(b.Offset / 2), (int)(b.Count / 2),
                (float)(b.MinX - OriginX), (float)(b.MinY - OriginY), (float)(b.MaxX - OriginX), (float)(b.MaxY - OriginY)));
        }

        int np = Native.mk_poly_count(h);
        var pp = (Native.MkPoly*)Native.mk_polys(h);
        var groups = new Dictionary<int, List<int>>();
        var order = new List<int>();
        for (int k = 0; k < np; k++)
        {
            if (!groups.TryGetValue(pp[k].Group, out var l)) { groups[pp[k].Group] = l = new(); order.Add(pp[k].Group); }
            l.Add(k);
        }
        var fills = order.Select(g =>
        {
            var ks = groups[g];
            var p0 = pp[ks[0]];
            return new FillGroup(p0.Layer, p0.Rgb, p0.Alpha, ks.Select(k => (int)(pp[k].Offset / 2)).ToArray(), ks.Select(k => pp[k].NPoints).ToArray());
        }).ToList();

        int nt = Native.mk_text_count(h);
        var tp = (Native.MkText*)Native.mk_texts(h);
        var pool = (byte*)Native.mk_text_pool(h);
        var texts = new List<CadText>(nt);
        for (int i = 0; i < nt; i++)
        {
            var t = tp[i];
            var s = t.Utf8Len == 0 ? "" : System.Text.Encoding.UTF8.GetString(pool + t.Utf8Offset, t.Utf8Len);
            texts.Add(new CadText((float)(t.X - OriginX), (float)(t.Y - OriginY), t.Height, t.Rotation, t.WidthFactor, t.BoxWidth,
                t.Layer, t.Rgb, t.HAlign, t.VAlign, s));
        }

        var ext = new float[4];
        Native.mk_extents(h, ext);
        return new CadScene
        {
            Name = Name,
            Version = Native.Str(Native.mk_dxf_version(h)),
            EntityCount = Native.mk_entity_count(h),
            Layers = layers,
            Vertices = v,
            Batches = batches,
            Fills = fills,
            Texts = texts,
            MinX = (float)(ext[0] - OriginX), MinY = (float)(ext[1] - OriginY),
            MaxX = (float)(ext[2] - OriginX), MaxY = (float)(ext[3] - OriginY),
            ParseMs = sw.ElapsedMilliseconds,
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_h != 0) Native.mk_close(_h);
            _h = 0;
        }
    }
}
