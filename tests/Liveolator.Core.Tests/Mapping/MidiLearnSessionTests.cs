using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class MidiLearnSessionTests
{
    private readonly MidiLearnSession _session = new();
    private ControllerBinding? _learned;

    public MidiLearnSessionTests() => _session.Learned += (_, b) => _learned = b;

    [Fact]
    public void Observe_Note_LearnsMomentaryNoteOnBinding()
    {
        _session.Begin(PerformanceActionKind.VisualBlackout, slot: 3);

        _session.Observe(new MidiMessage(MidiMessageType.NoteOn, 2, 36, 127));

        Assert.NotNull(_learned);
        Assert.Equal(MidiMessageType.NoteOn, _learned!.TriggerType);
        Assert.Equal(ActionInputMode.Momentary, _learned.InputMode);
        Assert.Equal(2, _learned.Channel);
        Assert.Equal(36, _learned.Data1);
        Assert.Equal(PerformanceActionKind.VisualBlackout, _learned.Action);
        Assert.Equal(3, _learned.Slot);
        Assert.False(_session.IsArmed);
    }

    [Fact]
    public void Observe_NoteOff_StillLearnsNoteOnTrigger()
    {
        _session.Begin(PerformanceActionKind.BeatLock);

        _session.Observe(new MidiMessage(MidiMessageType.NoteOff, 0, 40, 0));

        Assert.Equal(MidiMessageType.NoteOn, _learned!.TriggerType);
        Assert.Equal(ActionInputMode.Momentary, _learned.InputMode);
    }

    [Fact]
    public void Observe_ControlChange_LearnsAbsoluteBinding()
    {
        _session.Begin(PerformanceActionKind.MixerCrossfade);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 10, 64));

        Assert.Equal(MidiMessageType.ControlChange, _learned!.TriggerType);
        Assert.Equal(ActionInputMode.Absolute, _learned.InputMode);
    }

    [Fact]
    public void Observe_CcButton_PreservesPreferredMomentaryMode()
    {
        _session.Begin(
            PerformanceActionKind.DeckPlayPause,
            preferredInputMode: ActionInputMode.Momentary);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 22, 127));

        Assert.Equal(ActionInputMode.Momentary, _learned!.InputMode);
    }

    [Fact]
    public void Observe_Slider_PreservesPreferredAbsoluteMode()
    {
        _session.Begin(
            PerformanceActionKind.MixerChannelGain,
            preferredInputMode: ActionInputMode.Absolute);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 7, 48));

        Assert.Equal(ActionInputMode.Absolute, _learned!.InputMode);
    }

    [Fact]
    public void Observe_Encoder_PreservesPreferredRelativeMode()
    {
        _session.Begin(
            PerformanceActionKind.BeatNudgeForward,
            preferredInputMode: ActionInputMode.Relative);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 33, 127));

        Assert.Equal(ActionInputMode.Relative, _learned!.InputMode);
    }

    [Fact]
    public void Observe_DefaultsToTwosComplementEncoding()
    {
        _session.Begin(PerformanceActionKind.BeatNudgeForward);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 33, 65));

        Assert.Equal(RelativeEncoding.TwosComplement, _learned!.Relative);
    }

    [Theory]
    [InlineData(RelativeEncoding.OffsetBinary)]
    [InlineData(RelativeEncoding.SignedBit)]
    [InlineData(RelativeEncoding.TwosComplement)]
    public void Observe_CapturesTheChosenRelativeEncoding(RelativeEncoding encoding)
    {
        _session.Begin(PerformanceActionKind.BeatNudgeForward, relativeEncoding: encoding);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 33, 65));

        Assert.Equal(encoding, _learned!.Relative); // an encoder that isn't two's-complement decodes correctly
    }

    [Fact]
    public void Observe_PreservesRelativeTickScaling_OnLearnedEncoder()
    {
        // A jog/encoder learned through the UI must carry its real ticks-per-revolution, not the
        // raw default of 1.0 that makes the learned binding ~128x too sensitive (doc 27).
        _session.Begin(
            PerformanceActionKind.BeatNudgeForward,
            preferredInputMode: ActionInputMode.Relative,
            relativeTicksPerRevolution: 128.0);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 33, 1));

        Assert.Equal(128.0, _learned!.RelativeTicksPerRevolution, precision: 6);

        // The captured binding produces the same sane per-tick delta as a hand-built relative binding.
        double learnedDelta = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.ControlChange, 0, 33, 1), _learned);
        Assert.Equal(1.0 / 128.0, learnedDelta, precision: 9);
    }

    [Fact]
    public void Observe_RelativeWithoutExplicitTicks_DefaultsToSaneScaling()
    {
        // Arming relative learn without passing tick metadata must not bind the raw 1.0 default
        // (one tick = a full revolution); it falls back to a sensible encoder resolution.
        _session.Begin(
            PerformanceActionKind.BeatNudgeForward,
            preferredInputMode: ActionInputMode.Relative);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 33, 1));

        Assert.True(_learned!.RelativeTicksPerRevolution > 1.0,
            "a learned relative encoder must not scrub a whole revolution per tick");
    }

    [Fact]
    public void Observe_AbsoluteLearn_KeepsRawTickDefault()
    {
        // The relative-scaling fallback must not leak into absolute learns (back-compat).
        _session.Begin(PerformanceActionKind.MixerCrossfade);

        _session.Observe(new MidiMessage(MidiMessageType.ControlChange, 0, 10, 64));

        Assert.Equal(ActionInputMode.Absolute, _learned!.InputMode);
        Assert.Equal(1.0, _learned.RelativeTicksPerRevolution, precision: 6);
    }

    [Fact]
    public void Observe_PreservesActionArgument()
    {
        _session.Begin(PerformanceActionKind.DeckHotCue, slot: 1, argument: "4");

        _session.Observe(new MidiMessage(MidiMessageType.NoteOn, 0, 20, 127));

        Assert.Equal("4", _learned!.Argument);
    }

    [Fact]
    public void Observe_WhenNotArmed_DoesNothing()
    {
        _session.Observe(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        Assert.Null(_learned);
    }

    [Fact]
    public void Cancel_Disarms_SoNextMessageIsIgnored()
    {
        _session.Begin(PerformanceActionKind.VisualBlackout);
        _session.Cancel();

        _session.Observe(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        Assert.Null(_learned);
        Assert.False(_session.IsArmed);
    }

    [Fact]
    public void Observe_OnlyCapturesFirstMessageAfterArming()
    {
        _session.Begin(PerformanceActionKind.VisualBlackout);

        _session.Observe(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        _session.Observe(new MidiMessage(MidiMessageType.NoteOn, 0, 99, 127));

        Assert.Equal(36, _learned!.Data1); // second message ignored
    }
}
