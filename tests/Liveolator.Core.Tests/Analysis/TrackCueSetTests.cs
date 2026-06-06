using System;
using System.Linq;
using Liveolator.Core.Analysis.Cues;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class TrackCueSetTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void NewSet_HasNoHotCues_AndPrimaryFallsBackToStart()
    {
        var set = new TrackCueSet(SampleRate);

        Assert.Empty(set.HotCues);
        Assert.Null(set.PrimaryCueSamples);
        Assert.Equal(0L, set.PrimaryCueTargetSamples);
        Assert.Equal(TrackCueSet.DefaultSlotCount, set.SlotCount);
    }

    [Fact]
    public void SetHotCue_StoresCueAtSlot_WithIndexLabelAndColor()
    {
        var set = new TrackCueSet(SampleRate).SetHotCue(2, 88_200, "Drop", 0xFF0000);

        Assert.True(set.IsHotCueSet(2));
        HotCue cue = set.GetHotCue(2)!.Value;
        Assert.Equal(2, cue.Index);
        Assert.Equal(88_200, cue.PositionSamples);
        Assert.Equal("Drop", cue.Label);
        Assert.Equal(0xFF0000, cue.Color);
    }

    [Fact]
    public void SetHotCue_IsImmutable_OriginalUnchanged()
    {
        var original = new TrackCueSet(SampleRate);
        var updated = original.SetHotCue(0, 1000);

        Assert.False(original.IsHotCueSet(0));
        Assert.True(updated.IsHotCueSet(0));
    }

    [Fact]
    public void SetHotCue_OnOccupiedSlot_Overwrites()
    {
        var set = new TrackCueSet(SampleRate)
            .SetHotCue(1, 1000, "First")
            .SetHotCue(1, 5000, "Second");

        HotCue cue = set.GetHotCue(1)!.Value;
        Assert.Equal(5000, cue.PositionSamples);
        Assert.Equal("Second", cue.Label);
        Assert.Single(set.HotCues);
    }

    [Fact]
    public void ClearHotCue_RemovesCue()
    {
        var set = new TrackCueSet(SampleRate).SetHotCue(3, 2000).ClearHotCue(3);

        Assert.False(set.IsHotCueSet(3));
        Assert.Null(set.RecallSamples(3));
    }

    [Fact]
    public void ClearHotCue_OnEmptySlot_IsNoOp()
    {
        var set = new TrackCueSet(SampleRate);
        var cleared = set.ClearHotCue(4);

        Assert.Empty(cleared.HotCues);
    }

    [Fact]
    public void RecallSamples_ReturnsStoredPosition_OrNullWhenEmpty()
    {
        var set = new TrackCueSet(SampleRate).SetHotCue(0, 12_345);

        Assert.Equal(12_345, set.RecallSamples(0));
        Assert.Null(set.RecallSamples(1));
    }

    [Fact]
    public void HotCues_AreOrderedBySlotIndex()
    {
        var set = new TrackCueSet(SampleRate)
            .SetHotCue(5, 5000)
            .SetHotCue(1, 1000)
            .SetHotCue(3, 3000);

        Assert.Equal(new[] { 1, 3, 5 }, set.HotCues.Select(c => c.Index).ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(TrackCueSet.DefaultSlotCount)]
    public void HotCueIndexOutOfRange_Throws(int index)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TrackCueSet(SampleRate).SetHotCue(index, 0));

    [Fact]
    public void SetHotCue_NegativePosition_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TrackCueSet(SampleRate).SetHotCue(0, -1));

    [Fact]
    public void PrimaryCue_SetAndClear()
    {
        var set = new TrackCueSet(SampleRate).SetPrimaryCue(44_100);
        Assert.Equal(44_100, set.PrimaryCueSamples);
        Assert.Equal(44_100, set.PrimaryCueTargetSamples);

        var cleared = set.ClearPrimaryCue();
        Assert.Null(cleared.PrimaryCueSamples);
        Assert.Equal(0L, cleared.PrimaryCueTargetSamples);
    }

    [Fact]
    public void SetPrimaryCue_PreservesHotCues()
    {
        var set = new TrackCueSet(SampleRate).SetHotCue(0, 1000).SetPrimaryCue(2000);

        Assert.True(set.IsHotCueSet(0));
        Assert.Equal(2000, set.PrimaryCueSamples);
    }

    [Fact]
    public void HotCue_PositionSeconds_ConvertsBySampleRate()
    {
        var cue = new HotCue(0, 88_200);
        Assert.Equal(2.0, cue.PositionSeconds(SampleRate), precision: 6);
    }

    [Fact]
    public void RecallQuantized_SnapsToNearestBeat()
    {
        // 120 BPM @ 44.1kHz => 22,050 samples per beat. A cue 2,000 samples past beat 2 (44,100)
        // snaps back down to beat 2; one just past the midpoint snaps up to beat 3 (66,150).
        var set = new TrackCueSet(SampleRate)
            .SetHotCue(0, 46_100)   // beat 2 + 2000  -> nearest is beat 2
            .SetHotCue(1, 56_000);  // between beat 2 and 3, past midpoint (55,125) -> beat 3

        Assert.Equal(44_100, set.RecallQuantizedSamples(0, 120.0));
        Assert.Equal(66_150, set.RecallQuantizedSamples(1, 120.0));
    }

    [Fact]
    public void RecallQuantized_EmptySlot_ReturnsNull()
        => Assert.Null(new TrackCueSet(SampleRate).RecallQuantizedSamples(0, 120.0));

    [Fact]
    public void RecallQuantized_NonPositiveBpm_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new TrackCueSet(SampleRate).SetHotCue(0, 1000).RecallQuantizedSamples(0, 0));

    [Fact]
    public void Constructor_InvalidSampleRate_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TrackCueSet(0));

    [Fact]
    public void Constructor_CustomSlotCount_Honored()
    {
        var set = new TrackCueSet(SampleRate, slotCount: 4);
        Assert.Equal(4, set.SlotCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => set.SetHotCue(4, 0));
    }
}
