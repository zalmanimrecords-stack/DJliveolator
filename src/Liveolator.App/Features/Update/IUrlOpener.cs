namespace Liveolator.App.Features.Update;

/// <summary>
/// Opens an external URL in the user's default browser. Abstracted so the update coordinator can be
/// unit-tested (asserting the download link was launched) without spawning a real process.
/// </summary>
public interface IUrlOpener
{
    /// <summary>Opens <paramref name="url"/> in the default browser. Failures are handled internally.</summary>
    void Open(string url);
}
