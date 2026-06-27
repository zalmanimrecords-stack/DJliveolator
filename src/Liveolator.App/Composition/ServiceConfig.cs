using Liveolator.App.Features.Addons;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Mappings;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Features.Playlists;
using Liveolator.App.Features.Settings;
using Liveolator.App.Features.Shared;
using Liveolator.App.Features.Studio;
using Liveolator.App.Features.Update;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.App.Skins;
using Liveolator.App.Theme;
using Liveolator.App.Shell;
using Liveolator.Audio;
using Liveolator.Audio.Capture;
using Liveolator.Audio.Playback;
using Liveolator.Audio.Recording;
using Liveolator.Audio.Waveform;
using Liveolator.Audio.Vst3;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis;
using Liveolator.Core.Audio;
using Liveolator.Core.Audio.Effects;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Beat;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Extensions;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Doctor;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Liveolator.Core.Mixer;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Recording;
using Liveolator.Core.Settings;
using Liveolator.Core.Update;
using Liveolator.Core.Visuals;
using Liveolator.Core.Waveform;
using Liveolator.Media;
using Liveolator.Media.Extensions;
using Liveolator.Media.Import;
using Liveolator.Midi;
using Liveolator.Online;
using Liveolator.Core.Platform;
using Liveolator.Platform;
using Liveolator.Platform.Audio;
using Liveolator.Visuals;
using Liveolator.Visuals.Gl;
using Liveolator.App.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Liveolator.App.Composition;

/// <summary>
/// The application's composition root — the single place where modules are wired together.
/// A "module" is a Core service plus the bindings it needs; registering it here is how it
/// becomes reachable from the UI (view-models take these services via constructor injection).
/// </summary>
public static class ServiceConfig
{
    public static IServiceProvider Build(
        IMidiDeviceProvider? midiProviderOverride = null,
        IAudioOutputDeviceCatalog? outputCatalogOverride = null,
        IAudioCaptureDeviceCatalog? captureCatalogOverride = null,
        IAudioCaptureSourceFactory? captureFactoryOverride = null,
        bool enableSystemMetrics = true,
        string? persistenceRootDirectory = null)
    {
        var services = new ServiceCollection();

        // EVERY on-disk store below roots here. Tests MUST pass a temp directory — building the real
        // composition root with the default once autosaved fake test tracks ("a.mp3"…) into the user's
        // real %APPDATA%/Liveolator live set, which the app then tried to load on every launch.
        string persistenceRoot = persistenceRootDirectory ?? JsonCatalogStore.DefaultRoot();

        // --- Persisted preferences (doc 12) ---
        // Loaded once up-front because the realtime engine needs the chosen output device + buffer
        // BEFORE it opens BASS. Blocking is acceptable in the composition root at startup (one small JSON
        // file) and the load is tolerant: a missing/corrupt file yields AppSettings.Default. The same
        // store instance is registered below so the Settings tab reads/writes the very same file.
        var settingsStore = new JsonSettingsStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        AppSettings appSettings = settingsStore.LoadAsync().GetAwaiter().GetResult();

        // --- On-disk logging (doc 12 diagnostics): the single place "log to a file" becomes wiring.
        // Every engine/UI logger resolved from DI writes to one rolling file under %APPDATA%/Liveolator/logs
        // at the persisted verbosity, so a field crash or a swallowed GL/audio failure leaves a trace.
        // The factory is a singleton; ILogger<T> is the open-generic over it; the locator backs the
        // Settings "Open logs folder" link. Global handlers capture otherwise-unhandled exceptions.
        var logOptions = new FileLoggerOptions
        {
            Directory = Path.Combine(persistenceRoot, "logs"),
            MinimumLevel = AppLogging.ParseLevel(appSettings.Diagnostics.MinimumLevel),
        };
        ILoggerFactory loggerFactory = AppLogging.CreateFactory(logOptions);
        AppLogging.InstallGlobalExceptionLogging(loggerFactory);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<ILogFileLocator>(new LogFileLocator(logOptions));

        // --- Authored Live-Mode data (doc 13): mapping profiles / scenes / macros / autopilot rule-sets
        // persisted under the per-user live/ root. The seam lives in Core; LiveProfileStore is the Media
        // binding. Registered so the host (and later the MIDI / Settings agents) can load/save snapshots;
        // a saved visual bank is loaded at startup below to feed the visual engine (scenes → banks).
        var liveProfileStore = new LiveProfileStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        services.AddSingleton<ILiveProfileStore>(liveProfileStore);
        services.AddSingleton<ITrackVisualProgramStore>(
            _ => new JsonTrackVisualProgramStore(
                persistenceRoot,
                onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));

        // --- Declarative extension packages -----------------------------------------------------
        // Trust is read from a separate user-controlled file; packages cannot add their own keys.
        // Enabled package content is loaded before the window is created so the selected UI theme
        // can be applied in App.OnFrameworkInitializationCompleted.
        var trustedPublishers = new JsonTrustedPublisherStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        var extensionCatalog = new ExtensionCatalog(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        var extensionValidator = new ExtensionPackageValidator(trustedPublishers);
        var extensionInstaller = new ExtensionInstaller(
            extensionValidator, extensionCatalog, appSettings.Extensions.DeveloperMode);
        var visualEffects = new VisualEffectRegistry();
        var generatorPresets = new GeneratorPresetRegistry();
        var uiThemes = new UiThemeManager();
        BuiltInUiThemes.Register(uiThemes);
        string shaderProbeName = OperatingSystem.IsWindows()
            ? "liveolator-shader-probe.exe"
            : "liveolator-shader-probe";
        var shaderProbe = new ProcessVisualShaderProbe(
            Path.Combine(AppContext.BaseDirectory, shaderProbeName));
        var extensionContent = new ExtensionContentLoader(
            extensionCatalog, visualEffects, uiThemes, shaderProbe,
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w),
            presets: generatorPresets);
        extensionContent.ReloadAsync().GetAwaiter().GetResult();

        // Register the built-in VU-meter generator (doc 26 reference add-on) AFTER the extension reload
        // so it is not removed, and under its own package id so it never collides with an installed pack.
        // A generator layer can then render out of the box and react to the live master level.
        // The persisted custom dial-face (Add-ons tab) becomes the generator's background at startup; a
        // null/missing path falls back to the built-in face.
        VuMeterAddon.TryRegister(
            visualEffects,
            backgroundPath: appSettings.Addons.VuMeterBackgroundImagePath,
            origin: appSettings.Addons.VuMeterNeedleOrigin,
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        PsyFractalVisualizerAddon.TryRegister(
            visualEffects,
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w));

        // Built-in FRKTL controllable preset (doc 28): a frame-feedback generator plus a preset exposing
        // five controllable knobs. Registered into both the effect and preset registries.
        FrktlPresetAddon.TryRegister(
            visualEffects, generatorPresets,
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w));

        // User-authored FRKTL presets (doc 29): a folder of self-contained .frktl files (each its own
        // shader + up to five controllable knobs), loaded after the built-ins so they extend the picker.
        ILogger frktlLog = loggerFactory.CreateLogger("Liveolator.Frktl");
        var frktlPresetLoader = new Liveolator.Media.Visuals.FrktlPresetFolderLoader(
            visualEffects, generatorPresets,
            folder: Path.Combine(persistenceRoot, "frktl-presets"),
            onWarning: w => frktlLog.LogWarning("{Warning}", w));
        int frktlCount = frktlPresetLoader.Load();
        frktlLog.LogInformation(
            "Loaded {Count} FRKTL folder preset(s) from {Folder}.", frktlCount, frktlPresetLoader.Folder);
        services.AddSingleton(frktlPresetLoader);
        // The same loader, exposed as the runtime reload seam so the LIVE surface can re-scan the folder
        // (e.g. after an MCP-authored preset) without an app restart.
        services.AddSingleton<IVisualPresetReloader>(frktlPresetLoader);

        // User/agent-authored control skins (doc 30): a folder of .ctrlskin files (parametric knob/slider
        // looks). Loaded here so the chosen skin can be applied in App.OnFrameworkInitializationCompleted
        // and the Settings pickers can list what exists. Tolerant: a bad file is skipped, not fatal.
        var controlSkins = new Liveolator.Media.Skins.ControlSkinFolderLoader(
            folder: Path.Combine(persistenceRoot, "control-skins"),
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        services.AddSingleton<IControlSkinCatalog>(new ControlSkinCatalog(controlSkins.Load()));
        services.AddSingleton<IControlSkinApplier>(
            new ApplicationControlSkinApplier(w => System.Diagnostics.Trace.TraceWarning(w)));
        services.AddSingleton<IUiThemeLiveApplier, ApplicationUiThemeLiveApplier>();
        // Export/import a MIDI mapping by device model (doc 05): file IO in Media, file dialog in App.
        services.AddSingleton<IMappingProfilePortability>(
            _ => new MappingProfilePortability(w => System.Diagnostics.Trace.TraceWarning(w)));
        services.AddSingleton<IMappingFilePicker, StorageProviderMappingFilePicker>();

        services.AddSingleton<ITrustedPublisherStore>(trustedPublishers);
        services.AddSingleton<IExtensionCatalog>(extensionCatalog);
        services.AddSingleton<IExtensionValidator>(extensionValidator);
        services.AddSingleton<IExtensionInstaller>(extensionInstaller);
        services.AddSingleton<IVisualEffectRegistry>(visualEffects);
        services.AddSingleton<IGeneratorPresetRegistry>(generatorPresets);
        services.AddSingleton<IVisualShaderProbe>(shaderProbe);
        services.AddSingleton<IUiThemeManager>(uiThemes);
        services.AddSingleton<IExtensionContentReloader>(extensionContent);

        // --- VST3 catalog + realtime racks -------------------------------------------------------
        // The scanner and native processor bridge are deliberately separate. Without the optional
        // native helper/bridge, plugins remain visible as unavailable placeholders and audio passes
        // through unchanged.
        string vstCatalogPath = Path.Combine(persistenceRoot, "vst3-catalog.json");
        string scannerName = OperatingSystem.IsWindows()
            ? "liveolator-vst3-scanner.exe"
            : "liveolator-vst3-scanner";
        var vstCatalog = new Vst3ScannerClient(
            Path.Combine(AppContext.BaseDirectory, scannerName),
            vstCatalogPath,
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        vstCatalog.RefreshAsync().GetAwaiter().GetResult();
        var effectRacks = new AudioEffectRackProvider(new Vst3AudioEffectProcessorFactory());
        // Restore persisted audio-effect rack state at startup and persist on every change, so VST3
        // chains / parameters / missing-plugin placeholders survive restarts (app-shell wave).
        var rackStateStore = new JsonAudioEffectRackStateStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        foreach (AudioEffectRackState state in rackStateStore.LoadAsync().GetAwaiter().GetResult())
            effectRacks.GetRack(state.Slot).Restore(state);
        services.AddSingleton<IAudioEffectPluginCatalog>(vstCatalog);
        services.AddSingleton<IAudioEffectRackProvider>(effectRacks);
        services.AddSingleton<IAudioEffectRackStateStore>(rackStateStore);
        var audioEffectHandler = new AudioEffectActionHandler(effectRacks, onChanged: () =>
        {
            AudioEffectRackState[] states = System.Linq.Enumerable.Range(0, AudioEffectRackSlot.Count)
                .Select(slot => effectRacks.GetRack(slot).State)
                .ToArray();
            _ = rackStateStore.SaveAsync(states).ContinueWith(
                task => System.Diagnostics.Trace.TraceWarning(
                    $"Audio effect rack state could not be saved: {task.Exception?.GetBaseException().Message}"),
                System.Threading.CancellationToken.None,
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
                System.Threading.Tasks.TaskScheduler.Default);
        });

        // --- Track-analysis / music-library module (doc 16) ---
        // Bindings come from the dedicated projects: Platform (filesystem) + Audio (WAV + FFmpeg).
        services.AddSingleton<IFileEnumerator, FileSystemEnumerator>();          // Liveolator.Platform
        services.AddSingleton<IFileExistenceProbe, FileSystemExistenceProbe>();
        services.AddSingleton<IFolderExistenceProbe, FileSystemFolderExistenceProbe>();
        services.AddSingleton<IAudioDecoder>(_ => new CompositeAudioDecoder());  // Liveolator.Audio
        services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();        // Liveolator.Audio (ATL.NET tags)
        // Deck waveform overview (doc 11): decodes the loaded track to peaks for the deck strip. Uses the
        // offline decoder, so it works headless (no realtime BASS needed); failures degrade to no waveform.
        services.AddSingleton<IWaveformProvider>(sp => new DecodedWaveformProvider(
            sp.GetRequiredService<IAudioDecoder>(),
            logger: loggerFactory.CreateLogger<DecodedWaveformProvider>()));
        services.AddSingleton<TrackAnalyzer>();
        services.AddSingleton<MusicLibrary>();
        WireOnlineEnrichment(services);
        WireUpdateCheck(services, loggerFactory);
        // Persists the analyzed catalog + scan folders under %APPDATA%/Liveolator so state survives
        // restarts (doc 13). The seams live in Core; one JsonCatalogStore binds both the music
        // (IMusicCatalogStore) and the visual (IVisualCatalogStore, Track C C1) catalog domains.
        var catalogStore = new JsonCatalogStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        services.AddSingleton<IMusicCatalogStore>(catalogStore);
        services.AddSingleton<IVisualCatalogStore>(catalogStore);
        services.AddSingleton<IMediaIdentityStore>(
            _ => new JsonMediaIdentityStore(
                persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));
        services.AddSingleton<IFileContentHasher, Sha256FileContentHasher>();
        services.AddSingleton<ISmartCollectionStore>(
            _ => new JsonSmartCollectionStore(
                persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));
        services.AddSingleton<LibraryDoctor>(sp => new LibraryDoctor(
            sp.GetRequiredService<IFileExistenceProbe>(),
            sp.GetRequiredService<IFolderExistenceProbe>()));
        // Visual-media library (doc 08/13, Track C C1): the same Core library + composite probe the MCP
        // scan_visual_folders tool uses. The composite probe routes images to a pure header reader and
        // videos to ffprobe, so the common image case needs no external tool.
        services.AddSingleton<IVisualMediaProbe>(_ => new CompositeVisualMediaProbe());
        services.AddSingleton<VisualMediaLibrary>(sp => new VisualMediaLibrary(
            sp.GetRequiredService<IFileEnumerator>(), sp.GetRequiredService<IVisualMediaProbe>()));
        // Visual-library preview (Track C C1): decodes the selected asset for the detail panel — images
        // via managed Skia, video via an ffmpeg-extracted frame (null preview when ffmpeg is absent).
        services.AddSingleton<IVisualThumbnailRenderer>(_ => new CompositeVisualThumbnailRenderer());
        // Removes an asset's file from disk when the user deletes it (OS-backed, Liveolator.Platform).
        services.AddSingleton<IFileRemover, FileSystemFileRemover>();
        // Named, saved playlists/sets (doc 09/13) — one JSON file per set under live/playlists/.
        services.AddSingleton<IPlaylistStore>(
            _ => new JsonPlaylistStore(
                persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));
        // STUDIO DAW arrangements (doc: STUDIO timeline) — one versioned JSON per project under
        // live/studio-projects/, separate from the flat playlists above.
        services.AddSingleton<IStudioProjectStore>(
            _ => new JsonStudioProjectStore(
                persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w)));
        // Persistent per-track hot cues (doc 11/13, A3) — a separate JSON file so cue edits never touch
        // the analyzed catalog. Threaded into the two-deck engine below so a track's cues reload on the
        // next run and survive a deck reload (tolerant: a missing/corrupt file degrades to no cues).
        var hotCueStore = new JsonHotCueStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        services.AddSingleton<IHotCueStore>(hotCueStore);

        // Automatic hot-cue placement (doc 11/16): an offline pass that decodes a track, detects its
        // musical structure (drop/breakdown/build/phrases) and writes suggested cues into the same hot-cue
        // store, preserving the DJ's manual cues. Pure-managed orchestration over the offline decoder — no
        // audio-thread work — so it is safe to run on a background thread. Registered here so a UI action
        // (library "Auto-cue track(s)") or an opt-in background pass can resolve and run it; the cues then
        // light up on the next deck load via the engine's existing hot-cue reload.
        services.AddSingleton<Liveolator.Core.Analysis.Cues.IAutoCueService>(sp =>
            new Liveolator.Core.Analysis.Cues.AutoCueService(
                sp.GetRequiredService<IAudioDecoder>(),
                sp.GetRequiredService<IHotCueStore>(),
                onError: w => System.Diagnostics.Trace.TraceWarning(w)));

        // Library import from other DJ apps (doc: import): plain-XML parsers (Rekordbox/Traktor) + the
        // format-agnostic mapping service that remaps paths and writes tracks/cues/playlists through the
        // existing stores. Parsing is pure (Core seam); the file-probe is the only OS touch (StatImportFile).
        services.AddSingleton<ILibraryImporter, RekordboxXmlImporter>();
        services.AddSingleton<ILibraryImporter, TraktorNmlImporter>();
        services.AddSingleton<ILibraryImporter, VirtualDjXmlImporter>();
        // Serato + Mixxx + Engine DJ are folder-based (Serato: per-file GEOB tags + binary .crate; Mixxx:
        // mixxxdb.sqlite; Engine: Database2/m.db), so they implement the folder seam, not the single-file one.
        services.AddSingleton<IFolderLibraryImporter, Liveolator.Media.Import.Serato.SeratoLibraryImporter>();
        services.AddSingleton<IFolderLibraryImporter, Liveolator.Media.Import.Mixxx.MixxxLibraryImporter>();
        services.AddSingleton<IFolderLibraryImporter, Liveolator.Media.Import.Engine.EngineLibraryImporter>();
        services.AddSingleton<LibraryImportService>(sp => new LibraryImportService(
            sp.GetRequiredService<IHotCueStore>(),
            sp.GetRequiredService<IPlaylistStore>(),
            path => ImportFileProbe.Stat(
                path, msg => sp.GetRequiredService<ILogger<LibraryImportService>>().LogWarning("{Warning}", msg))));

        // --- Shared performance clock (the product differentiator: ONE beat clock drives both the
        // visuals and the Live tap controls). Pure-managed, no native — so the "tap a tempo and the
        // visuals pulse on the beat" experience works with NO audio hardware. The audio-driven
        // AudioBeatClock (registered below when BASS is present) is a separate source used for the
        // Libraries live-BPM readout; unifying the two is a later step (doc 03).
        var hostClock = new SystemHostClock();
        var sharedLiveClock = new ManualBeatClock(hostClock.TicksPerSecond);

        // --- Software mixer (doc 11): pure-constructable with NO native dependency — BassMixer is a
        // routing skeleton that logs+drops calls for slots whose deck channel is not registered yet.
        // So the mixer model + DSP math drive from the UI headless; native FX routing lands later.
        var mixer = new BassMixer();
        services.AddSingleton<IMixer>(mixer);
        services.AddSingleton<IDeckLevelMeter>(mixer);
        var mixerHandler = new MixerActionHandler(mixer);

        // --- Global OS volume (the computer's master output level, not the app's mix): the per-OS
        // controller (WASAPI on Windows, osascript on macOS, no-op elsewhere) behind the Core seam, driven
        // through the dispatcher like any other action. Always present and pure-managed at this layer, so
        // the SystemMasterVolume kind is owned even when realtime audio is absent.
        ISystemVolumeController systemVolume = SystemVolumeControllers.Create(
            w => System.Diagnostics.Trace.TraceWarning(w));
        services.AddSingleton(systemVolume);
        var systemVolumeHandler = new SystemVolumeActionHandler(systemVolume, loggerFactory);

        // --- Realtime audio engine (docs 01/02/11): built BEFORE the visual engine so its beat clock,
        // when present, can drive the visuals (the audible signal is authoritative). The BASSmix backend
        // needs native bass + bassmix at runtime; if absent the app still runs as a catalog browser and
        // the deck transport is simply unrouted. The two decks feed ONE master mix and the beat clock is
        // driven off that master (MasterMixPlaybackEngine), so it follows the audible post-crossfader
        // signal (doc 11) rather than a single switched deck. The IBeatClock/IMultiDeckPlaybackEngine
        // registrations stay below, next to the dispatcher composition that consumes them.
        TwoDeckBassEngine? deckEngine = TryBuildDeckEngine(mixer, appSettings.Audio, effectRacks, hotCueStore, loggerFactory);
        // The master-mix clock phase-locks its detected grid onto the audible kick (OnsetPhaseLock), so
        // when a deck is NOT the sync master — an un-analyzed track, or live input with no precomputed
        // grid — the shared clock still tracks the beat without drifting (doc 03 drift prevention).
        MasterMixPlaybackEngine? masterMix = deckEngine is null
            ? null
            : new MasterMixPlaybackEngine(deckEngine.MasterSource, hostClock, phaseLock: new OnsetPhaseLock());
        bool realtimeUp = deckEngine is not null;

        // Audio-engine self-check (doc 11 / global #26): a missing native library is otherwise invisible —
        // the decks render but every track load throws and is swallowed, so playback and SYNC silently do
        // nothing. Probe it once now and register the result so the shell can show a banner up front.
        AudioEngineStatus audioStatus = !realtimeUp
            ? new AudioEngineStatus(
                PlaybackAvailable: false, EffectsAvailable: false,
                Warning: "Live audio engine unavailable — track playback and SYNC are off (native BASS not found).")
            : deckEngine!.EffectsLibraryAvailable()
                ? AudioEngineStatus.Healthy
                : new AudioEngineStatus(
                    PlaybackAvailable: true, EffectsAvailable: false,
                    Warning: "Audio effects library (bass_fx) is missing — tracks can't load and SYNC won't work. Reinstall Liveolator.");
        services.AddSingleton(audioStatus);

        // --- Visual engine (doc 08): the GL compositor binds to the live clock. Base source = the
        // audio-driven master-mix clock when realtime audio is up (visuals lock to the music), else the
        // shared manual tap clock (headless). Wrapped in a SwitchingBeatClock so that when a deck becomes
        // the sync MASTER, the visuals (and the registered IBeatClock below) follow that deck's
        // deterministic grid directly — the product's audio↔visual lock (doc 03/11). MasterClockBridge
        // does the per-tick switching on the render loop. Runs before the dispatcher so its handler exists
        // when the dispatcher composes.
        IBeatClock visualBaseClock = LiveClockSelector.Select(masterMix?.BeatClock, sharedLiveClock);
        var deckBeatClock = new DeckDrivenBeatClock(hostClock.TicksPerSecond);
        var sharedVisualClock = new SwitchingBeatClock(visualBaseClock);

        // Clock-driven quantized-launch scheduler (doc 31): visual scene launches and playlist skips
        // defer to the next beat/bar on the ONE shared clock, replacing the interim immediate scheduler.
        // Registered so DI disposes its clock subscription at shutdown.
        var beatScheduler = new ClockBeatScheduler(sharedVisualClock);
        services.AddSingleton<IBeatScheduler>(beatScheduler);

        // The live master level feeding reactive shaders (doc 26). When realtime audio is up the meter
        // taps the same master-mix frames the beat clock reads; headless it rests at silence. Registered
        // as a singleton so DI disposes the frame subscription at shutdown.
        IVisualAudioLevelSource audioLevel = masterMix is not null
            ? new FrameAudioLevelMeter(masterMix.FrameProvider)
            : new SilentVisualAudioLevelSource();
        services.AddSingleton<IVisualAudioLevelSource>(audioLevel);

        (VisualActionHandler visualHandler, GlVisualPerformanceEngine visualEngine) =
            WireVisuals(services, sharedVisualClock, liveProfileStore, visualEffects, generatorPresets, audioLevel,
                beatScheduler, loggerFactory);

        // --- Live playlist / set (doc 09): the performance-editable Now/Next/Later queue the DJ tab
        // shows. Pure-managed. SkipOn(...) defers through the shared clock-driven IBeatScheduler so a
        // skip-on-next-bar lands on the same grid as the audio (doc 03/31). The handler owns the playlist
        // edits (insert/move/remove/skip) so the UI drives them through the one dispatcher.
        var livePlaylist = new LivePlaylist(beatScheduler, loggerFactory.CreateLogger<LivePlaylist>());
        services.AddSingleton<ILivePlaylist>(livePlaylist);

        // Deck B's own live queue (doc 09/11): loading onto a PLAYING deck appends here instead of
        // cutting the deck off (DeckTrackLoader policy); the queued track plays when the current one
        // ends via the slot-1 PlaylistAudioPlayer below. Persisted in its own file beside deck A's set.
        var deckBPlaylist = new LivePlaylist(beatScheduler, loggerFactory.CreateLogger<LivePlaylist>());

        // Persist + restore the live set so the DJ tab opens where the last run left off (doc 13) instead
        // of an empty queue. Restore runs HERE — synchronously, before the queue's audio binding is wired
        // (WirePlaylistAudio / the eager PlaylistAudioPlayer at the end of Build) — so the restored Now is
        // shown but not auto-played on launch. After restoring, every later edit is saved on the queue's
        // Changed event (fire-and-forget, faults logged) so a crash loses at most the last edit. Tolerant:
        // a missing/corrupt set degrades to an empty queue (global standards #16/#26).
        var liveSetStore = new JsonLiveSetStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        services.AddSingleton<ILiveSetStore>(liveSetStore);
        RestoreAndPersistLiveSet(livePlaylist, liveSetStore);
        var deckBSetStore = new JsonLiveSetStore(
            persistenceRoot,
            onWarning: w => System.Diagnostics.Trace.TraceWarning(w), fileName: "deck-b-set.json");
        RestoreAndPersistLiveSet(deckBPlaylist, deckBSetStore);

        services.AddSingleton<LibraryReferenceRewriter>(sp => new LibraryReferenceRewriter(
            new ILibraryReferenceRewriteStore[]
            {
                new PlaylistReferenceRewriteStore(sp.GetRequiredService<IPlaylistStore>()),
                new LiveSetReferenceRewriteStore("deck A live set", liveSetStore),
                new LiveSetReferenceRewriteStore("deck B live set", deckBSetStore),
                new TrackVisualProgramReferenceRewriteStore(sp.GetRequiredService<ITrackVisualProgramStore>()),
            }));

        var deckSessionStore = new JsonDeckSessionStore(
            persistenceRoot, onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
        services.AddSingleton<IDeckSessionStore>(deckSessionStore);

        // Per-deck queues, addressed by the playlist action's Slot (A = 0, B = 1).
        var playlistHandler = new PlaylistActionHandler(
            new ILivePlaylist[] { livePlaylist, deckBPlaylist },
            loggerFactory.CreateLogger<PlaylistActionHandler>());

        // Register the realtime engine seams (constructed above, before the visual engine). Registering
        // IMultiDeckPlaybackEngine lets the deck handler address both decks AND lets the two-deck engine
        // register its per-deck channel into the BassMixer as decks load — closing the seam so the mixer's
        // gain/EQ/filter actually route to audio. Registering IBeatClock gives the Libraries tab its
        // live-BPM readout; it is the same master clock the visual engine binds to (LiveClockSelector).
        MasterClockPump? syncPump = null;
        if (realtimeUp)
        {
            services.AddSingleton<IMultiDeckPlaybackEngine>(deckEngine!);
            // The shared clock the Libraries readout + visuals follow is the switching clock, so all of
            // them lock to the sync-master deck when one is engaged (else the audio-mix base). The bridge
            // pump drives the deck clock and flips the switch independently of UI responsiveness.
            services.AddSingleton<IBeatClock>(sharedVisualClock);
            var syncBridge = new MasterClockBridge(
                deckEngine!, deckBeatClock, sharedVisualClock, visualBaseClock);
            syncPump = new MasterClockPump(syncBridge, hostClock);
            services.AddSingleton(syncPump);
        }

        // --- Master recording (roadmap X2): capture the post-limiter master to a clean WAV via the
        // IMasterRecorder seam, without touching playback. The recorder taps the SAME master IAudioSource
        // the analysis tap reads, so the file matches what the house hears. When realtime audio is absent
        // the recorder is built with a null master (IsAvailable = false), so the MasterRecordToggle kind is
        // still owned and the REC button simply greys out (headless-safe). Files land under a timestamped
        // recordings folder beside the app's other state.
        var masterRecorder = new BassMasterRecorder(
            deckEngine?.MasterSource,
            deckEngine?.MasterChannels ?? 2,
            deckEngine?.MasterSampleRate ?? 48_000,
            loggerFactory);
        services.AddSingleton<IMasterRecorder>(masterRecorder);
        var recordingHandler = new RecordingActionHandler(
            masterRecorder, new TimestampedRecordingPathProvider(persistenceRoot), loggerFactory);

        // --- THE one dispatcher (doc 04): every input source — UI, controller, autopilot — drives the
        // engines through this single instance, so handler state never diverges (doc 12, one source of
        // truth). Beat + mixer + visual handlers are always present (all pure-managed); the deck
        // transport handler joins only when the realtime engine is up.
        var handlers = new List<IPerformanceActionHandler>
        {
            new BeatActionHandler(sharedLiveClock, hostClock),
            mixerHandler,
            systemVolumeHandler,
            visualHandler,
            playlistHandler,
            audioEffectHandler,
            recordingHandler,
        };
        if (realtimeUp)
        {
            handlers.Add(new DeckActionHandler(deckEngine!));
        }

        var dispatcher = new PerformanceActionDispatcher(
            handlers,
            loggerFactory.CreateLogger<PerformanceActionDispatcher>(),
            requireCompleteOwnership: realtimeUp);
        services.AddSingleton(dispatcher);

        // Seed BassMixer's per-slot gain cache with the initial crossfader position (default = centre).
        // Without this, the first deck loaded would play at raw BASS volume (1.0) regardless of the
        // crossfader, because SetDeckGain is only called on action dispatch — never on construction.
        // This push stores the correct gains in BassMixer._gains[] so SetChannel can re-apply them
        // the moment a track is loaded (doc 11 / crossfader-before-load invariant).
        if (realtimeUp)
        {
            dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.MixerCrossfade,
                ActionInputMode.Absolute,
                Value: mixerHandler.State.Crossfader,
                Slot: 0));

            // Push the authoritative smart-limiter defaults (SMART on, balanced character, −1 dBTP) to the
            // running master limiter at startup. Any one MixerLimiter* action re-applies the whole settings
            // via the handler, so an absolute character push (value unchanged) carries the SMART flag too.
            dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.MixerLimiterCharacter,
                ActionInputMode.Absolute,
                Value: mixerHandler.State.Limiter.Character,
                Slot: 0));
        }

        if (realtimeUp)
        {
            var deckSession = new DeckSessionPersistence(
                dispatcher, deckSessionStore, deckEngine!.DeckCount,
                fileExists: File.Exists,
                logger: loggerFactory.CreateLogger<DeckSessionPersistence>());
            services.AddSingleton(deckSession);
        }

        // Autosave the live visual layer arrangement so it survives a restart (the engine otherwise only
        // mutates the in-memory active scene). Restored automatically by LoadBanksOrStarter loading the
        // "Live" bank first. Registered as a singleton so the feedback subscription outlives Build().
        var visualSession = new VisualSessionPersistence(
            dispatcher, () => visualEngine.ActiveScene, liveProfileStore);
        services.AddSingleton(visualSession);

        WirePlaylistAudio(services, livePlaylist, deckBPlaylist, dispatcher, deckEngine);

        // --- MIDI controller → dispatcher (doc 05/07) ---
        // Open the SETTINGS-chosen controller and route the live hardware through the SAME dispatcher via
        // MidiControlSession, which also publishes LED feedback, runs the activity monitor, and exposes
        // IMidiControlStatus for the shell's connection/signal indicators. Best-effort: a missing/absent
        // device or native rtmidi failure leaves the session idle (logged), never blocking startup
        // (global standards #16/#26). The default-profile auto-select path (CmdStudio2AProfile) lives in
        // TryOpenMidiPipeline for the controller-profile-capture increment (doc 22 step A8).
        var midiProvider = midiProviderOverride ?? new RtMidiDeviceProvider();
        MidiSettings effectiveMidiSettings = ResolveMidiSettings(appSettings.Midi, midiProvider);
        if (effectiveMidiSettings != appSettings.Midi.Normalized())
        {
            appSettings = appSettings with { Midi = effectiveMidiSettings };
            settingsStore.SaveAsync(appSettings).GetAwaiter().GetResult();
        }
        var midiSession = new MidiControlSession(
            midiProvider,
            dispatcher,
            liveProfileStore,
            new MidiLearnSession(),
            AvailableMidiProfiles(),
            loggerFactory);
        try
        {
            midiSession.StartAsync(effectiveMidiSettings).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"MIDI control session could not start: {ex.Message}.");
        }
        services.AddSingleton(midiSession);
        services.AddSingleton<IMidiControlSession>(midiSession);
        services.AddSingleton<IMidiControlStatus>(midiSession);
        var globalMidiLearn = new GlobalMidiLearnCoordinator(midiSession);
        services.AddSingleton(globalMidiLearn);
        services.AddSingleton<IPerformanceActionDispatcher>(
            new LearningPerformanceActionDispatcher(dispatcher, globalMidiLearn));

        // Shared decks + crossfader (doc 11/12): ONE PerformanceDeckSet drives both the Live tab and the
        // DJ tab, so a track loaded on one is reflected on the other (one source of truth). It carries the
        // MusicLibrary so each deck can surface its loaded track's Key · BPM · duration. Both view-models
        // resolve this singleton instead of each building a private set (the Live tab previously built one
        // without the library, so its decks showed no BPM).
        services.AddSingleton<PerformanceDeckSet>(sp => new PerformanceDeckSet(
            sp.GetService<IPerformanceActionDispatcher>(),
            sp.GetService<IWaveformProvider>(),
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetService<IDeckLevelMeter>(),
            appSettings.Visuals.WaveformZoomSeconds,
            appSettings.Visuals.NudgeSeconds,
            // Deck transport is handled only when the realtime engine is up; in catalog-browser mode the
            // decks disable Play/Cue/Sync/etc. instead of silently dropping those actions (QA finding S1).
            deckTransportEnabled: realtimeUp,
            autoCueService: sp.GetService<Liveolator.Core.Analysis.Cues.IAutoCueService>()));

        WireCaptureSources(services, captureCatalogOverride, captureFactoryOverride);
        WireLiveTab(services, sharedLiveClock, hostClock);

        // --- View-models ---
        // Shared back-end for the per-track right-click menu (Add to Deck A/B, Add to playlist). The
        // dispatcher is passed only when the realtime engine is up (deck items disable otherwise);
        // add-to-playlist works regardless (store-only). One singleton drives every track row's menu.
        services.AddSingleton<TrackContextActions>(sp => new TrackContextActions(
            realtimeUp ? sp.GetService<IPerformanceActionDispatcher>() : null,
            sp.GetRequiredService<IPlaylistStore>(),
            onStatus: w => System.Diagnostics.Trace.TraceInformation(w),
            library: sp.GetRequiredService<MusicLibrary>(),
            catalogStore: sp.GetRequiredService<IMusicCatalogStore>(),
            metadataProvider: sp.GetService<IMetadataProvider>(),
            fingerprinter: sp.GetService<IAudioFingerprinter>(),
            editor: sp.GetRequiredService<ITrackEditor>(),
            autoCueService: sp.GetService<Liveolator.Core.Analysis.Cues.IAutoCueService>()));
        services.AddSingleton<ITrackEditor, TrackEditor>();
        // Modal yes/no confirmation for destructive actions (e.g. deleting a visual asset's file).
        services.AddSingleton<IConfirmationService, ConfirmationService>();

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
            sp.GetService<LibraryDoctor>(),
            sp.GetService<IMediaIdentityStore>(),
            sp.GetService<VisualMediaLibrary>(),
            sp.GetService<IFileContentHasher>(),
            sp.GetRequiredService<PlaylistBuilderViewModel>(),
            sp.GetRequiredService<TrackContextActions>(),
            autoCueService: sp.GetService<Liveolator.Core.Analysis.Cues.IAutoCueService>(),
            hotCueStore: sp.GetService<IHotCueStore>(),
            importService: sp.GetService<LibraryImportService>(),
            importers: sp.GetServices<ILibraryImporter>().ToList(),
            folderImporters: sp.GetServices<IFolderLibraryImporter>().ToList()));

        // VJ / Visual Library tab (Track C C1): browse/search/filter the scanned image + video catalog.
        services.AddSingleton<VisualLibraryViewModel>(sp => new VisualLibraryViewModel(
            sp.GetRequiredService<VisualMediaLibrary>(),
            sp.GetRequiredService<IVisualCatalogStore>(),
            sp.GetRequiredService<IVisualThumbnailRenderer>(),
            sp.GetRequiredService<IFileRemover>(),
            sp.GetRequiredService<IConfirmationService>()));

        // STUDIO tab: the DAW-timeline arrangement editor. Plays the arrangement live via the dispatcher
        // (4-deck engine) and renders it offline to WAV; degrades to plan-only when the realtime engine
        // or decoder is absent. A fresh SystemHostClock drives the transport tick (stateless stopwatch).
        services.AddSingleton<StudioViewModel>(sp => new StudioViewModel(
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetRequiredService<IStudioProjectStore>(),
            realtimeUp ? sp.GetService<IPerformanceActionDispatcher>() : null,
            new SystemHostClock(),
            sp.GetService<IWaveformProvider>(),
            sp.GetService<IAudioDecoder>(),
            sp.GetRequiredService<TrackContextActions>(),
            loggerFactory: loggerFactory));

        // DJ tab: the two decks + the live set (queue). Drives playback/queue through the one
        // dispatcher; reads ILivePlaylist + the catalog for the set readout (like the beat readout).
        services.AddSingleton<DjViewModel>(sp => new DjViewModel(
            sp.GetRequiredService<IPerformanceActionDispatcher>(),
            sp.GetRequiredService<ILivePlaylist>(),
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetRequiredService<TrackContextActions>(),
            sp.GetService<IWaveformProvider>(),
            sp.GetRequiredService<PerformanceDeckSet>(),
            deckBQueue: deckBPlaylist));

        // Settings tab (doc 12): detect audio output + MIDI equipment and persist the choice. The
        // device catalogs degrade to empty lists when native bass/rtmidi is absent (so the tab works
        // headless), and the choice is saved to settings.json. The audio output device + buffer are
        // applied at startup (loaded above, threaded into the realtime engine); the chosen MIDI
        // controller is now opened into the dispatcher above (MidiControlSession). The Settings tab reuses
        // the SAME provider instance that the MIDI control session opened the device through.
        services.AddSingleton<IAudioOutputDeviceCatalog>(
            outputCatalogOverride ?? new BassOutputDeviceCatalog());
        services.AddSingleton<IMidiDeviceProvider>(midiProvider);
        services.AddSingleton<ISettingsStore>(settingsStore);

        // Runtime device changes (doc 12 deferral): when the realtime engine is up, a Save in Settings
        // re-opens the output device/buffer without a restart via the pure AudioReinitCoordinator (rolls
        // back to the prior working device on failure), and applies the chosen capture source through the
        // existing factory + a stable SwitchableAudioSource. Both are optional — in catalog-browser mode
        // (no native BASS) they are null and the choice is saved for next launch only.
        AudioReinitCoordinator? audioReinit = realtimeUp
            ? new AudioReinitCoordinator(
                new BassAudioEngineReinitializer(deckEngine!), appSettings.Audio,
                loggerFactory.CreateLogger<AudioReinitCoordinator>())
            : null;

        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<IAudioOutputDeviceCatalog>(),
            sp.GetRequiredService<IAudioCaptureDeviceCatalog>(),
            sp.GetRequiredService<IMidiDeviceProvider>(),
            sp.GetRequiredService<ISettingsStore>(),
            audioReinit,
            realtimeUp
                ? new CaptureSourceController(
                    sp.GetRequiredService<IAudioCaptureSourceFactory>(),
                    new SwitchableAudioSource(),
                    loggerFactory.CreateLogger<CaptureSourceController>())
                : null,
            sp.GetRequiredService<IMidiControlSession>(),
            sp.GetRequiredService<IExtensionCatalog>(),
            sp.GetRequiredService<IExtensionInstaller>(),
            sp.GetRequiredService<IUiThemeManager>(),
            sp.GetRequiredService<IExtensionContentReloader>(),
            // The shared decks so a saved waveform-zoom change applies live (without a restart).
            sp.GetRequiredService<PerformanceDeckSet>(),
            sp.GetService<ILogFileLocator>(),
            // Control skins (doc 30): the catalog feeds the pickers; the applier re-skins live on Save.
            sp.GetRequiredService<IControlSkinCatalog>(),
            sp.GetRequiredService<IControlSkinApplier>(),
            sp.GetRequiredService<IUiThemeLiveApplier>(),
            sp.GetRequiredService<MappingsViewModel>()));

        // Shell top-bar status: audio route + MIDI connection/activity, driven off IMidiControlStatus
        // (the MidiControlSession registered above). AppSettings feeds the device-name readouts, and the
        // process metrics sampler feeds the live CPU/RAM readout (DI injects it into ShellStatusViewModel).
        services.AddSingleton(appSettings);
        if (enableSystemMetrics)
            services.AddSingleton<ISystemMetricsSampler, ProcessSystemMetricsSampler>();
        services.AddSingleton<MappingsViewModel>();

        // Add-ons tab (doc 26): list built-in add-ons + installed packages and configure the built-in VU
        // meter — replace its dial-face (background) image and persist it. The VU meter is a single
        // self-contained generator that samples its face as the background, so applying a new image is a
        // re-register of the generator descriptor + a recomposition (re-dispatching the VU layer's source
        // forces the renderer to rebuild the generator and reload the image). Works in any scene that
        // contains the VU generator; null engine (headless) just persists for next launch.
        services.AddSingleton<IImageDimensionsProbe, SkiaImageDimensionsProbe>();
        services.AddSingleton<AddonsViewModel>(sp =>
        {
            var dispatcher = sp.GetRequiredService<IPerformanceActionDispatcher>();
            IVisualPerformanceEngine? engine = sp.GetService<IVisualPerformanceEngine>();
            int? vuLayer = FindVuMeterLayer(engine)?.Slot;

            // Apply the chosen dial-face + needle origin live: re-register the VU generator with the new
            // background + origin, then nudge the VU layer's source so the compositor rebuilds the
            // generator and reloads the image/uniforms.
            void ApplyBackground(string? path, VuMeterNeedleOrigin origin)
            {
                VuMeterAddon.TryRegister(
                    visualEffects, backgroundPath: path, origin: origin,
                    onWarning: w => System.Diagnostics.Trace.TraceWarning(w));
                if (vuLayer is int slot)
                    dispatcher.Dispatch(new PerformanceAction(
                        PerformanceActionKind.VisualSetLayerSource,
                        Slot: slot,
                        Argument: VisualSourceActionCodec.Encode(
                            new VisualSourceRef(VisualSourceKind.Generator, VuMeterAddon.EffectId))));
            }

            return new AddonsViewModel(
                sp.GetRequiredService<ISettingsStore>(),
                VuMeterAddon.FaceSpec,
                VuMeterAddon.FaceImagePath,
                appSettings.Addons.VuMeterBackgroundImagePath,
                appSettings.Addons.VuMeterNeedleOrigin,
                ApplyBackground,
                sp.GetService<IVisualEffectRegistry>(),
                sp.GetService<IExtensionCatalog>(),
                sp.GetService<IImageDimensionsProbe>());
        });

        services.AddSingleton<ShellStatusViewModel>();
        // Global volume knob (top bar): drives the OS master volume via the dispatcher; disables itself
        // when the host has no controllable system volume.
        services.AddSingleton<SystemVolumeControlViewModel>(sp => new SystemVolumeControlViewModel(
            sp.GetService<IPerformanceActionDispatcher>()));
        services.AddSingleton<MainWindowViewModel>();

        ServiceProvider provider = services.BuildServiceProvider();
        // Populate the "Add to playlist" submenu once at startup (best-effort; guarded internally).
        _ = provider.GetRequiredService<TrackContextActions>().RefreshPlaylistsAsync();
        // Eagerly activate the live-queue audio bindings (when the realtime engine is up) so both
        // deck players start subscribing to NowChanged/DeckEnded immediately — nothing else resolves them.
        foreach (PlaylistAudioPlayer player in provider.GetServices<PlaylistAudioPlayer>())
            _ = player;
        provider.GetService<MasterClockPump>()?.Start();
        return provider;
    }

    // Builds the realtime two-deck BASS engine (registering its channels into the mixer), or null when
    // the native bass/bassmix libraries are absent (e.g. CI / a dev box without the per-platform
    // binaries). Never throws for that case — the app falls back to the catalog browser.
    private static TwoDeckBassEngine? TryBuildDeckEngine(
        BassMixer mixer,
        AudioSettings audioSettings,
        IAudioEffectRackProvider effectRacks,
        IHotCueStore hotCueStore,
        ILoggerFactory loggerFactory)
    {
        try
        {
            // Output latency for the phase-lock loop ≈ the configured output buffer. It cancels for
            // deck-to-deck phase (both decks share one output) but aligns the master-driven shared clock
            // (and the visuals) to what the listener actually hears.
            var phaseLock = new PhaseLockSettings(OutputLatencySeconds: audioSettings.BufferMilliseconds / 1000.0);
            // Pass the app logger so the engine's diagnostics — including the SYNC engage/skip lines that
            // explain a failed beatmatch — reach the rolling log file (global #26), instead of the default
            // NullLogger silently dropping them.
            return new TwoDeckBassEngine(
                mixer, loggerFactory: loggerFactory, audioSettings: audioSettings, effectRacks: effectRacks,
                hotCueStore: hotCueStore, phaseLock: phaseLock);
        }
        catch (Exception ex) when (ex is BassPlaybackException or DllNotFoundException)
        {
            System.Diagnostics.Trace.TraceWarning($"Realtime audio disabled: {ex.Message}.");
            return null;
        }
    }

    private static void WireOnlineEnrichment(IServiceCollection services)
    {
        services.AddSingleton<IAudioFingerprinter>(_ => new FpcalcFingerprinter(
            Environment.GetEnvironmentVariable("LIVEOLATOR_FPCALC_PATH")));

        string? getSongBpmKey =
            Environment.GetEnvironmentVariable("LIVEOLATOR_GETSONGBPM_KEY");
        if (string.IsNullOrWhiteSpace(getSongBpmKey))
            return;

        var bpmClient = new GetSongBpmClient(
            new HttpClient { BaseAddress = new Uri("https://api.getsong.co/") },
            getSongBpmKey);
        services.AddSingleton<IGetSongBpmClient>(bpmClient);

        string? acoustIdKey =
            Environment.GetEnvironmentVariable("LIVEOLATOR_ACOUSTID_KEY");
        IAcoustIdClient? acoustId = string.IsNullOrWhiteSpace(acoustIdKey)
            ? null
            : new AcoustIdClient(
                new HttpClient { BaseAddress = new Uri("https://api.acoustid.org/") },
                acoustIdKey);
        if (acoustId is not null)
            services.AddSingleton(acoustId);

        services.AddSingleton<IMetadataProvider>(
            new OnlineMetadataProvider(bpmClient, acoustId));
    }

    // The marketing site (liveolator.zalmanim.com) serves a static version.json kept in step with each
    // build by scripts/publish-website-release.ps1. The same host distributes the installer, so it is the
    // canonical, always-in-sync source for the startup "newer version available" check.
    private const string UpdateManifestUrl = "https://liveolator.zalmanim.com/version.json";

    // Wires the startup update check (doc 12): an HTTP manifest source + the running-version provider feed
    // the pure decision, and the App-side prompt/url-opener act on the user's choice. All best-effort and
    // headless-safe — the checker is only resolved by the real desktop lifetime (App.OnFramework...), and
    // every leg degrades to "no update" on failure (global standards #16/#26).
    private static void WireUpdateCheck(IServiceCollection services, ILoggerFactory loggerFactory)
    {
        services.AddSingleton<IUpdateManifestSource>(_ => new HttpUpdateManifestSource(
            new HttpClient(), UpdateManifestUrl, loggerFactory.CreateLogger<HttpUpdateManifestSource>()));
        services.AddSingleton<IInstalledVersionProvider, AssemblyInstalledVersionProvider>();
        services.AddSingleton<IUrlOpener>(_ => new SystemUrlOpener(loggerFactory.CreateLogger<SystemUrlOpener>()));
        services.AddSingleton<IUpdatePrompt, AvaloniaUpdatePrompt>();
        services.AddSingleton<StartupUpdateChecker>();
    }

    // --- Live set restore + autosave (doc 09/13) --------------------------------------------------
    // Loads the saved set into the queue at startup (Now first, then the upcoming order) and then keeps
    // the on-disk snapshot in step with every edit via the queue's Changed event. The save is fire-and-
    // forget so a UI edit is never blocked on disk; a write fault is logged, never swallowed silently
    // (global standards #16/#26). Subscribing AFTER the restore avoids re-saving the freshly loaded set.
    private static void RestoreAndPersistLiveSet(ILivePlaylist livePlaylist, ILiveSetStore store)
    {
        try
        {
            IReadOnlyList<string>? savedSet = store.LoadAsync().GetAwaiter().GetResult();
            if (savedSet is { Count: > 0 })
                livePlaylist.Load(savedSet);
        }
        catch (Exception ex)
        {
            // The store load is itself tolerant; this guards only against an unexpected fault so a bad
            // snapshot can never block startup (global standards #16/#26).
            System.Diagnostics.Trace.TraceWarning($"Could not restore the live set: {ex.Message}.");
        }

        livePlaylist.Changed += (_, _) =>
        {
            var paths = new List<string>();
            if (livePlaylist.Now is { } now)
                paths.Add(now.TrackPath);
            foreach (QueueEntry entry in livePlaylist.Upcoming)
                paths.Add(entry.TrackPath);

            _ = store.SaveAsync(paths).ContinueWith(
                task => System.Diagnostics.Trace.TraceWarning(
                    $"Live set could not be saved: {task.Exception?.GetBaseException().Message}"),
                System.Threading.CancellationToken.None,
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
                System.Threading.Tasks.TaskScheduler.Default);
        };
    }

    // --- Visual engine (doc 08, task 5) -----------------------------------------------------------
    // Registers the GL compositor as IVisualPerformanceEngine and returns its VisualActionHandler so
    // WireLiveAudio can add it to the one dispatcher.
    //
    // HEADLESS-SAFE: the GL engine opens a window/GL context only inside Run(); we register it for
    // resolution but NEVER call Run() here, so the app launches headless. Launching the render window
    // is a deferred user action — see the RENDER-WINDOW SEAM note below.
    //
    // The engine reads the live clock chosen by LiveClockSelector (audio-driven master clock when
    // realtime audio is up, else the shared manual tap clock), so once the render window runs it pulses
    // on the actual music — or on the Live tab taps when headless. A persisted visual bank (doc 13) is
    // loaded at startup and feeds the engine when present; otherwise the placeholder starter bank is used
    // (tolerant — a missing/corrupt snapshot degrades to the starter bank with a warning).
    private static (VisualActionHandler Handler, GlVisualPerformanceEngine Engine) WireVisuals(
        IServiceCollection services,
        IBeatClock liveClock,
        ILiveProfileStore profileStore,
        IVisualEffectRegistry effectRegistry,
        IGeneratorPresetRegistry presetRegistry,
        IVisualAudioLevelSource audioLevel,
        IBeatScheduler beatScheduler,
        ILoggerFactory loggerFactory)
    {
        var brightnessMacro = new VisualMacro(
            GlVisualPerformanceEngine.BrightnessMacro,
            min: 0.0, max: 1.0, @default: 1.0,
            target: new MacroTarget(Layer: 0, Parameter: GlVisualPerformanceEngine.BrightnessMacro));

        IReadOnlyList<VisualBank> banks = LoadBanksOrStarter(profileStore);
        IReadOnlyList<VisualMacro> macros;
        try
        {
            macros = profileStore.LoadVisualMacrosAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not restore visual macros: {ex.Message}.");
            macros = Array.Empty<VisualMacro>();
        }
        var visualEngine = new GlVisualPerformanceEngine(
            banks,
            brightnessMacro,
            liveClock,
            logger: loggerFactory.CreateLogger<GlVisualPerformanceEngine>(),
            effectRegistry: effectRegistry,
            macros: macros,
            audioLevel: audioLevel,
            loggerFactory: loggerFactory);
        var visualHandler = new VisualActionHandler(
            visualEngine, loggerFactory.CreateLogger<VisualActionHandler>(),
            presets: presetRegistry, effects: effectRegistry, scheduler: beatScheduler);

        services.AddSingleton<IVisualPerformanceEngine>(visualEngine);
        services.AddSingleton(visualHandler);

        // RENDER-WINDOW SEAM: the GL render loop blocks and needs a display, so it runs on a dedicated
        // background thread, launched on demand from the Live tab's "Show Visuals" command — never
        // during composition (that would crash headless/CI). The engine reads the shared clock, so the
        // window pulses on the same beat the Live tab taps.
        services.AddSingleton<IVisualStage>(
            new VisualStage(
                visible => visualEngine.Run("Liveolator Visuals", visible: visible),
                () => visualEngine.RequestPresent(),
                loggerFactory.CreateLogger<VisualStage>(),
                stop: () => visualEngine.RequestStop()));

        return (visualHandler, visualEngine);
    }

    // The compositor's first slice needs a renderable image layer. Generate a placeholder image and
    // wrap it in a one-scene bank; on any failure fall back to an empty bank (Show Visuals then logs
    // and no-ops rather than crashing startup). A real scene catalog from persistence (doc 13) replaces this.
    private static VisualBank BuildStarterBank()
    {
        try
        {
            string imagePath = StarterImage.EnsureCreated();
            var background = new VisualLayer(
                name: "Starter",
                source: new VisualSourceRef(VisualSourceKind.Image, imagePath),
                effects: Array.Empty<EffectRef>(),
                blend: BlendMode.Normal,
                opacity: 1.0);
            // The built-in VU meter (doc 26 reference) is a SINGLE self-contained generator: it samples its
            // dial face (the built-in VuMeterFace, or a custom image set from the Add-ons tab) as the
            // background and draws the live needle over it. One opaque layer fills the frame and reacts to
            // the master level; if the generator fails the renderer skips it.
            var vuMeter = new VisualLayer(
                name: "VU Meter",
                source: new VisualSourceRef(VisualSourceKind.Generator, VuMeterAddon.EffectId),
                effects: Array.Empty<EffectRef>(),
                blend: BlendMode.Normal,
                opacity: 1.0);
            // The fractal generator is an alternate visual kept on top at zero opacity, so its own Visual
            // Control toggle can bring it over the VU meter without disturbing the meter.
            var psyFractal = new VisualLayer(
                name: "Psy Fractal Visualizer",
                source: new VisualSourceRef(VisualSourceKind.Generator, PsyFractalVisualizerAddon.EffectId),
                effects: Array.Empty<EffectRef>(),
                blend: BlendMode.Normal,
                opacity: 0.0);
            var scene = new VisualScene(
                name: "Starter",
                layers: new[] { background, vuMeter, psyFractal },
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

    /// <summary>The well-known name of the user's authored startup scene bank under <c>live/scenes/</c>.</summary>
    private const string StartupVisualBankName = "Live";

    // Loads every authored visual bank from persistence (doc 13/22 C3) so the Scene Grid can switch
    // banks at runtime (VisualSelectBank → real bank data). The startup bank ("Live") is placed first
    // so it is the active bank on launch (preserving prior single-bank behaviour); any other saved
    // banks follow in name order. When nothing is saved, the engine ships the single placeholder starter
    // bank. Tolerant: a missing/corrupt/old snapshot is skipped (the store already warned), never fatal —
    // blocking on these small JSON files in the composition root mirrors the settings/macros load above.
    private static IReadOnlyList<VisualBank> LoadBanksOrStarter(ILiveProfileStore profileStore)
    {
        var banks = new List<VisualBank>();
        try
        {
            IReadOnlyList<string> names = profileStore.ListVisualBankNamesAsync().GetAwaiter().GetResult();
            // Startup bank first (active on launch), then the rest, de-duplicated and skipping empties.
            foreach (string name in names
                         .OrderByDescending(n => string.Equals(n, StartupVisualBankName, StringComparison.OrdinalIgnoreCase))
                         .ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                VisualBank? bank = profileStore.LoadVisualBankAsync(name).GetAwaiter().GetResult();
                if (bank is not null && bank.Scenes.Count > 0)
                    banks.Add(bank);
            }
        }
        catch (Exception ex)
        {
            // The store loads are themselves tolerant; this guards only against an unexpected fault so a
            // bad snapshot can never block startup (global standards #16/#26).
            System.Diagnostics.Trace.TraceWarning($"Could not enumerate saved visual banks: {ex.Message}.");
        }

        // Always have at least one bank so the engine + Scene Grid have something to address.
        if (banks.Count == 0)
            banks.Add(BuildStarterBank());

        return banks;
    }

    // --- Live tab: tap-tempo performance surface, demonstrable with NO audio hardware (docs 03/04/12) ---
    // Drives the SHARED ManualBeatClock and routes every control through the SINGLE dispatcher composed
    // in Build(), so the Live UI reaches the beat, mixer and visual handlers (and deck transport when
    // the realtime engine is up) — never a direct engine call (doc 04). The clock is shared on purpose:
    // tap/lock/nudge advance the same clock the visual engine reads.
    private static void WireLiveTab(
        IServiceCollection services, ManualBeatClock clock, SystemHostClock hostClock)
    {
        services.AddSingleton<LiveViewModel>(sp =>
        {
            IVisualPerformanceEngine? visualEngine = sp.GetService<IVisualPerformanceEngine>();
            // Resolve the built-in VU-meter layer so the Visual Control toggle addresses the right slot
            // (and hides itself when the startup scene has no built-in meter).
            (int Slot, bool Shown)? vuMeter = FindVuMeterLayer(visualEngine);
            return new LiveViewModel(
                sp.GetRequiredService<IPerformanceActionDispatcher>(),
                clock, clock, hostClock, new DispatcherLiveBeatTimer(),
                sp.GetService<IVisualStage>(),
                sp.GetService<IWaveformProvider>(),
                sp.GetRequiredService<PerformanceDeckSet>(),
                // Real bank names from the engine so the Scene Grid's bank tabs map to actual banks (doc 22 C3).
                visualEngine?.BankNames,
                sp.GetService<IVisualEffectRegistry>(),
                sp.GetService<IExtensionCatalog>(),
                sp.GetService<IExtensionInstaller>(),
                sp.GetService<IExtensionContentReloader>(),
                vuMeter?.Slot,
                vuMeter?.Shown ?? true,
                visualEngine,
                sp.GetService<ILivePlaylist>(),
                sp.GetService<ITrackVisualProgramStore>(),
                sp.GetService<IGeneratorPresetRegistry>(),
                sp.GetService<IVisualPresetReloader>());
        });
    }

    // Locates the built-in VU-meter generator in the engine's startup scene (the active bank's first
    // scene, which the engine renders on launch). Returns its layer index plus whether it ships visible,
    // or null when the scene has no VU-meter layer. The engine's ToggleLayer addresses the active scene
    // by index, so this index is valid for the startup scene.
    private static (int Slot, bool Shown)? FindVuMeterLayer(IVisualPerformanceEngine? engine)
    {
        VisualScene? scene = engine?.ActiveBank.Scene(0);
        if (scene is null)
            return null;

        for (int i = 0; i < scene.Layers.Count; i++)
        {
            VisualLayer layer = scene.Layers[i];
            if (layer.Source.Kind == VisualSourceKind.Generator
                && string.Equals(layer.Source.Reference, VuMeterAddon.EffectId, StringComparison.Ordinal))
                return (i, layer.Opacity > 0.0);
        }
        return null;
    }

    // --- Live playlist audio binding (doc 09) -----------------------------------------------------
    // Binds the pure ILivePlaylist queue to the realtime engine: PlaylistAudioPlayer drives the deck on
    // each NowChanged (load + play), so editing the future never disturbs Now (the doc 09 invariant lives
    // in the pure queue; this side only reacts to NowChanged). Wired ONLY when the realtime two-deck
    // engine is up — headless (no native BASS) there is no deck to drive, so the app stays a catalog
    // browser and the queue still edits freely. Registered as a singleton so the binding outlives Build()
    // and keeps reacting for the app's lifetime.
    //
    // PRELOAD SEAM: NextTrackPreloader is wired only when an IDeckPreloader is registered. The native
    // pre-buffering implementation (opening the upcoming BASS stream ahead, verified manually) is the
    // remaining deferred piece; the pure preloader sequencing is built + unit-tested in Liveolator.Audio.
    private static void WirePlaylistAudio(
        IServiceCollection services,
        ILivePlaylist livePlaylist,
        ILivePlaylist deckBPlaylist,
        IPerformanceActionDispatcher dispatcher,
        IMultiDeckPlaybackEngine? deckEngine)
    {
        if (deckEngine is null)
            return; // catalog-browser mode: no deck to bind the queue to.

        // Deck A (slot 0) hosts the auto-advancing live queue. A restored Now track loads paused at
        // startup; later skips and end-of-track advances still auto-play immediately.
        services.AddSingleton(sp => new PlaylistAudioPlayer(
            livePlaylist,
            dispatcher,
            deckEngine,
            // Exact-then-file-name match: a deck-queue / mapped-drive path can differ from the scanned
            // catalog path, so an exact lookup misses and the engine would get no BPM (silently breaking
            // SYNC). The file-name fallback gives the engine the same BPM the deck UI already shows.
            path => sp.GetRequiredService<MusicLibrary>().TryGetByPathOrName(path)?.Bpm,
            slot: 0,
            autoPlay: true,
            autoPlayExistingNow: false));

        // Deck B (slot 1) hosts its own queue, fed by load-while-playing appends (DeckTrackLoader):
        // when deck B's track ends, the next queued track loads + plays on B automatically.
        services.AddSingleton(sp => new PlaylistAudioPlayer(
            deckBPlaylist,
            dispatcher,
            deckEngine,
            // Exact-then-file-name match: a deck-queue / mapped-drive path can differ from the scanned
            // catalog path, so an exact lookup misses and the engine would get no BPM (silently breaking
            // SYNC). The file-name fallback gives the engine the same BPM the deck UI already shows.
            path => sp.GetRequiredService<MusicLibrary>().TryGetByPathOrName(path)?.Bpm,
            slot: 1,
            autoPlay: true,
            autoPlayExistingNow: false));
    }

    // --- Capture sources: system loopback + sound-card/line input (doc 01 Phase 1b, task 8) ---
    // Registers the BASS capture engine as both the device catalog and the source factory. A single
    // engine instance backs both seams. Native bass is not required to construct the engine
    // (enumeration/creation only touch native on demand and degrade to "no devices" if it is absent),
    // so this never disables app startup.
    //
    // SETTINGS-UI SEAM: the Settings tab now consumes these — it lists EnumerateCaptureDevices(), lets the
    // user pick an AudioCaptureDevice, persists the choice in AudioSettings, and applies it through the
    // Core ICaptureSourceController (CaptureSourceController calls CreateCaptureSource(device) and routes the
    // source into a stable SwitchableAudioSource). Remaining: feeding that switch into the analysis/beat
    // pipeline and modelling source selection as a PerformanceAction (no live capture consumer exists yet).
    private static void WireCaptureSources(
        IServiceCollection services,
        IAudioCaptureDeviceCatalog? catalogOverride,
        IAudioCaptureSourceFactory? factoryOverride)
    {
        if (catalogOverride is not null || factoryOverride is not null)
        {
            services.AddSingleton(
                catalogOverride ?? throw new ArgumentException(
                    "A capture catalog override requires a matching source-factory override."));
            services.AddSingleton(
                factoryOverride ?? throw new ArgumentException(
                    "A capture source-factory override requires a matching catalog override."));
            return;
        }

        var engine = new BassCaptureEngine();
        services.AddSingleton<IAudioCaptureDeviceCatalog>(engine);
        services.AddSingleton<IAudioCaptureSourceFactory>(engine);
    }

    // --- MIDI default-profile pipeline (doc 05/07) ------------------------------------------------
    // Builds a MidiInputPipeline that routes the SETTINGS-chosen controller through MidiControllerRouter
    // → ControllerMapper → the one dispatcher, with MidiProfileSelector auto-selecting a profile by
    // device name (CmdStudio2AProfile.Default today) and MidiFeedbackPublisher driving LEDs.
    //
    // The running app opens the controller through MidiControlSession (see Build) — which also exposes
    // IMidiControlStatus for the shell. This default-profile path is retained (and unit-tested) for the
    // controller-profile-capture increment (doc 22 step A8), where the captured CMD STUDIO 2A profile
    // becomes the session's default instead of an empty profile.
    //
    // DEGRADES GRACEFULLY (global standards #16/#26): no controller chosen, no matching device, or a
    // native rtmidi/open failure all log + return null — never throw at startup.
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
                input, output, dispatcher, AvailableMidiProfiles(),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
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
    // (DJ), Pioneer DDJ-FLX4 (DJ), and Ableton Push 1 (visuals) today; persisted/custom profiles (doc 13)
    // extend this set later. The generic template is appended LAST and intentionally has an empty
    // DeviceHint, so MidiProfileSelector always prefers an exact device match and the generic never wins
    // auto-selection — it is a learn-from-scratch label any unrecognized controller falls back to.
    // Internal so the catalog's membership (e.g. that Push 1 is wired in, not orphaned) is unit-testable.
    internal static IReadOnlyList<ControllerMappingProfile> AvailableMidiProfiles()
        => new[]
        {
            CmdStudio2AProfile.Default,
            DdjFlx4Profile.Default,
            Push1Profile.Default,
            GenericControllerProfile.Default,
        };

    internal static MidiSettings ResolveMidiSettings(
        MidiSettings configured,
        IMidiDeviceProvider provider)
    {
        MidiSettings normalized = configured.Normalized();
        if (!string.IsNullOrWhiteSpace(normalized.ControllerInputName))
            return normalized;

        // No controller chosen yet: auto-detect the first connected device whose name matches any
        // catalogued profile's hint (CMD STUDIO 2A, DDJ-FLX4, …). MidiProfileSelector then loads the
        // matching profile downstream — so adding a profile to the catalog extends detection for free.
        // Enumeration is wrapped so a native rtmidi failure degrades to "no device" instead of crashing
        // the composition root (global standards #16/#26) — matching the best-effort MIDI wiring below.
        string[] inputNames;
        try
        {
            inputNames = provider.GetInputDeviceNames().ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not enumerate MIDI input devices: {ex.Message}. Running without auto-detection.");
            return normalized;
        }

        string? detected = AvailableMidiProfiles()
            .Select(profile => inputNames.FirstOrDefault(name =>
                !string.IsNullOrEmpty(profile.DeviceHint)
                && name.Contains(profile.DeviceHint, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(name => name is not null);

        // No KNOWN-hint device matched: fall back to the FIRST connected input so plugging in ANY
        // arbitrary controller makes it active and ready to learn from scratch on next start (the generic
        // template profile loads downstream because no device hint matches). Conservative: only kicks in
        // when the user has chosen nothing AND there is exactly something plugged in; zero inputs keeps the
        // prior behaviour (stays null / normalized).
        detected ??= inputNames.FirstOrDefault();

        return detected is null
            ? normalized
            : normalized with { ControllerInputName = detected };
    }
}
