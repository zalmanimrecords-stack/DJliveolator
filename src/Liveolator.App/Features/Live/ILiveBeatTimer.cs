namespace Liveolator.App.Features.Live;

/// <summary>
/// The render-loop seam that ticks the manual beat clock between taps so beat/bar phase and the
/// pulse indicator advance smoothly. Behind an interface (rather than a concrete Avalonia timer) so
/// <see cref="LiveViewModel"/> stays UI-free and unit-testable: tests drive the tick manually, the
/// app uses the real frame-rate timer.
/// </summary>
public interface ILiveBeatTimer
{
    /// <summary>Raised on each tick; the subscriber advances the clock to "now".</summary>
    event EventHandler? Tick;

    /// <summary>Begins raising <see cref="Tick"/> at the timer's interval.</summary>
    void Start();

    /// <summary>Stops raising <see cref="Tick"/>.</summary>
    void Stop();
}
