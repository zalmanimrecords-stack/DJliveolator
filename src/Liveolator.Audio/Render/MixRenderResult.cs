namespace Liveolator.Audio.Render;

/// <summary>
/// What a render actually produced, as opposed to what it was asked to produce.
/// <para>Exists because every decode failure degrades to an empty buffer with a log warning rather than a
/// throw (global #16/#26). That is the right per-clip behaviour, but on its own it means a render whose
/// sources all failed writes a full-length file of digital silence and reports success — the worst
/// possible outcome of a long unattended export. A caller that can see <see cref="SilentSources"/> can
/// refuse to publish it; a caller that only had the log could not.</para>
/// </summary>
/// <param name="SourceCount">Distinct (track, warp factor) sources the render decoded.</param>
/// <param name="SilentSources">Track paths whose decode came back with zero frames, deduplicated. Each one
/// contributed nothing at all to the mix, so the timeline it should have covered is silence.</param>
/// <param name="WrittenFrames">Stereo frames written to the output file.</param>
/// <param name="SilentFrames">How many of those frames were below audibility on both channels. Counted
/// while each block was already in hand — re-reading a finished 700 MB mix to ask the same question is not
/// free, and whole-file loudness demonstrably does not answer it (a 95%-silent mix measured a healthy
/// -10.3 LUFS, because the few sounding clips carried the average).</param>
public sealed record MixRenderResult(
    int SourceCount,
    IReadOnlyList<string> SilentSources,
    long WrittenFrames,
    long SilentFrames)
{
    /// <summary>How much of the written mix is silence, 0..1. Zero when nothing was written.</summary>
    public double SilentFraction => WrittenFrames == 0 ? 0.0 : SilentFrames / (double)WrittenFrames;
}
