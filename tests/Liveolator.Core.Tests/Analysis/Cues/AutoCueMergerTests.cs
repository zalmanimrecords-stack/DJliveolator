using Liveolator.Core.Analysis.Cues;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Cues;

public class AutoCueMergerTests
{
    private const int Sr = 44_100;

    private static TrackCueSet Auto() => new TrackCueSet(Sr)
        .SetHotCue(0, 1000, "Start", 0xFFFFFF, isAuto: true)
        .SetHotCue(1, 50_000, "Drop", 0xFF3B30, isAuto: true)
        .SetHotCue(2, 90_000, "Breakdown", 0x0A84FF, isAuto: true);

    [Fact]
    public void Merge_NoExisting_ReturnsAutoUnchanged()
    {
        TrackCueSet merged = new AutoCueMerger().Merge(existing: null, Auto());

        Assert.Equal(3, merged.HotCues.Count);
        Assert.True(merged.GetHotCue(1)!.Value.IsAuto);
    }

    [Fact]
    public void Merge_ManualCue_IsPreservedOverNewAuto()
    {
        // The DJ committed slot 1 by hand; re-analysis must not overwrite it.
        var existing = new TrackCueSet(Sr).SetHotCue(1, 12_345, "My Drop", 0x00FF00, isAuto: false);

        TrackCueSet merged = new AutoCueMerger().Merge(existing, Auto());

        HotCue slot1 = merged.GetHotCue(1)!.Value;
        Assert.False(slot1.IsAuto);
        Assert.Equal(12_345, slot1.PositionSamples);
        Assert.Equal("My Drop", slot1.Label);
    }

    [Fact]
    public void Merge_AutoCue_IsReplacedByNewAuto()
    {
        // A previously-suggested (still auto) slot is refreshed by the new analysis.
        var existing = new TrackCueSet(Sr).SetHotCue(1, 999, "Old", 0x111111, isAuto: true);

        TrackCueSet merged = new AutoCueMerger().Merge(existing, Auto());

        Assert.Equal(50_000, merged.GetHotCue(1)!.Value.PositionSamples);
        Assert.Equal("Drop", merged.GetHotCue(1)!.Value.Label);
    }

    [Fact]
    public void Merge_StaleAutoSlotWithNoNewCue_IsCleared()
    {
        // Slot 5 held an auto cue last time but the new analysis finds nothing there -> it is cleared,
        // not left as a stale suggestion.
        var existing = new TrackCueSet(Sr).SetHotCue(5, 777, "Stale", 0x222222, isAuto: true);

        TrackCueSet merged = new AutoCueMerger().Merge(existing, Auto());

        Assert.False(merged.IsHotCueSet(5));
    }

    [Fact]
    public void Merge_PreservesPrimaryCue()
    {
        var existing = new TrackCueSet(Sr).SetPrimaryCue(33_333);

        TrackCueSet merged = new AutoCueMerger().Merge(existing, Auto());

        Assert.Equal(33_333, merged.PrimaryCueSamples);
    }

    [Fact]
    public void Merge_DifferentSampleRate_RescalesPreservedManualPositions()
    {
        // Manual cue stored at 22.05 kHz, new auto grid at 44.1 kHz -> position must double to stay at
        // the same point in time.
        var existing = new TrackCueSet(22_050)
            .SetHotCue(3, 10_000, "Manual", 0x123456, isAuto: false)
            .SetPrimaryCue(5_000);

        TrackCueSet merged = new AutoCueMerger().Merge(existing, Auto());

        Assert.Equal(Sr, merged.SampleRate);
        Assert.Equal(20_000, merged.GetHotCue(3)!.Value.PositionSamples);
        Assert.Equal(10_000, merged.PrimaryCueSamples);
    }
}
