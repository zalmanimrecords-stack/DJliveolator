namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// A one-pole smoother for a normalized 0..1 effect parameter. <c>SetParameter</c> fires from the action
/// thread mid-stream, so the raw target must be glided toward per-sample or a knob move zippers/clicks.
/// Advance once per audio frame (not per interleaved sample) so a stereo block does not smooth twice as fast.
/// </summary>
internal sealed class SmoothedParameter
{
    private readonly double _coeff;
    private double _current;
    private double _target;

    /// <param name="initial">Starting (and target) value.</param>
    /// <param name="sampleRate">Frames per second, for the glide-time constant.</param>
    /// <param name="glideMs">Time constant of the glide, in milliseconds.</param>
    public SmoothedParameter(double initial, int sampleRate, double glideMs = 12.0)
    {
        _current = _target = initial;
        double frames = Math.Max(1.0, glideMs * 0.001 * sampleRate);
        _coeff = Math.Exp(-1.0 / frames);
    }

    /// <summary>The value being glided toward.</summary>
    public double Target => _target;

    /// <summary>The current glided value (last returned by <see cref="Next"/>).</summary>
    public double Current => _current;

    /// <summary>Set a new glide target (already-clamped 0..1 expected).</summary>
    public void SetTarget(double value) => _target = value;

    /// <summary>Advance one frame toward the target and return the new current value.</summary>
    public double Next()
    {
        _current = _target + (_current - _target) * _coeff;
        if (Math.Abs(_current - _target) < 1e-7)
            _current = _target; // snap to kill the exponential tail (and any denormal crawl)
        return _current;
    }

    /// <summary>True when both current and target sit at <paramref name="value"/> — i.e. fully settled there.</summary>
    public bool SettledAt(double value)
        => Math.Abs(_current - value) < 1e-6 && Math.Abs(_target - value) < 1e-6;
}
