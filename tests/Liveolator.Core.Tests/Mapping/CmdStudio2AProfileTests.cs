using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// The CMD STUDIO 2A default profile is a convenient starting layout, not a hardcoded device driver:
/// every binding is a plain <see cref="ControllerBinding"/> the performer can override via MIDI learn
/// (doc 05/07). These tests pin the documented control coverage and the action/slot/argument
/// conventions the engine handlers expect — not the exact CC numbers (those are learn-overridable).
/// </summary>
public class CmdStudio2AProfileTests
{
    private static ControllerMappingProfile Profile => CmdStudio2AProfile.Default;

    [Fact]
    public void Default_HasDeviceHintThatAutoSelectsTheController()
    {
        // The hint must match the controller's USB name so MidiProfileSelector picks it on plug-in.
        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "CMD Studio 2A", new[] { Profile });

        Assert.Same(Profile, selected);
    }

    [Fact]
    public void Default_BindingsAreUnique_NoSilentConflictWinner()
    {
        // Two bindings on the same trigger would let one silently shadow the other (doc 05). The
        // default layout must be conflict-free out of the box.
        IReadOnlyList<MappingConflict> conflicts = MappingConflictDetector.Detect(Profile);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Default_MapsTransportPlayPause_PerDeck()
    {
        ControllerBinding deckA = SingleFor(PerformanceActionKind.DeckPlayPause, slot: 0);
        ControllerBinding deckB = SingleFor(PerformanceActionKind.DeckPlayPause, slot: 1);

        Assert.Equal(ActionInputMode.Momentary, deckA.InputMode);
        Assert.Equal(ActionInputMode.Momentary, deckB.InputMode);
    }

    [Fact]
    public void Default_MapsSyncOnce_PerDeck()
    {
        ControllerBinding deckA = SingleFor(PerformanceActionKind.DeckSyncOnce, slot: 0);
        ControllerBinding deckB = SingleFor(PerformanceActionKind.DeckSyncOnce, slot: 1);

        Assert.Equal(ActionInputMode.Momentary, deckA.InputMode);
        Assert.Equal(ActionInputMode.Momentary, deckB.InputMode);
    }

    [Fact]
    public void Default_MapsCrossfader_AsAbsoluteControlChange()
    {
        ControllerBinding crossfader = SingleFor(PerformanceActionKind.MixerCrossfade, slot: 0);

        Assert.Equal(MidiMessageType.ControlChange, crossfader.TriggerType);
        Assert.Equal(ActionInputMode.Absolute, crossfader.InputMode);
    }

    [Fact]
    public void Default_MapsChannelGain_ForBothDecks_AsAbsoluteFaders()
    {
        foreach (int slot in new[] { 0, 1 })
        {
            ControllerBinding fader = SingleFor(PerformanceActionKind.MixerChannelGain, slot);
            Assert.Equal(MidiMessageType.ControlChange, fader.TriggerType);
            Assert.Equal(ActionInputMode.Absolute, fader.InputMode);
        }
    }

    [Fact]
    public void Default_MapsThreeBandEq_PerDeck_WithBandArgument()
    {
        foreach (int slot in new[] { 0, 1 })
        {
            IReadOnlyList<ControllerBinding> eq = Profile.Bindings
                .Where(b => b.Action == PerformanceActionKind.MixerEqBand && b.Slot == slot)
                .ToList();

            // Low/Mid/High — three knobs per deck, each tagged by band name (the handler parses it).
            Assert.Equal(3, eq.Count);
            Assert.Contains(eq, b => string.Equals(b.Argument, "Low", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(eq, b => string.Equals(b.Argument, "Mid", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(eq, b => string.Equals(b.Argument, "High", StringComparison.OrdinalIgnoreCase));
            Assert.All(eq, b => Assert.Equal(ActionInputMode.Absolute, b.InputMode));
        }
    }

    [Fact]
    public void Default_MapsFilter_PerDeck_AsAbsolute()
    {
        foreach (int slot in new[] { 0, 1 })
        {
            ControllerBinding filter = SingleFor(PerformanceActionKind.MixerFilter, slot);
            Assert.Equal(ActionInputMode.Absolute, filter.InputMode);
        }
    }

    [Fact]
    public void Default_MapsJogWheels_AsRelativeBeatNudge_PerDeck()
    {
        // Slow jog = tempo/phase nudge (doc 07); an endless jog encodes as a relative control.
        foreach (int slot in new[] { 0, 1 })
        {
            ControllerBinding jog = SingleFor(PerformanceActionKind.BeatNudgeForward, slot);
            Assert.Equal(ActionInputMode.Relative, jog.InputMode);
        }
    }

    [Fact]
    public void Default_BindingsAllStayWithinTwoDeckSlots()
    {
        // The CMD STUDIO 2A is dual-deck; no binding should address a third deck slot.
        Assert.All(Profile.Bindings, b => Assert.InRange(b.Slot, 0, 1));
    }

    private static ControllerBinding SingleFor(PerformanceActionKind kind, int slot)
        => Assert.Single(Profile.Bindings.Where(b => b.Action == kind && b.Slot == slot));
}
