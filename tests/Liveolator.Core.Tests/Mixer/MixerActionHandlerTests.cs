using System;
using Liveolator.Core.Actions;
using Liveolator.Core.Mixer;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Core.Tests.Mixer;

public class MixerActionHandlerTests
{
    private const double Tol = 1e-9;

    private static MixerActionHandler NewHandler(out FakeMixer mixer)
    {
        mixer = new FakeMixer();
        return new MixerActionHandler(mixer);
    }

    [Fact]
    public void HandledKinds_AreTheSevenMixerKinds()
    {
        MixerActionHandler handler = NewHandler(out _);

        Assert.Contains(PerformanceActionKind.MixerCrossfade, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerChannelGain, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerEqBand, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerFilter, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerCueToggle, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerCueLevel, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerCueMix, handler.HandledKinds);
        Assert.Equal(7, handler.HandledKinds.Count);
    }

    [Fact]
    public void Crossfade_Absolute_UpdatesStateAndPushesBothDeckGains()
    {
        MixerActionHandler handler = NewHandler(out FakeMixer mixer);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Value: 0.0));

        Assert.Equal(0.0, handler.State.Crossfader, Tol);
        // Full deck A: A at full, B silent (default linear-equivalent at the ends).
        Assert.Equal(1.0, mixer.DeckGain[MixerState.DeckA], Tol);
        Assert.Equal(0.0, mixer.DeckGain[MixerState.DeckB], Tol);
    }

    [Fact]
    public void Crossfade_Relative_AppliesDeltaAndClamps()
    {
        MixerActionHandler handler = NewHandler(out _);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Relative, Value: -1.0));

        Assert.Equal(0.0, handler.State.Crossfader, Tol); // 0.5 - 1.0 clamped to 0
    }

    [Fact]
    public void ChannelGain_UpdatesDeckSlotAndPushesCombinedGain()
    {
        MixerActionHandler handler = NewHandler(out FakeMixer mixer);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerChannelGain, ActionInputMode.Absolute, Value: 0.5,
            Slot: MixerState.DeckA));

        Assert.Equal(0.5, handler.State.Channel(MixerState.DeckA).Gain, Tol);
        // Center crossfader (smooth) → ~0.707; combined ~0.3536.
        Assert.Equal(0.5 * Math.Sqrt(0.5), mixer.DeckGain[MixerState.DeckA], 1e-6);
    }

    [Fact]
    public void ChannelGain_RaisesValueFeedbackForUiAndMidi()
    {
        MixerActionHandler handler = NewHandler(out _);
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, e) => feedback = e;

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerChannelGain,
            ActionInputMode.Absolute,
            Value: 0.25,
            Slot: MixerState.DeckB));

        Assert.Equal(PerformanceActionKind.MixerChannelGain, feedback!.Kind);
        Assert.Equal(MixerState.DeckB, feedback.Slot);
        Assert.Equal(0.25, feedback.State.Value, Tol);
    }

    [Theory]
    [InlineData("Low", EqBand.Low)]
    [InlineData("Mid", EqBand.Mid)]
    [InlineData("High", EqBand.High)]
    public void EqBand_TargetsBandFromArgument(string argument, EqBand expected)
    {
        MixerActionHandler handler = NewHandler(out FakeMixer mixer);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, Value: 0.9,
            Slot: MixerState.DeckB, Argument: argument));

        Assert.True(mixer.Eq.ContainsKey((MixerState.DeckB, expected)));
        double bandValue = expected switch
        {
            EqBand.Low => handler.State.Channel(MixerState.DeckB).Eq.Low,
            EqBand.Mid => handler.State.Channel(MixerState.DeckB).Eq.Mid,
            _ => handler.State.Channel(MixerState.DeckB).Eq.High,
        };
        Assert.Equal(0.9, bandValue, Tol);
    }

    [Fact]
    public void EqBand_RaisesValueAndBandFeedback()
    {
        MixerActionHandler handler = NewHandler(out _);
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, e) => feedback = e;

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerEqBand,
            ActionInputMode.Absolute,
            Value: 0.7,
            Slot: MixerState.DeckA,
            Argument: "Mid"));

        Assert.Equal(PerformanceActionKind.MixerEqBand, feedback!.Kind);
        Assert.Equal("Mid", feedback.State.Argument);
        Assert.Equal(0.7, feedback.State.Value, Tol);
    }

    [Fact]
    public void EqBand_MissingOrBadArgument_Throws()
    {
        MixerActionHandler handler = NewHandler(out _);

        Assert.Throws<ArgumentException>(() => handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, Value: 0.5, Slot: 0)));
        Assert.Throws<ArgumentException>(() => handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, Value: 0.5, Slot: 0, Argument: "Bogus")));
    }

    [Fact]
    public void Filter_UpdatesStateAndPushesCoefficients()
    {
        MixerActionHandler handler = NewHandler(out FakeMixer mixer);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute, Value: 0.1, Slot: MixerState.DeckA));

        Assert.Equal(0.1, handler.State.Channel(MixerState.DeckA).Filter, Tol);
        Assert.True(mixer.Filter.ContainsKey(MixerState.DeckA));
    }

    [Fact]
    public void Filter_RaisesValueFeedback()
    {
        MixerActionHandler handler = NewHandler(out _);
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, e) => feedback = e;

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerFilter,
            ActionInputMode.Absolute,
            Value: 0.8,
            Slot: MixerState.DeckA));

        Assert.Equal(0.8, feedback!.State.Value, Tol);
    }

    [Fact]
    public void CueToggle_FlipsRoutingAndRaisesFeedback()
    {
        MixerActionHandler handler = NewHandler(out FakeMixer mixer);
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, e) => feedback = e;

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueToggle, ActionInputMode.Toggle, Slot: MixerState.DeckB));

        Assert.True(handler.State.Channel(MixerState.DeckB).CueEnabled);
        Assert.True(mixer.Cue[MixerState.DeckB]);
        Assert.NotNull(feedback);
        Assert.True(feedback!.State.IsActive);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueToggle, ActionInputMode.Toggle, Slot: MixerState.DeckB));
        Assert.False(handler.State.Channel(MixerState.DeckB).CueEnabled);
    }

    [Fact]
    public void CueLevel_Absolute_UpdatesStateAndPushesScaledOutputGains()
    {
        MixerActionHandler handler = NewHandler(out FakeMixer mixer);

        // Default cue mix is full-cue (blend cue=1, master=0); level 0.5 scales the cue leg.
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueLevel, ActionInputMode.Absolute, Value: 0.5));

        Assert.Equal(0.5, handler.State.CueBus.Level, Tol);
        Assert.NotNull(mixer.CueOutputGains);
        Assert.Equal(0.5, mixer.CueOutputGains!.Value.CueGain, 1e-6);
        Assert.Equal(0.0, mixer.CueOutputGains!.Value.MasterGain, 1e-6);
    }

    [Fact]
    public void CueLevel_Clamps()
    {
        MixerActionHandler handler = NewHandler(out _);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueLevel, ActionInputMode.Absolute, Value: 2.0));

        Assert.Equal(1.0, handler.State.CueBus.Level, Tol);
    }

    [Fact]
    public void CueMix_Center_PushesEqualPowerOutputGains()
    {
        MixerActionHandler handler = NewHandler(out FakeMixer mixer);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueMix, ActionInputMode.Absolute, Value: 0.5));

        Assert.Equal(0.5, handler.State.CueBus.Mix, Tol);
        Assert.NotNull(mixer.CueOutputGains);
        // Level is 1.0 by default, so the output gains equal the equal-power blend.
        Assert.Equal(Math.Sqrt(0.5), mixer.CueOutputGains!.Value.CueGain, 1e-6);
        Assert.Equal(Math.Sqrt(0.5), mixer.CueOutputGains!.Value.MasterGain, 1e-6);
    }

    [Fact]
    public void CueMix_Relative_AppliesDelta()
    {
        MixerActionHandler handler = NewHandler(out _);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueMix, ActionInputMode.Relative, Value: 0.25));

        Assert.Equal(0.25, handler.State.CueBus.Mix, Tol); // from FullCue (0) + 0.25
    }

    [Fact]
    public void CueLevelAndMix_ReportFeedback()
    {
        MixerActionHandler handler = NewHandler(out _);
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueLevel, ActionInputMode.Absolute, Value: 0.7));
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueMix, ActionInputMode.Absolute, Value: 0.3));

        ActionFeedbackState level = handler.GetFeedback(PerformanceActionKind.MixerCueLevel, slot: 0);
        ActionFeedbackState mix = handler.GetFeedback(PerformanceActionKind.MixerCueMix, slot: 0);
        Assert.True(level.IsAvailable);
        Assert.Equal(0.7, level.Value, Tol);
        Assert.True(mix.IsAvailable);
        Assert.Equal(0.3, mix.Value, Tol);
    }

    [Fact]
    public void BadDeckSlot_Throws()
    {
        MixerActionHandler handler = NewHandler(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() => handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerChannelGain, ActionInputMode.Absolute, Value: 0.5, Slot: 7)));
    }

    [Fact]
    public void Feedback_ReportsCrossfaderAndCueState()
    {
        MixerActionHandler handler = NewHandler(out _);
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Value: 0.25));

        ActionFeedbackState xf = handler.GetFeedback(PerformanceActionKind.MixerCrossfade, slot: 0);
        Assert.True(xf.IsAvailable);
        Assert.Equal(0.25, xf.Value, Tol);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCueToggle, ActionInputMode.Toggle, Slot: MixerState.DeckA));
        ActionFeedbackState cue = handler.GetFeedback(PerformanceActionKind.MixerCueToggle, MixerState.DeckA);
        Assert.True(cue.IsActive);
    }

    [Fact]
    public void RoutesThroughDispatcher_EndToEnd()
    {
        var mixer = new FakeMixer();
        var dispatcher = new PerformanceActionDispatcher(
            new[] { new MixerActionHandler(mixer) },
            NullLogger<PerformanceActionDispatcher>.Instance);

        dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Value: 1.0));

        Assert.Equal(0.0, mixer.DeckGain[MixerState.DeckA], Tol);
        Assert.Equal(1.0, mixer.DeckGain[MixerState.DeckB], Tol);
    }
}
