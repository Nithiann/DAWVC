using Avalonia;

namespace DawVcs.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
