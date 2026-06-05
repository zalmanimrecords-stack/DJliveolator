using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Public entry point to the BASS realtime backend (doc 01). Owns the initialised output device
/// and hands out <see cref="IAudioSource"/> decks for files. The App composes one engine and
/// disposes it on shutdown; the underlying BASS device is freed then.
/// </summary>
public sealed class BassAudioEngine : IDisposable
{
    private readonly BassPlayback _bass;
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    public BassAudioEngine(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _bass = new BassPlayback(_loggerFactory.CreateLogger<BassPlayback>());
    }

    /// <summary>Create a deck audio source for a file. The deck does not start until <c>Start()</c>.</summary>
    public IAudioSource CreateDeck(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath must be a non-empty path.", nameof(filePath));
        return new DeckAudioSource(_bass, filePath, _loggerFactory.CreateLogger<DeckAudioSource>());
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _bass.Dispose();
    }
}
