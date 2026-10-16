using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MuhenKit.Core;
using Windows.Storage.Pickers;
using Windows.UI;

namespace MuhenKit.Pages;

/// <summary>Projelerim: otomatik kaydedilen çizimler (önizleme, ad, tarih, kaydedilmemiş işareti)</summary>
public sealed partial class ProjectsPage : Page
{
    private List<ProjectMeta> _all = [];

    public ProjectsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Reload();
    }

    private void Reload()
    {
        _all = ProjectStore.List();
        Fill();
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Fill();

    private void Fill()
    {
        var q = Search.Text.Trim();
        var list = _all.Where(m => q.Length == 0 || m.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        CountText.Text = $"{_all.Count} proje · düzenlemeler otomatik kaydedilir, orijinal dosya yalnızca \"Kaydet\" ile değişir";
        Cards.Children.Clear();
        foreach (var m in list) Cards.Children.Add(Card(m));
        Empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _all.Count == 0
            ? "Henüz proje yok. Bir DXF açın ya da boş proje başlatın — düzenlemeleriniz burada otomatik saklanır."
            : "Aramayla eşleşen proje yok.";
    }

    private static SolidColorBrush B(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));

    private FrameworkElement Card(ProjectMeta m)
    {
        var thumb = new Image { Stretch = Stretch.UniformToFill, Height = 170 };
        var tp = ProjectStore.Thumb(m.Id);
        if (File.Exists(tp))
        {
            // dosya kilitlenmesin ve güncel önizleme gelsin: bellekten yükle
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(File.ReadAllBytes(tp));
            bmp.SetSource(ms.AsRandomAccessStream());
            thumb.Source = bmp;
        }
        var thumbBox = new Grid { Height = 170, Background = B(11, 14, 16) };
        thumbBox.Children.Add(thumb);
        if (thumb.Source == null) thumbBox.Children.Add(new FontIcon { Glyph = "", FontSize = 36, Foreground = B(0x3A, 0x44, 0x50) });
        if (m.DirtyToSource)
            thumbBox.Children.Add(new Border
            {
                Background = B(0xFF, 0xB6, 0x27), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = m.Source == null ? "DOSYAYA KAYDEDİLMEDİ" : "KAYDEDİLMEMİŞ DEĞİŞİKLİK", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = B(0x1A, 0x12, 0x00) },
            });

        var name = new TextBlock { Text = m.Name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var when = DateTimeOffset.FromUnixTimeMilliseconds(m.Modified).ToLocalTime();
        var info = new TextBlock
        {
            Text = $"{Ago(when)} · {Size(m.SizeBytes)}" + (m.Source != null ? " · " + Path.GetFileName(m.Source) : ""),
            FontSize = 12, Foreground = B(0x9F, 0xB6, 0xDC), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(info, m.Source ?? "Kaynak dosyası yok (uygulama içinde oluşturuldu)");

        var more = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Style = (Style)Application.Current.Resources["SubtleButtonStyle"], VerticalAlignment = VerticalAlignment.Center };
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Aç", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => Open(m);
        var rename = new MenuFlyoutItem { Text = "Yeniden adlandır", Icon = new FontIcon { Glyph = "" } };
        rename.Click += async (_, _) => await Rename(m);
        var folder = new MenuFlyoutItem { Text = "Kaynak klasörü göster", Icon = new FontIcon { Glyph = "" }, IsEnabled = m.Source != null && File.Exists(m.Source) };
        folder.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{m.Source}\"");
        var del = new MenuFlyoutItem { Text = "Sil", Icon = new FontIcon { Glyph = "" } };
        del.Click += async (_, _) => await Delete(m);
        menu.Items.Add(open);
        menu.Items.Add(rename);
        menu.Items.Add(folder);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(del);
        more.Flyout = menu;

        var text = new Grid { Padding = new Thickness(12, 10, 6, 10), ColumnSpacing = 4 };
        text.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        text.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { Spacing = 2 };
        sp.Children.Add(name);
        sp.Children.Add(info);
        text.Children.Add(sp);
        Grid.SetColumn(more, 1);
        text.Children.Add(more);

        var body = new StackPanel();
        body.Children.Add(thumbBox);
        body.Children.Add(text);
        var card = new Border
        {
            Width = 284, Margin = new Thickness(0, 0, 16, 16), CornerRadius = new CornerRadius(8), Background = B(0x14, 0x17, 0x19),
            BorderBrush = B(0x2A, 0x2F, 0x33), BorderThickness = new Thickness(1), Child = body,
        };
        card.PointerEntered += (_, _) => card.BorderBrush = B(0xF2, 0xB2, 0x66);
        card.PointerExited += (_, _) => card.BorderBrush = B(0x2A, 0x2F, 0x33);
        thumbBox.Tapped += (_, _) => Open(m);
        name.Tapped += (_, _) => Open(m);
        card.ContextFlyout = menu;
        return card;
    }

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        if (d.TotalMinutes < 1) return "az önce";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} dk önce";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} sa önce";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} gün önce";
        return t.ToString("d MMM yyyy", new System.Globalization.CultureInfo("tr-TR"));
    }

    private static string Size(long b) => b < 1024 * 1024 ? $"{Math.Max(1, b / 1024)} KB" : $"{b / 1024.0 / 1024:0.0} MB";

    private void Open(ProjectMeta m) => (Application.Current as App)?.Navigate(typeof(CadPage), new CadRequest.Project(m.Id));

    private async Task Rename(ProjectMeta m)
    {
        var box = new TextBox { Text = m.Name, MinWidth = 320 };
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Yeniden adlandır", Content = box, PrimaryButtonText = "Kaydet", CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Primary, RequestedTheme = ElementTheme.Dark,
        };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary && box.Text.Trim().Length > 0)
        {
            ProjectStore.Rename(m.Id, box.Text.Trim());
            Reload();
        }
    }

    private async Task Delete(ProjectMeta m)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"\"{m.Name}\" silinsin mi?",
            Content = m.DirtyToSource
                ? "Bu projede dosyaya kaydedilmemiş değişiklikler var; silinince kaybolur."
                : "Proje listeden ve uygulama alanından silinir. Orijinal DXF dosyanıza dokunulmaz.",
            PrimaryButtonText = "Sil", CloseButtonText = "Vazgeç", RequestedTheme = ElementTheme.Dark,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            ProjectStore.Delete(m.Id);
            Reload();
        }
    }

    private async void OnOpenDxf(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        picker.FileTypeFilter.Add(".dxf");
        var f = await picker.PickSingleFileAsync();
        if (f != null) (Application.Current as App)?.Navigate(typeof(CadPage), f.Path);
    }

    /// <summary>Örnek proje (core/content/demo.dxf): 2+1 daire, semboller, tablolar, antet. Varsa mevcut olanı açar.</summary>
    private void OnDemo(object sender, RoutedEventArgs e)
    {
        var m = ProjectStore.List().FirstOrDefault(p => p.Name == "Örnek proje");
        if (m == null)
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "content", "demo.dxf"));
            m = ProjectStore.Create("Örnek proje", bytes, null);
        }
        Open(m);
    }

    private void OnNew(object sender, RoutedEventArgs e) => (Application.Current as App)?.Navigate(typeof(CadPage), new CadRequest.Blank());

    private void OnHome(object sender, RoutedEventArgs e) => (Application.Current as App)?.GoHome();
}
