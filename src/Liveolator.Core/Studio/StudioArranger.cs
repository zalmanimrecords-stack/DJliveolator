using Liveolator.Core.Actions;

namespace Liveolator.Core.Studio;

/// <summary>
/// Turns a <see cref="StudioProject"/> arrangement into the timed work the live transport (Phase 4)
/// and the offline renderer (Phase 6) carry out: idempotent per-tick automation
/// <see cref="PerformanceAction"/>s, and clip Start/Stop events. Pure and clock-free — it answers
/// "what is true at time t" / "what happens in the window [t0, t1)" with no side effects, so it
/// unit-tests deterministically.
/// </summary>
public sealed class StudioArranger
{
    /// <summary>Origin stamp on every emitted action so engines/observers can tell timeline automation
    /// from a human gesture (the yield-to-performer rule, doc 04).</summary>
    public const string Origin = "studio";

    private const double UnityGain = 1.0;

    private readonly StudioProject _project;

    public StudioArranger(StudioProject project)
        => _project = project ?? throw new ArgumentNullException(nameof(project));

    /// <summary>
    /// The project tempo (BPM) at a timeline position — the tempo a warped clip starting there should play
    /// at. Reads the project's tempo curve (falling back to the fixed project BPM). The live transport uses
    /// it to warp a clip's deck to the project grid on its Start event.
    /// </summary>
    public double ProjectTempoAt(double timelineSeconds)
        => _project.EffectiveTempo.TempoAt(timelineSeconds, _project.Bpm);

    /// <summary>
    /// Clip Start/Stop events whose time falls in <c>[t0, t1)</c>, in time order. A clip emits a Start
    /// at its <see cref="StudioClip.TimelineStartSeconds"/> and a Stop at its
    /// <see cref="StudioClip.TimelineEndSeconds"/> (only when the out-point is known).
    /// </summary>
    public IReadOnlyList<StudioClipEvent> ClipEventsBetween(double t0, double t1)
    {
        var events = new List<StudioClipEvent>();
        foreach (StudioClip clip in _project.Clips)
        {
            if (InWindow(clip.TimelineStartSeconds, t0, t1))
                events.Add(new StudioClipEvent(clip.TimelineStartSeconds, StudioClipEventKind.Start, clip));

            if (clip.TimelineEndSeconds is { } end && InWindow(end, t0, t1))
                events.Add(new StudioClipEvent(end, StudioClipEventKind.Stop, clip));
        }

        events.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
        return events;
    }

    /// <summary>
    /// One absolute, idempotent <see cref="PerformanceAction"/> per non-empty automation lane,
    /// carrying the lane's interpolated value at <paramref name="timeSeconds"/>. Safe to dispatch
    /// every transport tick (each is an absolute set, not a relative nudge).
    /// <para>The deck-gain action folds in the active clip's <see cref="ClipGain.EffectiveGainAt"/>
    /// envelope so a live playback fade matches the offline render (the render path folds the same
    /// envelope into <c>DeckMixState.Gain</c>): emitted gain = lane value (or 1.0 with no lane) x clip
    /// envelope. A deck with a per-clip gain/fade but no gain lane still emits a gain action so the
    /// fade is heard.</para>
    /// </summary>
    public IReadOnlyList<PerformanceAction> AutomationActionsAt(double timeSeconds)
    {
        var actions = new List<PerformanceAction>(_project.Automation.Count);
        var slotsWithGainLane = new HashSet<int>();

        foreach (AutomationLane lane in _project.Automation)
        {
            if (lane.Keyframes.Count == 0)
                continue; // an empty lane controls nothing

            if (lane.Target == AutomationTarget.DeckGain)
            {
                slotsWithGainLane.Add(lane.DeckSlot);
                double combined = lane.ValueAt(timeSeconds) * ClipEnvelopeAt(lane.DeckSlot, timeSeconds);
                actions.Add(Absolute(PerformanceActionKind.MixerChannelGain, lane.DeckSlot, combined));
                continue;
            }

            actions.Add(ToAction(lane, lane.ValueAt(timeSeconds)));
        }

        // Decks with a per-clip gain/fade but no gain lane: emit the clip envelope (lane value defaults
        // to 1.0) so the live fade is still heard and stays in lockstep with the render. A clip at full
        // unity contributes nothing, so we skip it to avoid spamming redundant neutral sets.
        foreach (StudioClip clip in _project.Clips)
        {
            if (slotsWithGainLane.Contains(clip.DeckSlot) || !IsSounding(clip, timeSeconds))
                continue;

            double envelope = ClipGain.EffectiveGainAt(clip, timeSeconds);
            if (envelope < UnityGain)
            {
                actions.Add(Absolute(PerformanceActionKind.MixerChannelGain, clip.DeckSlot, envelope));
                slotsWithGainLane.Add(clip.DeckSlot); // one gain action per deck even with overlapping clips
            }
        }

        return actions;
    }

    // The effective clip-gain envelope on a deck at t: the sounding clip's EffectiveGainAt, or unity
    // when no clip is sounding there (so a bare gain lane keeps its own value). Overlapping clips on one
    // deck resolve latest-started-wins, matching MixPlan.ActiveClip.
    private double ClipEnvelopeAt(int slot, double timeSeconds)
    {
        StudioClip? active = ActiveClip(slot, timeSeconds);
        return active is null ? UnityGain : ClipGain.EffectiveGainAt(active, timeSeconds);
    }

    private StudioClip? ActiveClip(int slot, double timeSeconds)
    {
        StudioClip? best = null;
        foreach (StudioClip clip in _project.Clips)
        {
            if (clip.DeckSlot != slot || !IsSounding(clip, timeSeconds))
                continue;
            if (best is null || clip.TimelineStartSeconds > best.TimelineStartSeconds)
                best = clip;
        }

        return best;
    }

    // Half-open [start, end): matches ClipEventsBetween and ClipGain's window. Open-ended clips sound
    // from their start onward. Warp is not modeled here (the live transport drives the source rate), so
    // the un-warped TimelineEndSeconds anchors the window, identical to the clip-event boundaries.
    private static bool IsSounding(StudioClip clip, double timeSeconds)
        => timeSeconds >= clip.TimelineStartSeconds
            && (clip.TimelineEndSeconds is not { } end || timeSeconds < end);

    private static bool InWindow(double t, double t0, double t1) => t >= t0 && t < t1;

    private static PerformanceAction ToAction(AutomationLane lane, double value) => lane.Target switch
    {
        AutomationTarget.DeckGain => Absolute(PerformanceActionKind.MixerChannelGain, lane.DeckSlot, value),
        AutomationTarget.EqLow => Eq(lane.DeckSlot, value, "Low"),
        AutomationTarget.EqMid => Eq(lane.DeckSlot, value, "Mid"),
        AutomationTarget.EqHigh => Eq(lane.DeckSlot, value, "High"),
        AutomationTarget.Filter => Absolute(PerformanceActionKind.MixerFilter, lane.DeckSlot, value),
        AutomationTarget.Pitch => Absolute(PerformanceActionKind.DeckPitch, lane.DeckSlot, value),
        _ => throw new ArgumentOutOfRangeException(nameof(lane), lane.Target, "Unknown automation target."),
    };

    private static PerformanceAction Absolute(PerformanceActionKind kind, int slot, double value)
        => new(kind, ActionInputMode.Absolute, value, slot, Origin: Origin);

    private static PerformanceAction Eq(int slot, double value, string band)
        => new(PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, value, slot, Argument: band, Origin: Origin);
}
