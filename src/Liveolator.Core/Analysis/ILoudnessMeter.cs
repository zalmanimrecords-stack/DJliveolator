namespace Liveolator.Core.Analysis;

/// <summary>
/// Measures a track's integrated loudness (EBU R128 / BS.1770) so clips can be gained to one level.
/// <para>A seam, not an algorithm: the measurement is done by whatever the platform provides (today the
/// FFmpeg CLI), while the rule that turns the number into a gain stays pure in Core
/// (<see cref="Liveolator.Core.Dsp.LoudnessGain"/>).</para>
/// </summary>
public interface ILoudnessMeter
{
    /// <summary>
    /// Integrated loudness of <paramref name="path"/> in LUFS, or null when it cannot be measured — a
    /// silent file, an unreadable one, or no measurement tool available. Null is a normal outcome that
    /// callers treat as "leave at unity", so this must not throw for ordinary unmeasurable input.
    /// </summary>
    Task<double?> MeasureIntegratedLufsAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// A meter that measures nothing, for a host with no measurement tool available (and for tests that do not
/// exercise the loudness pass). Every track reports null, which callers already treat as unity gain — so a
/// set still builds, it simply is not level-matched.
/// </summary>
public sealed class NullLoudnessMeter : ILoudnessMeter
{
    public static NullLoudnessMeter Instance { get; } = new();

    public Task<double?> MeasureIntegratedLufsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult<double?>(null);
}
