using Liveolator.Core.Mixer;

namespace Liveolator.Core.Studio.Render;

/// <summary>
/// The pure, deterministic plan an offline renderer follows to mix a <see cref="StudioProject"/> down
/// to audio: for any output time it answers, per deck, which clip is sounding, where to read it, and
/// the gain/EQ/filter from the automation. No decode, no sample loop, no native code — so the mix
/// math unit-tests without audio. Tempo/keylock are out of MVP scope, so the source advances 1:1 with
/// the timeline (native rate).
/// </summary>
public sealed class MixPlan
{
    // Per-deck defaults when a lane is absent: unity gain, flat EQ, filter off.
    private const double DefaultGain = 1.0;
    private const double FlatBand = EqBands.Unity;
    private const double FilterOff = DeckChannelState.FilterCenter;

    private readonly StudioProject _project;
    private readonly Dictionary<(int Slot, AutomationTarget Target), AutomationLane> _lanes;

    public MixPlan(StudioProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _lanes = new Dictionary<(int, AutomationTarget), AutomationLane>();
        foreach (AutomationLane lane in project.Automation)
            if (lane.Keyframes.Count > 0)
                _lanes[(lane.DeckSlot, lane.Target)] = lane; // last lane for a (slot,target) wins
    }

    /// <summary>The number of deck lanes to mix (matches the engine's deck count).</summary>
    public int DeckCount => MixerState.DeckCount;

    /// <summary>The render length: the timeline end of the last clip with a known length.</summary>
    public double DurationSeconds => _project.DurationSeconds;

    /// <summary>
    /// The mix state for <paramref name="slot"/> at <paramref name="timeSeconds"/>: the clip sounding
    /// there (if any) mapped to a source read position, plus the per-deck gain/EQ/filter from automation
    /// (neutral defaults where no lane exists).
    /// </summary>
    public DeckMixState EvaluateDeck(int slot, double timeSeconds)
    {
        double gain = Math.Clamp(LaneValue(slot, AutomationTarget.DeckGain, timeSeconds, DefaultGain), 0.0, 1.0);
        var eq = new EqBands(
            LaneValue(slot, AutomationTarget.EqLow, timeSeconds, FlatBand),
            LaneValue(slot, AutomationTarget.EqMid, timeSeconds, FlatBand),
            LaneValue(slot, AutomationTarget.EqHigh, timeSeconds, FlatBand));
        double filter = LaneValue(slot, AutomationTarget.Filter, timeSeconds, FilterOff);

        StudioClip? clip = ActiveClip(slot, timeSeconds);
        if (clip is null)
            return new DeckMixState(HasAudio: false, SourcePath: null, SourceSeconds: 0, Gain: gain, Eq: eq, Filter: filter);

        double source = clip.SourceIn.TotalSeconds + (timeSeconds - clip.TimelineStartSeconds);
        return new DeckMixState(HasAudio: true, SourcePath: clip.TrackPath, SourceSeconds: source, Gain: gain, Eq: eq, Filter: filter);
    }

    // The clip sounding on a deck at t: covers [start, end) when the length is known, or [start, ∞) when
    // open-ended. With overlapping clips on one deck (unusual), the latest-started one wins.
    private StudioClip? ActiveClip(int slot, double timeSeconds)
    {
        StudioClip? best = null;
        foreach (StudioClip clip in _project.Clips)
        {
            if (clip.DeckSlot != slot || clip.TimelineStartSeconds > timeSeconds)
                continue;
            double end = clip.TimelineEndSeconds ?? double.PositiveInfinity;
            if (timeSeconds >= end)
                continue;
            if (best is null || clip.TimelineStartSeconds > best.TimelineStartSeconds)
                best = clip;
        }

        return best;
    }

    private double LaneValue(int slot, AutomationTarget target, double timeSeconds, double fallback)
        => _lanes.TryGetValue((slot, target), out AutomationLane? lane) ? lane.ValueAt(timeSeconds) : fallback;
}
