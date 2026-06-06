using Liveolator.Core.Beat;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Pure selection of which <see cref="IBeatClock"/> the visual compositor should bind to so visuals
/// react to the actual music (doc 08 — "the same beat clock that drives the DJ mix drives the
/// visuals"; doc 18 RENDER-WINDOW SEAM, clock half).
///
/// Two clock sources can be present at composition: the audio-driven clock (the master mix's
/// <c>AudioBeatClock</c>, present only when the realtime BASS engine is up) and the manual tap clock
/// (always present, drives the Live tab and works headless). When the audio clock exists it is
/// authoritative — visuals lock to the audible signal; otherwise the visuals follow the manual tap
/// clock. This is the pure decision so the binding is testable without GL or audio hardware.
/// </summary>
public static class LiveClockSelector
{
    /// <summary>
    /// Returns the clock the visuals should read: <paramref name="audioClock"/> when present
    /// (authoritative — visuals lock to the audible signal), otherwise <paramref name="manualClock"/>
    /// (the always-present tap clock). <paramref name="manualClock"/> must not be null — it is the
    /// guaranteed fallback that makes the slice work headless.
    /// </summary>
    public static IBeatClock Select(IBeatClock? audioClock, IBeatClock manualClock)
    {
        ArgumentNullException.ThrowIfNull(manualClock);
        return audioClock ?? manualClock;
    }
}
