namespace Liveolator.Core.Audio;

/// <summary>
/// Drives an <see cref="AudioLevelEnvelope"/> from the shared analysis frames and publishes the result
/// as an <see cref="IVisualAudioLevelSource"/> for the visual compositor (doc 26). It subscribes to
/// <see cref="IAudioFrameProvider.FrameAvailable"/> — the same master-mix frames the beat clock reads —
/// so the metered level matches the audible signal the visuals lock to.
/// </summary>
/// <remarks>
/// Single-writer: frames arrive sequentially from one audio thread, so the envelope's internal state is
/// mutated by that thread alone. The latest snapshot is published into a <c>volatile</c> field as one
/// atomic reference swap, so the render thread reads a consistent <see cref="VisualAudioLevel"/> without
/// locking (the whole-record-swap pattern <see cref="Beat.BeatClockState"/> uses). Pure managed — no
/// native — so it unit-tests with a fake frame provider.
/// </remarks>
public sealed class FrameAudioLevelMeter : IVisualAudioLevelSource, IVisualAudioBandsSource, IDisposable
{
    private readonly IAudioFrameProvider _frames;
    private readonly AudioLevelEnvelope _envelope;
    private readonly FrequencyBandEnvelope _bands;
    private double _lastTimestamp = double.NaN;
    private volatile VisualAudioLevel _current = VisualAudioLevel.Silent;
    private volatile VisualAudioBands _currentBands = VisualAudioBands.Silent;
    private volatile bool _disposed;

    public FrameAudioLevelMeter(
        IAudioFrameProvider frames,
        AudioLevelEnvelope? envelope = null,
        FrequencyBandEnvelope? bands = null)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _envelope = envelope ?? new AudioLevelEnvelope();
        _bands = bands ?? new FrequencyBandEnvelope();
        _frames.FrameAvailable += OnFrame;
    }

    /// <inheritdoc />
    public VisualAudioLevel Current => _current;
    public VisualAudioBands CurrentBands => _currentBands;

    private void OnFrame(object? sender, AudioFrameData frame)
    {
        // Ignore the empty/priming frame and frames with no mono feed; the meter needs the time-domain
        // signal (MonoPcm), not the spectrum the beat clock uses.
        if (_disposed || frame.FrameIndex < 0 || frame.MonoPcm.Length == 0)
            return;

        double dt = double.IsNaN(_lastTimestamp) ? 0.0 : frame.TimestampSeconds - _lastTimestamp;
        _lastTimestamp = frame.TimestampSeconds;

        _current = _envelope.Process(frame.MonoPcm, dt);
        _currentBands = _bands.Process(frame.Spectrum, frame.SampleRate, dt);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _frames.FrameAvailable -= OnFrame;
    }
}
