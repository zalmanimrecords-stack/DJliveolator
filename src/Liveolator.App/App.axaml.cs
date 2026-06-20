using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Liveolator.App.Composition;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.App.Shell;
using Liveolator.App.Skins;
using Liveolator.App.Theme;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Skins;
using Microsoft.Extensions.DependencyInjection;

namespace Liveolator.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The composition root: this is where modules (Core services + bindings) are wired.
        IServiceProvider services = ServiceConfig.Build();
        AppSettings settings = services.GetRequiredService<ISettingsStore>()
            .LoadAsync().GetAwaiter().GetResult();
        if (settings.Extensions.ActiveUiThemeId is { } themeId
            && services.GetRequiredService<IUiThemeManager>().TryGet(themeId, out UiThemeDefinition theme))
            UiThemeApplier.Apply(this, theme);

        // Apply the persisted control skins AFTER the theme so any colour the skin omits falls back to the
        // themed token (doc 30). A missing/uninstalled id resolves to null = the built-in look.
        IControlSkinCatalog skins = services.GetRequiredService<IControlSkinCatalog>();
        ControlSkinApplier.Apply(this,
            ResolveSkin(skins, settings.Extensions.ActiveKnobSkinId),
            ResolveSkin(skins, settings.Extensions.ActiveSliderSkinId),
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow { DataContext = mainWindowViewModel };
            // Restore the persisted window size/position + full-screen state so the app reopens where the
            // performer left it (the active tab is restored by the view-model from the same settings).
            ApplyWindowLayout(mainWindow, settings.WindowLayout.Normalized());
            // Persist the layout on close. Reload the latest settings first so a device/theme change made
            // during the session (saved from the Settings tab) is never clobbered by this layout write.
            mainWindow.Closing += (_, _) =>
                SaveWindowLayout(services.GetRequiredService<ISettingsStore>(), mainWindow, mainWindowViewModel);
            desktop.MainWindow = mainWindow;

            // Restore the persisted library state (scan folders + analyzed catalog) so the app opens
            // where the last run left off. The same Libraries singleton backs the open tab; the call
            // is guarded internally and updates the UI on the main scheduler, so it is safe to start
            // here without blocking window creation.
            _ = services.GetRequiredService<LibrariesViewModel>().InitializeAsync();
            // Likewise restore the VJ / Visual Library tab (scan folders + asset catalog), Track C C1.
            _ = services.GetRequiredService<VisualLibraryViewModel>().InitializeAsync();
            // Restore device selections and extension settings into the Settings tab. Without this,
            // the pickers always start at "(none)" even when settings.json contains a controller.
            _ = services.GetRequiredService<SettingsViewModel>().InitializeAsync();

            // Start the visual render loop hidden so the in-app Program Out preview is live from launch
            // (the loop is what feeds preview frames). "OPEN VISUAL SCREEN" later reveals the output
            // window. Only here, in the real desktop lifetime — never during composition or headless
            // tests — so the app stays headless-safe; GL failures are logged and swallowed by the stage.
            services.GetService<IVisualStage>()?.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ControlSkinFile? ResolveSkin(IControlSkinCatalog catalog, string? skinId)
        => skinId is not null && catalog.TryGet(skinId, out ControlSkinFile skin) ? skin : null;

    // Applies a persisted window layout to the main window at startup: size, an optional saved position,
    // and the full-screen / windowed state (which also sets the decorations + toggle-button label).
    private static void ApplyWindowLayout(MainWindow window, WindowLayoutSettings layout)
    {
        window.Width = layout.Width;
        window.Height = layout.Height;
        if (layout.X is { } x && layout.Y is { } y)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint((int)x, (int)y);
        }
        window.SetFullScreen(layout.IsFullScreen);
    }

    // Persists the current window layout on close. Reloads the latest settings first so a device/theme
    // change saved during the session is preserved (only the WindowLayout section is updated). Tolerant:
    // a failed read/write is logged, never thrown, so it cannot block shutdown (global standards #16/#26).
    private static void SaveWindowLayout(ISettingsStore store, MainWindow window, MainWindowViewModel vm)
    {
        try
        {
            AppSettings current = store.LoadAsync().GetAwaiter().GetResult();
            bool fullScreen = window.WindowState == WindowState.FullScreen;
            // While full-screen the window bounds are the screen, not the user's chosen size — keep the
            // last windowed size/position and record only the active tab + the full-screen flag. When
            // windowed, capture the live bounds + position.
            WindowLayoutSettings layout = fullScreen
                ? current.WindowLayout with { ActiveTabId = vm.CurrentTabId, IsFullScreen = true }
                : new WindowLayoutSettings(
                    ActiveTabId: vm.CurrentTabId,
                    Width: window.Bounds.Width,
                    Height: window.Bounds.Height,
                    X: window.Position.X,
                    Y: window.Position.Y,
                    IsFullScreen: false);
            store.SaveAsync(current with { WindowLayout = layout.Normalized() }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not save the window layout: {ex.Message}.");
        }
    }
}
