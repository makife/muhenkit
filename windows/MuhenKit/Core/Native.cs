using System.Runtime.InteropServices;

namespace MuhenKit.Core;

/// <summary>C++ çekirdeği (mkcore.dll: mkcad + mkcalc). Yapılar mkcad.h ile birebir.</summary>
internal static partial class Native
{
    private const string Dll = "mkcore.dll"; // C# derlemesi MuhenKit.dll ile büyük/küçük harf çakışmasın

    [StructLayout(LayoutKind.Sequential)]
    public struct MkBatch
    {
        public int Layer;
        public uint Rgb;
        public int Kind;
        public int Pad;
        public long Offset;
        public long Count;
        public float MinX, MinY, MaxX, MaxY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MkPoly
    {
        public int Layer;
        public uint Rgb;
        public int Group;
        public int NPoints;
        public long Offset;
        public float Alpha;
        public int Pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MkText
    {
        public float X, Y, Height, Rotation, WidthFactor, BoxWidth;
        public int Layer;
        public uint Rgb;
        public int HAlign, VAlign;
        public int Utf8Offset, Utf8Len;
    }

    // ── belge
    [LibraryImport(Dll)] public static partial nint mk_open(byte[] data, long len);
    [LibraryImport(Dll)] public static partial nint mk_last_error();
    [LibraryImport(Dll)] public static partial void mk_close(nint doc);
    [LibraryImport(Dll)] public static partial int mk_build_scene(nint doc);
    [LibraryImport(Dll)] public static partial nint mk_dxf_version(nint doc);
    [LibraryImport(Dll)] public static partial int mk_entity_count(nint doc);
    [LibraryImport(Dll)] public static partial int mk_layer_count(nint doc);
    [LibraryImport(Dll)] public static partial nint mk_layer_name(nint doc, int i);
    [LibraryImport(Dll)] public static partial uint mk_layer_rgb(nint doc, int i);
    [LibraryImport(Dll)] public static partial int mk_layer_flags(nint doc, int i);
    [LibraryImport(Dll)] public static partial int mk_layer_entity_count(nint doc, int i);
    [LibraryImport(Dll)] public static partial void mk_extents(nint doc, [Out] float[] out4);
    [LibraryImport(Dll)] public static partial nint mk_vertices(nint doc);
    [LibraryImport(Dll)] public static partial long mk_vertex_float_count(nint doc);
    [LibraryImport(Dll)] public static partial int mk_batch_count(nint doc);
    [LibraryImport(Dll)] public static partial nint mk_batches(nint doc);
    [LibraryImport(Dll)] public static partial int mk_poly_count(nint doc);
    [LibraryImport(Dll)] public static partial nint mk_polys(nint doc);
    [LibraryImport(Dll)] public static partial int mk_text_count(nint doc);
    [LibraryImport(Dll)] public static partial nint mk_texts(nint doc);
    [LibraryImport(Dll)] public static partial nint mk_text_pool(nint doc);

    // ── kayıt
    [LibraryImport(Dll)] public static partial nint mk_save(nint doc, out long len);
    [LibraryImport(Dll)] public static partial nint mk_save_copy(nint doc, out long len);
    [LibraryImport(Dll)] public static partial void mk_set_dirty(nint doc, int dirty);
    [LibraryImport(Dll)] public static partial int mk_is_dirty(nint doc);

    // ── düzenleme (kimlikler: model alanı nesne indeksi; koordinatlar mutlak)
    [LibraryImport(Dll)] public static partial int mk_pick(nint doc, double x, double y, double tol);
    [LibraryImport(Dll)] public static partial int mk_snap(nint doc, double x, double y, double tol, [Out] double[] out2);
    [LibraryImport(Dll)] public static partial nint mk_entity_geometry(nint doc, int id, out long floatCount);
    [LibraryImport(Dll)] public static partial int mk_select_rect(nint doc, double x0, double y0, double x1, double y1, int crossing, [Out] int[]? ids, int cap);
    [LibraryImport(Dll)] public static partial nint mk_entity_info(nint doc, int id);
    [LibraryImport(Dll)] public static partial int mk_move(nint doc, int[] ids, int n, double dx, double dy);
    [LibraryImport(Dll)] public static partial int mk_copy(nint doc, int[] ids, int n, double dx, double dy);
    [LibraryImport(Dll)] public static partial int mk_delete(nint doc, int[] ids, int n);
    [LibraryImport(Dll)] public static partial int mk_add_line(nint doc, byte[] layer, double x1, double y1, double x2, double y2);
    [LibraryImport(Dll)] public static partial int mk_add_text(nint doc, byte[] layer, double x, double y, double h, double rot, byte[] utf8);
    [LibraryImport(Dll)] public static partial int mk_add_circle(nint doc, byte[] layer, double cx, double cy, double r);
    [LibraryImport(Dll)] public static partial int mk_add_polyline(nint doc, byte[] layer, double[] xy, int n, int closed);
    [LibraryImport(Dll)] public static partial int mk_offset(nint doc, int id, double d, double px, double py);
    [LibraryImport(Dll)] public static partial int mk_define_block(nint doc, byte[] name, byte[] snippet);
    [LibraryImport(Dll)] public static partial int mk_insert_block(nint doc, byte[] layer, byte[] name, double x, double y, double scale, double rot);
    [LibraryImport(Dll)] public static partial nint mk_insert_counts(nint doc);
    [LibraryImport(Dll)] public static partial int mk_group(nint doc, int[] ids, int n);
    [LibraryImport(Dll)] public static partial int mk_explode(nint doc, int[] ids, int n, out int count);
    [LibraryImport(Dll)] public static partial int mk_rotate(nint doc, int[] ids, int n, double cx, double cy, double deg);
    [LibraryImport(Dll)] public static partial int mk_scale(nint doc, int[] ids, int n, double cx, double cy, double k);
    [LibraryImport(Dll)] public static partial int mk_mirror(nint doc, int[] ids, int n, double x1, double y1, double x2, double y2, int keep);
    [LibraryImport(Dll)] public static partial nint mk_entity_text(nint doc, int id);
    [LibraryImport(Dll)] public static partial int mk_set_text(nint doc, int id, byte[] utf8);
    [LibraryImport(Dll)] public static partial int mk_set_layer(nint doc, int[] ids, int n, byte[] layer);
    [LibraryImport(Dll)] public static partial int mk_set_color(nint doc, int[] ids, int n, int aci);
    [LibraryImport(Dll)] public static partial int mk_undo(nint doc);
    [LibraryImport(Dll)] public static partial int mk_redo(nint doc);
    [LibraryImport(Dll)] public static partial int mk_undo_count(nint doc);
    [LibraryImport(Dll)] public static partial int mk_redo_count(nint doc);

    // ── hesap çekirdeği
    [LibraryImport(Dll)] public static partial nint mc_calc(byte[] module, byte[] input);
    [LibraryImport(Dll)] public static partial nint mc_table(byte[] name);

    /// <summary>C dizgesini (UTF-8) oku</summary>
    public static string Str(nint p) => p == 0 ? "" : Marshal.PtrToStringUTF8(p) ?? "";

    /// <summary>Sıfır sonlu UTF-8 bayt dizisi</summary>
    public static byte[] Z(string s)
    {
        var b = System.Text.Encoding.UTF8.GetBytes(s);
        Array.Resize(ref b, b.Length + 1);
        return b;
    }
}
