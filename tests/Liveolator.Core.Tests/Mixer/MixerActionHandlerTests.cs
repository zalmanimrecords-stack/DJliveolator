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
    public void HandledKinds_AreTheFiveMixerKinds()
    {
        MixerActionHandler handler = NewHandler(out _);

        Assert.Contains(PerformanceActionKind.MixerCrossfade, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerChannelGain, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerEqBand, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerFilter, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.MixerCueToggle, handler.HandledKinds);
        Assert.Equal(5, handler.HandledKinds.Count);
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
