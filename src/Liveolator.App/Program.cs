using Avalonia;
using Avalonia.ReactiveUI;
using Liveolator.App.Composition;

namespace Liveolator.App;

internal static class Program
{
    // Avalonia entry point. Keep this minimal — app composition lives in App / ServiceConfig.
    [STAThread]
    public static void Main(string[] args)
    {
        // Only one instance per user: a second launch shares the same %APPDATA% state and used to
        // corrupt the FRKTL shader cache (presets vanished). If we are not primary, exit quietly.
        using var instance = new SingleInstanceGuard();
        if (!instance.IsPrimary)
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Used by the Avalonia designer and Main.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
