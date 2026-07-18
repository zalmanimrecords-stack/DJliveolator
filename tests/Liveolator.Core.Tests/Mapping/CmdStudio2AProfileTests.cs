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
    public void EveryAbsoluteControl_EnablesSoftTakeover_SoFadersAndEqDoNotJump()
    {
        // The pickup engine is built + tested but was unreachable: no shipped binding enabled it, so
        // absolute faders/EQ/filter still jumped on a profile/track change (doc 31 #17). Every absolute
        // control on the shipped layout must opt in; relative/momentary controls must not (it is ignored
        // there and would only confuse a future reader).
        List<ControllerBinding> absolute = Profile.Bindings
            .Where(b => b.InputMode == ActionInputMode.Absolute).ToList();

        Assert.NotEmpty(absolute);
        Assert.All(absolute, b => Assert.True(b.SoftTakeover, $"{b.Action} {b.Argument} should pick up"));
        Assert.All(
            Profile.Bindings.Where(b => b.InputMode != ActionInputMode.Absolute),
            b => Assert.False(b.SoftTakeover));
    }

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
    public void Default_MapsSyncLock_PerDeck()
    {
        // SYNC is the top-level sync lock, matching the on-screen SYNC control. Toggle bindings fire only
        // on the press edge, so MIDI note release does not immediately undo the lock.
        ControllerBinding deckA = SingleFor(PerformanceActionKind.DeckSyncToggle, slot: 0);
        ControllerBinding deckB = SingleFor(PerformanceActionKind.DeckSyncToggle, slot: 1);

        Assert.Equal(ActionInputMode.Toggle, deckA.InputMode);
        Assert.Equal(ActionInputMode.Toggle, deckB.InputMode);
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
            // The CMD jog is offset-binary around 64, like Pioneer and every mainstream DJ deck; decoding
            // it as two's-complement made rest (0x40) read as -64 and flipped direction (the jog bug).
            Assert.Equal(RelativeEncoding.OffsetBinary, jog.Relative);
            Assert.Equal(128.0, jog.RelativeTicksPerRevolution);
        }
    }

    [Fact]
    public void UpgradeLegacyJogBindings_PreservesPhysicalControlAndTargetsDeckJog()
    {
        ControllerBinding legacy = new(
            MidiMessageType.ControlChange, Channel: 0, Data1: 0x21,
            PerformanceActionKind.BeatNudgeForward, ActionInputMode.Relative, Slot: 0);
        var profile = new ControllerMappingProfile("saved", "CMD Studio 2A", [legacy]);

        ControllerMappingProfile upgraded = CmdStudio2AProfile.UpgradeLegacyJogBindings(profile);

        ControllerBinding jog = Assert.Single(upgraded.Bindings);
        Assert.Equal(PerformanceActionKind.DeckJog, jog.Action);
        Assert.Equal(128.0, jog.RelativeTicksPerRevolution);
        // The retargeted jog must also land on the DJ-standard offset-binary encoding.
        Assert.Equal(RelativeEncoding.OffsetBinary, jog.Relative);
        Assert.Equal(legacy.Channel, jog.Channel);
        Assert.Equal(legacy.Data1, jog.Data1);
    }

    [Fact]
    public void UpgradeLegacyJogBindings_HealsLearnedTwosComplementJog_ToOffsetBinary_KeyedOnAction()
    {
        // A jog LEARNED before the fix is DeckJog/Relative/TwosComplement on the device's real CC (not the
        // guessed default 0x21). Healing must key on the action, not the CC, and preserve the physical
        // control so the performer never has to re-learn.
        ControllerBinding learned = new(
            MidiMessageType.ControlChange, Channel: 1, Data1: 0x33,
            PerformanceActionKind.DeckJog, ActionInputMode.Relative, Slot: 1,
            Relative: RelativeEncoding.TwosComplement, RelativeTicksPerRevolution: 128.0);
        var profile = new ControllerMappingProfile("saved", "CMD Studio 2A", [learned]);

        ControllerBinding jog = Assert.Single(
            CmdStudio2AProfile.UpgradeLegacyJogBindings(profile).Bindings);

        Assert.Equal(RelativeEncoding.OffsetBinary, jog.Relative);
        Assert.Equal(1, jog.Channel);
        Assert.Equal(0x33, jog.Data1);
        Assert.Equal(PerformanceActionKind.DeckJog, jog.Action);
        Assert.Equal(128.0, jog.RelativeTicksPerRevolution);
    }

    [Fact]
    public void UpgradeLegacyJogBindings_LeavesAHealthyProfileUntouched_SoNoNeedlessReSave()
    {
        // The shipped default is already offset-binary with no legacy jog, so the heal must be a no-op
        // that returns the SAME instance — MidiControlSession only re-saves when the reference changes.
        ControllerMappingProfile healthy = CmdStudio2AProfile.Default;

        Assert.Same(healthy, CmdStudio2AProfile.UpgradeLegacyJogBindings(healthy));
    }

    [Fact]
    public void UpgradeLegacySyncBindings_PreservesLearnedButtonAndTargetsSyncLock()
    {
        // A profile saved while SYNC was one-shot is healed up to the top-level sync lock, keeping the
        // learned physical button (channel/note) the user mapped.
        ControllerBinding legacy = new(
            MidiMessageType.NoteOn, Channel: 3, Data1: 4,
            PerformanceActionKind.DeckSyncOnce, ActionInputMode.Momentary, Slot: 0);
        var profile = new ControllerMappingProfile("saved", "CMD Studio 2A", [legacy]);

        ControllerBinding sync = Assert.Single(
            CmdStudio2AProfile.UpgradeLegacySyncBindings(profile).Bindings);

        Assert.Equal(PerformanceActionKind.DeckSyncToggle, sync.Action);
        Assert.Equal(ActionInputMode.Toggle, sync.InputMode);
        Assert.Equal(3, sync.Channel);
        Assert.Equal(4, sync.Data1);
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
