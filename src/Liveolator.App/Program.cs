using Avalonia;
using Avalonia.ReactiveUI;

namespace Liveolator.App;

internal static class Program
{
    // Avalonia entry point. Keep this minimal — app composition lives in App / ServiceConfig.
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Used by the Avalonia designer and Main.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
