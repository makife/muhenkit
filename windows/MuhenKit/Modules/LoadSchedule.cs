using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MuhenKit.Core;
using static MuhenKit.Core.Calc;
using static MuhenKit.Modules.ModuleUi;

namespace MuhenKit.Modules;

/// <summary>Yükleme cetveli (Android modules/LoadSchedule.kt ile aynı çekirdek girdileri). Masaüstünde linyeler tablo satırı.</summary>
public static class LoadSchedule
{
    /// <summary>Linye: tür L aydınlatma, P priz, O özel, M motor; faz R/S/T ya da RST</summary>
    private sealed class Linye(string name, string type, string count, string unitW, string length, string phase)
    {
        public string Name = name, Type = type, Count = count, UnitW = unitW, Length = length, Phase = phase;
    }

    private static readonly Dictionary<string, string> TypeNames = new() { ["L"] = "Aydınlatma", ["P"] = "Priz", ["O"] = "Özel", ["M"] = "Motor" };

    public static void Build(ModuleUi ui)
    {
        ui.Subtitle = "Linyeler, faz dağılımı, sigorta/kablo seçimi, gerilim düşümü";
        var ls = new List<Linye>
        {
            new("L1", "L", "6", "100", "15", "R"), new("L2", "L", "6", "100", "15", "S"),
            new("P1", "P", "5", "300", "15", "T"), new("P2", "P", "5", "300", "15", "R"),
            new("P3", "O", "1", "2000", "10", "S"),
        };

        var lineInfo = new List<TextBlock>();
        var pano = ui.Card("Pano");
        bool threeNow = true;
        var dfField = ui.Num("Talep faktörü", "0.6", null, out var demandFactor);
        dfField.Visibility = Visibility.Collapsed;
        StackPanel? rowsPanel = null;
        Button? balanceBtn = null;
        pano.Children.Add(ui.Choice(null, [true, false], true, t => t ? "Trifaze pano" : "Monofaze pano", out var three, t =>
        {
            threeNow = t;
            if (balanceBtn != null) balanceBtn.Visibility = t ? Visibility.Visible : Visibility.Collapsed;
            Rebuild();
        }));
        pano.Children.Add(ui.Choice("Talep gücü", [0, 1], 0, m => m == 0 ? "Konut %60/%40" : "Sabit katsayı", out var demandMode,
            m => dfField.Visibility = m == 1 ? Visibility.Visible : Visibility.Collapsed));
        pano.Children.Add(dfField);
        pano.Children.Add(Row(ui.Num("Kolon uzunluğu", "15", "m", out var kolonL), ui.Num("%e sınırı", "1.5", "%", out var limit), ui.Num("cos φ (kolon)", "0.8", null, out var cos)));

        var lc = ui.Card("Linyeler");
        rowsPanel = new StackPanel { Spacing = 6 };
        lc.Children.Add(rowsPanel);
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var t in new[] { "L", "P", "O", "M" })
        {
            var b = new Button { Content = "+ " + TypeNames[t] };
            b.Click += (_, _) => { Add(t); Rebuild(); };
            addRow.Children.Add(b);
        }
        balanceBtn = new Button { Content = "Fazları dengele", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        balanceBtn.Click += (_, _) =>
        {
            var bres = Run("load_schedule", Input(true));
            for (int i = 0; i < ls.Count; i++) if (bres[$"linye.{i}.phase"] is { Length: > 0 } ph) ls[i].Phase = ph;
            Rebuild();
        };
        addRow.Children.Add(balanceBtn);
        lc.Children.Add(addRow);

        var res = ui.Card("Sonuç", right: true);
        var rInst = ui.Result(res, "Kurulu güç");
        var rDem = ui.Result(res, "Talep gücü", big: true);
        var rCur = ui.Result(res, "Pano akımı");
        var rFuse = ui.Result(res, "Ana sigorta", big: true);
        var rKolon = ui.Result(res, "Kolon kablosu");
        var rKDrop = ui.Result(res, "Kolon %e");
        var rTot = ui.Result(res, "Toplam %e");
        var rPh = ui.Result(res, "Faz yükleri R / S / T");
        var rUnb = ui.Result(res, "Dengesizlik");
        var rKakr = ui.Result(res, "Kaçak akım rölesi");

        Dictionary<string, object?> Input(bool balance)
        {
            var m = new Dictionary<string, object?>
            {
                ["three"] = three(), ["demand_mode"] = demandMode(), ["demand_factor"] = demandFactor(), ["kolon_l"] = kolonL(),
                ["limit"] = limit(), ["cos"] = cos(), ["n"] = ls.Count, ["balance"] = balance,
            };
            for (int i = 0; i < ls.Count; i++)
            {
                var l = ls[i];
                m[$"linye.{i}.name"] = l.Name; m[$"linye.{i}.type"] = l.Type; m[$"linye.{i}.count"] = l.Count;
                m[$"linye.{i}.unit_w"] = l.UnitW; m[$"linye.{i}.length"] = l.Length; m[$"linye.{i}.phase"] = three() ? l.Phase : "R";
            }
            return m;
        }

        void Add(string t)
        {
            var prefix = t switch { "L" => "L", "M" => "M", _ => "P" };
            int n = ls.Count(l => l.Name.StartsWith(prefix)) + 1;
            ls.Add(t switch
            {
                "L" => new Linye($"{prefix}{n}", t, "6", "100", "15", "R"),
                "P" => new Linye($"{prefix}{n}", t, "5", "300", "15", "R"),
                "O" => new Linye($"{prefix}{n}", t, "1", "2000", "10", "R"),
                _ => new Linye($"{prefix}{n}", t, "1", "4000", "10", threeNow ? "RST" : "R"),
            });
        }

        // satır: ad · tür · adet · birim W · uzunluk · faz · sonuç · sil
        void Rebuild()
        {
            rowsPanel!.Children.Clear();
            lineInfo.Clear();
            for (int i = 0; i < ls.Count; i++)
            {
                var l = ls[i];
                int idx = i;
                var g = new Grid { ColumnSpacing = 8 };
                foreach (var w in new[] { 0.8, 1.0, 1.0, 1.0, 0.9, 0.0 })
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w, GridUnitType.Star) : GridLength.Auto });
                var head = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
                head.Children.Add(Text(l.Name, bold: true, size: 15));
                head.Children.Add(Note(TypeNames[l.Type]));
                g.Children.Add(head);
                void Field(int col, string label, string val, string? unit, Action<string> set)
                {
                    var f = ui.Num(label, val, unit, out _, set);
                    Grid.SetColumn(f, col);
                    g.Children.Add(f);
                }
                Field(1, l.Type == "L" ? "Sorti" : "Adet", l.Count, null, v => l.Count = v);
                Field(2, "Birim güç", l.UnitW, "W", v => l.UnitW = v);
                Field(3, "Uzunluk", l.Length, "m", v => l.Length = v);
                if (threeNow)
                {
                    var phases = l.Type == "M" ? new List<string> { "RST" } : ["R", "S", "T", "RST"];
                    var cb = new ComboBox { Header = "Faz", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = phases, SelectedItem = phases.Contains(l.Phase) ? l.Phase : phases[0] };
                    cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is string p) { l.Phase = p; ui.Changed(); } };
                    Grid.SetColumn(cb, 4);
                    g.Children.Add(cb);
                }
                var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, VerticalAlignment = VerticalAlignment.Bottom, Style = (Style)Application.Current.Resources["SubtleButtonStyle"] };
                ToolTipService.SetToolTip(del, "Linyeyi sil");
                del.Click += (_, _) => { ls.RemoveAt(idx); Rebuild(); };
                Grid.SetColumn(del, 5);
                g.Children.Add(del);
                var info = Text("", bold: true);
                info.Foreground = AccentBrush;
                lineInfo.Add(info);
                var block = new StackPanel { Spacing = 4, Padding = new Thickness(0, 6, 0, 8), BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(24, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 0, 1) };
                block.Children.Add(g);
                block.Children.Add(info);
                rowsPanel.Children.Add(block);
            }
            ui.Changed();
        }

        ui.Recalc = () =>
        {
            var r = Run("load_schedule", Input(false));
            for (int i = 0; i < lineInfo.Count; i++)
            {
                var p = $"linye.{i}.";
                lineInfo[i].Text = $"{Fmt(r.D(p + "power"), 0)} W · {Fmt(r.D(p + "current"))} A · {r[p + "fuse"]} A sigorta · {r[p + "cable"]} · %e {Fmt(r.D(p + "drop"), 3)}";
            }
            rInst.Set(Fmt(r.D("installed"), 0), "W");
            rDem.Set(Fmt(r.D("demand"), 0), "W");
            rCur.Set(Fmt(r.D("board_current")), "A");
            rFuse.Set(r["main_fuse"], "A");
            rKolon.Set(r["kolon.name"].Length > 0 ? r["kolon.name"] : "sınırı sağlayan yok", ok: r["kolon.name"].Length > 0);
            rKDrop.Set(Fmt(r.D("kolon_drop"), 3), "%");
            int worst = r.I("worst");
            rTot.LabelText = $"Toplam %e (kolon + {(worst >= 0 ? r[$"linye.{worst}.name"] : "-")})";
            rTot.Set(Fmt(r.D("total_drop"), 3), "%", r.D("total_drop") <= (Num(limit()) ?? 1.5) * 2);
            rPh.Visible = three();
            rUnb.Visible = three();
            rPh.Set($"{Fmt(r.D("phase_r"), 0)} / {Fmt(r.D("phase_s"), 0)} / {Fmt(r.D("phase_t"), 0)}", "W");
            rUnb.Set(Fmt(r.D("unbalance"), 1), "%", r.D("unbalance") <= 20);
            rKakr.Set(r["kakr"]);
        };
        Rebuild();
    }
}
