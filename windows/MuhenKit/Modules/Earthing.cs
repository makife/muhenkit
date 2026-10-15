using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MuhenKit.Core;
using static MuhenKit.Core.Calc;
using static MuhenKit.Modules.ModuleUi;

namespace MuhenKit.Modules;

/// <summary>Topraklama ve paratoner (Android modules/Earthing.kt ile aynı)</summary>
public static class Earthing
{
    private static void Show(FrameworkElement e, bool on) => e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    // ───────────────────────── Topraklama ─────────────────────────

    public static void Grounding(ModuleUi ui)
    {
        ui.Subtitle = "Şerit, çubuk, temel ve gözlü topraklama · Wenner · Onderdonk";
        var conductors = Named("conductors");

        var soil = ui.Card("Toprak");
        soil.Children.Add(ui.Num("Özgül direnç ρ", "100", "Ωm", out var rho));
        soil.Children.Add(Note("Ölçüm yaptıysan Wenner bölümünden ortalama ρ'yu bulup buraya yaz."));

        var el = ui.Card("Topraklayıcılar");
        var stripRow = Row(ui.Num("Uzunluk", "106", "m", out var stripL), ui.Num("Derinlik", "1", "m", out var stripH));
        var stripCond = ui.Named("İletken (eşdeğer çap)", conductors, 0.008, out var stripD);
        var rodRow = Row(ui.Num("Boy", "1.75", "m", out var rodL), ui.Num("Çap", "0.02", "m", out var rodD), ui.Num("Adet", "8", null, out var rodN));
        var foundRow = Row(ui.Num("Temel eni", "17", "m", out var fa), ui.Num("Temel boyu", "28", "m", out var fb));
        var meshRow = ui.Num("Toplam ağ iletkeni", "170", "m", out var meshL);
        el.Children.Add(ui.Toggle("Gömülü şerit / iletken", true, out var useStrip, on => { Show(stripRow, on); Show(stripCond, on); }));
        el.Children.Add(stripRow);
        el.Children.Add(stripCond);
        el.Children.Add(ui.Toggle("Çubuk topraklayıcı", true, out var useRod, on => Show(rodRow, on)));
        el.Children.Add(rodRow);
        el.Children.Add(ui.Toggle("Temel topraklayıcı", true, out var useFound));
        el.Children.Add(foundRow);
        el.Children.Add(Note("Temel ölçüleri en az çubuk sayısı hesabında da kullanılır."));
        el.Children.Add(ui.Toggle("Gözlü (ağ) topraklama", false, out var useMesh, on => Show(meshRow, on)));
        el.Children.Add(meshRow);
        Show(meshRow, false);

        var res = ui.Card("Sonuç", right: true);
        var rs = ui.Result(res, "Şerit R");
        var rr = ui.Result(res, "Çubuklar R");
        var rf = ui.Result(res, "Temel R");
        var rm = ui.Result(res, "Gözlü R");
        var rt = ui.Result(res, "Paralel eşdeğer R", big: true);
        res.Children.Add(Row(ui.Num("Ia (koruma akımı)", "25", "A", out var ia), ui.Num("UL (dokunma)", "50", "V", out var ul)));
        var rmax = ui.Result(res, "İzin verilen en büyük R = UL / Ia");
        var rmin = ui.Result(res, "Temel alanına göre en az çubuk");

        var wc = ui.Card("Wenner ölçümü (ρ = 2π·a·R)", right: true);
        string[] aList = ["1", "2", "4", "6"];
        var wr = new Func<string>[4];
        var wrho = new TextBlock[4];
        for (int i = 0; i < 4; i++)
        {
            var g = new Grid { ColumnSpacing = 10 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            var lab = Text($"a = {aList[i]} m");
            lab.VerticalAlignment = VerticalAlignment.Bottom;
            lab.Margin = new Thickness(0, 0, 0, 8);
            var f = ui.Num("Ölçülen R", "", "Ω", out wr[i]);
            Grid.SetColumn(f, 1);
            wrho[i] = Text("ρ = —");
            wrho[i].VerticalAlignment = VerticalAlignment.Bottom;
            wrho[i].Margin = new Thickness(0, 0, 0, 8);
            Grid.SetColumn(wrho[i], 2);
            g.Children.Add(lab);
            g.Children.Add(f);
            g.Children.Add(wrho[i]);
            wc.Children.Add(g);
        }
        wc.Children.Add(ui.Num("Mevsim katsayısı", "2", null, out var season));
        var ravg = ui.Result(wc, "Hesaba esas ortalama ρ", big: true);

        var oc = ui.Card("Onderdonk: kısa devreye göre iletken kesiti", right: true);
        oc.Children.Add(Row(ui.Num("Kısa devre akımı", "10", "kA", out var ika), ui.Num("Süre", "1", "s", out var t)));
        var o1 = ui.Result(oc, "Kaynak (Cadweld)");
        var o2 = ui.Result(oc, "Lehim / bindirme kaynağı");
        var o3 = ui.Result(oc, "Cıvatalı / pres");

        ui.Recalc = () =>
        {
            var input = new Dictionary<string, object?>
            {
                ["rho"] = rho(), ["use_strip"] = useStrip(), ["strip_l"] = stripL(), ["strip_d"] = stripD(), ["strip_h"] = stripH(),
                ["use_rod"] = useRod(), ["rod_l"] = rodL(), ["rod_d"] = rodD(), ["rod_n"] = rodN(), ["use_found"] = useFound(), ["fa"] = fa(), ["fb"] = fb(),
                ["use_mesh"] = useMesh(), ["mesh_l"] = meshL(), ["ia"] = ia(), ["ul"] = ul(), ["season"] = season(), ["ika"] = ika(), ["t"] = t(),
            };
            for (int i = 0; i < 4; i++) input[$"w_r.{i}"] = wr[i]();
            var r = Run("grounding", input);
            rs.Visible = useStrip(); rs.Set(Fmt(r.D("r_strip")), "Ω");
            rr.Visible = useRod(); rr.Set(Fmt(r.D("r_rod")), "Ω");
            rf.Visible = useFound(); rf.Set(Fmt(r.D("r_found")), "Ω");
            rm.Visible = useMesh(); rm.Set(Fmt(r.D("r_mesh")), "Ω");
            rt.Set(Fmt(r.D("r_total"), 3), "Ω", r.B("ok"));
            rmax.Set(Fmt(r.D("r_max"), 3), "Ω");
            rmin.Set(r["min_rods"], "adet");
            for (int i = 0; i < 4; i++) wrho[i].Text = "ρ = " + Fmt(r.D($"w_rho.{i}"), 1);
            ravg.Set(Fmt(r.D("rho_avg"), 1), "Ωm");
            o1.Set(Fmt(r.D("onderdonk_cadweld"), 1), "mm²");
            o2.Set(Fmt(r.D("onderdonk_lehim"), 1), "mm²");
            o3.Set(Fmt(r.D("onderdonk_civata"), 1), "mm²");
        };
    }

    // ───────────────────────── Paratoner ─────────────────────────

    public static void Lightning(ModuleUi ui)
    {
        ui.Subtitle = "Yıldırım risk analizi (Nd/Nc), koruma seviyesi, aktif paratoner yarıçapı";
        var b = ui.Card("Yapı");
        b.Children.Add(Row(ui.Num("Boy L", "26", "m", out var l), ui.Num("En W", "21", "m", out var w), ui.Num("Yükseklik H", "30", "m", out var h)));
        b.Children.Add(ui.Num("Yıllık yıldırımlı gün Nk", "20", "gün", out var nk));
        b.Children.Add(Note("Nk: aşağıdaki yıldırımlı gün haritasından."));
        b.Children.Add(ui.Named("C1 · çevre", Named("lp_c1"), 0.25, out var c1));
        b.Children.Add(ui.Named("C2 · yapı türü", Named("lp_c2"), 1.0, out var c2));
        b.Children.Add(ui.Named("C3 · içerik", Named("lp_c3"), 0.5, out var c3));
        b.Children.Add(ui.Named("C4 · kullanım", Named("lp_c4"), 1.0, out var c4));
        b.Children.Add(ui.Named("C5 · sonuç", Named("lp_c5"), 5.0, out var c5));
        ui.Card("Yıldırımlı gün haritası (Nk)").Children.Add(Image("img/yildirim_haritasi.jpg"));

        var risk = ui.Card("Risk", right: true);
        var rae = ui.Result(risk, "Eşdeğer toplama alanı Ae");
        var rng = ui.Result(risk, "Yer yıldırım yoğunluğu Ng");
        var rnd = ui.Result(risk, "Beklenen düşme Nd");
        var rnc = ui.Result(risk, "Kabul edilebilir Nc");
        var rres = ui.Result(risk, "Sonuç", big: true);

        var ese = ui.Card("Aktif paratoner (ESE)", right: true);
        var levelText = new[] { "Otomatik", "Seviye 1", "Seviye 2", "Seviye 3", "Seviye 4" };
        ese.Children.Add(ui.Choice("Koruma seviyesi", [0, 1, 2, 3, 4], 0, i => levelText[i], out var level));
        ese.Children.Add(ui.Num("Direk yüksekliği", "6", "m", out var mast));
        ese.Children.Add(ui.Choice("ΔT (µs)", [15, 25, 45, 60], 60, i => i.ToString(), out var dt));
        var rd = ui.Result(ese, "Yuvarlanan küre D");
        var rlv = ui.Result(ese, "Kullanılan seviye");
        var rp = ui.Result(ese, "Koruma yarıçapı Rp", big: true);
        var rhd = ui.Result(ese, "Yapının yarı köşegeni");
        var rcov = ui.Result(ese, "Tek paratoner yeterli mi");

        ui.Recalc = () =>
        {
            var r = Run("lightning", new Dictionary<string, object?>
            {
                ["l"] = l(), ["w"] = w(), ["h"] = h(), ["nk"] = nk(), ["c1"] = c1(), ["c2"] = c2(), ["c3"] = c3(), ["c4"] = c4(), ["c5"] = c5(),
                ["mast"] = mast(), ["delta_t"] = dt(), ["level"] = level(),
            });
            var inv = CultureInfo.InvariantCulture;
            rae.Set(Fmt(r.D("ae"), 0), "m²");
            rng.Set(Fmt(r.D("ng"), 3), "/km²·yıl");
            rnd.Set(r.D("nd").ToString("0.000E+0", inv).Replace('.', ','));
            rnc.Set(r.D("nc").ToString("0.000E+0", inv).Replace('.', ','));
            if (r.I("level") == 0) rres.Set("Koruma zorunlu değil", ok: true);
            else rres.Set($"E = %{Fmt(r.D("e") * 100, 1)} → Seviye {r["level"]}", ok: false);
            rd.Set(Fmt(r.D("d"), 0), "m");
            rlv.Set(r["level_used"]);
            rp.Set(Fmt(r.D("rp"), 1), "m");
            rhd.Set(Fmt(r.D("half_diag"), 1), "m");
            rcov.Set(r.B("covers") ? "Evet" : "Hayır", ok: r.B("covers"));
        };
    }
}
