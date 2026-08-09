using Liveolator.Audio;
using Liveolator.Audio.Render;
using Liveolator.Core.Analysis;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Liveolator.Media;
using Liveolator.Media.Import;
using Liveolator.Media.Import.Engine;
using Liveolator.Media.Import.Mixxx;
using Liveolator.Media.Import.Serato;
using Liveolator.Mcp.Session;
using Liveolator.Online;
using Liveolator.Visuals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Liveolator.Mcp;

/// <summary>Registers the music-intelligence services shared by both transports (stdio and HTTP).</summary>
internal static class ServiceRegistration
{
    public static IServiceCollection AddLiveolatorMusicServices(this IServiceCollection services, ServerConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(_ => new FfmpegOptions(config.FfmpegPath));
        services.AddSingleton<IAudioDecoder>(sp => new CompositeAudioDecoder(sp.GetRequiredService<FfmpegOptions>()));
        services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();
        services.AddSingleton<IFileEnumerator, FileSystemFileEnumerator>();
        services.AddSingleton(new TrackAnalyzer());
        // Per-row SQLite catalog store (doc 31 M1) — the SAME database the app writes, so an agent's scans
        // and the app share one catalog without clobbering each other's rows. A one-time migration carries
        // a legacy JSON catalog over. One store serves both the music and visual catalog seams.
        string dataDirectory = config.DataDirectory ?? JsonCatalogStore.DefaultRoot();
        CatalogMigration.JsonToSqliteIfNeeded(
            dataDirectory, msg => System.Diagnostics.Trace.TraceWarning(msg));
        services.AddSingleton(sp => new SqliteCatalogStore(
            dataDirectory,
            onWarning: msg => sp.GetRequiredService<ILogger<SqliteCatalogStore>>().LogWarning("{Warning}", msg)));
        services.AddSingleton<IMusicCatalogStore>(sp => sp.GetRequiredService<SqliteCatalogStore>());
        services.AddSingleton<IVisualCatalogStore>(sp => sp.GetRequiredService<SqliteCatalogStore>());
        services.AddSingleton<PlaylistWriter>();

        // DJ-library import (doc: import) for agents: file-based (Rekordbox/Traktor/VirtualDJ) + folder-based
        // (Serato/Mixxx) parsers feeding one mapping service. Imported cues/playlists persist via the same
        // JSON stores under the data directory — no new on-disk format.
        services.AddSingleton<IHotCueStore>(sp => new JsonHotCueStore(
            config.DataDirectory,
            onWarning: msg => sp.GetRequiredService<ILogger<JsonHotCueStore>>().LogWarning("{Warning}", msg)));
        services.AddSingleton<IPlaylistStore>(sp => new JsonPlaylistStore(
            config.DataDirectory,
            onWarning: msg => sp.GetRequiredService<ILogger<JsonPlaylistStore>>().LogWarning("{Warning}", msg)));
        services.AddSingleton<ILibraryImporter, RekordboxXmlImporter>();
        services.AddSingleton<ILibraryImporter, TraktorNmlImporter>();
        services.AddSingleton<ILibraryImporter, VirtualDjXmlImporter>();
        services.AddSingleton<IFolderLibraryImporter, SeratoLibraryImporter>();
        services.AddSingleton<IFolderLibraryImporter, MixxxLibraryImporter>();
        services.AddSingleton<IFolderLibraryImporter, EngineLibraryImporter>();
        services.AddSingleton(sp => new LibraryImportService(
            sp.GetRequiredService<IHotCueStore>(), sp.GetRequiredService<IPlaylistStore>(),
            path => ImportFileProbe.Stat(
                path, msg => sp.GetRequiredService<ILogger<LibraryImportService>>().LogWarning("{Warning}", msg))));

        services.AddSingleton<LibrarySession>();

        // DJ set building: the arranger is pure Core, so all this layer owns is where the arrangement is
        // stored (the same live/studio-projects folder the app's STUDIO tab lists) and the offline
        // renderer used to audition the transitions.
        services.AddSingleton<IStudioProjectStore>(sp => new JsonStudioProjectStore(
            config.DataDirectory,
            onWarning: msg => sp.GetRequiredService<ILogger<JsonStudioProjectStore>>().LogWarning("{Warning}", msg)));
        services.AddSingleton(sp => new OfflineMixRenderer(
            sp.GetRequiredService<IAudioDecoder>(), sp.GetRequiredService<ILogger<OfflineMixRenderer>>()));
        services.AddSingleton<DjSetSession>();

        // Visual-media catalog (doc 17 Phase 3): image dimensions are pure-managed; video duration
        // uses ffprobe, which resolves itself via LIVEOLATOR_FFPROBE_PATH/PATH (its own executable,
        // distinct from ffmpeg) — so we let it default rather than forcing the ffmpeg path on it.
        services.AddSingleton<IVisualMediaProbe>(_ => new CompositeVisualMediaProbe());
        services.AddSingleton<VisualSession>();

        // FRKTL preset authoring (doc 29): lets an agent generate + save .frktl visual presets into the
        // shared FRKTL presets folder, so the app picks them up. Writer/validation live in Media/Core.
        services.AddSingleton<VisualPresetSession>();

        // Control-skin authoring (doc 30): lets an agent design parametric knob/slider looks and save them
        // as .ctrlskin into the shared control-skins folder. Writer/validation live in Media/Core.
        services.AddSingleton<ControlSkinSession>();

        AddOnlineEnrichment(services, config);
        return services;
    }

    // Online BPM/key enrichment (doc 16). Opt-in: only active when a GetSongBPM key is supplied
    // (--getsongbpm-key / LIVEOLATOR_GETSONGBPM_KEY). Without it, a disabled provider is registered so
    // the lookup tool resolves but reports "not configured" rather than failing to compose. AcoustID
    // (fingerprint) and fpcalc are layered in only when their key/binary are available — otherwise the
    // lookup matches by tags. All HTTP/CLI failures resolve to null (offline-first).
    private static void AddOnlineEnrichment(IServiceCollection services, ServerConfig config)
    {
        // Always available — fpcalc degrades to null when the binary is absent, so the tool can take it
        // regardless of whether enrichment is configured.
        services.AddSingleton<IAudioFingerprinter>(sp =>
            new FpcalcFingerprinter(config.FpcalcPath, sp.GetRequiredService<ILogger<FpcalcFingerprinter>>()));

        if (string.IsNullOrWhiteSpace(config.GetSongBpmKey))
        {
            services.AddSingleton<IMetadataProvider, DisabledMetadataProvider>();
            return;
        }

        services.AddSingleton<IGetSongBpmClient>(sp => new GetSongBpmClient(
            new HttpClient { BaseAddress = new Uri("https://api.getsong.co/") },
            config.GetSongBpmKey!,
            sp.GetRequiredService<ILogger<GetSongBpmClient>>()));

        if (!string.IsNullOrWhiteSpace(config.AcoustIdKey))
        {
            services.AddSingleton<IAcoustIdClient>(sp => new AcoustIdClient(
                new HttpClient { BaseAddress = new Uri("https://api.acoustid.org/") },
                config.AcoustIdKey!,
                sp.GetRequiredService<ILogger<AcoustIdClient>>()));
        }

        services.AddSingleton<IMetadataProvider>(sp => new OnlineMetadataProvider(
            sp.GetRequiredService<IGetSongBpmClient>(),
            sp.GetService<IAcoustIdClient>(),
            sp.GetRequiredService<ILogger<OnlineMetadataProvider>>()));
    }
}

/// <summary>
/// Stand-in <see cref="IMetadataProvider"/> when no GetSongBPM key is configured: always returns
/// <c>null</c>, so the <c>lookup_track_online</c> tool composes and reports "not configured" cleanly.
/// </summary>
internal sealed class DisabledMetadataProvider : IMetadataProvider
{
    public Task<OnlineTrackMetadata?> LookupAsync(TrackLookupQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<OnlineTrackMetadata?>(null);
}
