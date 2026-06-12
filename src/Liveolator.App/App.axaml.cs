using Avalonia;
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };

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
}
