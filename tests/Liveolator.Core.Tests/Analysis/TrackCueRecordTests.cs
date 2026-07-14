using System.Linq;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class TrackCueRecordTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void FromCueSet_ThenToCueSet_RoundTripsAllCues()
    {
        var original = new TrackCueSet(SampleRate)
            .SetHotCue(0, 1000, "Intro", 0x00FF00)
            .SetHotCue(4, 200_000, "Drop")
            .SetPrimaryCue(44_100);

        var record = TrackCueRecord.FromCueSet(@"C:\Music\a.wav", original);
        var rebuilt = record.ToCueSet();

        Assert.Equal(@"C:\Music\a.wav", record.TrackPath);
        Assert.Equal(SampleRate, rebuilt.SampleRate);
        Assert.Equal(original.SlotCount, rebuilt.SlotCount);
        Assert.Equal(44_100, rebuilt.PrimaryCueSamples);
        Assert.Equal(original.HotCues, rebuilt.HotCues);
    }

    [Fact]
    public void ToCueSet_SkipsOutOfRangeCues_WithoutThrowing()
    {
        // A hand-edited or older file could carry a cue whose index exceeds the slot count.
        var record = new TrackCueRecord(
            @"C:\Music\a.wav",
            SampleRate,
            SlotCount: 4,
            PrimaryCueSamples: null,
            HotCues: new[] { new HotCue(0, 1000), new HotCue(9, 2000) });

        var set = record.ToCueSet();

        Assert.Single(set.HotCues);
        Assert.Equal(0, set.HotCues[0].Index);
    }

    [Fact]
    public void ToCueSet_ZeroSlotCount_FallsBackToDefault()
    {
        var record = new TrackCueRecord(@"C:\Music\a.wav", SampleRate, SlotCount: 0, null,
            HotCues: System.Array.Empty<HotCue>());

        Assert.Equal(TrackCueSet.DefaultSlotCount, record.ToCueSet().SlotCount);
    }
}
