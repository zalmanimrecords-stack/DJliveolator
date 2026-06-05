using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Converts a raw <see cref="MidiMessage"/> into the <see cref="PerformanceAction.Value"/> a
/// binding expects, per its <see cref="ActionInputMode"/>, <see cref="ValueCurve"/>, and
/// <see cref="RelativeEncoding"/>. Pure math, isolated here so each conversion rule is tested on
/// its own (doc 05).
/// </summary>
public static class ControlValueConverter
{
    private const int MaxData = 127;
    private const int PitchBendCenter = 8192; // 14-bit center

    /// <summary>Produces the action value for <paramref name="message"/> under <paramref name="binding"/>.</summary>
    public static double ToActionValue(MidiMessage message, ControllerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(binding);

        return binding.InputMode switch
        {
            ActionInputMode.Absolute => Absolute(message, binding.Curve),
            ActionInputMode.Relative => DecodeRelative(message.Data2, binding.Relative),
            // Momentary/Toggle ignore magnitude, but note velocity is still made available.
            _ => message.Type is MidiMessageType.NoteOn or MidiMessageType.NoteOff
                ? Normalize(message.Data2)
                : 0,
        };
    }

    /// <summary>Decodes a 7-bit endless-encoder value into a signed step.</summary>
    public static int DecodeRelative(int data2, RelativeEncoding encoding) => encoding switch
    {
        // 0..63 are positive steps; 64..127 wrap to negative (two's complement of a 7-bit value).
        RelativeEncoding.TwosComplement => data2 < 64 ? data2 : data2 - 128,
        RelativeEncoding.OffsetBinary => data2 - 64,
        RelativeEncoding.SignedBit => (data2 & 0x40) != 0 ? -(data2 & 0x3F) : data2 & 0x3F,
        _ => 0,
    };

    /// <summary>Applies a curve to a normalized 0..1 value.</summary>
    public static double ApplyCurve(double normalized, ValueCurve curve) => curve switch
    {
        ValueCurve.Exponential => normalized * normalized,
        ValueCurve.Logarithmic => Math.Sqrt(normalized),
        _ => normalized,
    };

    private static double Absolute(MidiMessage message, ValueCurve curve)
    {
        if (message.Type == MidiMessageType.PitchBend)
        {
            // 14-bit bipolar: MSB in Data2, LSB in Data1, centered at 8192 → -1..1.
            int raw = (message.Data2 << 7) | (message.Data1 & MaxData);
            return Math.Clamp((raw - PitchBendCenter) / (double)PitchBendCenter, -1.0, 1.0);
        }

        return ApplyCurve(Normalize(message.Data2), curve);
    }

    private static double Normalize(int data) => Math.Clamp(data / (double)MaxData, 0.0, 1.0);
}
