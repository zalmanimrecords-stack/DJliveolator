using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class ControlValueConverterTests
{
    private static ControllerBinding Binding(
        ActionInputMode mode,
        ValueCurve curve = ValueCurve.Linear,
        RelativeEncoding relative = RelativeEncoding.TwosComplement,
        MidiMessageType trigger = MidiMessageType.ControlChange)
        => new(trigger, Channel: 0, Data1: 10, PerformanceActionKind.MixerCrossfade, mode, Curve: curve, Relative: relative);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(127, 1.0)]
    public void Absolute_NormalizesCcTo0To1(int data2, double expected)
    {
        double value = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.ControlChange, 0, 10, data2), Binding(ActionInputMode.Absolute));

        Assert.Equal(expected, value, precision: 6);
    }

    [Fact]
    public void Absolute_AppliesExponentialCurve()
    {
        double value = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.ControlChange, 0, 10, 64),
            Binding(ActionInputMode.Absolute, ValueCurve.Exponential));

        double linear = 64 / 127.0;
        Assert.Equal(linear * linear, value, precision: 6);
    }

    [Theory]
    [InlineData(ValueCurve.Linear, 0.5, 0.5)]
    [InlineData(ValueCurve.Exponential, 0.5, 0.25)]
    [InlineData(ValueCurve.Logarithmic, 0.25, 0.5)]
    public void ApplyCurve_ShapesNormalizedValue(ValueCurve curve, double input, double expected)
        => Assert.Equal(expected, ControlValueConverter.ApplyCurve(input, curve), precision: 6);

    [Theory]
    [InlineData(0, -1.0)]       // raw 0 → fully bent down
    [InlineData(127, -0.9844)]  // raw 127, just above the floor
    public void Absolute_PitchBendIsBipolar_AtData2Zero(int data1, double expected)
    {
        double value = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.PitchBend, 0, data1, 0),
            Binding(ActionInputMode.Absolute, trigger: MidiMessageType.PitchBend));

        Assert.Equal(expected, value, precision: 3);
    }

    [Fact]
    public void Absolute_PitchBendCenterIsZero()
    {
        // Data2 = 64 → 64<<7 = 8192 = center.
        double value = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.PitchBend, 0, 0, 64),
            Binding(ActionInputMode.Absolute, trigger: MidiMessageType.PitchBend));

        Assert.Equal(0.0, value, precision: 6);
    }

    [Theory]
    [InlineData(RelativeEncoding.TwosComplement, 1, 1)]
    [InlineData(RelativeEncoding.TwosComplement, 127, -1)]
    [InlineData(RelativeEncoding.TwosComplement, 64, -64)]
    [InlineData(RelativeEncoding.OffsetBinary, 65, 1)]
    [InlineData(RelativeEncoding.OffsetBinary, 63, -1)]
    [InlineData(RelativeEncoding.OffsetBinary, 64, 0)]
    [InlineData(RelativeEncoding.SignedBit, 3, 3)]
    [InlineData(RelativeEncoding.SignedBit, 67, -3)]
    public void DecodeRelative_HandlesEachEncoding(RelativeEncoding encoding, int data2, int expected)
        => Assert.Equal(expected, ControlValueConverter.DecodeRelative(data2, encoding));

    [Fact]
    public void Relative_ReturnsDecodedDeltaAsValue()
    {
        double value = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.ControlChange, 0, 10, 127),
            Binding(ActionInputMode.Relative));

        Assert.Equal(-1.0, value, precision: 6);
    }

    [Fact]
    public void Momentary_NoteExposesVelocity_ButCcIsZero()
    {
        double noteVelocity = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127),
            Binding(ActionInputMode.Momentary, trigger: MidiMessageType.NoteOn));
        double ccValue = ControlValueConverter.ToActionValue(
            new MidiMessage(MidiMessageType.ControlChange, 0, 10, 127),
            Binding(ActionInputMode.Momentary));

        Assert.Equal(1.0, noteVelocity, precision: 6);
        Assert.Equal(0.0, ccValue, precision: 6);
    }
}
