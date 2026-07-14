namespace Liveolator.Core.Beat;

/// <summary>
/// Defers an action to a quantized boundary on the live clock. This is the bridge quantized
/// actions (e.g. VisualTransitionNextBar, PlaylistSkipOnNextBar — doc 04) resolve against. The
/// fire-time math lives in <see cref="BeatQuantizer"/>; an implementation pairs it with a timer.
/// </summary>
public interface IBeatScheduler
{
    /// <summary>
    /// Invokes <paramref name="onFire"/> when <paramref name="when"/> next occurs.
    /// <paramref name="everyN"/> applies only to <see cref="Quantize.EveryNBars"/>.
    /// </summary>
    void Schedule(Quantize when, int everyN, Action onFire);
}
