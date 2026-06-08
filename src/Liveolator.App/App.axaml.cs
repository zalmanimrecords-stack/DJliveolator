using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Liveolator.App.Composition;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.App.Shell;
using Liveolator.App.Theme;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
