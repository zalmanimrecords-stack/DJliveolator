using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Playlists;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.Shared;
using Liveolator.App.Shell;
using Liveolator.Audio;
using Liveolator.Audio.Capture;
using Liveolator.Audio.Playback;
using Liveolator.Audio.Waveform;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Liveolator.Core.Mixer;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using Liveolator.Core.Waveform;
using Liveolator.Media;
using Liveolator.Midi;
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

        // --- Persisted preferences (doc 12) ---
        // Loaded once up-front because the realtime engine needs the chosen output device + buffer
        // BEFORE it opens BASS. Blocking is acceptable in the composition root at startup (one small JSON
        // file) and the load is tolerant: a missing/corrupt file yields AppSettings.Default. The same
        // store instance is registered below so the Settings tab reads/writes the very same file.
        var settingsStore = new JsonSettingsStore(onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        AppSettings appSettings = settingsStore.LoadAsync().GetAwaiter().GetResult();

        // --- Track-analysis / music-library module (doc 16) ---
        // Bindings come from the dedicated projects: Platform (filesystem) + Audio (WAV + FFmpeg).
        services.AddSingleton<IFileEnumerator, FileSystemEnumerator>();          // Liveolator.Platform
        services.AddSingleton<IAudioDecoder>(_ => new CompositeAudioDecoder());  // Liveolator.Audio
        services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();        // Liveolator.Audio (ATL.NET tags)
        // Deck waveform overview (doc 11): decodes the loaded track to peaks for the deck strip. Uses the
        // offline decoder, so it works headless (no realtime BASS needed); failures degrade to no waveform.
        services.AddSingleton<IWaveformProvider>(sp => new DecodedWaveformProvider(
            sp.GetRequiredService<IAudioDecoder>()));
        services.AddSingleton<TrackAnalyzer>();
        services.AddSingleton<MusicLibrary>();
        // Persists the analyzed catalog + scan folders under %APPDATA%/Liveolator so state survives
        // restarts (doc 13). The seam lives in Core; JsonCatalogStore is the Media binding.
        services.AddSingleton<IMusicCatalogStore>(
            _ => new JsonCatalogStore(onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));
        // Named, saved playlists/sets (doc 09/13) — one JSON file per set under live/playlists/.
        services.AddSingleton<IPlaylistStore>(
            _ => new JsonPlaylistStore(onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));

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

        // --- Live playlist / set (doc 09): the performance-editable Now/Next/Later queue the DJ tab
        // shows. Pure-managed. SkipOn(...) defers through IBeatScheduler — wired to an interim
        // immediate scheduler until a clock-driven one lands (doc 03). The handler owns the playlist
        // edits (insert/move/remove/skip) so the UI drives them through the one dispatcher.
        var livePlaylist = new LivePlaylist(new ImmediateBeatScheduler(), NullLogger<LivePlaylist>.Instance);
        services.AddSingleton<ILivePlaylist>(livePlaylist);
        var playlistHandler = new PlaylistActionHandler(livePlaylist, NullLogger<PlaylistActionHandler>.Instance);

        // --- Realtime audio engine (docs 01/02/11): best-effort two-deck DJ engine. The BASSmix backend
        // needs the native bass + bassmix libraries at runtime; if they are absent the app still runs as a
        // catalog browser and the deck transport is simply unrouted. The two decks feed ONE master mix and
        // the beat clock is driven off that master (MasterMixPlaybackEngine), so it follows the audible
        // post-crossfader signal (doc 11) rather than a single switched deck. Registering
        // IMultiDeckPlaybackEngine lets the deck handler address both decks AND lets the two-deck engine
        // register its per-deck channel into the BassMixer as decks load — closing the seam so the mixer's
        // gain/EQ/filter actually route to audio. Registering IBeatClock gives the Libraries tab its
        // live-BPM readout (a separate source from the shared manual clock — unifying them is doc 03).
        TwoDeckBassEngine? deckEngine = TryBuildDeckEngine(mixer, appSettings.Audio);
        MasterMixPlaybackEngine? masterMix =
            deckEngine is null ? null : new MasterMixPlaybackEngine(deckEngine.MasterSource, hostClock);
        bool realtimeUp = deckEngine is not null;
        if (realtimeUp)
        {
            services.AddSingleton<IMultiDeckPlaybackEngine>(deckEngine!);
            services.AddSingleton<IBeatClock>(masterMix!.BeatClock);
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
            playlistHandler,
        };
        if (realtimeUp)
            handlers.Add(new DeckActionHandler(deckEngine!));

        var dispatcher = new PerformanceActionDispatcher(
            handlers, NullLogger<PerformanceActionDispatcher>.Instance);
        services.AddSingleton<IPerformanceActionDispatcher>(dispatcher);

        // --- MIDI controller → dispatcher (doc 05/07) ---
        // Open the SETTINGS-chosen controller and route the live hardware through the same dispatcher
        // (a missing/absent device degrades to running without MIDI — see WireMidiInput).
        var midiProvider = new RtMidiDeviceProvider();
        WireMidiInput(services, midiProvider, dispatcher, appSettings.Midi);

        WireCaptureSources(services);
        WireLiveTab(services, sharedLiveClock, hostClock);

        // --- View-models ---
        // Shared back-end for the per-track right-click menu (Add to Deck A/B, Add to playlist). The
        // dispatcher is passed only when the realtime engine is up (deck items disable otherwise);
        // add-to-playlist works regardless (store-only). One singleton drives every track row's menu.
        services.AddSingleton<TrackContextActions>(sp => new TrackContextActions(
            realtimeUp ? sp.GetService<IPerformanceActionDispatcher>() : null,
            sp.GetRequiredService<IPlaylistStore>(),
            onStatus: w => System.Diagnostics.Trace.TraceInformation(w)));

        // Libraries playback is gated on the realtime engine, not merely the dispatcher: without
        // native BASS there is no deck to play, so pass the dispatcher only when the engine is up
        // (keeps the Play transport hidden in catalog-browser mode).
        // Playlist/set builder (opened from the Libraries "Playlists" button): curate from the catalog,
        // save via IPlaylistStore, and push a set to the live queue (ILivePlaylist).
        services.AddSingleton<PlaylistBuilderViewModel>(sp => new PlaylistBuilderViewModel(
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetRequiredService<IPlaylistStore>(),
            sp.GetRequiredService<ILivePlaylist>(),
            sp.GetRequiredService<TrackContextActions>()));

        services.AddSingleton<LibrariesViewModel>(sp => new LibrariesViewModel(
            sp.GetRequiredService<MusicLibrary>(),
            realtimeUp ? sp.GetService<IPerformanceActionDispatcher>() : null,
            sp.GetService<IBeatClock>(),
            sp.GetRequiredService<IMusicCatalogStore>(),
            sp.GetRequiredService<PlaylistBuilderViewModel>(),
            sp.GetRequiredService<TrackContextActions>()));

        // DJ tab: the two decks + the live set (queue). Drives playback/queue through the one
        // dispatcher; reads ILivePlaylist + the catalog for the set readout (like the beat readout).
        services.AddSingleton<DjViewModel>(sp => new DjViewModel(
            sp.GetRequiredService<IPerformanceActionDispatcher>(),
            sp.GetRequiredService<ILivePlaylist>(),
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetRequiredService<TrackContextActions>(),
            sp.GetService<IWaveformProvider>()));

        // Settings tab (doc 12): detect audio output + MIDI equipment and persist the choice. The
        // device catalogs degrade to empty lists when native bass/rtmidi is absent (so the tab works
        // headless), and the choice is saved to settings.json. The audio output device + buffer are
        // applied at startup (loaded above, threaded into the realtime engine); the chosen MIDI
        // controller is now opened into the dispatcher above (WireMidiInput). The Settings tab reuses
        // the SAME provider instance that WireMidiInput opened the device through.
        services.AddSingleton<IAudioOutputDeviceCatalog>(new BassOutputDeviceCatalog());
        services.AddSingleton<IMidiDeviceProvider>(midiProvider);
        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<IAudioOutputDeviceCatalog>(),
            sp.GetRequiredService<IAudioCaptureDeviceCatalog>(),
            sp.GetRequiredService<IMidiDeviceProvider>(),
            sp.GetRequiredService<ISettingsStore>()));

        services.AddSingleton<MainWindowViewModel>();

        ServiceProvider provider = services.BuildServiceProvider();
        // Populate the "Add to playlist" submenu once at startup (best-effort; guarded internally).
        _ = provider.GetRequiredService<TrackContextActions>().RefreshPlaylistsAsync();
        return provider;
    }

    // Builds the realtime two-deck BASS engine (registering its channels into the mixer), or null when
    // the native bass/bassmix libraries are absent (e.g. CI / a dev box without the per-platform
    // binaries). Never throws for that case — the app falls back to the catalog browser.
    private static TwoDeckBassEngine? TryBuildDeckEngine(BassMixer mixer, AudioSettings audioSettings)
    {
        try
        {
            return new TwoDeckBassEngine(mixer, audioSettings: audioSettings);
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
            sp.GetService<IVisualStage>(),
            sp.GetService<IWaveformProvider>()));
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

    // --- MIDI controller → dispatcher (doc 05/07) -------------------------------------------------
    // Opens the SETTINGS-chosen MIDI controller and routes it through MidiControllerRouter →
    // ControllerMapper → the one dispatcher, with MidiProfileSelector auto-selecting a profile by
    // device name and MidiFeedbackPublisher driving LEDs back out (all composed by MidiInputPipeline,
    // a pure Core seam). The resulting pipeline is registered so the DI container disposes it (which
    // closes the device) at shutdown.
    //
    // DEGRADES GRACEFULLY (global standards #16/#26): no controller chosen, no matching device, or a
    // native rtmidi/open failure all log + leave the app running WITHOUT MIDI — never throw at startup.
    // The dispatcher still serves the UI; the hardware simply does not drive it. AvailableProfiles is the
    // default-profile catalog (CMD STUDIO 2A today); a persisted/custom profile set (doc 13) extends it.
    private static void WireMidiInput(
        IServiceCollection services,
        IMidiDeviceProvider midiProvider,
        IPerformanceActionDispatcher dispatcher,
        MidiSettings midiSettings)
    {
        MidiInputPipeline? pipeline = TryOpenMidiPipeline(midiProvider, dispatcher, midiSettings);
        if (pipeline is not null)
            services.AddSingleton(pipeline);
    }

    // Internal for testing the graceful-degradation paths (no selection / not found / open throws)
    // with a fake provider — the App's mandatory error-handling contract for the device-open path.
    internal static MidiInputPipeline? TryOpenMidiPipeline(
        IMidiDeviceProvider midiProvider,
        IPerformanceActionDispatcher dispatcher,
        MidiSettings midiSettings)
    {
        string? inputName = midiSettings.Normalized().ControllerInputName;
        if (string.IsNullOrWhiteSpace(inputName))
        {
            System.Diagnostics.Trace.TraceInformation(
                "No MIDI controller selected; running without hardware control.");
            return null;
        }

        try
        {
            IMidiInput? input = midiProvider.OpenInput(inputName);
            if (input is null)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"MIDI controller '{inputName}' not found; running without hardware control.");
                return null;
            }

            // Feedback output is optional (doc 06) — control still works without LEDs. A null/blank name
            // or a missing device simply skips feedback.
            string? outputName = midiSettings.Normalized().FeedbackOutputName;
            IMidiOutput? output = string.IsNullOrWhiteSpace(outputName)
                ? null
                : midiProvider.OpenOutput(outputName);

            return MidiInputPipeline.Create(
                input, output, dispatcher, AvailableMidiProfiles(), NullLoggerFactory.Instance);
        }
        catch (Exception ex)
        {
            // A native rtmidi failure or a device that vanished between enumeration and open must not
            // crash startup — the app degrades to no-MIDI (global standards #16/#26).
            System.Diagnostics.Trace.TraceWarning(
                $"Opening MIDI controller '{inputName}' failed: {ex.Message}. Running without hardware control.");
            return null;
        }
    }

    // The default mapping-profile catalog the pipeline auto-selects from by device name. CMD STUDIO 2A
    // today; persisted/custom profiles (doc 13) extend this set later.
    private static IReadOnlyList<ControllerMappingProfile> AvailableMidiProfiles()
        => new[] { CmdStudio2AProfile.Default };
}
