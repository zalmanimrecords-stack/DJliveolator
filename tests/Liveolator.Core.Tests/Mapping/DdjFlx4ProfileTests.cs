using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// The Pioneer DDJ-FLX4 default profile is a convenient starting layout, not a hardcoded device
/// driver: every binding is a plain <see cref="ControllerBinding"/> the performer can override via
/// MIDI learn (doc 05/07). These tests pin the documented control coverage and the action/slot/argument
/// conventions the engine handlers expect — not the exact CC numbers (those are learn-overridable).
/// </summary>
public class DdjFlx4ProfileTests
{
    private static ControllerMappingProfile Profile => DdjFlx4Profile.Default;

    [Fact]
    public void Default_HasDeviceHintThatAutoSelectsTheController()
    {
        // The hint must match the controller's USB name so MidiProfileSelector picks it on plug-in.
        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "DDJ-FLX4", new[] { Profile });

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
        ControllerBinding deck1 = SingleFor(PerformanceActionKind.DeckPlayPause, slot: 0);
        ControllerBinding deck2 = SingleFor(PerformanceActionKind.DeckPlayPause, slot: 1);

        Assert.Equal(ActionInputMode.Momentary, deck1.InputMode);
        Assert.Equal(ActionInputMode.Momentary, deck2.InputMode);
    }

    [Fact]
    public void Default_MapsCue_PerDeck_AsMomentary()
    {
        ControllerBinding deck1 = SingleFor(PerformanceActionKind.DeckCue, slot: 0);
        ControllerBinding deck2 = SingleFor(PerformanceActionKind.DeckCue, slot: 1);

        Assert.Equal(ActionInputMode.Momentary, deck1.InputMode);
        Assert.Equal(ActionInputMode.Momentary, deck2.InputMode);
    }

    [Fact]
    public void Default_MapsSyncOnce_PerDeck()
    {
        // SYNC is a one-shot beatmatch (tempo + phase), not a persistent latch — a press fires a
        // momentary DeckSyncOnce, leaving the deck free for manual fine-tuning afterwards.
        ControllerBinding deck1 = SingleFor(PerformanceActionKind.DeckSyncOnce, slot: 0);
        ControllerBinding deck2 = SingleFor(PerformanceActionKind.DeckSyncOnce, slot: 1);

        Assert.Equal(ActionInputMode.Momentary, deck1.InputMode);
        Assert.Equal(ActionInputMode.Momentary, deck2.InputMode);
    }

    [Fact]
    public void Default_MapsTempoFader_PerDeck_AsAbsolutePitch()
    {
        foreach (int slot in new[] { 0, 1 })
        {
            ControllerBinding tempo = SingleFor(PerformanceActionKind.DeckPitch, slot);
            Assert.Equal(MidiMessageType.ControlChange, tempo.TriggerType);
            Assert.Equal(ActionInputMode.Absolute, tempo.InputMode);
        }
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
    public void Default_MapsJogWheels_AsRelativeDeckJog_PerDeck()
    {
        foreach (int slot in new[] { 0, 1 })
        {
            ControllerBinding jog = SingleFor(PerformanceActionKind.DeckJog, slot);
            Assert.Equal(ActionInputMode.Relative, jog.InputMode);
            // Pioneer jog pitch-bend is offset-binary centered on 64.
            Assert.Equal(RelativeEncoding.OffsetBinary, jog.Relative);
            Assert.Equal(128.0, jog.RelativeTicksPerRevolution);
        }
    }

    [Fact]
    public void Default_MapsFourHotCuePads_PerDeck_WithIndexArgument()
    {
        foreach (int slot in new[] { 0, 1 })
        {
            IReadOnlyList<ControllerBinding> pads = Profile.Bindings
                .Where(b => b.Action == PerformanceActionKind.DeckHotCue && b.Slot == slot)
                .ToList();

            Assert.Equal(DdjFlx4Profile.HotCuePadCount, pads.Count);
            Assert.All(pads, b => Assert.Equal(MidiMessageType.NoteOn, b.TriggerType));
            Assert.All(pads, b => Assert.Equal(ActionInputMode.Momentary, b.InputMode));

            // Each pad carries a distinct 0-based hot-cue index in Argument (the deck handler parses it).
            int[] indices = pads
                .Select(b => int.Parse(b.Argument!, System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(i => i)
                .ToArray();
            Assert.Equal(Enumerable.Range(0, DdjFlx4Profile.HotCuePadCount), indices);
        }
    }

    [Fact]
    public void Default_BindingsAllStayWithinTwoDeckSlots()
    {
        // The DDJ-FLX4 default layout is dual-deck; no binding should address a third deck slot.
        Assert.All(Profile.Bindings, b => Assert.InRange(b.Slot, 0, 1));
    }

    private static ControllerBinding SingleFor(PerformanceActionKind kind, int slot)
        => Assert.Single(Profile.Bindings.Where(b => b.Action == kind && b.Slot == slot));
}
