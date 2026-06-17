using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.App.Tests.Mappings;

/// <summary>
/// The per-binding row exposes a compact raw-MIDI identity (CC/Note number + 1-based channel) so a
/// performer can tell two similar controls apart and debug a controller.
/// </summary>
public sealed class MappingBindingViewModelTests
{
    [Fact]
    public void ControlChange_FormatsAsCcNumberAndOneBasedChannel()
    {
        var binding = new ControllerBinding(
            MidiMessageType.ControlChange, Channel: 0, Data1: 21,
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute);

        var vm = new MappingBindingViewModel(binding);

        Assert.Equal("CC 21 ch1", vm.MidiIdentity);
    }

    [Fact]
    public void NoteOn_FormatsAsNoteNumberAndOneBasedChannel()
    {
        var binding = new ControllerBinding(
            MidiMessageType.NoteOn, Channel: 9, Data1: 36,
            PerformanceActionKind.DeckPlayPause, ActionInputMode.Momentary);

        var vm = new MappingBindingViewModel(binding);

        Assert.Equal("Note 36 ch10", vm.MidiIdentity);
    }

    [Fact]
    public void NoteOff_FormatsAsNoteNumber()
    {
        var binding = new ControllerBinding(
            MidiMessageType.NoteOff, Channel: 0, Data1: 60,
            PerformanceActionKind.DeckCue, ActionInputMode.Momentary);

        var vm = new MappingBindingViewModel(binding);

        Assert.Equal("Note 60 ch1", vm.MidiIdentity);
    }

    [Fact]
    public void PitchBend_OmitsTheDataByte_BecauseItIsPerChannel()
    {
        var binding = new ControllerBinding(
            MidiMessageType.PitchBend, Channel: 0, Data1: 0,
            PerformanceActionKind.DeckJog, ActionInputMode.Relative);

        var vm = new MappingBindingViewModel(binding);

        Assert.Equal("PB ch1", vm.MidiIdentity);
    }
}
