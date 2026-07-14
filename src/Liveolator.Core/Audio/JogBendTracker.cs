using Liveolator.Core.Settings;

namespace Liveolator.Core.Audio;

/// <summary>
/// Turns a stream of jog ticks — each a signed revolution-delta, clockwise/forward positive — into the
/// temporary pitch-bend fraction a deck should hold while it plays (the standard "nudge to beat-match"
/// feel). Stateful but pure: it smooths the tick rate into an angular velocity (rev/s) and maps that via
/// <see cref="JogMath.BendFraction"/>. An endless encoder sends no "release", so the bend is held until
/// <see cref="TryReleaseStale"/> observes the ticks have stopped. The caller supplies the clock (seconds),
/// so this unit-tests with a fake time source and keeps <c>Liveolator.Core</c> free of real timers.
/// </summary>
/// <remarks>Not thread-safe: one tracker per deck slot, driven from the single action-dispatch path.</remarks>
public sealed class JogBendTracker
{
    // Clamp the gap between ticks before dividing, so a stalled frame (huge dt) or a burst of MIDI (tiny
    // dt) can't spike the velocity to the rail.
    private const double MinDtSeconds = 0.004;
    private const double MaxDtSeconds = 0.100;

    private readonly double _gain;
    private readonly double _deadzone;
    private readonly double _maxFraction;
    private readonly double _alpha;
    private readonly double _releaseTimeoutSeconds;

    private double _velocityRevPerSecond;
    private double _lastTickSeconds;
    private bool _hasLastTick;
    private bool _bending;

    public JogBendTracker(JogWheelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        JogWheelSettings s = settings.Normalized();
        _gain = s.BendGainPerRevPerSecond;
        _deadzone = s.DeadzoneRevPerSecond;
        _maxFraction = s.BendMaxFraction;
        _alpha = s.VelocityEmaAlpha;
        _releaseTimeoutSeconds = s.ReleaseTimeoutMs / 1000.0;
    }

    /// <summary>
    /// Feeds one jog tick (signed revolution-delta) seen at <paramref name="nowSeconds"/> and returns the
    /// bend fraction to apply now. Records the tick so a later <see cref="TryReleaseStale"/> can end the
    /// bend once ticks stop. Non-finite input is ignored (returns 0 without disturbing the estimator).
    /// </summary>
    public double OnJog(double revolutionDelta, double nowSeconds)
    {
        if (!double.IsFinite(revolutionDelta) || !double.IsFinite(nowSeconds))
            return 0.0;

        double dt = _hasLastTick
            ? Math.Clamp(nowSeconds - _lastTickSeconds, MinDtSeconds, MaxDtSeconds)
            : MaxDtSeconds; // a first (or post-release) tick is seeded conservatively, never as a dt spike
        double instant = revolutionDelta / dt;
        _velocityRevPerSecond = _hasLastTick
            ? (_alpha * instant) + ((1.0 - _alpha) * _velocityRevPerSecond)
            : instant;

        _hasLastTick = true;
        _lastTickSeconds = nowSeconds;

        double bend = JogMath.BendFraction(_velocityRevPerSecond, _gain, _deadzone, _maxFraction);
        _bending = bend != 0.0;
        return bend;
    }

    /// <summary>
    /// True when a bend is active and its last tick is older than the release timeout — i.e. the ticks
    /// have stopped and the deck should snap back to its normal rate (the caller applies bend 0). Returns
    /// true at most once per bend; a non-finite clock reading never forces a release.
    /// </summary>
    public bool TryReleaseStale(double nowSeconds)
    {
        if (!_bending || !double.IsFinite(nowSeconds))
            return false;
        if (nowSeconds - _lastTickSeconds < _releaseTimeoutSeconds)
            return false;

        _bending = false;
        _hasLastTick = false;
        _velocityRevPerSecond = 0.0;
        return true;
    }
}
