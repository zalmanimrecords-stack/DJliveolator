using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Audio;

/// <summary>
/// Default <see cref="ICaptureSourceController"/>: creates a capture source from the chosen device via
/// <see cref="IAudioCaptureSourceFactory"/>, starts it, and routes it into the live pipeline through a
/// stable <see cref="SwitchableAudioSource"/> (so the frame pipeline / beat clock stay subscribed across
/// a swap, mirroring how the deck source is switched — doc 01). Pure C#: the factory is a seam, so this
/// orchestration unit-tests with a fake. Owns the lifetime of the capture source it creates (disposes the
/// previous one when a new device is selected or when detaching).
/// </summary>
public sealed class CaptureSourceController : ICaptureSourceController
{
    private readonly IAudioCaptureSourceFactory _factory;
    private readonly SwitchableAudioSource _liveInput;
    private readonly ILogger<CaptureSourceController> _logger;
    private readonly object _gate = new();
    private IAudioSource? _current;

    /// <param name="factory">Creates capture sources for a chosen device (a fake in tests).</param>
    /// <param name="liveInput">
    /// The stable switch the live pipeline subscribes to; this controller swaps its inner source.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public CaptureSourceController(
        IAudioCaptureSourceFactory factory,
        SwitchableAudioSource liveInput,
        ILogger<CaptureSourceController>? logger = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _liveInput = liveInput ?? throw new ArgumentNullException(nameof(liveInput));
        _logger = logger ?? NullLogger<CaptureSourceController>.Instance;
    }

    public bool SelectCaptureSource(AudioCaptureDevice? device)
    {
        lock (_gate)
        {
            if (device is null)
            {
                DetachCurrent();
                _logger.LogInformation("Capture source detached.");
                return true;
            }

            IAudioSource source;
            try
            {
                source = _factory.CreateCaptureSource(device);
                source.Start();
            }
            catch (Exception ex)
            {
                // Leave the prior source running — a failed device change must not kill live audio.
                _logger.LogError(ex, "Failed to open capture source '{Device}'; keeping the current source.", device.Name);
                return false;
            }

            DetachCurrent();
            _current = source;
            _liveInput.SetSource(source);
            _logger.LogInformation("Capture source set to '{Device}' ({Kind}).", device.Name, device.Kind);
            return true;
        }
    }

    // Caller holds the gate. Detaches and disposes the current capture source, if any.
    private void DetachCurrent()
    {
        if (_current is null)
            return;
        _liveInput.SetSource(null);
        try
        {
            _current.Stop();
            _current.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing the previous capture source.");
        }
        _current = null;
    }
}
