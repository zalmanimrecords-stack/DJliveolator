using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Liveolator.App.Composition;
using Liveolator.App.Features.Legal;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.Shared;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.App.Shell;
using Liveolator.App.Skins;
using Liveolator.App.Theme;
using Liveolator.Core.Actions;
using Liveolator.Core.Mixer;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Skins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Populate the analyzed catalog into the library BEFORE the decks/tabs are created, so a restored
        // deck can resolve its track's BPM + beat grid right away. Otherwise the catalog only loads later,
        // async, when the Libraries tab restores (line below) — and the decks come up with no BPM/grid until
        // then. Idempotent (the Libraries tab re-restores) and tolerant (a load failure just leaves it empty).
        try
        {
            var musicLibrary = services.GetRequiredService<Liveolator.Core.Library.Music.MusicLibrary>();
            IReadOnlyList<Liveolator.Core.Library.Music.MusicTrack> cachedTracks =
                services.GetRequiredService<IMusicCatalogStore>().LoadMusicAsync().GetAwaiter().GetResult();
            if (cachedTracks is { Count: > 0 })
                musicLibrary.Restore(cachedTracks);
            // Only after a load that passed the store's integrity check, so a damaged catalog never
            // rotates the last good copy out.
            StartCatalogBackup(services);
        }
        catch (Exception ex)
        {
            // Logged, not Traced: a damaged catalog (CatalogCorruptException) must leave a trace in the log.
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Liveolator.Catalog")
                .LogError(ex, "Eager catalog preload failed: {Message}", ex.Message);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow { DataContext = mainWindowViewModel };
            // Restore the persisted window size/position + full-screen state so the app reopens where the
            // performer left it (the active tab is restored by the view-model from the same settings).
            ApplyWindowLayout(mainWindow, settings.WindowLayout.Normalized());
            // Close handling. Window.Closing is the one event guaranteed to fire when the user clicks X
            // (unlike desktop.ShutdownRequested, which the OnLastWindowClose path does not reliably
            // raise). We ARM THE FORCED-EXIT WATCHDOG FIRST — before persisting the layout or any
            // teardown — so that even if a step below wedges on a native lock, the process still dies
            // within the grace window. The GL render loop (GLFW) and BASS run on native threads the CLR
            // cannot abandon cleanly, so without deterministic teardown the process hangs after the
            // window closes — catastrophic mid-performance (the user-reported "X freezes the app").
            mainWindow.Closing += (_, _) =>
            {
                ArmForcedExitWatchdog(ShutdownGrace);
                SaveShutdownSettings(
                    services.GetRequiredService<ISettingsStore>(),
                    services.GetRequiredService<IPerformanceActionDispatcher>(),
                    mainWindow, mainWindowViewModel);
                BeginShutdown(services);
            };
            // Defensive secondary trigger: dispose the rest of the container once the app is actually
            // exiting. Idempotent with the Closing path; harmless if it never fires.
            desktop.Exit += (_, _) => BeginShutdown(services);
            desktop.MainWindow = mainWindow;

            // Restore the persisted library state (scan folders + analyzed catalog) so the app opens
            // where the last run left off. The same Libraries singleton backs the open tab; the call
            // is guarded internally and updates the UI on the main scheduler, so it is safe to start
            // here without blocking window creation.
            var librariesViewModel = services.GetRequiredService<LibrariesViewModel>();
            Task librariesRestored = librariesViewModel.InitializeAsync();

            // First-launch Terms of Use gate (doc 12), then the startup scan prompt (owner request,
            // 2026-09-26): both are modal, so they run in sequence over the main window the moment it
            // opens rather than racing each other. Declining the terms exits the app (mainWindow.Close()
            // inside EnforceTermsAcceptanceAsync), which skips the scan prompt.
            mainWindow.Opened += async (_, _) =>
            {
                bool proceed = true;
                if (!settings.Legal.HasAcceptedCurrentTerms)
                    proceed = await EnforceTermsAcceptanceAsync(services.GetRequiredService<ISettingsStore>(), mainWindow);
                if (proceed)
                    await PromptStartupScanAsync(
                        services.GetRequiredService<IMusicCatalogStore>(),
                        services.GetRequiredService<IConfirmationService>(),
                        librariesViewModel, librariesRestored);
            };
            // Restore the visual catalog (scan folders + assets) — LIBRARIES reads it even with the VJ tab gone.
            _ = services.GetRequiredService<VisualLibraryViewModel>().InitializeAsync();
            // Restore device selections and extension settings into the Settings tab. Without this,
            // the pickers always start at "(none)" even when settings.json contains a controller.
            _ = services.GetRequiredService<SettingsViewModel>().InitializeAsync();

            // Start the visual render loop hidden so the in-app Program Out preview is live from launch
            // (the loop is what feeds preview frames). "OPEN VISUAL SCREEN" later reveals the output
            // window. Only here, in the real desktop lifetime — never during composition or headless
            // tests — so the app stays headless-safe; GL failures are logged and swallowed by the stage.
            services.GetService<IVisualStage>()?.Start();

            // Check the website for a newer build and, if there is one, prompt the user (doc 12). Fire-and-
            // forget: the fetch is async and the prompt waits for the network, so this never blocks window
            // creation; the checker swallows every failure so a check can't crash or freeze startup.
            _ = services.GetService<Features.Update.StartupUpdateChecker>()?.CheckAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The grace window the shutdown watchdog allows for a clean teardown before it force-terminates the
    // process. Generous enough for the GL thread + BASS to stop normally, short enough that a wedged X
    // click can never strand a performer for long.
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    // Run-once guards: the close path (Window.Closing) and the defensive desktop.Exit path can both fire,
    // so the watchdog is armed at most once and the teardown runs at most once.
    private static int _watchdogArmed;
    private static int _shutdownBegun;

    // Tears down the native-owning subsystems on close. The GL render loop (GLFW) and BASS run on native
    // threads; if either is mid-call the CLR cannot abandon it cleanly and the process hangs after the
    // window closes. We stop the visuals, free the audio engine, then dispose the rest of the container.
    // Arms the watchdog too (idempotent) so the Exit-only path is still covered. Runs at most once.
    private static void BeginShutdown(IServiceProvider services)
    {
        ArmForcedExitWatchdog(ShutdownGrace);
        if (System.Threading.Interlocked.Exchange(ref _shutdownBegun, 1) != 0)
            return;

        try
        {
            services.GetService<IVisualStage>()?.Stop(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Stopping the visual stage on shutdown failed: {ex.Message}.");
        }

        // BASS engines are registered as pre-built instances, which the DI container does NOT dispose, so
        // free the realtime engine explicitly (Dispose -> Bass.Free) to stop the audio threads cleanly.
        try
        {
            (services.GetService<Liveolator.Core.Audio.IMultiDeckPlaybackEngine>() as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Freeing the audio engine on shutdown failed: {ex.Message}.");
        }

        try
        {
            (services as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Disposing the service provider on shutdown failed: {ex.Message}.");
        }
    }

    // Last-resort guarantee that the process exits. Runs on a background thread so it never blocks the
    // normal shutdown path: if teardown completes and the process exits first, this thread is abandoned
    // harmlessly; if anything wedges past the grace window, it kills the process so the X click always
    // takes effect. Kill() is an OS-level termination that cannot itself hang (unlike Environment.Exit,
    // which still runs finalizers that a stuck native lib could block). Armed at most once.
    private static void ArmForcedExitWatchdog(TimeSpan grace)
    {
        if (System.Threading.Interlocked.Exchange(ref _watchdogArmed, 1) != 0)
            return;

        var watchdog = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(grace);
            System.Diagnostics.Trace.TraceWarning(
                "Shutdown exceeded its grace window; force-terminating the process.");
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch
            {
                Environment.Exit(0);
            }
        })
        {
            IsBackground = true,
            Name = "Liveolator ForcedExit Watchdog",
        };
        watchdog.Start();
    }

    // Shows the Terms of Use acceptance dialog modally over the main window. On accept, records the
    // accepted terms version (re-reading the latest settings first so a concurrent change is preserved);
    // on decline or any other close, exits the app by closing the main window (which runs the normal
    // teardown). Tolerant: a failed persist is logged, never thrown, so it cannot wedge startup (#16/#26).
    // Returns whether the app should proceed (true = accepted), so a caller chaining another startup
    // prompt after this one knows not to run it once the app is on its way out.
    private static async Task<bool> EnforceTermsAcceptanceAsync(ISettingsStore store, MainWindow mainWindow)
    {
        bool accepted;
        try
        {
            accepted = await new TermsOfUseWindow().ShowDialog<bool>(mainWindow);
        }
        catch (Exception ex)
        {
            // If the dialog itself fails we cannot confirm consent — fail closed by exiting.
            System.Diagnostics.Trace.TraceWarning($"Terms-of-use dialog failed to show: {ex.Message}.");
            mainWindow.Close();
            return false;
        }

        if (!accepted)
        {
            mainWindow.Close();
            return false;
        }

        try
        {
            AppSettings current = await store.LoadAsync();
            await store.SaveAsync(current with { Legal = LegalSettings.AcceptedCurrent });
        }
        catch (Exception ex)
        {
            // Acceptance not persisted — the user re-accepts next launch; do not block this session.
            System.Diagnostics.Trace.TraceWarning($"Could not persist terms acceptance: {ex.Message}.");
        }

        return true;
    }

    // Startup scan prompt (owner request, 2026-09-26): ask once per launch whether to scan the
    // configured music folders for new files. InitializeAsync only restores the last saved catalog —
    // it does not pick up files added since the last scan — so without this prompt a DJ who dropped new
    // tracks into a watched folder would not see them until manually pressing "Scan" in Libraries.
    // Skipped when no folder is configured yet (nothing to scan; first-time setup happens via the
    // Libraries tab's own "Add folder" flow). Tolerant throughout: any failure here is logged and
    // never blocks the rest of startup (global #16/#26).
    private static async Task PromptStartupScanAsync(
        IMusicCatalogStore store, IConfirmationService confirmation,
        LibrariesViewModel libraries, Task librariesRestored)
    {
        IReadOnlyList<string> folders;
        try
        {
            // Wait for the same restore InitializeAsync started, so the folder set read here (and the
            // one RunScanAsync reads from the view-model's Folders collection if the user says yes)
            // agree — otherwise "yes" could scan zero folders because the restore hadn't populated them yet.
            await librariesRestored;
            folders = await store.LoadScanFoldersAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not read scan folders for the startup scan prompt: {ex.Message}.");
            return;
        }
        if (folders.Count == 0)
            return;

        bool scan;
        try
        {
            scan = await confirmation.ConfirmAsync(
                "Scan music folders",
                "Scan your music folders for new files now?",
                confirmLabel: "Scan");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Startup scan prompt failed to show: {ex.Message}.");
            return;
        }

        if (!scan)
            return;

        try
        {
            await libraries.ScanCommand.Execute().ToTask();
        }
        catch (Exception ex)
        {
            // RunScanAsync already catches and surfaces its own failures on ScanStatus; this only
            // guards against the command plumbing itself throwing.
            System.Diagnostics.Trace.TraceWarning($"Startup scan failed: {ex.Message}.");
        }
    }

    // One verified catalog copy per launch, off the UI thread (a large catalog takes a moment to copy).
    private static void StartCatalogBackup(IServiceProvider services)
    {
        var backup = services.GetRequiredService<Liveolator.Media.CatalogBackup>();
        ILogger log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Liveolator.Catalog");
        _ = Task.Run(() =>
        {
            try
            {
                string? copy = backup.CreateVerified(DateTime.UtcNow);
                if (copy is not null)
                    log.LogInformation("Catalog backed up to {Backup}.", copy);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Catalog backup failed: {Message}", ex.Message);
            }
        });
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

    // Persists the current window layout and MIX SEC on close. Reloads the latest settings first so a
    // device/theme change saved during the session is preserved (only those two sections are updated). Tolerant:
    // a failed read/write is logged, never thrown, so it cannot block shutdown (global standards #16/#26).
    private static void SaveShutdownSettings(
        ISettingsStore store, IPerformanceActionDispatcher dispatcher, MainWindow window, MainWindowViewModel vm)
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
            double mixSeconds = AutoCrossfadeRamp.KnobToSeconds(
                dispatcher.GetFeedback(PerformanceActionKind.MixerAutoCrossfadeTime).Value);
            store.SaveAsync(current with
            {
                WindowLayout = layout.Normalized(),
                Mixer = new MixerSettings(mixSeconds),
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not save the window layout and MIX SEC: {ex.Message}.");
        }
    }
}
