namespace Liveolator.Core.Dsp;

/// <summary>
/// The user-facing controls of the master "smart limiter" (doc 11) — the small, opinionated control set
/// a live DJ actually wants, kept deliberately tiny so it reads at a glance on the mixer:
/// <list type="bullet">
/// <item><see cref="SmartRelease"/> — the SAFE↔SMART toggle. Off = the known, predictable fixed-release
/// brick-wall limiter; on = the release time adapts to what is playing (the "fits itself" behaviour).</item>
/// <item><see cref="Character"/> — one knob from 0 (Transparent: longer releases, gentle) to 1 (Punchy:
/// faster releases) that biases the adaptive-release range. Ignored when <see cref="SmartRelease"/> is off.</item>
/// <item><see cref="CeilingDbTp"/> — the true-peak output ceiling in dBTP; always at or below 0.</item>
/// </list>
/// </summary>
/// <remarks>
/// The "smart" behaviour is <b>program-dependent release only</b> (per the live-DJ advisor): dense,
/// constantly-limiting material gets a longer release (no pumping on 4-on-the-floor kicks) while sparse
/// material gets a faster, more transparent recovery. It deliberately does <em>not</em> change the
/// look-ahead, so the limiter's latency stays constant and the shared audio↔visual beat clock stays
/// aligned (doc 00/03/11). Loudness auto-gain is a separate, later concern and is intentionally not here.
/// Pure data; <see cref="MasterLimiter.ApplySettings"/> clamps every field defensively before use.
/// </remarks>
/// <param name="SmartRelease">When true, the release time adapts to the program material; when false the
/// limiter uses its fixed release (the predictable brick-wall fallback).</param>
/// <param name="Character">Adaptive-release bias, 0 (transparent) .. 1 (punchy). Clamped on apply.</param>
/// <param name="CeilingDbTp">True-peak output ceiling in dB; must be ≤ 0. Clamped on apply.</param>
public sealed record LimiterSettings(bool SmartRelease, double Character, double CeilingDbTp)
{
    /// <summary>The product default: smart release on, balanced character, broadcast −1.0 dBTP ceiling.</summary>
    public static LimiterSettings Default { get; } = new(SmartRelease: true, Character: 0.5, CeilingDbTp: -1.0);
}
