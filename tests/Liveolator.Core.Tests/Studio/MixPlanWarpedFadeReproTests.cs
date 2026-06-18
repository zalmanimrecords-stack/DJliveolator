using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Render;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

/// <summary>
/// Regression guard for the H1 defect in qa-report-2026-06-18 (now fixed). The bug: the per-clip fade
/// envelope (<see cref="ClipGain"/>) anchored its fade-out and silence cutoff on the <b>un-warped</b> end
/// (<see cref="StudioClip.TimelineEndSeconds"/>), while <see cref="MixPlan"/> selected the active clip on
/// the <b>warped</b> end. For a warped clip these disagreed, so the fade math ran against the wrong end.
/// The fix has <see cref="MixPlan"/> pass the warped end to <see cref="ClipGain.EffectiveGainAt"/> so the
/// fade anchors on the same end the clip is selected over. These tests assert that corrected behavior.
/// </summary>
public class MixPlanWarpedFadeReproTests
{
    private const double Tol = 1e-9;

    /// <summary>
    /// Warp-down → no silent tail. A 120-BPM, 60s source warped to a 100-BPM project plays slower, so its
    /// warped timeline end is 60 / (100/120) = 72s. MixPlan keeps the clip active until 72s. With a 4s
    /// fade-out the operator expects full gain through ~68s, then a ramp to 0 at 72s. Before the fix
    /// ClipGain anchored on the un-warped end (60s) and returned 0 from 60s onward, so the warped tail
    /// [60s, 72s) was audible-by-selection but silent-by-gain — a 12-second dropout. This asserts the tail
    /// is at full gain at 65s.
    /// </summary>
    [Fact]
    public void Repro_WarpedClipTail_GoesSilentBeforeItsWarpedEnd()
    {
        var clip = new StudioClip(
            0, "/m/d0.wav", TimelineStartSeconds: 0,
            SourceIn: TimeSpan.Zero, SourceOut: TimeSpan.FromSeconds(60),
            SourceBpm: 120, WarpEnabled: true, Gain: 1.0, FadeInSeconds: 0, FadeOutSeconds: 4);
        var plan = new MixPlan(new StudioProject("p", 100, new[] { clip }, Array.Empty<AutomationLane>()));

        // Sanity: the warped clip is still the active, sounding clip at t = 65s (warped end ≈ 72s).
        DeckMixState at65 = plan.EvaluateDeck(0, 65);
        Assert.True(at65.HasAudio, "clip should still be active at 65s (warped end ≈ 72s)");

        // At 65s we are well before the 4s fade-out region (which should begin at 72 - 4 = 68s), so the
        // operator expects full gain. The bug makes it 0 because ClipGain cuts off at the un-warped 60s end.
        Assert.Equal(1.0, at65.Gain, Tol);
    }

    /// <summary>
    /// Warp-up → the fade-out completes at the audible end. A 120-BPM, 60s source warped to a 140-BPM
    /// project plays faster: warped end = 60 / (140/120) ≈ 51.43s. With a 4s fade-out the ramp should run
    /// [47.43s, 51.43s] and reach ~0 at the warped end. Before the fix ClipGain anchored on the un-warped
    /// 60s end, so at the true last audible instant (just before 51.43s) the gain was still essentially
    /// full and the requested fade-out was missing from the render. This asserts gain is near 0 there.
    /// </summary>
    [Fact]
    public void Repro_WarpedUpClip_FadeOutDoesNotReachZeroAtItsAudibleEnd()
    {
        var clip = new StudioClip(
            0, "/m/d0.wav", TimelineStartSeconds: 0,
            SourceIn: TimeSpan.Zero, SourceOut: TimeSpan.FromSeconds(60),
            SourceBpm: 120, WarpEnabled: true, Gain: 1.0, FadeInSeconds: 0, FadeOutSeconds: 4);
        var plan = new MixPlan(new StudioProject("p", 140, new[] { clip }, Array.Empty<AutomationLane>()));

        double warpedEnd = 60.0 / (140.0 / 120.0); // ≈ 51.4286s
        DeckMixState nearEnd = plan.EvaluateDeck(0, warpedEnd - 0.01);
        Assert.True(nearEnd.HasAudio, "clip should still be active just before its warped end");

        // Just before the audible end the fade-out should have nearly completed (gain near 0). The bug
        // leaves it near full gain because the fade anchors on the (later) un-warped 60s end.
        Assert.True(
            nearEnd.Gain < 0.1,
            $"expected the fade-out to be near 0 at the warped clip end, but gain was {nearEnd.Gain}");
    }
}
