namespace Liveolator.Core.Enrichment;

/// <summary>
/// Computes an acoustic fingerprint (Chromaprint) for an audio file (doc 16), so a track can be
/// identified by sound rather than by an unreliable filename. The concrete implementation (the
/// <c>fpcalc</c> CLI) lives in a binding; Core depends only on this seam.
/// </summary>
/// <remarks>
/// Offline-first: a missing fpcalc binary, an unreadable file, or a tool error resolves to
/// <c>null</c> — never an exception — so fingerprinting failure degrades to a tag-based lookup (or no
/// enrichment) rather than disrupting the local analysis the app already has.
/// </remarks>
public interface IAudioFingerprinter
{
    /// <summary>Computes the fingerprint for a file, or <c>null</c> when it cannot be produced.</summary>
    Task<AudioFingerprint?> ComputeAsync(string filePath, CancellationToken cancellationToken = default);
}
