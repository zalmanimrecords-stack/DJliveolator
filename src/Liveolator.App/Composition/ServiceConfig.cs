using Liveolator.App.Features.Libraries;
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

        // --- Visual engine (doc 08): register the GL compositor + its action handler. ---
        // Runs BEFORE WireLiveAudio so the VisualActionHandler exists when the dispatcher is built.
        VisualActionHandler visualHandler = WireVisuals(services);

        // --- Live Mode: realtime playback + beat clock (docs 01/02/03/04) ---
        // Best-effort: the BASS backend needs the native bass library at runtime. If it is
        // absent (e.g. a dev box without the per-platform binaries), Live Mode stays off and the
        // app still runs as a catalog browser — the UI hides the transport controls.
        WireLiveAudio(services, visualHandler);
        WireCaptureSources(services);

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
    // WireLiveAudio can add it to the one dispatcher. This block is intentionally self-contained.
    //
    // HEADLESS-SAFE: the GL engine opens a window/GL context only inside Run(); we register it for
    // resolution but NEVER call Run() here, so the app launches headless. Launching the render window
    // is a deferred user action — see the RENDER-WINDOW SEAM note below.
    //
    // The engine is seeded with a placeholder bank + the brightness macro (the compositor's first
    // slice); a real scene/bank catalog from persistence (doc 13) replaces StarterBank later. It
    // currently runs off its own ManualBeatClock; binding it to the shared live-audio IBeatClock is
    // part of the render-window seam (the GL render thread reads the clock per frame, doc 08 §beat-sync).
    private static VisualActionHandler WireVisuals(IServiceCollection services)
    {
        var brightnessMacro = new VisualMacro(
            GlVisualPerformanceEngine.BrightnessMacro,
            min: 0.0, max: 1.0, @default: 1.0,
            target: new MacroTarget(Layer: 0, Parameter: GlVisualPerformanceEngine.BrightnessMacro));

        // Empty starter bank: no GPU assets to load at startup. LoadScene/LaunchClip degrade safely
        // until a scene catalog is wired (doc 13). Ticks-per-second matches SystemHostClock's domain.
        var starterBank = new VisualBank("Starter", Array.Empty<VisualScene>());
        var visualClock = new ManualBeatClock(TimeSpan.TicksPerSecond);

        var visualEngine = new GlVisualPerformanceEngine(starterBank, brightnessMacro, visualClock);
        var visualHandler = new VisualActionHandler(visualEngine);

        services.AddSingleton<IVisualPerformanceEngine>(visualEngine);
        services.AddSingleton(visualHandler);

        // RENDER-WINDOW SEAM (deferred): a "show visuals" PerformanceAction (or UI command) should
        // start visualEngine.Run() on a dedicated STA/render thread. Do NOT call it during composition
        // — Run() blocks and needs a display, which would crash headless/CI startup.

        return visualHandler;
    }

    // --- Capture sources: system loopback + sound-card/line input (doc 01 Phase 1b, task 8) ---
    // Registers the BASS capture engine as both the device catalog and the source factory. A single
    // engine instance backs both seams. Native bass is not required to construct the engine
    // (enumeration/creation only touch native on demand and degrade to "no devices" if it is absent),
    // so this never disables app startup.
    //
    // SETTINGS-UI SEAM (for the Live-tab / Settings agent): the device picker should resolve
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
