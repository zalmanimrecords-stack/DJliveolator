namespace Liveolator.Core.Automix;

/// <summary>
/// One evaluated step of an auto-mix style profile: the mixer parameters the style wants at a given
/// transition progress. Every field is nullable — null means "this style does not touch that
/// parameter", so the performer's own setting stands. All values are the same normalized 0..1 the
/// mixer actions speak (EQ/filter: 0.5 = flat/center).
/// </summary>
/// <param name="Crossfader">Crossfader position (0 = full deck A, 1 = full deck B).</param>
/// <param name="FromLow">Outgoing deck low-band EQ.</param>
/// <param name="FromMid">Outgoing deck mid-band EQ.</param>
/// <param name="FromHigh">Outgoing deck high-band EQ.</param>
/// <param name="FromFilter">Outgoing deck single-knob filter (0.5 = off).</param>
/// <param name="ToLow">Incoming deck low-band EQ.</param>
/// <param name="ToMid">Incoming deck mid-band EQ.</param>
/// <param name="ToHigh">Incoming deck high-band EQ.</param>
/// <param name="ToFilter">Incoming deck single-knob filter (0.5 = off).</param>
public sealed record AutomixFrame(
    double? Crossfader = null,
    double? FromLow = null,
    double? FromMid = null,
    double? FromHigh = null,
    double? FromFilter = null,
    double? ToLow = null,
    double? ToMid = null,
    double? ToHigh = null,
    double? ToFilter = null);

/// <summary>
/// The fixed geometry of one transition, captured when it starts: which crossfader extreme each deck
/// owns and how many beats the blend spans (length in bars × beats per bar). Style profiles are pure
/// functions of (progress, shape).
/// </summary>
/// <param name="FromSide">Crossfader position that is 100% the outgoing deck (0 or 1).</param>
/// <param name="ToSide">Crossfader position that is 100% the incoming deck (1 or 0).</param>
/// <param name="BeatsTotal">Total transition length in beats — lets a style spread a move over exactly one beat.</param>
public sealed record AutomixTransitionShape(double FromSide, double ToSide, double BeatsTotal);
