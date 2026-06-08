namespace Liveolator.Core.Settings;

/// <summary>
/// Musical jog-wheel sensitivity expressed as seconds moved by one full physical revolution.
/// Paused scrubbing follows a 33 1/3 RPM platter; playing movement is deliberately much finer
/// until temporary pitch-bend jog behavior is implemented.
/// </summary>
public sealed record JogWheelSettings(
    double PausedSecondsPerRevolution = 1.8,
    double PlayingSecondsPerRevolution = 0.2)
{
    public const double DefaultPausedSecondsPerRevolution = 1.8;
    public const double DefaultPlayingSecondsPerRevolution = 0.2;

    public static JogWheelSettings Default { get; } = new();

    public JogWheelSettings Normalized()
        => this with
        {
            PausedSecondsPerRevolution = Normalize(
                PausedSecondsPerRevolution, DefaultPausedSecondsPerRevolution),
            PlayingSecondsPerRevolution = Normalize(
                PlayingSecondsPerRevolution, DefaultPlayingSecondsPerRevolution),
        };

    private static double Normalize(double value, double fallback)
        => double.IsFinite(value) && value > 0.0 ? value : fallback;
}
