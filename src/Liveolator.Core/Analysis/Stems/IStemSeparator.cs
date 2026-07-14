using System.Threading;
using System.Threading.Tasks;

namespace Liveolator.Core.Analysis.Stems;

/// <summary>
/// Offline stem-separation seam (doc 32 §2, Phase 2). The concrete implementation runs a Python +
/// Open-Unmix subprocess and lives in Liveolator.Media; Core depends only on this interface so it
/// stays pure managed and unit-tests with a fake. Invoked at import/analysis time only — never on the
/// realtime BASS path.
/// </summary>
public interface IStemSeparator
{
    /// <summary>
    /// Separates <paramref name="filePath"/> into four stems, writing FLAC sidecars to a local cache and
    /// returning their paths. Returns <c>null</c> when separation is unavailable (Python runtime / model
    /// absent, subprocess failure, or unparsable output) — callers degrade gracefully. Never throws on the
    /// separation path.
    /// </summary>
    /// <param name="decoder">The offline decoder for the file format (part of the seam contract, mirroring
    /// <c>ISongStructureAnalyzer</c>; the Open-Unmix impl reads the file directly so it is unused there).</param>
    /// <param name="filePath">Path to the audio file to separate.</param>
    Task<StemSet?> SeparateAsync(
        IAudioDecoder decoder, string filePath, CancellationToken ct = default);
}
