using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using MuhenKit.Core;
using Windows.UI;
using static MuhenKit.Core.Calc;
using static MuhenKit.Modules.ModuleUi;

namespace MuhenKit.Modules;

/// <summary>Elektrik hesap modülleri (Android modules/Electrical.kt ile aynı girdiler ve çekirdek çağrıları)</summary>
public static class Electrical
{
    // ───────────────────────── Aydınlatma (verim metodu) ─────────────────────────

    public static void Lighting(ModuleUi ui)
    {
        ui.Subtitle = "Verim metodu: armatür sayısı, oda indeksi ve yerleşim";
        var combos = Table("combos").Skip(1).Select(r => r[0]).ToList();
        var soiling = Named("soiling");
        var lums = Named("luminaires");

        var room = ui.Card("Mahal");
        room.Children.Add(Row(ui.Num("En a", "6", "m", out var a), ui.Num("Boy b", "4", "m", out var b), ui.Num("Yükseklik H", "3", "m", out var hRoom)));
        room.Children.Add(Row(ui.Num("Çalışma düzlemi", "0.85", "m", out var work), ui.Num("Sarkıt", "0", "m", out var susp), ui.Num("Aydınlık E", "300", "lx", out var lux)));
        room.Children.Add(ui.Choice("Tavan-duvar-yer yansıtma", Enumerable.Range(0, combos.Count).ToList(), 0, i => combos[i], out var combo));
        room.Children.Add(ui.Named("Kirlenme (bakım) faktörü", soiling, 1.25, out var soil));

        var lum = ui.Card("Armatür");
        var fluxField = ui.Num("Armatür ışık akısı Ø", "3600", "lm", out var flux, out var setFlux);
        lum.Children.Add(ui.Named("Hazır armatür", lums, 3600, out _, v => setFlux(Field(v))));
        lum.Children.Add(fluxField);

        var res = ui.Card("Sonuç", right: true);
        var rh = ui.Result(res, "Hesap yüksekliği h = H − düzlem − sarkıt");
        var rk = ui.Result(res, "Oda indeksi k = a·b / h·(a+b)");
        var reta = ui.Result(res, "Verim η (tablodan)");
        var rflux = ui.Result(res, "Gerekli toplam akı Φ = E·A·d / η");
        var rexact = ui.Result(res, "Hesaplanan armatür");
        var rcount = ui.Result(res, "Kullanılacak armatür", big: true);
        var rlux = ui.Result(res, "Elde edilen aydınlık");

        var lay = ui.Card("Yerleşim", right: true);
        int cols = 1, rows = 1;
        double aa = 6, bb = 4;
        var canvas = new CanvasControl { Height = 260, ClearColor = Color.FromArgb(0, 0, 0, 0) };
        canvas.Draw += (s, e) =>
        {
            var ds = e.DrawingSession;
            float W = (float)s.ActualWidth, H = (float)s.ActualHeight;
            float k = (float)Math.Min(W / aa, H / bb) * 0.9f;
            float w = (float)aa * k, h = (float)bb * k, ox = (W - w) / 2, oy = (H - h) / 2;
            ds.DrawRectangle(ox, oy, w, h, Color.FromArgb(255, 0xED, 0xEA, 0xDF), 2);
            var c = Color.FromArgb(255, 0xF2, 0xB2, 0x66);
            float r = Math.Min(w / cols, h / rows) * 0.18f;
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    var p = new Vector2(ox + w * (i + 0.5f) / cols, oy + h * (j + 0.5f) / rows);
                    ds.DrawCircle(p, r, c, 2.5f);
                    float q = r * 0.7f;
                    ds.DrawLine(p.X - q, p.Y - q, p.X + q, p.Y + q, c, 1.5f);
                    ds.DrawLine(p.X - q, p.Y + q, p.X + q, p.Y - q, c, 1.5f);
                }
        };
        lay.Children.Add(canvas);
        var layNote = Note("");
        lay.Children.Add(layNote);
        ui.Card("Armatür yüksekliği").Children.Add(Image("img/armatur_yukseklik.jpg"));

        ui.Recalc = () =>
        {
            var r = Run("lighting", new Dictionary<string, object?>
            {
                ["a"] = a(), ["b"] = b(), ["H"] = hRoom(), ["work"] = work(), ["susp"] = susp(), ["lux"] = lux(),
                ["combo"] = combo(), ["soiling"] = soil(), ["flux"] = flux(),
            });
            rh.Set(Fmt(r.D("h")), "m");
            rk.Set(Fmt(r.D("k")));
            reta.Set(Fmt(r.D("eta"), 3));
            rflux.Set(Fmt(r.D("total_flux"), 0), "lm");
            rexact.Set(Fmt(r.D("exact_count")));
            rcount.Set($"{r.I("count")}  ({r.I("cols")} × {r.I("rows")})");
            rlux.Set(Fmt(r.D("achieved_lux"), 0), "lx", r.D("achieved_lux") >= (Num(lux()) ?? 0));
            cols = Math.Max(1, r.I("cols"));
            rows = Math.Max(1, r.I("rows"));
            aa = Math.Max(Num(a()) ?? 1, 0.1);
            bb = Math.Max(Num(b()) ?? 1, 0.1);
            layNote.Text = $"Armatürler aralarında eşit, duvara yarım aralık olacak şekilde yerleştirilir: {Fmt(aa / cols)} m × {Fmt(bb / rows)} m.";
            canvas.Invalidate();
        };
    }

    // ───────────────────────── Gerilim düşümü ─────────────────────────

    private static readonly double[] Sections = [1.5, 2.5, 4, 6, 10, 16, 25, 35, 50, 70, 95, 120, 150, 185, 240, 300];

    public static void VoltageDrop(ModuleUi ui)
    {
        ui.Subtitle = "Bir / üç faz %e, kesit önerisi ve ısınma kontrolü";
        var line = ui.Card("Hat");
        line.Children.Add(ui.Choice(null, [true, false], true, t => t ? "Üç faz 380 V" : "Bir faz 220 V", out var three));
        line.Children.Add(ui.Choice(null, [true, false], true, c => c ? "Bakır (k=56)" : "Alüminyum (k=35)", out var cu));
        line.Children.Add(Row(ui.Num("Güç P", "11008", "W", out var p), ui.Num("Uzunluk L", "30", "m", out var l)));
        line.Children.Add(Row(ui.Num("%e sınırı", "1.5", "%", out var limit), ui.Num("cos φ", "0.8", null, out var cos)));
        line.Children.Add(ui.Choice("Kesit (mm²)", Sections, 10.0, s => Fmt(s, 1), out var s));

        var res = ui.Card("Sonuç", right: true);
        var re = ui.Result(res, "Gerilim düşümü %e", big: true);
        var rdv = ui.Result(res, "ΔU");
        var ri = ui.Result(res, "Akım");
        var rf = ui.Result(res, "Sigorta");
        var rheat = ui.Result(res, "Isınma (kablo ≥ sigorta)");
        var rmin = ui.Result(res, "Sınır için en az kesit");
        var rsug = ui.Result(res, "Önerilen kablo", big: true);

        ui.Recalc = () =>
        {
            var r = Run("voltage_drop", new Dictionary<string, object?>
            {
                ["three"] = three(), ["k"] = cu() ? 56 : 35, ["p"] = p(), ["l"] = l(), ["s"] = s(), ["limit"] = limit(), ["cos"] = cos(),
            });
            re.Set(Fmt(r.D("e"), 3), "%", r.B("drop_ok"));
            rdv.Set(Fmt(r.D("dv")), "V");
            ri.Set(Fmt(r.D("i")), "A");
            rf.Set(r["fuse"], "A");
            if (r["selected.name"].Length == 0) rheat.Set("tabloda yok");
            else rheat.Set($"{r["selected.name"]}: {Fmt(r.D("selected.ampacity"), 0)} A", ok: r.B("heat_ok"));
            rmin.Set(Fmt(r.D("s_min")), "mm²");
            rsug.Set(r["suggested.name"]);
        };
    }

    // ───────────────────────── Eşzamanlılık ─────────────────────────

    public static void Diversity(ModuleUi ui)
    {
        ui.Subtitle = "Daire %60/%40 kuralı ve bina eşzamanlılık katsayısı";
        var bld = ui.Card("Bina");
        bld.Children.Add(Row(ui.Num("Daire kurulu gücü", "16000", "W", out var flatW), ui.Num("Daire sayısı", "12", null, out var flats)));
        bld.Children.Add(Row(ui.Num("Ortak alan gücü", "6000", "W", out var common), ui.Num("Ortak alan faktörü", "1", null, out var cf)));
        bld.Children.Add(ui.Num("cos φ", "0.8", null, out var cos));
        bld.Children.Add(Note("Ortak alan: merdiven, asansör, hidrofor vb."));

        var res = ui.Card("Sonuç", right: true);
        var r1 = ui.Result(res, "Daire talep gücü (8 kW'a kadar %60, üstü %40)");
        var r2 = ui.Result(res, "Bina eşzamanlılık katsayısı");
        var r3 = ui.Result(res, "Dairelerin talep gücü");
        var r4 = ui.Result(res, "Ortak alan talep gücü");
        var r5 = ui.Result(res, "Toplam talep gücü", big: true);
        var r6 = ui.Result(res, "Akım");
        var r7 = ui.Result(res, "Ana sigorta", big: true);

        var tc = ui.Card("Bina eşzamanlılık tablosu", right: true);
        tc.Children.Add(Note("Konutlarda bir dairenin eşzamanlı yükü: kurulu gücün 8 kW'a kadar olan bölümü %60, kalan bölümü %40. Binanın eşzamanlı yükü için daire sayısına göre katsayı:"));
        var (tview, table) = ModuleUi.Table(["Daire sayısı", "Katsayı"],
            Calc.Table("diversity").Skip(1).Select(t => (IList<string>)[t[1] == "99999" ? $"{t[0]} ve üstü" : $"{t[0]} – {t[1]}", "%" + t[2]]));
        tc.Children.Add(tview);

        ui.Recalc = () =>
        {
            var r = Run("diversity", new Dictionary<string, object?> { ["flat_w"] = flatW(), ["flats"] = flats(), ["common"] = common(), ["common_factor"] = cf(), ["cos"] = cos() });
            r1.Set(Fmt(r.D("per_flat"), 0), "W");
            r2.Set("%" + r["factor"]);
            r3.Set(Fmt(r.D("flats_demand"), 0), "W");
            r4.Set(Fmt(r.D("common_demand"), 0), "W");
            r5.Set(Fmt(r.D("total"), 0), "W");
            r6.Set(Fmt(r.D("i")), "A");
            r7.Set(r["fuse"], "A");
            table.Highlight(r.I("row"));
        };
    }

    // ───────────────────────── Kablo seçimi ─────────────────────────

    public static void Cable(ModuleUi ui)
    {
        ui.Subtitle = "NYY akım taşıma tablolarına göre sigortaya uygun kesit";
        var inp = ui.Card("Girdi");
        inp.Children.Add(ui.Num("Yük akımı", "45", "A", out var amps));
        inp.Children.Add(ui.Choice("Döşeme", [0, 1, 2], 0, m => new[] { "Hesap (ATK)", "Toprakta", "Havada" }[m], out var mode));
        inp.Children.Add(ui.Num("Düzeltme katsayısı (sıcaklık, gruplama)", "1", null, out var factor));

        var res = ui.Card("Sonuç", right: true);
        var rf = ui.Result(res, "Sigorta");
        var rc = ui.Result(res, "Seçilen kablo", big: true);
        var rs = ui.Result(res, "Faz kesiti");
        var rr = ui.Result(res, "R / X");

        var c1 = ui.Card("Hesap tablosu (ATK)");
        var (v1, t1) = ModuleUi.Table(["Kablo", "Hesap A", "Toprak A", "Hava A", "R Ω/km"],
            Calc.Table("cables").Skip(1).Select(t => (IList<string>)[t[0], t[2], t[3], t[4], t[5]]), [1.4, 1, 1, 1, 1]);
        c1.Children.Add(v1);
        var c2 = ui.Card("NYY (YVV) 0,6/1 kV kablo kataloğu · TS 212", right: true);
        c2.Children.Add(ModuleUi.Table(["Kesit mm²", "R Ω/km", "Toprak A", "Hava A", "kg/km"],
            Calc.Table("nyy").Skip(1).Select(t => (IList<string>)[t[0], t[1], t[2], t[3], t[4]]), [1.4, 1, 1, 1, 1]).View);

        ui.Recalc = () =>
        {
            var r = Run("cable", new Dictionary<string, object?> { ["amps"] = amps(), ["mode"] = mode(), ["factor"] = factor() });
            rf.Set(r["fuse"], "A");
            bool has = r["cable.name"].Length > 0;
            rc.Set(has ? r["cable.name"] : "tabloda yok", ok: has);
            rs.Visible = has;
            rr.Visible = has;
            rs.Set(Fmt(r.D("cable.section"), 1), "mm²");
            rr.Set($"{Fmt(r.D("cable.r"), 4)} / {Fmt(r.D("cable.x"), 3)}", "Ω/km");
            t1.Highlight(has ? r.I("index") : -1);
        };
    }

    // ───────────────────────── Motor & TMŞ ─────────────────────────

    public static void Motor(ModuleUi ui)
    {
        ui.Subtitle = "Motor gücüne göre akım, termik röle, sigorta ve kesit (Tablo 1.1)";
        var m = ui.Card("Motor");
        m.Children.Add(ui.Num("Motor gücü", "7.5", "kW", out var kw));
        m.Children.Add(Row(ui.Num("cos φ", "0.85", null, out var cos), ui.Num("Verim η", "0.88", null, out var eff)));

        var res = ui.Card("Sonuç", right: true);
        var r1 = ui.Result(res, "Hesaplanan akım");
        var r2 = ui.Result(res, "Tablo akımı");
        var r3 = ui.Result(res, "Termik röle ayar aralığı", big: true);
        var r4 = ui.Result(res, "Buşonlu sigorta (normal)");
        var r5 = ui.Result(res, "Buşonlu sigorta (gecikmeli)");
        var r6 = ui.Result(res, "NH sigorta");
        var r7 = ui.Result(res, "İletken kesiti", big: true);

        var tc = ui.Card("Tablo 1.1 · Motor gücüne göre termik röle ve sigorta seçimi");
        var (tv, table) = ModuleUi.Table(["kW", "HP", "Akım A", "mm²", "Termik A", "Buşon N", "Buşon G", "NH A"],
            Calc.Table("motor").Skip(1).Select(t => (IList<string>)t.Take(8).ToList()), [0.8, 0.8, 0.9, 0.7, 1.3, 1, 1, 1.1]);
        tc.Children.Add(tv);
        tc.Children.Add(Note("Buşon N: buşonlu normal · Buşon G: buşonlu gecikmeli · NH: bıçaklı sigorta"));

        ui.Recalc = () =>
        {
            var r = Run("motor", new Dictionary<string, object?> { ["kw"] = kw(), ["cos"] = cos(), ["eff"] = eff() });
            SetTitle(res, $"Sonuç (tablodan {r["kw"]} kW / {r["hp"]} HP)");
            r1.Set(Fmt(r.D("i_calc")), "A");
            r2.Set(Fmt(r.D("amps")), "A");
            r3.Set(r["termik"], "A");
            r4.Set(r["buson_normal"], "A");
            r5.Set(r["buson_gecikmeli"], "A");
            r6.Set(r["nh"], "A");
            r7.Set(Fmt(r.D("kesit"), 1), "mm²");
            table.Highlight(r.I("row"));
        };
    }
}
