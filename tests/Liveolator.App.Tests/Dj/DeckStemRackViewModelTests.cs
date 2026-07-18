using Liveolator.App.Features.Dj;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Stems;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// The DJ PRO stem rack's availability gating: the per-stem knobs are enabled only while the loaded track
/// is a 4-stem deck (tracked from DeckStemGain feedback, which carries IsAvailable = IsStemDeck), and each
/// knob emits an absolute DeckStemGain for its stem + slot.
/// </summary>
public class DeckStemRackViewModelTests
{
    [Fact]
    public void Knobs_AreUnavailable_WhenNoStemDeckLoaded()
    {
        var rack = new DeckStemRackViewModel(new FakeDispatcher(), slot: 0);
        Assert.False(rack.IsAvailable);
    }

    [Fact]
    public void SeedsAvailability_FromCurrentFeedback()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(PerformanceActionKind.DeckStemGain, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 1));

        var rack = new DeckStemRackViewModel(dispatcher, slot: 1);

        Assert.True(rack.IsAvailable);
    }

    [Fact]
    public void Availability_TracksFeedback_ForItsSlotOnly()
    {
        var dispatcher = new FakeDispatcher();
        var rack = new DeckStemRackViewModel(dispatcher, slot: 0);
        Assert.False(rack.IsAvailable);

        // A stem deck loads on slot 0 → knobs enable.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckStemGain, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 1, Argument: nameof(StemKind.Drums)));
        Assert.True(rack.IsAvailable);

        // Feedback for the OTHER slot is ignored.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckStemGain, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: false, Value: 1));
        Assert.True(rack.IsAvailable);

        // A single-file track loads on slot 0 → knobs disable again.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckStemGain, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: false, Value: 1, Argument: nameof(StemKind.Drums)));
        Assert.False(rack.IsAvailable);
    }

    [Fact]
    public void TurningAKnob_EmitsAbsoluteDeckStemGain_ForItsStemAndSlot()
    {
        var dispatcher = new FakeDispatcher();
        var rack = new DeckStemRackViewModel(dispatcher, slot: 1);

        rack.Bass.Value = 0.3;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckStemGain, action.Kind);
        Assert.Equal(1, action.Slot);
        Assert.Equal(nameof(StemKind.Bass), action.Argument);
        Assert.Equal(0.3, action.Value, 3);
    }

    [Fact]
    public void Dispose_StopsTrackingFeedback()
    {
        var dispatcher = new FakeDispatcher();
        var rack = new DeckStemRackViewModel(dispatcher, slot: 0);
        rack.Dispose();

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckStemGain, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 1, Argument: nameof(StemKind.Drums)));

        Assert.False(rack.IsAvailable); // unsubscribed — the echo no longer flips availability
    }
}
