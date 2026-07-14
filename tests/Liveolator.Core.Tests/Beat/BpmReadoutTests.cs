using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class BpmReadoutTests
{
    [Fact]
    public void StartsWithNoValue()
    {
        var readout = new BpmReadout();

        Assert.False(readout.HasValue);
        Assert.Null(readout.DisplayedBpm);
    }

    [Fact]
    public void AdoptsFirstConfidentSample_QuantizedToTenth()
    {
        var readout = new BpmReadout(confidenceFloor: 0.1);

        bool changed = readout.Update(bpm: 128.034, confidence: 0.9);

        Assert.True(changed);
        Assert.Equal(128.0, readout.DisplayedBpm);
    }

    [Fact]
    public void IgnoresSamplesBelowConfidenceFloor()
    {
        var readout = new BpmReadout(confidenceFloor: 0.2);

        bool changed = readout.Update(bpm: 128.0, confidence: 0.1);

        Assert.False(changed);
        Assert.False(readout.HasValue);
    }

    [Fact]
    public void IgnoresNonPositiveBpm()
    {
        var readout = new BpmReadout(confidenceFloor: 0.0);

        bool changed = readout.Update(bpm: 0.0, confidence: 1.0);

        Assert.False(changed);
        Assert.False(readout.HasValue);
    }

    [Fact]
    public void HoldsLastGoodValueWhenConfidenceDrops()
    {
        var readout = new BpmReadout(confidenceFloor: 0.2);
        readout.Update(bpm: 124.0, confidence: 0.9);

        // A low-confidence (jittery) estimate must not disturb the held value.
        bool changed = readout.Update(bpm: 200.0, confidence: 0.05);

        Assert.False(changed);
        Assert.Equal(124.0, readout.DisplayedBpm);
    }

    [Fact]
    public void SmallChangeWithinHysteresisDoesNotRepaint()
    {
        var readout = new BpmReadout(confidenceFloor: 0.1, changeThresholdBpm: 0.3);
        readout.Update(bpm: 128.0, confidence: 0.9);

        // 128.1 is within the 0.3 BPM hysteresis band → the displayed value stays put (no flicker).
        bool changed = readout.Update(bpm: 128.1, confidence: 0.9);

        Assert.False(changed);
        Assert.Equal(128.0, readout.DisplayedBpm);
    }

    [Fact]
    public void ChangeBeyondHysteresisUpdatesValue()
    {
        var readout = new BpmReadout(confidenceFloor: 0.1, changeThresholdBpm: 0.3);
        readout.Update(bpm: 128.0, confidence: 0.9);

        bool changed = readout.Update(bpm: 130.0, confidence: 0.9);

        Assert.True(changed);
        Assert.Equal(130.0, readout.DisplayedBpm);
    }

    [Fact]
    public void ResetClearsTheValue()
    {
        var readout = new BpmReadout(confidenceFloor: 0.1);
        readout.Update(bpm: 128.0, confidence: 0.9);

        readout.Reset();

        Assert.False(readout.HasValue);
        Assert.Null(readout.DisplayedBpm);
    }

    [Fact]
    public void UpdateReturnsTrueOnlyWhenDisplayedValueChanges()
    {
        var readout = new BpmReadout(confidenceFloor: 0.1, changeThresholdBpm: 0.3);

        Assert.True(readout.Update(bpm: 128.0, confidence: 0.9));   // first adoption
        Assert.False(readout.Update(bpm: 128.05, confidence: 0.9)); // within hysteresis
        Assert.True(readout.Update(bpm: 132.0, confidence: 0.9));   // beyond hysteresis
    }
}
