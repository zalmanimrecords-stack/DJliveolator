namespace Liveolator.Core.Beat;

/// <summary>
/// An immutable Link-style timeline: a constant tempo plus one (hostTime, beat) anchor define the
/// whole host-time↔beat-time mapping. Pure math with no internal clock reads, so it is fully
/// deterministic and unit-testable (doc 03). A new tempo or re-anchor produces a new instance.
/// </summary>
public sealed class BeatTimeline : IBeatTimeline
{
    // A beat lands exactly on a boundary when its grid index is within this many beats of an
    // integer — absorbs floating-point error so an on-boundary time resolves to "now", not "next".
    private const double BoundaryEpsilon = 1e-9;

    private readonly double _beatsPerTick;

    /// <param name="bpm">Tempo in beats per minute; must be positive.</param>
    /// <param name="anchorBeat">The musical beat position at <paramref name="anchorHostTimeTicks"/>.</param>
    /// <param name="anchorHostTimeTicks">A host time the anchor beat is pinned to.</param>
    /// <param name="ticksPerSecond">Host-time tick resolution; must be positive.</param>
    public BeatTimeline(double bpm, double anchorBeat, long anchorHostTimeTicks, long ticksPerSecond)
    {
        if (bpm <= 0 || double.IsNaN(bpm))
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "Tempo must be positive.");
        if (ticksPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), ticksPerSecond, "Tick rate must be positive.");

        Bpm = bpm;
        AnchorBeat = anchorBeat;
        AnchorHostTimeTicks = anchorHostTimeTicks;
        TicksPerSecond = ticksPerSecond;
        _beatsPerTick = bpm / 60.0 / ticksPerSecond;
    }

    /// <summary>Builds a timeline using the .NET system tick resolution (100 ns).</summary>
    public static BeatTimeline FromSystemClock(double bpm, double anchorBeat, long anchorHostTimeTicks)
        => new(bpm, anchorBeat, anchorHostTimeTicks, TimeSpan.TicksPerSecond);

    public double Bpm { get; }

    public double AnchorBeat { get; }

    public long AnchorHostTimeTicks { get; }

    public long TicksPerSecond { get; }

    /// <inheritdoc />
    public double BeatAtTime(long hostTimeTicks)
        => AnchorBeat + (hostTimeTicks - AnchorHostTimeTicks) * _beatsPerTick;

    /// <inheritdoc />
    public double PhaseAtTime(long hostTimeTicks, double quantumBeats)
    {
        RequirePositiveQuantum(quantumBeats);
        return Mod(BeatAtTime(hostTimeTicks), quantumBeats) / quantumBeats;
    }

    /// <inheritdoc />
    public long NextBoundary(long fromHostTimeTicks, double quantumBeats)
    {
        RequirePositiveQuantum(quantumBeats);

        double beat = BeatAtTime(fromHostTimeTicks);
        double gridIndex = Math.Ceiling(beat / quantumBeats - BoundaryEpsilon);
        double boundaryBeat = gridIndex * quantumBeats;
        return BeatToTicks(boundaryBeat);
    }

    private long BeatToTicks(double beat)
        => AnchorHostTimeTicks + (long)Math.Round((beat - AnchorBeat) / _beatsPerTick);

    private static void RequirePositiveQuantum(double quantumBeats)
    {
        if (quantumBeats <= 0 || double.IsNaN(quantumBeats))
            throw new ArgumentOutOfRangeException(nameof(quantumBeats), quantumBeats, "Quantum must be positive.");
    }

    /// <summary>Floored modulo so phase stays in [0, n) even for times before the anchor.</summary>
    private static double Mod(double a, double n)
    {
        double r = a % n;
        return r < 0 ? r + n : r;
    }
}
