using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Audio;

/// <summary>
/// Composes the realtime audio chain (doc 01/02/03): a stable <see cref="SwitchableAudioSource"/>
/// feeds one <see cref="AudioFramePipeline"/> feeding one <see cref="AudioBeatClock"/>, while the
/// underlying deck is swapped per track via an <see cref="IDeckSourceFactory"/>. Implements the
/// <see cref="IAudioPlaybackEngine"/> seam the action layer drives, and exposes the live
/// <see cref="BeatClock"/> for the UI/visuals to observe. Pure managed — no native — so it
/// unit-tests with a fake deck factory.
/// </summary>
public sealed class LivePlaybackEngine : IAudioPlaybackEngine, IDisposable
{
    private readonly IDeckSourceFactory _factory;
    private readonly SwitchableAudioSource _switch = new();
    private readonly AudioFramePipeline _pipeline;
    private readonly AudioBeatClock _beatClock;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private IAudioSource? _deck;
    private bool _disposed;

    public LivePlaybackEngine(
        IDeckSourceFactory factory,
        IHostClock hostClock,
        SpectrumAnalyzer? analyzer = null,
        int hop = 512,
        ILoggerFactory? loggerFactory = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        ArgumentNullException.ThrowIfNull(hostClock);

        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<LivePlaybackEngine>();
        _pipeline = new AudioFramePipeline(
            _switch, analyzer ?? new SpectrumAnalyzer(), hop, loggerFactory.CreateLogger<AudioFramePipeline>());
        _beatClock = new AudioBeatClock(
            _pipeline, hostClock, logger: loggerFactory.CreateLogger<AudioBeatClock>());
    }

    /// <summary>The live beat clock fed by the playing deck; stable for the engine's lifetime.</summary>
    public IBeatClock BeatClock => _beatClock;

    public bool IsPlaying
    {
        get { lock (_gate) return _deck?.IsRunning ?? false; }
    }

    public void Load(string trackPath)
    {
        if (string.IsNullOrWhiteSpace(trackPath))
            throw new ArgumentException("trackPath must be a non-empty path.", nameof(trackPath));

        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LivePlaybackEngine));

            try
            {
                IAudioSource newDeck = _factory.CreateDeck(trackPath);
                _switch.SetSource(newDeck);
                _deck?.Dispose();
                _deck = newDeck;
                _logger.LogInformation("Loaded deck track {Track}", trackPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load deck track {Track}", trackPath);
                throw;
            }
        }
    }

    public void PlayPause()
    {
        lock (_gate)
        {
            if (_deck is null)
            {
                _logger.LogWarning("PlayPause requested with no track loaded; ignoring.");
                return;
            }
            if (_deck.IsRunning)
                _deck.Stop();
            else
                _deck.Start();
        }
    }

    public void Stop()
    {
        lock (_gate) _deck?.Stop();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _beatClock.Dispose();
            _pipeline.Dispose();
            _switch.Dispose();
            _deck?.Dispose();
        }
    }
}
