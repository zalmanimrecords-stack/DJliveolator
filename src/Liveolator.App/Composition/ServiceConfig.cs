using Liveolator.App.Features.Libraries;
using Liveolator.App.Shell;
using Liveolator.Audio;
using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Platform;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<TrackAnalyzer>();
        services.AddSingleton<MusicLibrary>();

        // --- View-models ---
        services.AddSingleton<LibrariesViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
