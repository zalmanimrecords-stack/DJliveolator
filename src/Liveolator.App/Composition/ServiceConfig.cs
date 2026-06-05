using Liveolator.App.Features.Libraries;
using Liveolator.App.Shell;
using Liveolator.Audio;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Platform;
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

        // --- Live Mode: realtime playback + beat clock (docs 01/02/03/04) ---
        // Best-effort: the BASS backend needs the native bass library at runtime. If it is
        // absent (e.g. a dev box without the per-platform binaries), Live Mode stays off and the
        // app still runs as a catalog browser — the UI hides the transport controls.
        WireLiveAudio(services);

        // --- View-models ---
        services.AddSingleton<LibrariesViewModel>(sp => new LibrariesViewModel(
            sp.GetRequiredService<MusicLibrary>(),
            sp.GetService<IPerformanceActionDispatcher>(),
            sp.GetService<IBeatClock>()));
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static void WireLiveAudio(IServiceCollection services)
    {
        try
        {
            var engine = new LivePlaybackEngine(new BassAudioEngine(), new SystemHostClock());
            var dispatcher = new PerformanceActionDispatcher(
                new IPerformanceActionHandler[] { new DeckActionHandler(engine) },
                NullLogger<PerformanceActionDispatcher>.Instance);

            services.AddSingleton<IAudioPlaybackEngine>(engine);
            services.AddSingleton<IBeatClock>(engine.BeatClock);
            services.AddSingleton<IPerformanceActionDispatcher>(dispatcher);
        }
        catch (Exception ex) when (ex is BassPlaybackException or DllNotFoundException)
        {
            // Native audio unavailable — leave Live Mode services unregistered; GetService returns null.
            System.Diagnostics.Trace.TraceWarning($"Live Mode disabled: realtime audio unavailable ({ex.Message}).");
        }
    }
}
