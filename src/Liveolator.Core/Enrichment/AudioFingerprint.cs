namespace Liveolator.Core.Enrichment;

/// <summary>
/// An acoustic fingerprint of an audio file (doc 16): the Chromaprint code plus the track duration,
/// which together identify a recording via AcoustID. Pure data; the computation (running fpcalc) lives
/// in a binding behind <see cref="IAudioFingerprinter"/>.
/// </summary>
/// <param name="Fingerprint">The Chromaprint fingerprint string.</param>
/// <param name="DurationSeconds">Track duration in seconds (AcoustID uses it to disambiguate matches).</param>
public sealed record AudioFingerprint(string Fingerprint, int DurationSeconds);
