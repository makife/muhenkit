using Microsoft.UI.Xaml;

namespace MuhenKit;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));
        AppWindow.SetIcon("content/brand/app.ico");
        // kapanırken açık çizimin son kaydı
        AppWindow.Closing += (_, _) => { if (Nav.Content is Pages.CadPage p) p.SaveAndWait(); };
    }

    public void Start(string? dxfPath)
    {
        Nav.Navigate(typeof(Pages.HomePage));
        if (dxfPath == "--projeler") Nav.Navigate(typeof(Pages.ProjectsPage));
        else if (dxfPath?.StartsWith("--modul=") == true && Enum.TryParse<Core.RouteId>(dxfPath[8..], out var rid)) Nav.Navigate(typeof(Pages.ModulePage), rid);
        else if (!string.IsNullOrWhiteSpace(dxfPath)) Nav.Navigate(typeof(Pages.CadPage), dxfPath);
    }

    public void Navigate(Core.RouteId id)
    {
        switch (id)
        {
            case Core.RouteId.Cad: Nav.Navigate(typeof(Pages.CadPage)); break;
            case Core.RouteId.Projects: Nav.Navigate(typeof(Pages.ProjectsPage)); break;
            default: Nav.Navigate(typeof(Pages.ModulePage), id); break;
        }
    }

    public void NavigateTo(System.Type page, object? parameter) => Nav.Navigate(page, parameter);

    /// <summary>Ana sayfaya dön (geçmişte varsa geri, yoksa yeni)</summary>
    public void GoHome()
    {
        while (Nav.BackStackDepth > 1) Nav.BackStack.RemoveAt(Nav.BackStackDepth - 1);
        if (Nav.CanGoBack && Nav.BackStack[0].SourcePageType == typeof(Pages.HomePage)) Nav.GoBack();
        else Nav.Navigate(typeof(Pages.HomePage));
    }
}
