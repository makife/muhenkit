using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MuhenKit.Core;
using MuhenKit.Modules;

namespace MuhenKit.Pages;

/// <summary>Hesap modülü sayfası: başlık + iki sütun (girdiler solda, sonuçlar sağda; dar pencerede alt alta)</summary>
public sealed partial class ModulePage : Page
{
    private ModuleUi? _ui;

    public ModulePage()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Arrange();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var id = (RouteId)e.Parameter;
        var info = Routes.All.First(r => r.Id == id);
        TitleText.Text = info.Title;
        SubtitleText.Text = info.Subtitle;
        TitleIcon.Glyph = info.Glyph;
        _ui = new ModuleUi(XamlRoot);
        try
        {
            ModuleCatalog.Build(id, _ui);
            if (_ui.Subtitle != null) SubtitleText.Text = _ui.Subtitle;
            _ui.Changed();
        }
        catch (Exception ex)
        {
            _ui.Left.Children.Add(new TextBlock { Text = "Modül açılamadı: " + ex.Message, TextWrapping = TextWrapping.Wrap });
        }
        Body.Children.Add(_ui.Left);
        Body.Children.Add(_ui.Right);
        Arrange();
    }

    /// <summary>Geniş: iki sütun; dar ya da tek sütunlu modül: alt alta</summary>
    private void Arrange()
    {
        if (_ui == null) return;
        bool wide = ActualWidth >= 980 && _ui.Right.Children.Count > 0;
        Body.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (Body.RowDefinitions.Count == 0)
        {
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Grid.SetColumn(_ui.Right, wide ? 1 : 0);
        Grid.SetRow(_ui.Right, wide ? 0 : 1);
        _ui.Right.Margin = wide ? new Thickness(0) : new Thickness(0, 12, 0, 0);
        Body.MaxWidth = _ui.Right.Children.Count == 0 ? 1000 : 1400;
        // genişlik sınırına gelince ortala (kaydırıcı içeriği sola/sağa yaslamasın)
        Body.Width = Math.Min(Body.MaxWidth, Math.Max(0, ActualWidth));
    }

    private void OnHome(object sender, RoutedEventArgs e) => (Application.Current as App)?.GoHome();
}
