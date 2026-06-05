using Liveolator.Audio;
using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using Liveolator.Media;
using Liveolator.Mcp.Session;
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
        return services;
    }
}
