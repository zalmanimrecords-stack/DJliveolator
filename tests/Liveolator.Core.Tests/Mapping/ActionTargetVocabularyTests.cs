using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class ActionTargetVocabularyTests
{
    /// <summary>
    /// The guard that makes the learn-target list self-maintaining: a kind added to
    /// <see cref="PerformanceActionKind"/> must either describe itself as a bindable target or be
    /// justified in <see cref="ActionTargetVocabulary.NotBindable"/>. Failing here means a new action
    /// has no route to a performer — the exact gap doc 06 recorded for 14 kinds.
    /// </summary>
    [Fact]
    public void EveryActionKind_IsEitherBindable_OrExplicitlyExcluded()
    {
        PerformanceActionKind[] unaccounted = Enum.GetValues<PerformanceActionKind>()
            .Where(kind => !ActionTargetVocabulary.Targets.ContainsKey(kind)
                           && !ActionTargetVocabulary.NotBindable.Contains(kind))
            .ToArray();

        Assert.Empty(unaccounted);
    }

    [Fact]
    public void NoKind_IsBothBindableAndExcluded()
        => Assert.Empty(ActionTargetVocabulary.Targets.Keys
            .Where(ActionTargetVocabulary.NotBindable.Contains));

    [Fact]
    public void EveryTarget_HasANonEmptyLabel()
        => Assert.Empty(ActionTargetVocabulary.Targets
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value.Label)));

    [Fact]
    public void ArgumentCarryingKinds_OfferEveryArgumentValue()
    {
        Assert.Equal(
            ["Low", "Mid", "High"],
            ActionTargetVocabulary.Targets[PerformanceActionKind.MixerEqKill].Arguments);
        Assert.Equal(
            ["Drums", "Bass", "Vocals", "Other"],
            ActionTargetVocabulary.Targets[PerformanceActionKind.DeckStemMute].Arguments);
        Assert.Equal(8, ActionTargetVocabulary.Targets[PerformanceActionKind.DeckHotCue].Arguments!.Count);
    }

    // The hardware facts that must survive generation: a jog is a relative encoder, an EQ band is an
    // absolute knob, and a latch (sync/keylock/quantize) is a toggle rather than a one-shot press.
    [Theory]
    [InlineData(PerformanceActionKind.DeckJog, ActionInputMode.Relative)]
    [InlineData(PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative)]
    [InlineData(PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute)]
    [InlineData(PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute)]
    [InlineData(PerformanceActionKind.DeckSyncToggle, ActionInputMode.Toggle)]
    [InlineData(PerformanceActionKind.DeckKeyLockToggle, ActionInputMode.Toggle)]
    [InlineData(PerformanceActionKind.DeckCuePlay, ActionInputMode.Momentary)]
    [InlineData(PerformanceActionKind.MixerEqKill, ActionInputMode.Momentary)]
    public void InputMode_MatchesTheControlTheKindExpects(
        PerformanceActionKind kind, ActionInputMode expected)
        => Assert.Equal(expected, ActionTargetVocabulary.Targets[kind].InputMode);

    [Theory]
    [InlineData(PerformanceActionKind.DeckPlayPause)]
    [InlineData(PerformanceActionKind.MixerChannelGain)]
    [InlineData(PerformanceActionKind.MixerCueToggle)]
    [InlineData(PerformanceActionKind.PlaylistSkipOnNextBar)]
    public void PerDeckKinds_AreMarkedPerDeck(PerformanceActionKind kind)
        => Assert.True(ActionTargetVocabulary.Targets[kind].PerDeck);

    [Theory]
    [InlineData(PerformanceActionKind.MixerCrossfade)]
    [InlineData(PerformanceActionKind.BeatTapTempo)]
    [InlineData(PerformanceActionKind.VisualBlackout)]
    public void GlobalKinds_AreNotMarkedPerDeck(PerformanceActionKind kind)
        => Assert.False(ActionTargetVocabulary.Targets[kind].PerDeck);
}
