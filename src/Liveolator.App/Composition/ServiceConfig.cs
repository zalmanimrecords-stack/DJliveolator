using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Shell;
using Liveolator.Audio;
using Liveolator.Audio.Capture;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Mixer;
using Liveolator.Core.Persistence;
using Liveolator.Core.Visuals;
using Liveolator.Media;
using Liveolator.Platform;
using Liveolator.Visuals.Gl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.App.Composition;

/// <summary>
/// The application's composition root — the single place where modules are wired together.
/// A "module" is a Core service plus the bindings it needs; registering it here is how it
/// becomes reachable from the UI (view-models take these services via constructor injection).
/// </summary>
public static class ServiceConfig
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // --- Track-analysis / music-library module (doc 16) ---
        // Bindings come from the dedicated projects: Platform (filesystem) + Audio (WAV + FFmpeg).
        services.AddSingleton<IFileEnumerator, FileSystemEnumerator>();          // Liveolator.Platform
        services.AddSingleton<IAudioDecoder>(_ => new CompositeAudioDecoder());  // Liveolator.Audio
        services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();        // Liveolator.Audio (ATL.NET tags)
        services.AddSingleton<TrackAnalyzer>();
        services.AddSingleton<MusicLibrary>();
        // Persists the analyzed catalog + scan folders under %APPDATA%/Liveolator so state survives
        // restarts (doc 13). The seam lives in Core; JsonCatalogStore is the Media binding.
        services.AddSingleton<IMusicCatalogStore>(
            _ => new JsonCatalogStore(onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));

        // --- Shared performance clock (the product differentiator: ONE beat clock drives both the
        // visuals and the Live tap controls). Pure-managed, no native — so the "tap a tempo and the
        // visuals pulse on the beat" experience works with NO audio hardware. The audio-driven
        // AudioBeatClock (registered in WireLiveAudio when BASS is present) is a separate source used
        // for the Libraries live-BPM readout; unifying the two is a later step (doc 03).
        var hostClock = new SystemHostClock();
        var sharedLiveClock = new ManualBeatClock(hostClock.TicksPerSecond);

        // --- Visual engine (doc 08): the GL compositor reads the shared clock and its action handler
        // joins the one dispatcher. Runs first so the handler exists when the dispatcher is composed.
        VisualActionHandler visualHandler = WireVisuals(services, sharedLiveClock);

        // --- Software mixer (doc 11): pure-constructable with NO native dependency — BassMixer is a
        // routing skeleton that logs+drops calls for slots whose deck channel is not registered yet.
        // So the mixer model + DSP math drive from the UI headless; native FX routing lands later.
        var mixer = new BassMixer();
        services.AddSingleton<IMixer>(mixer);
        var mixerHandler = new MixerActionHandler(mixer);

        // --- Realtime audio engine (docs 01/02/03): best-effort. The BASS backend needs the native
        // bass library at runtime; if it is absent the app still runs as a catalog browser and the
        // deck transport is simply unrouted. Registering IBeatClock here gives the Libraries tab its
        // live-BPM readout (a separate source from the shared manual clock — unifying them is doc 03).
        LivePlaybackEngine? audioEngine = TryBuildAudioEngine();
        if (audioEngine is not null)
        {
            services.AddSingleton<IAudioPlaybackEngine>(audioEngine);
            services.AddSingleton<IBeatClock>(audioEngine.BeatClock);
        }

        // --- THE one dispatcher (doc 04): every input source — UI, controller, autopilot — drives the
        // engines through this single instance, so handler state never diverges (doc 12, one source of
        // truth). Beat + mixer + visual handlers are always present (all pure-managed); the deck
        // transport handler joins only when the realtime engine is up.
        var handlers = new List<IPerformanceActionHandler>
        {
            new BeatActionHandler(sharedLiveClock, hostClock),
            mixerHandler,
            visualHandler,
        };
        if (audioEngine is not null)
            handlers.Add(new DeckActionHandler(audioEngine));

        var dispatcher = new PerformanceActionDispatcher(
            handlers, NullLogger<PerformanceActionDispatcher>.Instance);
        services.AddSingleton<IPerformanceActionDispatcher>(dispatcher);

        WireCaptureSources(services);
        WireLiveTab(services, sharedLiveClock, hostClock);

        // --- View-models ---
        // Libraries playback is gated on the realtime engine, not merely the dispatcher: without
        // native BASS there is no deck to play, so pass the dispatcher only when the engine is up
        // (keeps the Play transport hidden in catalog-browser mode).
        services.AddSingleton<LibrariesViewModel>(sp => new LibrariesViewModel(
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetService<IAudioPlaybackEngine>() is not null ? sp.GetService<IPerformanceActionDispatcher>() : null,
            sp.GetService<IBeatClock>(),
            sp.GetRequiredService<IMusicCatalogStore>()));
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    // Builds the realtime BASS playback engine, or null when the native bass library is absent
    // (e.g. CI / a dev box without the per-platform binaries). Never throws for that case.
    private static LivePlaybackEngine? TryBuildAudioEngine()
    {
        try
        {
            return new LivePlaybackEngine(new BassAudioEngine(), new SystemHostClock());
        }
        catch (Exception ex) when (ex is BassPlaybackException or DllNotFoundException)
        {
            System.Diagnostics.Trace.TraceWarning($"Realtime audio disabled: {ex.Message}.");
            return null;
        }
    }

    // --- Visual engine (doc 08, task 5) -----------------------------------------------------------
    // Registers the GL compositor as IVisualPerformanceEngine and returns its VisualActionHandler so
    // WireLiveAudio can add it to the one dispatcher.
    //
    // HEADLESS-SAFE: the GL engine opens a window/GL context only inside Run(); we register it for
    // resolution but NEVER call Run() here, so the app launches headless. Launching the render window
    // is a deferred user action — see the RENDER-WINDOW SEAM note below.
    //
    // The engine reads the SHARED live clock (passed in), so once the render window runs it pulses on
    // the same beat the Live tab taps. The starter bank is empty (no GPU assets at startup); a real
    // scene/bank catalog from persistence (doc 13) replaces it later.
    private static VisualActionHandler WireVisuals(IServiceCollection services, ManualBeatClock sharedLiveClock)
    {
        var brightnessMacro = new VisualMacro(
            GlVisualPerformanceEngine.BrightnessMacro,
            min: 0.0, max: 1.0, @default: 1.0,
            target: new MacroTarget(Layer: 0, Parameter: GlVisualPerformanceEngine.BrightnessMacro));

        var visualEngine = new GlVisualPerformanceEngine(BuildStarterBank(), brightnessMacro, sharedLiveClock);
        var visualHandler = new VisualActionHandler(visualEngine);

        services.AddSingleton<IVisualPerformanceEngine>(visualEngine);
        services.AddSingleton(visualHandler);

        // RENDER-WINDOW SEAM: the GL render loop blocks and needs a display, so it runs on a dedicated
        // background thread, launched on demand from the Live tab's "Show Visuals" command — never
        // during composition (that would crash headless/CI). The engine reads the shared clock, so the
        // window pulses on the same beat the Live tab taps.
        services.AddSingleton<IVisualStage>(
            new VisualStage(() => visualEngine.Run("Liveolator Visuals"), NullLogger<VisualStage>.Instance));

        return visualHandler;
    }

    // The compositor's first slice needs a renderable image layer. Generate a placeholder image and
    // wrap it in a one-scene bank; on any failure fall back to an empty bank (Show Visuals then logs
    // and no-ops rather than crashing startup). A real scene catalog from persistence (doc 13) replaces this.
    private static VisualBank BuildStarterBank()
    {
        try
        {
            string imagePath = StarterImage.EnsureCreated();
            var layer = new VisualLayer(
                name: "Starter",
                source: new VisualSourceRef(VisualSourceKind.Image, imagePath),
                effects: Array.Empty<EffectRef>(),
                blend: BlendMode.Normal,
                opacity: 1.0);
            var scene = new VisualScene(
                name: "Starter",
                layers: new[] { layer },
                macroValues: new Dictionary<string, double>(),
                transition: TransitionStyle.Cut,
                beatBehavior: BeatBehavior.None);
            return new VisualBank("Starter", new[] { scene });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Starter visual image unavailable ({ex.Message}); visuals window disabled.");
            return new VisualBank("Starter", Array.Empty<VisualScene>());
        }
    }

    // --- Live tab: tap-tempo performance surface, demonstrable with NO audio hardware (docs 03/04/12) ---
    // Drives the SHARED ManualBeatClock and routes every control through the SINGLE dispatcher composed
    // in Build(), so the Live UI reaches the beat, mixer and visual handlers (and deck transport when
    // the realtime engine is up) — never a direct engine call (doc 04). The clock is shared on purpose:
    // tap/lock/nudge advance the same clock the visual engine reads.
    private static void WireLiveTab(IServiceCollection services, ManualBeatClock clock, SystemHostClock hostClock)
    {
        services.AddSingleton<LiveViewModel>(sp => new LiveViewModel(
            sp.GetRequiredService<IPerformanceActionDispatcher>(),
            clock, clock, hostClock, new DispatcherLiveBeatTimer(),
            sp.GetService<IVisualStage>()));
    }

    // --- Capture sources: system loopback + sound-card/line input (doc 01 Phase 1b, task 8) ---
    // Registers the BASS capture engine as both the device catalog and the source factory. A single
    // engine instance backs both seams. Native bass is not required to construct the engine
    // (enumeration/creation only touch native on demand and degrade to "no devices" if it is absent),
    // so this never disables app startup.
    //
    // SETTINGS-UI SEAM (for the Settings agent): the device picker should resolve
    // IAudioCaptureDeviceCatalog, list EnumerateCaptureDevices(), let the user choose an
    // AudioCaptureDevice, then call IAudioCaptureSourceFactory.CreateCaptureSource(device) and feed
    // the returned IAudioSource into the live pipeline (via SwitchableAudioSource.SetSource, mirroring
    // how the deck source is swapped). Switching source is itself a PerformanceAction (doc 04) — wire
    // it through the dispatcher, not by calling the engine directly. This task deliberately stops at
    // the seam and does not build the picker UI.
    private static void WireCaptureSources(IServiceCollection services)
    {
        var engine = new BassCaptureEngine();
        services.AddSingleton<IAudioCaptureDeviceCatalog>(engine);
        services.AddSingleton<IAudioCaptureSourceFactory>(engine);
    }
}
