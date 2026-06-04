using Liveolator.App.Features.Libraries;
using Liveolator.App.Services;
using Liveolator.App.Shell;
using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
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
        services.AddSingleton<IFileEnumerator, FileSystemEnumerator>();
        services.AddSingleton<IAudioDecoder, WavAudioDecoder>(); // WAV today; FFmpeg behind the same seam later
        services.AddSingleton<TrackAnalyzer>();
        services.AddSingleton<MusicLibrary>();

        // --- View-models ---
        services.AddSingleton<LibrariesViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
