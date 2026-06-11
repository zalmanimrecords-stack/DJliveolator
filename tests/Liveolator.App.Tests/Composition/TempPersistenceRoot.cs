using Liveolator.App.Composition;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace Liveolator.App.Tests.Composition;

/// <summary>
/// A disposable temp directory passed to <see cref="ServiceConfig.Build"/> as the persistence root, so
/// composition-root tests never read or write the real per-user %APPDATA%/Liveolator data. Building
/// without it once leaked fake test tracks into the user's live set (loaded on every app launch).
/// </summary>
internal sealed class TempPersistenceRoot : IDisposable
{
    public TempPersistenceRoot()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "liveolator-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public ServiceProvider Build(
        IMidiDeviceProvider? midiProvider = null,
        IAudioOutputDeviceCatalog? outputCatalog = null,
        IAudioCaptureDeviceCatalog? captureCatalog = null,
        IAudioCaptureSourceFactory? captureFactory = null)
        => (ServiceProvider)ServiceConfig.Build(
            midiProvider, outputCatalog, captureCatalog, captureFactory,
            enableSystemMetrics: false,
            persistenceRootDirectory: Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A log file can still be open while the provider tears down; the OS temp cleaner gets it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
