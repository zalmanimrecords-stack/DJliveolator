namespace Liveolator.Audio.Render;

/// <summary>
/// A run of the rendered master whose level stayed under the hole floor — a withdrawal far deeper than any
/// mastered mix reaches, so it is a defect in the arrangement rather than a quiet passage.
/// </summary>
/// <param name="StartSeconds">Where the run starts in the written mix.</param>
/// <param name="DurationSeconds">How long the run lasted.</param>
/// <param name="DeepestDbfs">The level of the DEEPEST window in the run, not its first. A hole that enters
/// as a fade and bottoms out in silence would otherwise be reported at its shallow edge — the 2026-08-13
/// export's join 1 bottomed at -63.7 dB against a local -7.7 dB, and that number is the whole signal.</param>
public sealed record MixHole(double StartSeconds, double DurationSeconds, double DeepestDbfs);

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
/// <param name="Holes">Stretches of the mix whose windowed level fell under the hole floor, in timeline
/// order. Empty is the normal outcome. <see cref="SilentFrames"/> answers "how much of the mix is silent"
/// as one fraction; this answers "and WHERE", which is the question a 10 s hole in a 70-minute set (0.2%
/// of it) can only be found by.</param>
/// <param name="MonoFallbackSources">Track paths that BASS could not decode and that were therefore read
/// through the managed decoder instead, deduplicated. That decoder is MONO by seam contract, so each of
/// these clips has no stereo image at all inside a stereo mix. Reported rather than only logged: the path
/// fires for any clip at warp factor 1.0 whose native decode fails, and it silently shipped eleven minutes
/// of a measured 68-minute export in mono.</param>
public sealed record MixRenderResult(
    int SourceCount,
    IReadOnlyList<string> SilentSources,
    long WrittenFrames,
    long SilentFrames,
    IReadOnlyList<MixHole> Holes,
    IReadOnlyList<string> MonoFallbackSources)
{
    /// <summary>How much of the written mix is silence, 0..1. Zero when nothing was written.</summary>
    public double SilentFraction => WrittenFrames == 0 ? 0.0 : SilentFrames / (double)WrittenFrames;
}
