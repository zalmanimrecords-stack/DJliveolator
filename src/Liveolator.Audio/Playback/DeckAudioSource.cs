using Liveolator.Core;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Realtime <see cref="IAudioSource"/> backed by a BASS file stream (doc 01): plays a track to the
/// output device and emits the played samples so the frame pipeline (doc 02) and beat engine see
/// exactly what is heard. The BASS calls go through <see cref="IBassPlayback"/>, so this state
/// machine unit-tests without native BASS. Construct via <see cref="BassAudioEngine.CreateDeck"/>.
/// </summary>
public sealed class DeckAudioSource : IAudioSource
{
    private readonly IBassPlayback _bass;
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private int _handle;
    private int _channels;
    private int _sampleRate;
    private bool _loaded;
    private bool _running;
    private bool _disposed;

    internal DeckAudioSource(IBassPlayback bass, string filePath, ILogger logger)
    {
        _bass = bass ?? throw new ArgumentNullException(nameof(bass));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => PortablePath.GetFileName(_filePath);

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeckAudioSource));
            if (_running) return;

            try
            {
                if (!_loaded)
                {
                    _handle = _bass.CreateFileStream(_filePath);
                    var info = _bass.GetChannelInfo(_handle);
                    _channels = info.Channels;
                    _sampleRate = info.SampleRate;
                    _bass.SetSampleTap(_handle, OnSamples);
                    _loaded = true;
                }

                _bass.Play(_handle);
                _running = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start deck audio source for {File}", _filePath);
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _bass.Pause(_handle);
            _running = false;
        }
    }

    // Runs on the BASS update thread; _channels/_sampleRate are set once before the tap is armed.
    private void OnSamples(float[] interleaved)
    {
        if (interleaved.Length == 0)
            return;
        SamplesAvailable?.Invoke(this, new AudioSamplesAvailable(interleaved, _channels, _sampleRate));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            if (_loaded)
            {
                _bass.Free(_handle);
                _loaded = false;
            }
        }
    }
}
