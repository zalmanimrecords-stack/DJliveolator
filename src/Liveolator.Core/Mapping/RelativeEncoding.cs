namespace Liveolator.Core.Mapping;

/// <summary>
/// How an endless encoder encodes a signed step into a 7-bit CC value. Encoding varies by device,
/// so it is explicit per binding and learnable (doc 05). Two's-complement is the most common
/// default.
/// </summary>
public enum RelativeEncoding
{
    /// <summary>7-bit two's complement: 1..63 = +1..+63, 127..65 = -1..-63 (0x7F = -1).</summary>
    TwosComplement,

    /// <summary>Offset-64: value − 64, so 65 = +1, 63 = -1, 64 = 0.</summary>
    OffsetBinary,

    /// <summary>Sign-and-magnitude: bit 6 is the sign, low 6 bits the magnitude (65 = -1).</summary>
    SignedBit,
}
