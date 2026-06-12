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

    private readonly StudioProject _project;

    public StudioArranger(StudioProject project)
        => _project = project ?? throw new ArgumentNullException(nameof(project));

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
    /// </summary>
    public IReadOnlyList<PerformanceAction> AutomationActionsAt(double timeSeconds)
    {
        var actions = new List<PerformanceAction>(_project.Automation.Count);
        foreach (AutomationLane lane in _project.Automation)
        {
            if (lane.Keyframes.Count == 0)
                continue; // an empty lane controls nothing
            actions.Add(ToAction(lane, lane.ValueAt(timeSeconds)));
        }

        return actions;
    }

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
