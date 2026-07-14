namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Per-frame band energy of a track: parallel arrays (one value per analysis frame) for the
/// low / mid / high crossover bands plus the broadband sum, with the frame rate that maps an
/// index back to seconds. This is the raw signal structural-cue detection reads — kick presence
/// lives in <see cref="Low"/>, risers/snare-rolls in <see cref="High"/>, overall loudness in
/// <see cref="Broadband"/> (doc 11/16 phrase analysis). Pure data, no behaviour.
/// </summary>
/// <param name="Low">Energy below the low crossover (kick/bass band), one value per frame.</param>
/// <param name="Mid">Energy between the low and high crossovers, one value per frame.</param>
/// <param name="High">Energy above the high crossover (presence/air band), one value per frame.</param>
/// <param name="Broadband">Total spectral energy across all bins, one value per frame.</param>
/// <param name="FrameRateHz">Analysis frames per second (sampleRate ÷ hop); 0 when no frames.</param>
public sealed record BandEnergyFrames(
    double[] Low,
    double[] Mid,
    double[] High,
    double[] Broadband,
    double FrameRateHz)
{
    /// <summary>Number of analysis frames (0 when the signal was shorter than one frame).</summary>
    public int FrameCount => Broadband.Length;

    /// <summary>An empty result (signal shorter than one frame): zero-length bands, 0 frame rate.</summary>
    public static BandEnergyFrames Empty { get; } =
        new(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), 0.0);

    /// <summary>The start time in seconds of frame <paramref name="frameIndex"/>.</summary>
    public double FrameSeconds(int frameIndex)
    {
        if (FrameRateHz <= 0.0)
            return 0.0;
        return frameIndex / FrameRateHz;
    }
}
