using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// The Ableton Push 1 default profile (doc 06) is a convenient starting layout, not a hardcoded
/// device driver: every binding is a plain <see cref="ControllerBinding"/> the performer can override
/// via MIDI learn (doc 05/06). These tests pin the documented control coverage and the
/// action/slot/argument conventions the visual/beat handlers expect  -  not the exact note/CC numbers
/// (those are learn-overridable, and Push must be in User mode for them to send raw MIDI).
/// </summary>
public class Push1ProfileTests
{
    private static ControllerMappingProfile Profile => Push1Profile.Default;

    [Fact]
    public void Default_HasDeviceHintThatAutoSelectsThePush()
    {
        // The hint must match the controller's USB name so MidiProfileSelector picks it on plug-in.
        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "Ableton Push", new[] { Profile });

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
    public void Default_MapsAllSixtyFourPads_ToDistinctSceneSlots()
    {
        // The 8x8 grid loads visual scenes: pad index 0..63 -> VisualLoadScene(slot = pad index).
        IReadOnlyList<ControllerBinding> scenePads = Profile.Bindings
            .Where(b => b.Action == PerformanceActionKind.VisualLoadScene)
            .ToList();

        Assert.Equal(64, scenePads.Count);

        // Slots cover 0..63 exactly once (the handler addresses a scene by Slot).
        IEnumerable<int> slots = scenePads.Select(b => b.Slot).OrderBy(s => s);
        Assert.Equal(Enumerable.Range(0, 64), slots);

        // Pad LEDs are driven by NoteOn (velocity = color), so the trigger must be a NoteOn press.
        Assert.All(scenePads, b =>
        {
            Assert.Equal(MidiMessageType.NoteOn, b.TriggerType);
            Assert.Equal(ActionInputMode.Momentary, b.InputMode);
        });
    }

    [Fact]
    public void Default_MapsAllEightEncoders_ToDistinctMacroNames_AsAbsolute()
    {
        IReadOnlyList<ControllerBinding> encoders = Profile.Bindings
            .Where(b => b.Action == PerformanceActionKind.VisualSetMacro)
            .ToList();

        Assert.Equal(8, encoders.Count);

        // Each encoder drives one named macro absolutely (doc 06: VisualSetMacro(name, value)).
        Assert.All(encoders, b =>
        {
            Assert.Equal(ActionInputMode.Absolute, b.InputMode);
            Assert.False(string.IsNullOrWhiteSpace(b.Argument));
        });

        // Eight distinct macro names so no two encoders fight over the same parameter.
        int distinctNames = encoders.Select(b => b.Argument).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(8, distinctNames);
    }

    [Theory]
    [InlineData(PerformanceActionKind.BeatTapTempo)]
    [InlineData(PerformanceActionKind.BeatLock)]
    [InlineData(PerformanceActionKind.BeatHalfTempo)]
    [InlineData(PerformanceActionKind.BeatDoubleTempo)]
    [InlineData(PerformanceActionKind.VisualBlackout)]
    [InlineData(PerformanceActionKind.VisualTransitionNow)]
    [InlineData(PerformanceActionKind.VisualTransitionNextBeat)]
    [InlineData(PerformanceActionKind.VisualTransitionNextBar)]
    public void Default_MapsUtilityAndTransitionButton(PerformanceActionKind kind)
    {
        ControllerBinding button = Assert.Single(Profile.Bindings.Where(b => b.Action == kind));

        // Push button LEDs are CC-driven, so these utility actions live on ControlChange triggers.
        Assert.Equal(MidiMessageType.ControlChange, button.TriggerType);
    }

    [Fact]
    public void Default_TapTempo_IsMomentary()
    {
        ControllerBinding tap = Assert.Single(
            Profile.Bindings.Where(b => b.Action == PerformanceActionKind.BeatTapTempo));

        Assert.Equal(ActionInputMode.Momentary, tap.InputMode);
    }

    [Fact]
    public void Default_AllBindingsAreOnUserModeChannelsWithinRange()
    {
        // Push 1 sends on a single channel in User mode; keep every binding to legal 0..15 channels.
        Assert.All(Profile.Bindings, b => Assert.InRange(b.Channel, 0, 15));
    }

    [Fact]
    public void Default_PadNotesAreWithinThePush1GridRange()
    {
        // Push 1's 8x8 grid is notes 36..99. The scene pads must stay inside that physical range.
        IEnumerable<int> padNotes = Profile.Bindings
            .Where(b => b.Action == PerformanceActionKind.VisualLoadScene)
            .Select(b => b.Data1);

        Assert.All(padNotes, note => Assert.InRange(note, 36, 99));
    }
}
