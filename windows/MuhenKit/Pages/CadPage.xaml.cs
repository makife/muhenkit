using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MuhenKit.Cad;
using MuhenKit.Core;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

namespace MuhenKit.Pages;

/// <summary>Çizim sayfasına giriş isteği (Frame.Navigate parametresi; dosya yolu için düz string)</summary>
public abstract record CadRequest
{
    public sealed record Project(string Id) : CadRequest;
    public sealed record Blank : CadRequest;
}

/// <summary>DXF çizim sayfası: masaüstü CAD düzeni (şerit, sol araçlar, tuval, sağ panel, komut/durum satırı)</summary>
public sealed partial class CadPage : Page
{
    private CadDocument? _doc;
    private readonly CadEditor _ed;
    private readonly Dictionary<CadTool, ToggleButton> _toolButtons = new();
    private bool _syncing;

    private static readonly (int Aci, uint Rgb, string Name)[] Colors =
    [
        (256, 0x000000, "Katman rengi (DAMARDAN)"), (1, 0xFF0000, "Kırmızı"), (2, 0xFFFF00, "Sarı"), (3, 0x00FF00, "Yeşil"),
        (4, 0x00FFFF, "Camgöbeği"), (5, 0x0000FF, "Mavi"), (6, 0xFF00FF, "Eflatun"), (7, 0xFFFFFF, "Beyaz/Siyah"),
        (8, 0x808080, "Koyu gri"), (9, 0xC0C0C0, "Açık gri"), (30, 0xFF7F00, "Turuncu"), (0, 0x000000, "Bloktan"),
    ];

    public CadPage()
    {
        InitializeComponent();
        _ed = new CadEditor(Canvas);
        _ed.Changed += SyncUi;
        _ed.LiveChanged += SyncLive;
        _ed.Message += ShowMessage;
        _ed.SceneChanged += s =>
        {
            _edits++;
            FillLayers(s);
            InfoText.Text = $"{s.EntityCount} nesne · {s.Layers.Count} katman";
        };
        _ed.TextRequested += OnTextRequested;
        Canvas.PointerMovedWorld += (x, y) =>
        {
            if (_doc == null) return;
            CoordText.Text = $"X {x + _doc.OriginX,11:F2}  Y {y + _doc.OriginY,11:F2}";
        };
        Canvas.ViewChanged += _ => _metaChanged = true;
        // sağ tık: çizgi/çoklu çizgi bitir, yoksa Esc
        Canvas.RightClick += _ed.RightClick;
        BuildToolStrip();
        BuildColors();
        BuildCommands(0);
        KeyDown += OnPageKey;
        CharacterReceived += OnPageChar;
        SyncUi();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        switch (e.Parameter)
        {
            case string path when File.Exists(path): await OpenPath(path); break;
            case CadRequest.Project pr: await OpenProject(pr.Id); break;
            case CadRequest.Blank: OnNew(this, new RoutedEventArgs()); break;
            default:
                // "Son projene devam et"
                if (_doc == null && ProjectStore.LastProject is { } last) await OpenProject(last);
                break;
        }
    }

    // ───────────────────────── şerit ─────────────────────────

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        BuildCommands(sender.Items.IndexOf(sender.SelectedItem));

    private void BuildCommands(int tab)
    {
        Commands.PrimaryCommands.Clear();
        bool has = _doc != null;
        AppBarButton B(string label, string glyph, Action click, bool enabled = true, string? tip = null)
        {
            var b = new AppBarButton { Label = label, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            if (tip != null) ToolTipService.SetToolTip(b, tip);
            b.Click += (_, _) => click();
            Commands.PrimaryCommands.Add(b);
            return b;
        }
        void T(CadTool t, string? tip = null) => B(CadEditor.ToolInfo[t].Label, CadEditor.ToolInfo[t].Glyph, () => _ed.SelectTool(t), has, tip);
        void Sep() => Commands.PrimaryCommands.Add(new AppBarSeparator());
        switch (tab)
        {
            case 0:
                B("Boş proje", "", () => OnNew(this, new RoutedEventArgs()));
                B("DXF aç", "", () => OnOpen(this, new RoutedEventArgs()), tip: "Ctrl+O");
                B("Kaydet", "", () => OnSave(this, new RoutedEventArgs()), has, "Ctrl+S");
                B("Farklı kaydet", "", () => OnSaveAs(this, new RoutedEventArgs()), has);
                Sep();
                B("PDF / resim", "", OnExport, has, "Ctrl+P — çizimi PDF ya da PNG olarak dışa aktar");
                break;
            case 1:
                T(CadTool.Line, "L");
                T(CadTool.PLine, "PL");
                T(CadTool.Circle, "C");
                T(CadTool.Rect, "REC");
                T(CadTool.Text, "T");
                Sep();
                T(CadTool.Dimension, "DIM");
                T(CadTool.Measure, "DI");
                break;
            case 2:
                B("Geri al", "", _ed.Undo, has && _ed.UndoCount > 0, "Ctrl+Z");
                B("Yinele", "", _ed.Redo, has && _ed.RedoCount > 0, "Ctrl+Y");
                Sep();
                T(CadTool.Move, "M");
                T(CadTool.Copy, "CO");
                T(CadTool.Rotate, "RO");
                T(CadTool.Scale, "SC");
                T(CadTool.Mirror, "MI");
                T(CadTool.Offset, "O");
                Sep();
                B("Grupla", "", _ed.GroupSelection, has, "G");
                B("Çöz", "", _ed.ExplodeSelection, has, "X");
                B("Sil", "", _ed.DeleteSelection, has, "Del");
                B("Tümünü seç", "", _ed.SelectAll, has, "Ctrl+A");
                break;
            case 3:
                B("Semboller", "", () => ShowPanel(2), has);
                B("Keşif (adetler)", "", ShowCounts, has);
                Sep();
                foreach (var (kind, title, hint, glyph) in TableForms.Kinds)
                    B(title switch { "Aydınlatma hesap tablosu" => "Aydınlatma tablosu", "Toplam güç tablosu" => "Toplam güç", _ => "Antet" }, glyph, () => AddTable(kind), has, hint);
                break;
            case 4:
                B("Sığdır", "", () => Canvas.Fit(), has, "Z");
                B("Yakınlaş", "", () => Canvas.ZoomBy(1.5), has);
                B("Uzaklaş", "", () => Canvas.ZoomBy(1 / 1.5), has);
                Sep();
                B("Sağ panel", "", () => RightPanel.Visibility = RightPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
                break;
        }
    }

    private void BuildToolStrip()
    {
        CadTool[][] groups =
        [
            [CadTool.Select, CadTool.Move, CadTool.Copy, CadTool.Rotate, CadTool.Scale, CadTool.Mirror, CadTool.Offset],
            [CadTool.Line, CadTool.PLine, CadTool.Circle, CadTool.Rect, CadTool.Text, CadTool.Symbol, CadTool.Dimension, CadTool.Measure],
        ];
        foreach (var g in groups)
        {
            foreach (var t in g)
            {
                var b = new ToggleButton { Style = (Style)Resources["ToolButton"], Content = new FontIcon { Glyph = CadEditor.ToolInfo[t].Glyph, FontSize = 17 } };
                ToolTipService.SetToolTip(b, CadEditor.ToolInfo[t].Label);
                b.Click += (_, _) =>
                {
                    if (t == CadTool.Symbol && _ed.Symbol == null) ShowPanel(2);
                    _ed.SelectTool(t);
                    SyncUi();
                };
                _toolButtons[t] = b;
                ToolStrip.Children.Add(b);
            }
            ToolStrip.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 6, 8, 6), Background = new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x2F, 0x33)) });
        }
        Button Act(string glyph, string tip, Action a)
        {
            var b = new Button { Width = 40, Height = 38, Padding = new Thickness(0), Margin = new Thickness(4, 2, 4, 2), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Content = new FontIcon { Glyph = glyph, FontSize = 17 } };
            ToolTipService.SetToolTip(b, tip);
            b.Click += (_, _) => a();
            ToolStrip.Children.Add(b);
            return b;
        }
        _undoBtn = Act("", "Geri al (Ctrl+Z)", _ed.Undo);
        _redoBtn = Act("", "Yinele (Ctrl+Y)", _ed.Redo);
        _delBtn = Act("", "Sil (Del)", _ed.DeleteSelection);
    }

    private Button? _undoBtn, _redoBtn, _delBtn;

    /// <summary>Düzenleyici durumunu arayüze yansıt</summary>
    private void SyncUi()
    {
        bool has = _doc != null;
        foreach (var (t, b) in _toolButtons)
        {
            b.IsChecked = has && _ed.Tool == t;
            b.IsEnabled = has && !_ed.Busy;
        }
        if (_undoBtn != null) _undoBtn.IsEnabled = has && _ed.UndoCount > 0;
        if (_redoBtn != null) _redoBtn.IsEnabled = has && _ed.RedoCount > 0;
        if (_delBtn != null) _delBtn.IsEnabled = has && _ed.Selection.Count > 0;
        HintText.Text = has ? $"{CadEditor.ToolInfo[_ed.Tool].Label}: {_ed.Hint}" : "";
        SyncLive();
        // özellikler
        _syncing = true;
        SelText.Text = _ed.SelectionInfo ?? "Seçim yok — nesneye tıklayın ya da pencereyle seçin.";
        SelProps.Visibility = _ed.Selection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var txt = _ed.SelectedText();
        TextEdit.Visibility = txt != null ? Visibility.Visible : Visibility.Collapsed;
        if (txt is { } tt && SelTextBox.Tag as int? != tt.Id) { SelTextBox.Text = tt.Text; SelTextBox.Tag = tt.Id; }
        if (_doc != null && _ed.Selection.Count > 0)
        {
            var layers = _ed.Selection.Take(200).Select(id => _doc.Info(id).Split('|') is var p && p.Length > 1 ? p[1] : "").Distinct().ToList();
            SelLayer.SelectedItem = layers.Count == 1 ? layers[0] : null;
        }
        TextHeightBox.Value = _ed.TextHeight;
        OffsetBox.Value = _ed.OffsetDist;
        SymScaleBox.Value = _ed.SymbolScale;
        SymRotBox.Value = _ed.SymbolRot;
        _syncing = false;
        TitleText.Text = has ? (_doc!.Name + ".dxf" + (_ed.Dirty ? " •" : "")) : "DXF Çizim";
        // şeritteki geri al/yinele etkinliği
        if (Tabs.SelectedItem != null && Tabs.Items.IndexOf(Tabs.SelectedItem) == 2 && Commands.PrimaryCommands.Count > 1)
        {
            ((AppBarButton)Commands.PrimaryCommands[0]).IsEnabled = has && _ed.UndoCount > 0;
            ((AppBarButton)Commands.PrimaryCommands[1]).IsEnabled = has && _ed.RedoCount > 0;
        }
    }

    private void SyncLive()
    {
        LiveText.Text = _ed.Live ?? "";
        LiveBadge.Visibility = string.IsNullOrEmpty(_ed.Live) ? Visibility.Collapsed : Visibility.Visible;
        if (_doc != null) HintText.Text = $"{CadEditor.ToolInfo[_ed.Tool].Label}: {_ed.Hint}";
    }

    private void ShowMessage(string m)
    {
        Toast.Message = m;
        Toast.Severity = InfoBarSeverity.Informational;
        Toast.IsOpen = true;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Toast.IsOpen = false;
        timer.Start();
    }

    // ───────────────────────── klavye ─────────────────────────

    private void OnPageKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or NumberBox) return;
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        e.Handled = HandleKey(e.Key, ctrl);
    }

    private bool HandleKey(VirtualKey k, bool ctrl)
    {
        if (ctrl)
        {
            switch (k)
            {
                case VirtualKey.Z: _ed.Undo(); return true;
                case VirtualKey.Y: _ed.Redo(); return true;
                case VirtualKey.S: OnSave(this, new RoutedEventArgs()); return true;
                case VirtualKey.O: OnOpen(this, new RoutedEventArgs()); return true;
                case VirtualKey.P: OnExport(); return true;
                case VirtualKey.A: _ed.SelectAll(); return true;
            }
            return false;
        }
        switch (k)
        {
            case VirtualKey.Escape: _ed.Escape(); return true;
            case VirtualKey.Delete: _ed.DeleteSelection(); return true;
            case VirtualKey.Enter: _ed.Enter(); return true;
            case VirtualKey.F3: SnapToggle.IsChecked = !(SnapToggle.IsChecked ?? false); OnModeToggle(this, new RoutedEventArgs()); return true;
            case VirtualKey.F8: OrthoToggle.IsChecked = !(OrthoToggle.IsChecked ?? false); OnModeToggle(this, new RoutedEventArgs()); return true;
            case VirtualKey.F10: PolarToggle.IsChecked = !(PolarToggle.IsChecked ?? false); OnModeToggle(this, new RoutedEventArgs()); return true;
            case VirtualKey.F11: TrackToggle.IsChecked = !(TrackToggle.IsChecked ?? false); OnModeToggle(this, new RoutedEventArgs()); return true;
        }
        return false;
    }

    /// <summary>Tuvaldeyken yazılan karakterler komut satırına gider (AutoCAD gibi)</summary>
    private void OnPageChar(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or NumberBox || _doc == null) return;
        char c = e.Character;
        if (char.IsControl(c)) return;
        if (c == ' ' && CommandBox.Text.Length == 0) return;
        CommandBox.Text += c;
        CommandBox.SelectionStart = CommandBox.Text.Length;
        CommandBox.Focus(FocusState.Programmatic);
        e.Handled = true;
    }

    private void OnCommandKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter || (e.Key == VirtualKey.Space && !CommandBox.Text.Contains(' ') && CommandBox.Text.Length > 0 && char.IsLetter(CommandBox.Text[0])))
        {
            var cmd = CommandBox.Text;
            CommandBox.Text = "";
            if (!_ed.Command(cmd)) ShowMessage($"Anlaşılmadı: \"{cmd}\"");
            Canvas.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CommandBox.Text = "";
            _ed.Escape();
            Canvas.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private void OnModeToggle(object sender, RoutedEventArgs e)
    {
        _ed.SnapOn = SnapToggle.IsChecked == true;
        _ed.Ortho = OrthoToggle.IsChecked == true;
        _ed.AngleSnap = PolarToggle.IsChecked == true;
        _ed.Tracking = TrackToggle.IsChecked == true;
    }

    // ───────────────────────── dosya ve projeler ─────────────────────────

    private ProjectMeta? _project;
    /// <summary>Son otomatik kayıttan beri yapılan düzenleme / görünüm-katman değişikliği</summary>
    private int _edits;
    private bool _metaChanged;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _autosaveTimer;
    private Task _saving = Task.CompletedTask;

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        picker.FileTypeFilter.Add(".dxf");
        var f = await picker.PickSingleFileAsync();
        if (f != null) await OpenPath(f.Path);
    }

    private async void OnNew(object sender, RoutedEventArgs e) => await Load(() =>
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "content", "blank.dxf"));
        return (ProjectStore.Create($"Yeni proje {ProjectStore.NextBlankNumber()}", bytes, null), bytes);
    });

    /// <summary>Dosyadan aç: proje olarak içeri al. Aynı dosyanın kaydedilmemiş projesi varsa onu aç.</summary>
    private async Task OpenPath(string path) => await Load(() =>
    {
        if (!path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
            throw new CadException($"\"{Path.GetFileName(path)}\" bir DXF dosyası değil. AutoCAD'de \"Farklı Kaydet → DXF\" ile kaydedin.");
        var existing = ProjectStore.FindBySource(path);
        if (existing is { DirtyToSource: true })
        {
            _pendingMessage = "Kaydedilmemiş değişikliklerinle Projelerim'den açıldı";
            return (existing, ProjectStore.Read(existing.Id));
        }
        var bytes = File.ReadAllBytes(path);
        if (existing != null)
        {
            ProjectStore.WriteDrawing(existing.Id, bytes);
            var m = existing with { Modified = ProjectStore.Now };
            ProjectStore.SaveMeta(m);
            return (m, bytes);
        }
        return (ProjectStore.Create(Path.GetFileNameWithoutExtension(path), bytes, path), bytes);
    });

    public async Task OpenProject(string id) => await Load(() => (ProjectStore.Meta(id), ProjectStore.Read(id)));

    private string? _pendingMessage;

    /// <summary>Ortak açma: önce açık çizimi kaydet, sonra yükle (Android CadEditor.load ile aynı)</summary>
    private async Task Load(Func<(ProjectMeta, byte[])> source)
    {
        if (Busy.IsActive) return;
        Busy.IsActive = true;
        EmptyState.Visibility = Visibility.Collapsed;
        Autosave(final: true);
        _doc = null;
        try
        {
            await _saving; // önceki projenin son kaydı bitsin
            var (m, doc) = await Task.Run(() =>
            {
                var (m, bytes) = source();
                var d = CadDocument.Open(m.Name, bytes);
                if (m.DirtyToSource) d.SetDirty(true);
                return (m, d);
            });
            _doc = doc;
            _project = m;
            _edits = 0;
            _metaChanged = false;
            ProjectStore.LastProject = m.Id;
            var s = doc.Scene;
            Canvas.Hidden.Clear();
            if (m.View != null && m.Hidden != null) foreach (var i in m.Hidden.Where(i => i < s.Layers.Count)) Canvas.Hidden.Add(i);
            else for (int i = 0; i < s.Layers.Count; i++) if (s.Layers[i].OffInFile) Canvas.Hidden.Add(i);
            // kayıtlı görünüm mutlak koordinatta; bu açılışın kökenine göre çevir
            ViewState? initial = m.View is { Length: 3 } v ? new ViewState(v[0] - doc.OriginX, v[1] - doc.OriginY, v[2]) : null;
            Canvas.SetScene(s, initial);
            _ed.Attach(doc);
            FillLayers(s);
            InfoText.Text = $"{s.EntityCount} nesne · {s.Layers.Count} katman · {s.Version} · {s.ParseMs} ms";
            BuildCommands(Tabs.Items.IndexOf(Tabs.SelectedItem));
            SyncUi();
            Canvas.Focus(FocusState.Programmatic);
            if (_pendingMessage != null) { ShowMessage(_pendingMessage); _pendingMessage = null; }
            if (_autosaveTimer == null)
            {
                _autosaveTimer = DispatcherQueue.CreateTimer();
                _autosaveTimer.Interval = TimeSpan.FromSeconds(45);
                _autosaveTimer.Tick += (_, _) => Autosave();
            }
            _autosaveTimer.Start();
            if (Environment.GetEnvironmentVariable("MUHENKIT_SELFTEST") == "1") _ = SelfTest();
            if (Environment.GetEnvironmentVariable("MUHENKIT_SELFTEST") == "2") _ = SelfTestTracking();
            if (Environment.GetEnvironmentVariable("MUHENKIT_SELFTEST") == "3") _ = SelfTestTables();
            if (Environment.GetEnvironmentVariable("MUHENKIT_SELFTEST") == "demo") _ = MakeDemo();
            if (Environment.GetEnvironmentVariable("MUHENKIT_SELFTEST") == "e") _ = SelfTestExport();
            if (Environment.GetEnvironmentVariable("MUHENKIT_SELFTEST") == "ed") OnExport();
            if (Environment.GetEnvironmentVariable("MUHENKIT_SELFTEST") is ['f', var fk]) AddTable((TableForms.Kind)(fk - '1'));
        }
        catch (Exception ex)
        {
            _pendingMessage = null;
            EmptyState.Visibility = Visibility.Visible;
            EmptyText.Text = ex is CadException ? ex.Message : "Dosya açılamadı: " + ex.Message;
            SyncUi();
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    /// <summary>
    /// Otomatik kayıt: çalışma kopyası + görünüm + katmanlar + önizleme (arka planda).
    /// final: ardından belge kapatılır.
    /// </summary>
    public void Autosave(bool final = false)
    {
        var d = _doc;
        if (d == null) return;
        var m = _project;
        int edits = _edits;
        bool changed = _metaChanged;
        _edits = 0;
        _metaChanged = false;
        if (final) _autosaveTimer?.Stop();
        if (m == null || (edits == 0 && !changed && !final))
        {
            if (final) _saving = _saving.ContinueWith(_ => d.Dispose());
            return;
        }
        var s = d.Scene;
        var hidden = Canvas.Hidden.ToArray();
        var v = Canvas.View;
        double[] view = [v.Cx + d.OriginX, v.Cy + d.OriginY, v.Zoom];
        var prev = _saving;
        _saving = Task.Run(async () =>
        {
            await prev;
            try
            {
                if (edits > 0) ProjectStore.WriteDrawing(m.Id, d.SaveCopy());
                var nm = m with { Modified = edits > 0 ? ProjectStore.Now : m.Modified, DirtyToSource = d.Dirty, View = view, Hidden = hidden };
                ProjectStore.SaveMeta(nm);
                if (edits > 0 || !File.Exists(ProjectStore.Thumb(m.Id))) ProjectStore.WriteThumb(m.Id, ScenePainter.RenderPng(s, hidden, 480, 300));
            }
            catch { }
            finally
            {
                if (final) d.Dispose();
            }
        });
        if (!final) _project = m with { DirtyToSource = d.Dirty };
    }

    /// <summary>Pencere kapanırken: son kaydı bekle</summary>
    public void SaveAndWait()
    {
        Autosave(final: true);
        _doc = null;
        try { _saving.Wait(TimeSpan.FromSeconds(10)); } catch { }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Autosave(final: true);
        _doc = null;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;
        var src = _project?.Source;
        if (src == null || !Directory.Exists(Path.GetDirectoryName(src))) { OnSaveAs(sender, e); return; }
        await SaveTo(src);
    }

    private async void OnSaveAs(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;
        var picker = new FileSavePicker { SuggestedFileName = _project?.Name ?? _doc.Name };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        picker.FileTypeChoices.Add("DXF", [".dxf"]);
        var f = await picker.PickSaveFileAsync();
        if (f != null) await SaveTo(f.Path, f);
    }

    private async Task SaveTo(string path, Windows.Storage.StorageFile? file = null)
    {
        var d = _doc!;
        try
        {
            var bytes = await Task.Run(d.Save);
            await WriteBytes(path, bytes, file);
            if (_project is { } m)
            {
                _project = m with { Source = path, DirtyToSource = false };
                ProjectStore.SaveMeta(_project);
            }
            _edits++; // çalışma kopyası da güncellensin
            Autosave();
            ShowMessage("Kaydedildi: " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            ShowMessage("Kaydedilemedi: " + ex.Message);
        }
        SyncUi();
    }

    private void OnHome(object sender, RoutedEventArgs e) => (Application.Current as App)?.GoHome();

    // ───────────────────────── sağ panel ─────────────────────────

    private void OnPanelTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int i = sender.Items.IndexOf(sender.SelectedItem);
        PropsPane.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        LayersPane.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        SymbolsPane.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (i == 2 && SymbolList.Children.Count == 0) BuildSymbols("");
    }

    private void ShowPanel(int i)
    {
        RightPanel.Visibility = Visibility.Visible;
        PanelTabs.SelectedItem = PanelTabs.Items[i];
    }

    private void BuildColors()
    {
        foreach (var (aci, rgb, name) in Colors)
        {
            var b = new Button { Width = 26, Height = 26, Padding = new Thickness(0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x3A, 0x44, 0x50)) };
            if (aci is 256 or 0)
                b.Content = new TextBlock { Text = aci == 256 ? "K" : "B", FontSize = 11 };
            else
                b.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            ToolTipService.SetToolTip(b, name);
            b.Click += (_, _) => _ed.ApplyColor(aci);
            ColorGrid.Children.Add(b);
        }
    }

    private void OnSelLayer(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SelLayer.SelectedItem is not string l) return;
        _ed.ApplyLayer(l);
    }

    private void OnActiveLayer(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveLayer.SelectedItem is string l) _ed.ActiveLayer = l;
    }

    private void OnTextHeight(NumberBox s, NumberBoxValueChangedEventArgs e) { if (!_syncing && e.NewValue > 0) _ed.TextHeight = e.NewValue; }
    private void OnOffsetDist(NumberBox s, NumberBoxValueChangedEventArgs e) { if (!_syncing && e.NewValue > 0) _ed.OffsetDist = e.NewValue; }
    private void OnSymScale(NumberBox s, NumberBoxValueChangedEventArgs e) { if (!_syncing && e.NewValue > 0) _ed.SymbolScale = e.NewValue; }
    private void OnSymRot(NumberBox s, NumberBoxValueChangedEventArgs e) { if (!_syncing && !double.IsNaN(e.NewValue)) _ed.SymbolRot = e.NewValue; }
    private void OnMirrorKeep(object sender, RoutedEventArgs e) { if (_ed != null) _ed.MirrorKeep = MirrorKeepSwitch.IsOn; }

    private void OnApplyText(object sender, RoutedEventArgs e)
    {
        if (_ed.SelectedText() is { } t) _ed.SetText(t.Id, SelTextBox.Text);
    }

    private void OnGroup(object sender, RoutedEventArgs e) => _ed.GroupSelection();
    private void OnExplode(object sender, RoutedEventArgs e) => _ed.ExplodeSelection();
    private void OnDelete(object sender, RoutedEventArgs e) => _ed.DeleteSelection();

    // ───────────────────────── yazı ─────────────────────────

    private async void OnTextRequested(double x, double y)
    {
        var box = new TextBox { AcceptsReturn = false, PlaceholderText = "Yazı", MinWidth = 320 };
        var h = new NumberBox { Header = "Yükseklik", Value = _ed.TextHeight, Minimum = 0.01, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(box);
        panel.Children.Add(h);
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Yazı ekle", Content = panel, PrimaryButtonText = "Ekle", CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Primary, RequestedTheme = ElementTheme.Dark,
        };
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary) _ed.AddText(x, y, box.Text, h.Value);
        Canvas.Focus(FocusState.Programmatic);
    }

    // ───────────────────────── dışa aktarma ─────────────────────────

    private static ExportOptions _exportOpt = new(ExportFormat.Pdf, Paper.All[0], PageOrientation.Auto, false, true);
    private static bool _openAfterExport = true;

    private async void OnExport()
    {
        if (_doc == null) return;
        var o = _exportOpt;
        RadioButtons Radio(string header, string[] items, int sel)
        {
            var r = new RadioButtons { Header = header, MaxColumns = items.Length };
            foreach (var it in items) r.Items.Add(it);
            r.SelectedIndex = sel;
            return r;
        }
        var fmt = Radio("Biçim", ["PDF (vektör)", "Resim (PNG)"], (int)o.Format);
        var paper = new ComboBox { Header = "Kâğıt", ItemsSource = Paper.All.Select(p => p.Name).ToList(), SelectedIndex = Array.IndexOf(Paper.All, o.Paper), MinWidth = 140 };
        var orient = Radio("Yön", ["Otomatik", "Yatay", "Dikey"], (int)o.Orientation);
        var area = Radio("Alan", ["Tüm çizim", "Ekranda görünen"], o.ViewOnly ? 1 : 0);
        var mono = new ToggleSwitch { Header = "Renk", OnContent = "Siyah-beyaz", OffContent = "Renkli", IsOn = o.Monochrome };
        var open = new CheckBox { Content = "Kaydettikten sonra aç (yazdırmak için)", IsChecked = _openAfterExport };
        var panel = new StackPanel { Spacing = 14, MinWidth = 420 };
        foreach (var e in new UIElement[] { fmt, paper, orient, area, mono, open }) panel.Children.Add(e);
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Dışa aktar", Content = panel, PrimaryButtonText = "Kaydet…", CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Primary, RequestedTheme = ElementTheme.Dark,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) { Canvas.Focus(FocusState.Programmatic); return; }
        o = _exportOpt = new ExportOptions((ExportFormat)Math.Max(0, fmt.SelectedIndex), Paper.All[Math.Max(0, paper.SelectedIndex)],
            (PageOrientation)Math.Max(0, orient.SelectedIndex), area.SelectedIndex == 1, mono.IsOn);
        _openAfterExport = open.IsChecked == true;
        bool pdf = o.Format == ExportFormat.Pdf;
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(_project?.Name ?? _doc.Name) };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        picker.FileTypeChoices.Add(pdf ? "PDF" : "PNG resmi", [pdf ? ".pdf" : ".png"]);
        var f = await picker.PickSaveFileAsync();
        if (f == null) { Canvas.Focus(FocusState.Programmatic); return; }
        try
        {
            await ExportTo(f.Path, o, f);
            ShowMessage($"Kaydedildi: {f.Name}");
            if (_openAfterExport) Windows.System.Launcher.LaunchFileAsync(f).AsTask().ContinueWith(_ => { });
        }
        catch (Exception ex)
        {
            App.Log("dışa aktarma: " + ex);
            ShowMessage("Dışa aktarılamadı: " + ex.Message);
        }
        Canvas.Focus(FocusState.Programmatic);
    }

    private async Task ExportTo(string path, ExportOptions o, Windows.Storage.StorageFile? file = null)
    {
        var s = _doc!.Scene;
        var hidden = Canvas.Hidden.ToHashSet();
        var win = o.ViewOnly ? Canvas.ViewWindow() : Exporter.Extents(s);
        var title = Path.GetFileNameWithoutExtension(_project?.Name ?? _doc.Name);
        var bytes = await Task.Run(() => Exporter.Render(s, hidden, win, o, title));
        await WriteBytes(path, bytes, file);
    }

    /// <summary>
    /// Dosyaya yaz. Seçicinin verdiği dosya önce StorageFile üzerinden yazılır: bazı klasörlerde (İndirilenler, OneDrive,
    /// virüs koruması) doğrudan yazma "Access denied" veriyor ve seçicinin açtığı 0 baytlık dosya "bozuk" olarak kalıyordu.
    /// Hepsi başarısız olursa boş dosya silinir.
    /// </summary>
    private static async Task WriteBytes(string path, byte[] bytes, Windows.Storage.StorageFile? file)
    {
        Exception? first = null;
        if (file != null)
        {
            try { await Windows.Storage.FileIO.WriteBytesAsync(file, bytes); return; }
            catch (Exception ex) { first = ex; App.Log("StorageFile yazma: " + ex.Message); }
        }
        for (int i = 0; i < 3; i++)
        {
            try { await File.WriteAllBytesAsync(path, bytes); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                first ??= ex;
                await Task.Delay(300);
            }
        }
        try { if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path); } catch { }
        throw new IOException("Dosya yazılamadı (klasöre yazma izni yok ya da dosya başka bir programda açık). Başka bir klasör deneyin.", first);
    }

    /// <summary>MUHENKIT_SELFTEST=e: açık çizimi %TEMP%'e PDF ve PNG olarak çıkarır</summary>
    private async Task SelfTestExport()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tmp = Path.GetTempPath();
            await ExportTo(Path.Combine(tmp, "muhenkit_export.pdf"), new(ExportFormat.Pdf, Paper.All[1], PageOrientation.Auto, false, true));
            log.AppendLine($"pdf {new FileInfo(Path.Combine(tmp, "muhenkit_export.pdf")).Length} bayt, {sw.ElapsedMilliseconds} ms");
            sw.Restart();
            await ExportTo(Path.Combine(tmp, "muhenkit_export.png"), new(ExportFormat.Png, Paper.All[0], PageOrientation.Auto, false, false));
            log.AppendLine($"png {new FileInfo(Path.Combine(tmp, "muhenkit_export.png")).Length} bayt, {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { log.AppendLine("HATA " + ex); }
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "muhenkit_selftest.txt"), log.ToString());
    }

    // ───────────────────────── tablolar ─────────────────────────

    private async void AddTable(TableForms.Kind kind)
    {
        if (_doc == null) return;
        try
        {
            if (await TableForms.Show(kind, XamlRoot, _ed.TextHeight) is { } table)
            {
                _ed.StartTable(table);
                SyncUi();
            }
        }
        catch (Exception ex) { App.Log("tablo: " + ex); }
        Canvas.Focus(FocusState.Programmatic);
    }

    // ───────────────────────── keşif ─────────────────────────

    private async void ShowCounts()
    {
        var list = _ed.Counts();
        var sp = new StackPanel { Spacing = 4, MinWidth = 380 };
        if (list.Count == 0) sp.Children.Add(new TextBlock { Text = "Çizimde blok/sembol yerleştirmesi yok." });
        foreach (var (name, n, lib) in list)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = name, Opacity = lib ? 1 : 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
            var c = new TextBlock { Text = $"{n} ad.", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xB2, 0x66)) };
            Grid.SetColumn(c, 1);
            row.Children.Add(c);
            sp.Children.Add(row);
        }
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Keşif — sembol adetleri", Content = new ScrollViewer { Content = sp, MaxHeight = 480 },
            CloseButtonText = "Kapat", RequestedTheme = ElementTheme.Dark,
        };
        await dlg.ShowAsync();
    }

    // ───────────────────────── semboller ─────────────────────────

    private void OnSymbolSearch(object sender, TextChangedEventArgs e) => BuildSymbols(SymbolSearch.Text.Trim());

    private void BuildSymbols(string q)
    {
        SymbolList.Children.Clear();
        var syms = SymbolLibrary.All.Where(s => q.Length == 0 || (s.Name + " " + s.Category).Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (syms.Count == 0)
        {
            SymbolList.Children.Add(new TextBlock { Text = SymbolLibrary.All.Count == 0 ? "Sembol kütüphanesi bulunamadı." : "Eşleşen sembol yok.", Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) });
            return;
        }
        foreach (var grp in syms.GroupBy(s => s.Category))
        {
            SymbolList.Children.Add(new TextBlock { Text = grp.Key, Style = (Style)Resources["PanelHeader"] });
            var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 92, ItemHeight = 96 };
            foreach (var s in grp)
            {
                var b = new Button { Width = 88, Height = 92, Padding = new Thickness(4), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var g = new StackPanel { Spacing = 2 };
                g.Children.Add(new Image { Source = SymbolPreview(s, 56), Width = 56, Height = 56 });
                g.Children.Add(new TextBlock { Text = s.Name, FontSize = 10.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
                b.Content = g;
                ToolTipService.SetToolTip(b, s.Name);
                b.Click += (_, _) =>
                {
                    _ed.StartSymbol(s);
                    Canvas.Focus(FocusState.Programmatic);
                };
                wrap.Children.Add(b);
            }
            SymbolList.Children.Add(wrap);
        }
    }

    private static readonly Dictionary<string, ImageSource> PreviewCache = new();

    /// <summary>Sembol önizlemesi (Win2D ile bir kez çizilir)</summary>
    private static ImageSource SymbolPreview(CadSymbol s, int px)
    {
        if (PreviewCache.TryGetValue(s.Id, out var cached)) return cached;
        var dev = CanvasDevice.GetSharedDevice();
        var src = new CanvasImageSource(dev, px, px, 96 * 2);
        using (var ds = src.CreateDrawingSession(Color.FromArgb(0, 0, 0, 0)))
        {
            var b = s.Bounds;
            float w = Math.Max(b[2] - b[0], 1e-3f), h = Math.Max(b[3] - b[1], 1e-3f);
            float k = (px - 8) / Math.Max(w, h);
            // Y yukarı → aşağı
            ds.Transform = Matrix3x2.CreateTranslation(-(b[0] + b[2]) / 2, -(b[1] + b[3]) / 2) * Matrix3x2.CreateScale(k, -k) * Matrix3x2.CreateTranslation(px / 2f, px / 2f);
            foreach (var (rgb, alpha, pts) in s.Fills)
            {
                if (pts.Length < 6) continue;
                var v = new Vector2[pts.Length / 2];
                for (int i = 0; i < v.Length; i++) v[i] = new(pts[i * 2], pts[i * 2 + 1]);
                using var geo = CanvasGeometry.CreatePolygon(dev, v);
                ds.FillGeometry(geo, Tint(rgb, alpha));
            }
            var stroke = new CanvasStrokeStyle { TransformBehavior = CanvasStrokeTransformBehavior.Hairline };
            foreach (var (rgb, pts) in s.Lines)
                for (int i = 0; i + 3 < pts.Length; i += 4)
                    ds.DrawLine(pts[i], pts[i + 1], pts[i + 2], pts[i + 3], Tint(rgb, 1), 1, stroke);
        }
        PreviewCache[s.Id] = src;
        return src;
    }

    private static Color Tint(uint rgb, float a)
    {
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        if (r * 299 + g * 587 + b * 114 < 40_000) r = g = b = 255;
        return Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), r, g, b);
    }

    // ───────────────────────── öz-test (geliştirme) ─────────────────────────

    /// <summary>MUHENKIT_SELFTEST=1: düzenleme araçlarını fareyi benzeterek dener, sonucu %TEMP%\muhenkit_selftest.txt'ye yazar</summary>
    private async Task SelfTest()
    {
        var log = new System.Text.StringBuilder();
        var d = _doc!;
        async Task Idle() { while (_ed.Busy) await Task.Delay(20); await Task.Delay(30); }
        var (x0, y0, x1, y1) = Canvas.ViewWindow();
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, u = (x1 - x0) / 40, tol = Canvas.Tolerance;
        void Click(double x, double y) { _ed.Press(x, y, tol, false); _ed.Release(x, y, tol); }
        void Note(string what) => log.AppendLine($"{what}: nesne={d.Scene.EntityCount} seçim={_ed.Selection.Count} geri={_ed.UndoCount} araç={_ed.Tool}");
        try
        {
            int n0 = d.Scene.EntityCount;
            Note("başlangıç");
            _ed.SnapOn = false;
            _ed.SelectTool(CadTool.Line); Click(cx, cy); Click(cx + 4 * u, cy); await Idle(); Click(cx + 4 * u, cy + 3 * u); await Idle(); _ed.Enter();
            Note("2 çizgi");
            _ed.SelectTool(CadTool.Rect); Click(cx - 6 * u, cy - 4 * u); Click(cx - 2 * u, cy - u); await Idle();
            Note("dikdörtgen");
            _ed.SelectTool(CadTool.Circle); Click(cx + 8 * u, cy); Click(cx + 10 * u, cy); await Idle();
            Note("daire");
            _ed.SelectTool(CadTool.PLine); Click(cx, cy - 6 * u); Click(cx + 3 * u, cy - 6 * u); Click(cx + 3 * u, cy - 8 * u); _ed.Command("K"); await Idle();
            Note("kapalı çoklu çizgi");
            _ed.Command("L"); _ed.Command(FormattableString.Invariant($"{cx + d.OriginX:F3};{cy - 10 * u + d.OriginY:F3}")); _ed.Command(FormattableString.Invariant($"{2 * u:F3}<45")); await Idle(); _ed.Enter();
            Note("komutla çizgi (mutlak + uzunluk<açı)");
            // pencere seçimi: soldan sağa (tamamen içeride)
            _ed.SelectTool(CadTool.Select);
            _ed.Press(cx - 7 * u, cy - 5 * u, tol, false); _ed.Move(cx + 11 * u, cy + 4 * u, tol, true); _ed.Release(cx + 11 * u, cy + 4 * u, tol);
            Note("pencere seçimi");
            int sel = _ed.Selection.Count;
            _ed.SelectTool(CadTool.Copy); Click(cx, cy); Click(cx, cy + 12 * u); await Idle(); _ed.Escape();
            Note($"kopya ({sel} nesne)");
            _ed.SelectTool(CadTool.Move); Click(cx, cy); Click(cx + u, cy + u); await Idle();
            Note("taşı");
            _ed.SelectTool(CadTool.Rotate); Click(cx, cy); _ed.Command("30"); await Idle();
            Note("döndür 30°");
            _ed.SelectTool(CadTool.Scale); Click(cx, cy); _ed.Command("1.5"); await Idle();
            Note("ölçekle 1,5");
            _ed.SelectTool(CadTool.Mirror); Click(cx, cy - 20 * u); Click(cx + u, cy - 20 * u); await Idle();
            Note("ayna (kopya)");
            _ed.GroupSelection(); await Idle();
            Note("grupla");
            _ed.ExplodeSelection(); await Idle();
            Note("çöz");
            _ed.Undo(); await Idle(); _ed.Undo(); await Idle();
            Note("2× geri al");
            _ed.Redo(); await Idle();
            Note("yinele");
            var sym = SymbolLibrary.All.FirstOrDefault();
            log.AppendLine($"sembol kütüphanesi: {SymbolLibrary.All.Count}");
            if (sym != null) { _ed.StartSymbol(sym); Click(cx - 10 * u, cy + 8 * u); await Idle(); Note("sembol: " + sym.Name); }
            _ed.Escape();
            _ed.SelectTool(CadTool.Offset); Click(cx - 6 * u, cy - 2.5 * u); Click(cx - 4 * u, cy - 2.5 * u); await Idle();
            Note("ötele");
            _ed.SelectTool(CadTool.Text);
            _ed.AddText(cx, cy - 14 * u, "MühenKit öz-test ÇĞİÖŞÜ", _ed.TextHeight); await Idle();
            Note("yazı");
            log.AppendLine($"eklenen nesne: {d.Scene.EntityCount - n0}");
            // ekran görüntüsü için: bir seçim ve önizleme bırak
            _ed.SelectTool(CadTool.Select);
            _ed.Press(cx - 7 * u, cy - 9 * u, tol, false); _ed.Release(cx - 7 * u, cy - 9 * u, tol);
            _ed.Press(cx + 12 * u, cy + 14 * u, tol, false); _ed.Move(cx - 8 * u, cy - 9 * u, tol, true); _ed.Release(cx - 8 * u, cy - 9 * u, tol);
            Note("kesişen seçim");
            _ed.SelectTool(CadTool.Copy); Click(cx, cy); _ed.Move(cx + 15 * u, cy - 3 * u, tol, false);
            Canvas.SetView(new ViewState(cx + 3 * u, cy - 2 * u, Canvas.View.Zoom * 1.6));
        }
        catch (Exception ex) { log.AppendLine("HATA " + ex); }
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "muhenkit_selftest.txt"), log.ToString());
    }

    /// <summary>
    /// MUHENKIT_SELFTEST=demo: örnek proje (core/content/demo.dxf) üretir — 2+1 daire planı, semboller, ölçü,
    /// aydınlatma ve güç tabloları, antet. Çıktı yolu MUHENKIT_DEMO_OUT.
    /// </summary>
    private async Task MakeDemo()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var d = _doc!;
            var ids = new Dictionary<int, List<int>>(); // renk → nesneler
            void Col(int aci, int id) { if (id >= 0) { if (!ids.TryGetValue(aci, out var l)) ids[aci] = l = new(); l.Add(id); } }
            void Rect(double x0, double y0, double x1, double y1, int aci) => Col(aci, d.AddPolyline("0", [(x0, y0), (x1, y0), (x1, y1), (x0, y1)], true));
            void Line(double x0, double y0, double x1, double y1, int aci) => Col(aci, d.AddLine("0", x0, y0, x1, y1));
            void Txt(double x, double y, double h, string t, int aci) => Col(aci, d.AddText("0", x, y, h, 0, t));
            CadSymbol S(string id) => SymbolLibrary.All.First(s => s.Id == id);
            void Sym(string id, double x, double y, double sc, double rot, int aci) => Col(aci, d.InsertSymbol(S(id), "0", x, y, sc, rot));

            // dış duvar (20 cm) ve bölmeler (10 cm); ölçüler cm
            double W = 900, H = 700, t = 20;
            Rect(0, 0, W, H, 7);
            Rect(t, t, W - t, H - t, 7);
            // salon | yatak odası dikey bölme x=500; mutfak/banyo yatay bölme y=420; banyo | mutfak x=280
            Line(500, t, 500, 330, 7); Line(510, t, 510, 330, 7); Line(500, 330, 510, 330, 7);
            Line(500, 420, 500, H - t, 7); Line(510, 420, 510, H - t, 7);
            Line(t, 420, 400, 420, 7); Line(t, 430, 400, 430, 7); Line(400, 420, 400, 430, 7);
            Line(280, 430, 280, H - t, 7); Line(290, 430, 290, H - t, 7);
            Line(510, 420, W - t, 420, 7); Line(510, 430, W - t, 430, 7);
            // oda adları
            Txt(200, 130, 18, "SALON", 7); Txt(615, 130, 18, "YATAK ODASI", 7); Txt(75, 470, 16, "BANYO", 7);
            Txt(330, 470, 16, "MUTFAK", 7); Txt(620, 480, 16, "ÇOCUK ODASI", 7); Txt(410, 390, 14, "HOL", 7);
            // aydınlatma
            Sym("AYD_ATY4X18W", 260, 260, 1.4, 0, 2); Sym("AYD_ATY4X18W", 700, 260, 1.2, 0, 2);
            Sym("AYD_B40W_S", 150, 600, 1.6, 0, 2); Sym("AYD_B40W_S", 400, 600, 1.6, 0, 2); Sym("AYD_V1_40W", 700, 590, 1.6, 0, 2);
            Sym("AYD_N3X20W", 470, 360, 1.2, 0, 2); Sym("AYD_C40W", 60, 520, 1.3, 0, 2);
            // anahtarlar ve prizler
            Sym("ANH_ADI", 470, 300, 1.4, 0, 3); Sym("ANH_VAVIEN", 540, 300, 1.4, 0, 3); Sym("ANH_ADI", 260, 450, 1.4, 0, 3);
            Sym("ANH_ADI", 320, 450, 1.4, 0, 3); Sym("ANH_ADI", 540, 450, 1.4, 0, 3);
            Sym("PRZ_TOPRAKLI", 45, 120, 1.4, 90, 1); Sym("PRZ_TOPRAKLI", 300, 45, 1.4, 0, 1); Sym("PRZ_TOPRAKLI", 470, 120, 1.4, 270, 1);
            Sym("PRZ_TOPRAKLI", 860, 120, 1.4, 270, 1); Sym("PRZ_TOPRAKLI", 700, 45, 1.4, 0, 1); Sym("PRZ_TOPRAKLI", 860, 560, 1.4, 270, 1);
            Sym("PRZ_ETANJ", 250, 650, 1.4, 180, 1); Sym("PRZ_TOPRAKLI", 450, 655, 1.4, 180, 1);
            Sym("KLIMA_1KW", 140, 50, 1.3, 0, 4); Sym("BMAK", 340, 650, 1.3, 0, 6); Sym("FIRIN", 440, 650, 1.3, 0, 6);
            Sym("CMAK", 120, 650, 1.3, 0, 6);
            Sym("ZA_TMA", 870, 380, 1.6, 90, 5);
            // ölçü (salon genişliği)
            var dim = _ed.BuildDimension((0, 0), (500, 0), (250, -60));
            Col(7, d.InsertDimension("0", 0, 0, dim.Snippet));
            var dim2 = _ed.BuildDimension((0, 0), (0, H), (-60, 350));
            Col(7, d.InsertDimension("0", 0, 0, dim2.Snippet));

            // tablolar (yazı yüksekliği 10)
            double th = 10;
            var rooms = new List<ProjectTables.Room>
            {
                new() { Name = "SALON", A = "4.8", B = "3.9", Lux = "150", Power = "36", Flux = "3600", Type = "LED PANEL" },
                new() { Name = "YATAK", A = "3.7", B = "3.9", Lux = "100", Power = "36", Flux = "3600", Type = "LED PANEL" },
                new() { Name = "ÇOCUK", A = "3.7", B = "2.5", Lux = "100", Power = "18", Flux = "1800", Type = "LED" },
                new() { Name = "MUTFAK", A = "2.1", B = "2.5", Lux = "200", Power = "18", Flux = "1800", Type = "LED" },
                new() { Name = "BANYO", A = "2.5", B = "2.5", Lux = "100", Power = "18", Flux = "1600", Type = "ETANJ" },
            };
            var light = ProjectTables.LightingTable(th, rooms, "Not: Tasarruflu LED armatür kullanılması tercih edilerek armatür seçimi yapılmıştır.");
            Col(7, d.InsertSymbol(light, "0", W + 140, H, 1, 0));
            var power = ProjectTables.PowerTable(th, "TOPLAM GÜÇ TABLOSU", [
                new() { Name = "AYDINLATMA", Installed = "1200", DemandPct = "100" }, new() { Name = "PRİZLER", Installed = "4500", DemandPct = "60" },
                new() { Name = "MUTFAK CİHAZLARI", Installed = "5000", DemandPct = "60" }, new() { Name = "KLİMA", Installed = "1000", DemandPct = "100" }]);
            Col(7, d.InsertSymbol(power, "0", W + 140, H - light.Height - 60, 1, 0));
            var vals = ProjectTables.TitleFields.ToDictionary(f => f.Key, f => f.Default);
            vals["firma"] = "ÖRNEK MÜHENDİSLİK"; vals["firmaAlt"] = "elektrik proje · taahhüt"; vals["sorumlu"] = "AD SOYAD"; vals["smm"] = "0000";
            vals["et"] = "00000"; vals["il"] = "İSTANBUL"; vals["ilce"] = "KADIKÖY"; vals["sinif"] = "3A"; vals["kat"] = "4"; vals["alan"] = "95";
            vals["bagimsiz"] = "8"; vals["kurulu"] = "11700"; vals["talepFaktor"] = "70"; vals["talep"] = "8190"; vals["cizen"] = "MühenKit"; vals["tarih"] = "2026";
            var title = ProjectTables.TitleBlock(th, vals);
            Col(7, d.InsertSymbol(title, "0", W + 140 + light.Width - title.Width, H - light.Height - 60 - power.Height - 60, 1, 0));
            foreach (var (aci, l) in ids) if (aci != 7) d.SetColor(l, aci);
            d.Rebuild();
            var outp = Environment.GetEnvironmentVariable("MUHENKIT_DEMO_OUT") ?? Path.Combine(Path.GetTempPath(), "demo.dxf");
            await File.WriteAllBytesAsync(outp, d.Save());
            log.AppendLine($"demo: {d.Scene.EntityCount} nesne -> {outp}");
            Canvas.UpdateScene(d.Scene);
            Canvas.Fit();
        }
        catch (Exception ex) { log.AppendLine("HATA " + ex); }
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "muhenkit_selftest.txt"), log.ToString());
    }

    /// <summary>MUHENKIT_SELFTEST=3: üç proje tablosunu örnek verilerle üretip yan yana yerleştirir</summary>
    private async Task SelfTestTables()
    {
        var log = new System.Text.StringBuilder();
        async Task Idle() { while (_ed.Busy) await Task.Delay(20); await Task.Delay(30); }
        try
        {
            double th = _ed.TextHeight, tol = Canvas.Tolerance;
            void Click(double x, double y) { _ed.Move(x, y, tol, false); _ed.Press(x, y, tol, false); _ed.Release(x, y, tol); }
            var rooms = new List<ProjectTables.Room>
            {
                new() { Name = "SALON", A = "4.5", B = "5", Lux = "150", Power = "18", Flux = "1500", Type = "LED PANEL" },
                new() { Name = "MUTFAK", A = "3", B = "3.2", Lux = "200", Power = "18", Flux = "1500", Type = "LED PANEL" },
                new() { Name = "BANYO", A = "2", B = "2.4", Lux = "100", Power = "9", Flux = "800", Type = "ETANJ" },
            };
            var t1 = ProjectTables.LightingTable(th, rooms, "Not: Tasarruflu ampül kullanılması tercih edilerek, armatür seçimi yapılmıştır.");
            var t2 = ProjectTables.PowerTable(th, "TOPLAM GÜÇ TABLOSU", [
                new() { Name = "1. KAT DAİRE", Installed = "9500", DemandPct = "60" }, new() { Name = "2. KAT DAİRE", Installed = "9500", DemandPct = "60" },
                new() { Name = "ORTAK ALAN", Installed = "1200", DemandPct = "100" }]);
            var vals = ProjectTables.TitleFields.ToDictionary(f => f.Key, f => f.Default);
            vals["firma"] = "ÖRNEK ELEKTRİK"; vals["firmaAlt"] = "elektrik – otomasyon"; vals["sorumlu"] = "AD SOYAD"; vals["il"] = "ANKARA"; vals["kurulu"] = "20200"; vals["talep"] = "12600";
            var t3 = ProjectTables.TitleBlock(th, vals);
            double x = 0, gap = th * 6;
            foreach (var t in new[] { t1, t2, t3 })
            {
                _ed.StartTable(t); Click(x, 0); await Idle();
                log.AppendLine($"{t.Name}: {t.Width:0.##} x {t.Height:0.##}, {t.Texts.Count} yazı");
                x += t.Width + gap;
            }
            _ed.Escape();
            Canvas.Fit();
            log.AppendLine("nesne: " + _doc!.Scene.EntityCount);
        }
        catch (Exception ex) { log.AppendLine("HATA " + ex); }
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "muhenkit_selftest.txt"), log.ToString());
    }

    /// <summary>MUHENKIT_SELFTEST=2: hizalama izleri ve ölçü aracı</summary>
    private async Task SelfTestTracking()
    {
        var log = new System.Text.StringBuilder();
        var d = _doc!;
        async Task Idle() { while (_ed.Busy) await Task.Delay(20); await Task.Delay(30); }
        try
        {
            Canvas.SetView(new ViewState(60, 60, 4));
            double tol = Canvas.Tolerance;
            void Click(double x, double y) { _ed.Move(x, y, tol, false); _ed.Press(x, y, tol, false); _ed.Release(x, y, tol); }
            void Hover(double x, double y) => _ed.Move(x, y, tol, false);
            log.AppendLine($"tol={tol:0.###}");
            // 1) yatay referans çizgi A(0,0)–B(100,0)
            _ed.SelectTool(CadTool.Line); Click(0, 0); Click(100, 0); await Idle(); _ed.Enter();
            // 2) eğik referans (0,100)–(80,160): uzunluk 100
            Click(0, 100); Click(80, 160); await Idle(); _ed.Enter();
            int n0 = d.Scene.EntityCount;
            // senaryo A: B'nin üstünden geç (izlemeye al), (0,40)'tan başla, (98,41)'e gel → (100,40) beklenir
            _ed.SelectTool(CadTool.Line);
            Hover(100, 0); Hover(50, 20);
            Click(0, 40);
            Hover(98, 41);
            log.AppendLine("A: " + _ed.Live);
            Click(98, 41); await Idle(); _ed.Enter();
            log.AppendLine("A çizgi: " + d.Info(d.Scene.EntityCount + 0 > 0 ? n0 : 0));
            // senaryo B: eğik referansın ucunun üstünden geç, (50,100)'den paralel çiz, (129,161)'e gel → (130,160) eşit uzunluk
            _ed.SelectTool(CadTool.Line);
            Hover(80, 160); Hover(40, 130);
            Click(50, 100);
            Hover(129, 161.5);
            log.AppendLine("B: " + _ed.Live);
            Click(129, 161.5); await Idle(); _ed.Enter();
            // senaryo C: ölçü
            int before = d.Scene.EntityCount;
            _ed.SelectTool(CadTool.Dimension); Click(0, 0); Click(100, 0); Hover(50, -20);
            log.AppendLine("C önizleme: " + _ed.Live);
            Click(50, -20); await Idle();
            log.AppendLine($"C: nesne {before} → {d.Scene.EntityCount}");
            int dimId = d.Scene.EntityCount; // kimlik = indeks; son eklenen
            // son eklenen nesneyi pencereyle seç ve bilgisi
            _ed.SelectTool(CadTool.Select);
            _ed.Press(110, -12, tol, false); _ed.Move(-10, -30, tol, true); _ed.Release(-10, -30, tol);
            log.AppendLine("C seçim: " + _ed.SelectionInfo + " · " + string.Join(",", _ed.Selection.Select(id => d.Info(id))));
            _ed.ExplodeSelection(); await Idle();
            log.AppendLine("C çöz: " + _ed.SelectionInfo + " · " + string.Join(",", _ed.Selection.Select(id => d.Info(id).Split('|')[0] + (d.Text(id) is { Length: > 0 } t ? "=" + t : ""))));
            _ed.Undo(); await Idle();
            // senaryo D: yakalama kapalıyken bile tıklanan noktalar referans olur; sağ tık = bitir/Esc
            _ed.SnapOn = false;
            _ed.SelectTool(CadTool.Line);
            Click(0, 200); Click(50, 200); await Idle();
            _ed.RightClick();
            log.AppendLine($"D sağ tık 1: araç={_ed.Tool}");
            Click(0, 230); Hover(49, 231.5);
            log.AppendLine("D: " + _ed.Live);
            _ed.RightClick(); _ed.RightClick();
            log.AppendLine($"D sağ tık 2-3: araç={_ed.Tool}");
            _ed.SnapOn = true;
            // ekran görüntüsü için: senaryo A'yı yeniden kur (kılavuzlar görünsün)
            _ed.SelectTool(CadTool.Line);
            Hover(100, 0); Hover(80, 160); Hover(50, 20);
            Click(0, 70);
            Hover(99, 69);
        }
        catch (Exception ex) { log.AppendLine("HATA " + ex); }
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "muhenkit_selftest.txt"), log.ToString());
    }

    // ───────────────────────── katmanlar ─────────────────────────

    private void FillLayers(CadScene s)
    {
        LayerList.Items.Clear();
        _syncing = true;
        var names = s.Layers.Select(l => l.Name).ToList();
        ActiveLayer.ItemsSource = names;
        ActiveLayer.SelectedItem = names.Contains(_ed.ActiveLayer) ? _ed.ActiveLayer : names.FirstOrDefault();
        SelLayer.ItemsSource = names.ToList();
        _syncing = false;
        for (int i = 0; i < s.Layers.Count; i++)
        {
            var l = s.Layers[i];
            int idx = i;
            var rgb = l.Rgb;
            byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
            if (r * 299 + g * 587 + b * 114 < 40_000) r = g = b = 255;
            var cb = new CheckBox { IsChecked = !Canvas.Hidden.Contains(i), MinWidth = 0, Padding = new Thickness(4, 0, 0, 0) };
            cb.Checked += (_, _) => { Canvas.Hidden.Remove(idx); _metaChanged = true; Canvas.Invalidate(); };
            cb.Unchecked += (_, _) => { Canvas.Hidden.Add(idx); _metaChanged = true; Canvas.Invalidate(); };
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(cb);
            var sw = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(255, r, g, b)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sw, 1);
            row.Children.Add(sw);
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = l.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(new TextBlock { Text = $"{l.Count} nesne", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 141, 168, 151)) });
            Grid.SetColumn(txt, 2);
            row.Children.Add(txt);
            LayerList.Items.Add(row);
        }
    }

    private void OnLayersAll(object sender, RoutedEventArgs e) => SetAllLayers(true);
    private void OnLayersNone(object sender, RoutedEventArgs e) => SetAllLayers(false);

    private void SetAllLayers(bool on)
    {
        if (_doc == null) return;
        Canvas.Hidden.Clear();
        if (!on) for (int i = 0; i < _doc.Scene.Layers.Count; i++) Canvas.Hidden.Add(i);
        _metaChanged = true;
        FillLayers(_doc.Scene);
        Canvas.Invalidate();
    }
}
