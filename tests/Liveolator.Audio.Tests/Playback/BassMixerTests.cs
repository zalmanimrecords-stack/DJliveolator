using System;
using System.Collections.Generic;
using Liveolator.Audio.Playback;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

public class BassMixerTests
{
    private sealed class FakeChannel : IBassMixerChannel
    {
        public DeckLevel Level { get; set; } = DeckLevel.Silent;
        public double? Volume { get; private set; }
        public Dictionary<EqBand, BiquadCoefficients> Eq { get; } = new();
        public BiquadCoefficients? Filter { get; private set; }
        public bool? Cue { get; private set; }

        public void SetVolume(double linearGain) => Volume = linearGain;
        public void SetEqBand(EqBand band, BiquadCoefficients coefficients) => Eq[band] = coefficients;
        public void SetFilter(BiquadCoefficients coefficients) => Filter = coefficients;
        public void SetCue(bool enabled) => Cue = enabled;
    }

    [Fact]
    public void GetLevel_ReturnsRegisteredChannelSnapshot_OrSilence()
    {
        var mixer = new BassMixer(deckCount: 2);
        var deckA = new FakeChannel { Level = new DeckLevel(0.8, 0.4) };
        mixer.SetChannel(0, deckA);

        Assert.Equal(deckA.Level, mixer.GetLevel(0));
        Assert.Equal(DeckLevel.Silent, mixer.GetLevel(1));
    }

    [Fact]
    public void ForwardsCallsToTheRegisteredChannelForEachSlot()
    {
        var mixer = new BassMixer(deckCount: 2);
        var deckA = new FakeChannel();
        var deckB = new FakeChannel();
        mixer.SetChannel(0, deckA);
        mixer.SetChannel(1, deckB);

        mixer.SetDeckGain(0, 0.75);
        mixer.SetEqBand(1, EqBand.High, new BiquadCoefficients(1, 0, 0, 0, 0));
        mixer.SetFilter(0, BiquadCoefficients.Bypass);
        mixer.SetCue(1, true);

        Assert.Equal(0.75, deckA.Volume);
        Assert.True(deckB.Eq.ContainsKey(EqBand.High));
        Assert.Equal(BiquadCoefficients.Bypass, deckA.Filter);
        Assert.True(deckB.Cue);
    }

    [Fact]
    public void UnregisteredSlot_DropsCallWithoutThrowing()
    {
        var mixer = new BassMixer(deckCount: 2);

        // No channel registered for slot 0 yet — must not throw.
        mixer.SetDeckGain(0, 0.5);
        mixer.SetCue(0, true);
    }

    [Fact]
    public void OutOfRangeSlot_Throws()
    {
        var mixer = new BassMixer(deckCount: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetDeckGain(2, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetChannel(-1, new FakeChannel()));
    }

    [Fact]
    public void DrivenByTheCoreHandler_EndToEnd()
    {
        // The Core action handler computes the math; the BASS mixer routes it. Prove the seam joins.
        var mixer = new BassMixer(deckCount: 2);
        var deckA = new FakeChannel();
        mixer.SetChannel(0, deckA);
        mixer.SetChannel(1, new FakeChannel());

        var handler = new MixerActionHandler(mixer);
        handler.Handle(new Core.Actions.PerformanceAction(
            Core.Actions.PerformanceActionKind.MixerCrossfade,
            Core.Actions.ActionInputMode.Absolute, Value: 0.0)); // full deck A

        Assert.Equal(1.0, deckA.Volume!.Value, 6);
    }

    // --- SetChannel re-applies gain (crossfader-before-load bug) ---

    [Fact]
    public void SetDeckGain_BeforeChannelRegistered_IsAppliedWhenChannelArrives()
    {
        // Reproduce: user sets crossfader to deck B, then loads a track on deck A.
        // Without the fix, deck A plays at raw volume (1.0) until the crossfader is moved.
        var mixer = new BassMixer(deckCount: 2);

        // Crossfader fully on deck B — deck A gain = 0
        mixer.SetDeckGain(0, 0.0);

        // Now load deck A (no channel was registered when the gain was set)
        var deckA = new FakeChannel();
        mixer.SetChannel(0, deckA);

        // Channel must receive the stored gain immediately — not wait for the next knob move
        Assert.Equal(0.0, deckA.Volume!.Value, 6);
    }

    [Fact]
    public void SetChannel_WithPriorGain_AppliesGainImmediately()
    {
        var mixer = new BassMixer(deckCount: 2);
        mixer.SetDeckGain(0, 0.42);

        var channel = new FakeChannel();
        mixer.SetChannel(0, channel);

        Assert.Equal(0.42, channel.Volume!.Value, 6);
    }

    [Fact]
    public void SetChannel_WithNullChannel_DoesNotThrow()
    {
        // Unloading a deck clears the channel — must be safe
        var mixer = new BassMixer(deckCount: 2);
        mixer.SetDeckGain(0, 0.5);
        mixer.SetChannel(0, new FakeChannel());

        var ex = Record.Exception(() => mixer.SetChannel(0, null));
        Assert.Null(ex);
    }

    [Fact]
    public void SetDeckGain_UpdatesStoredGain_AfterChannelIsRegistered()
    {
        // After the channel is registered, a subsequent gain update must still reach the channel.
        var mixer = new BassMixer(deckCount: 2);
        var channel = new FakeChannel();
        mixer.SetChannel(0, channel);

        mixer.SetDeckGain(0, 0.8);

        Assert.Equal(0.8, channel.Volume!.Value, 6);
    }

    [Fact]
    public void ReplacingChannel_AppliesCurrentStoredGainToNewChannel()
    {
        // When a new track is loaded on a slot that already had a channel, the new channel must
        // also receive the current gain so there's no momentary volume pop.
        var mixer = new BassMixer(deckCount: 2);
        var first = new FakeChannel();
        mixer.SetChannel(0, first);
        mixer.SetDeckGain(0, 0.3);

        // Reload: new channel registered
        var second = new FakeChannel();
        mixer.SetChannel(0, second);

        Assert.Equal(0.3, second.Volume!.Value, 6);
    }
}
