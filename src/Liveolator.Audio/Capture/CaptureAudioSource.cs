using Liveolator.Audio.Playback;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Capture;

/// <summary>
/// Realtime <see cref="IAudioSource"/> backed by a BASS capture (doc 01): system-loopback or a
/// hardware line-input, selected by the <see cref="AudioCaptureDevice"/> passed in. Emits the
/// captured samples through <see cref="AudioSamplesAvailable"/> exactly like <c>DeckAudioSource</c>,
/// so it plugs straight into the existing frame pipeline (doc 02) and beat clock (doc 03). The
/// native BASS calls go through <see cref="ICaptureBackend"/>, so this state machine unit-tests
/// without native BASS. Construct via <see cref="BassCaptureEngine.CreateCaptureSource"/>.
/// </summary>
public sealed class CaptureAudioSource : IAudioSource
{
    private readonly ICaptureBackend _backend;
    private readonly AudioCaptureDevice _device;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private int _channels;
    private int _sampleRate;
    private bool _running;
    private bool _disposed;

    internal CaptureAudioSource(ICaptureBackend backend, AudioCaptureDevice device, ILogger logger)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => _device.Name;

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CaptureAudioSource));
            if (_running) return;

            try
            {
                // Unlike a deck (a reusable file stream), a capture session is opened fresh each
                // Start so a device reselected/hot-plugged between runs is picked up.
                BassChannelInfo info = _backend.Start(_device, OnSamples);
                _channels = info.Channels;
                _sampleRate = info.SampleRate;
                _running = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start capture source '{Device}' ({Kind}).", _device.Name, _device.Kind);
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            try
            {
                _backend.Stop();
            }
            catch (Exception ex)
            {
                // Stopping must not throw into a caller tearing down a source; log and move on.
                _logger.LogWarning(ex, "Error stopping capture source '{Device}'.", _device.Name);
            }
            _running = false;
        }
    }

    // Runs on the BASS capture thread; _channels/_sampleRate are set before the backend reports running.
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
            _backend.Dispose();
        }
    }
}
