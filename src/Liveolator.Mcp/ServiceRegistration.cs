using Liveolator.Audio;
using Liveolator.Core.Analysis;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using Liveolator.Media;
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
        services.AddSingleton<IFileEnumerator, FileSystemFileEnumerator>();
        services.AddSingleton(new TrackAnalyzer());
        services.AddSingleton(sp => new JsonCatalogStore(
            config.DataDirectory,
            onWarning: msg => sp.GetRequiredService<ILogger<JsonCatalogStore>>().LogWarning("{Warning}", msg)));
        services.AddSingleton<PlaylistWriter>();
        services.AddSingleton<LibrarySession>();

        // Visual-media catalog (doc 17 Phase 3): image dimensions are pure-managed; video duration
        // uses ffprobe, which resolves itself via LIVEOLATOR_FFPROBE_PATH/PATH (its own executable,
        // distinct from ffmpeg) — so we let it default rather than forcing the ffmpeg path on it.
        services.AddSingleton<IVisualMediaProbe>(_ => new CompositeVisualMediaProbe());
        services.AddSingleton<VisualSession>();

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
