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
using Liveolator.Core.Visuals;
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

        // --- Shared performance clock (the product differentiator: ONE beat clock drives both the
        // visuals and the Live tap controls). Pure-managed, no native — so the "tap a tempo and the
        // visuals pulse on the beat" experience works with NO audio hardware. The audio-driven
        // AudioBeatClock (registered in WireLiveAudio when BASS is present) is a separate source used
        // for the Libraries live-BPM readout; unifying the two is a later step (doc 03).
        var hostClock = new SystemHostClock();
        var sharedLiveClock = new ManualBeatClock(hostClock.TicksPerSecond);

        // --- Visual engine (doc 08): the GL compositor reads the shared clock and its action handler
        // joins the one dispatcher. Runs BEFORE WireLiveAudio so the handler exists when it is built.
        VisualActionHandler visualHandler = WireVisuals(services, sharedLiveClock);

        // --- Live Mode: realtime playback + beat clock (docs 01/02/03/04) ---
        // Best-effort: the BASS backend needs the native bass library at runtime. If it is
        // absent (e.g. a dev box without the per-platform binaries), Live Mode stays off and the
        // app still runs as a catalog browser — the UI hides the transport controls.
        WireLiveAudio(services, visualHandler);
        WireCaptureSources(services);
        WireLiveTab(services, sharedLiveClock, hostClock);

        // --- View-models ---
        services.AddSingleton<LibrariesViewModel>(sp => new LibrariesViewModel(
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetService<IPerformanceActionDispatcher>(),
            sp.GetService<IBeatClock>()));
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static void WireLiveAudio(IServiceCollection services, VisualActionHandler visualHandler)
    {
        try
        {
            var engine = new LivePlaybackEngine(new BassAudioEngine(), new SystemHostClock());
            // Software mixer (doc 11): the Core handler owns mixer state + DSP math and drives the
            // BASS-side routing seam. Routing into live deck channels lands with the two-deck engine
            // (next increment); the handler is wired now so UI/controllers can drive the mixer.
            var mixer = new BassMixer();
            var dispatcher = new PerformanceActionDispatcher(
                new IPerformanceActionHandler[]
                {
                    new DeckActionHandler(engine),
                    new MixerActionHandler(mixer),
                    visualHandler, // doc 08 — visual engine joins the one dispatcher (task 5)
                },
                NullLogger<PerformanceActionDispatcher>.Instance);

            services.AddSingleton<IAudioPlaybackEngine>(engine);
            services.AddSingleton<IBeatClock>(engine.BeatClock);
            services.AddSingleton<IMixer>(mixer);
            services.AddSingleton<IPerformanceActionDispatcher>(dispatcher);
        }
        catch (Exception ex) when (ex is BassPlaybackException or DllNotFoundException)
        {
            // Native audio unavailable — leave Live Mode services unregistered; GetService returns null.
            System.Diagnostics.Trace.TraceWarning($"Live Mode disabled: realtime audio unavailable ({ex.Message}).");
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
    // Drives the SHARED ManualBeatClock via a BeatActionHandler, so tap/lock/nudge advance the same
    // clock the visual engine reads. Transport (DeckPlayPause / TransportStop) routes to the real deck
    // engine when it is registered, otherwise it is a logged no-op — the UI still emits the intent
    // through the dispatcher (doc 04, never a direct engine call). The Live tab uses its OWN dispatcher
    // so its handler set is independent of the audio dispatcher, but the CLOCK is shared on purpose.
    private static void WireLiveTab(IServiceCollection services, ManualBeatClock clock, SystemHostClock hostClock)
    {
        services.AddSingleton<LiveViewModel>(sp =>
        {
            var handlers = new List<IPerformanceActionHandler>
            {
                new BeatActionHandler(clock, hostClock),
            };

            // Compose transport routing only when the realtime deck engine is present.
            var engine = sp.GetService<IAudioPlaybackEngine>();
            if (engine is not null)
                handlers.Add(new DeckActionHandler(engine));

            var dispatcher = new PerformanceActionDispatcher(
                handlers, NullLogger<PerformanceActionDispatcher>.Instance);

            return new LiveViewModel(
                dispatcher, clock, clock, hostClock, new DispatcherLiveBeatTimer(),
                sp.GetService<IVisualStage>());
        });
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
