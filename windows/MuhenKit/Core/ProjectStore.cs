using System.Text.Json;
using System.Text.Json.Nodes;

namespace MuhenKit.Core;

/// <summary>Proje bilgisi (Android ProjectMeta ile aynı alanlar)</summary>
public sealed record ProjectMeta(
    string Id, string Name, string? Source, long Created, long Modified,
    /// <summary>Orijinal dosyaya kaydedilmemiş değişiklik var</summary>
    bool DirtyToSource = false,
    /// <summary>Görünüm (mutlak dünya koordinatı): merkez x, y ve yakınlaştırma</summary>
    double[]? View = null,
    int[]? Hidden = null,
    long SizeBytes = 0);

/// <summary>
/// "Projelerim": açılan her DXF uygulamanın kendi alanına kopyalanır, düzenlemeler bu kopyaya
/// otomatik kaydedilir. Kullanıcının orijinal dosyası yalnızca "Kaydet" ile değişir.
///   %LOCALAPPDATA%\MuhenKit\projects\&lt;id&gt;\drawing.dxf | meta.json | thumb.png
/// </summary>
public static class ProjectStore
{
    private static readonly object Lock = new();
    public static string Root
    {
        get
        {
            var r = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuhenKit", "projects");
            Directory.CreateDirectory(r);
            return r;
        }
    }

    private static string Dir(string id) => Path.Combine(Root, id);
    public static string Drawing(string id) => Path.Combine(Dir(id), "drawing.dxf");
    public static string Thumb(string id) => Path.Combine(Dir(id), "thumb.png");
    private static string LastFile => Path.Combine(Root, "..", "last.txt");

    public static long Now => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    public static string? LastProject
    {
        get
        {
            try
            {
                var id = File.Exists(LastFile) ? File.ReadAllText(LastFile).Trim() : null;
                return id != null && Directory.Exists(Dir(id)) ? id : null;
            }
            catch { return null; }
        }
        set { try { File.WriteAllText(LastFile, value ?? ""); } catch { } }
    }

    public static List<ProjectMeta> List()
    {
        lock (Lock)
            return Directory.GetDirectories(Root).Select(d => { try { return Meta(Path.GetFileName(d)); } catch { return null; } })
                .OfType<ProjectMeta>().OrderByDescending(m => m.Modified).ToList();
    }

    public static ProjectMeta Meta(string id)
    {
        lock (Lock)
        {
            var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Dir(id), "meta.json")))!.AsObject();
            var src = (string?)j["source"];
            var drawing = new FileInfo(Drawing(id));
            return new ProjectMeta(id, (string?)j["name"] ?? id, string.IsNullOrEmpty(src) ? null : src,
                (long?)j["created"] ?? 0, (long?)j["modified"] ?? 0, (bool?)j["dirty"] ?? false,
                j["view"]?.AsArray().Select(v => (double)v!).ToArray(),
                j["hidden"]?.AsArray().Select(v => (int)v!).ToArray(),
                drawing.Exists ? drawing.Length : 0);
        }
    }

    public static void SaveMeta(ProjectMeta m)
    {
        var j = new JsonObject
        {
            ["name"] = m.Name, ["source"] = m.Source ?? "", ["created"] = m.Created, ["modified"] = m.Modified, ["dirty"] = m.DirtyToSource,
        };
        if (m.View != null) j["view"] = new JsonArray(m.View.Select(v => (JsonNode)v).ToArray());
        j["hidden"] = new JsonArray((m.Hidden ?? []).Select(v => (JsonNode)v).ToArray());
        lock (Lock) AtomicWrite(Path.Combine(Dir(m.Id), "meta.json"), System.Text.Encoding.UTF8.GetBytes(j.ToJsonString()));
    }

    /// <summary>Yarım kalmış yazım çalışma kopyasını bozmasın: önce geçici dosya, sonra yer değiştir</summary>
    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            f.Write(bytes);
            f.Flush(true);
        }
        File.Move(tmp, path, true);
    }

    public static ProjectMeta Create(string name, byte[] bytes, string? source)
    {
        lock (Lock)
        {
            long now = Now;
            var id = now.ToString();
            while (Directory.Exists(Dir(id))) id += "_";
            AtomicWrite(Drawing(id), bytes);
            var m = new ProjectMeta(id, name, source, now, now, SizeBytes: bytes.Length);
            SaveMeta(m);
            return m;
        }
    }

    public static void WriteDrawing(string id, byte[] bytes) { lock (Lock) AtomicWrite(Drawing(id), bytes); }
    public static byte[] Read(string id) => File.ReadAllBytes(Drawing(id));

    public static ProjectMeta? FindBySource(string path) =>
        List().FirstOrDefault(m => string.Equals(m.Source, path, StringComparison.OrdinalIgnoreCase));

    public static void Rename(string id, string name) { lock (Lock) SaveMeta(Meta(id) with { Name = name }); }

    public static void Delete(string id)
    {
        lock (Lock)
        {
            try { Directory.Delete(Dir(id), true); } catch { }
            if (LastProject == id) LastProject = null;
        }
    }

    public static void WriteThumb(string id, byte[] png) { lock (Lock) AtomicWrite(Thumb(id), png); }

    /// <summary>"Yeni proje N" için sıradaki numara</summary>
    public static int NextBlankNumber() => List().Count(m => m.Name.StartsWith("Yeni proje")) + 1;
}
