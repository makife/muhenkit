using Microsoft.UI.Xaml;

namespace MuhenKit;

public partial class App : Application
{
    private MainWindow? _window;

    /// <summary>Dosya seçicileri için ana pencere tanıtıcısı</summary>
    public static nint MainHwnd { get; private set; }

    public App()
    {
        InitializeComponent();
        // yakalanmamış hata günlüğü
        UnhandledException += (_, e) => Log(e.Exception.ToString());
    }

    public static void Log(string s)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "muhenkit_log.txt"), DateTime.Now + " " + s + Environment.NewLine); } catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _window.Activate();
        // komut satırından DXF: "MuhenKit.exe dosya.dxf"
        var cmd = Environment.GetCommandLineArgs();
        _window.Start(cmd.Length > 1 ? cmd[1] : null);
    }

    /// <summary>Ana sayfadaki çipten ekrana geç</summary>
    public void Navigate(Core.RouteId id) => _window?.Navigate(id);
    public void Navigate(System.Type page, object? parameter) => _window?.NavigateTo(page, parameter);
    public void GoHome() => _window?.GoHome();
}
