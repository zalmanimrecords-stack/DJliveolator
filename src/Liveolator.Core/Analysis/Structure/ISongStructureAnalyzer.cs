using System.Threading;
using System.Threading.Tasks;

namespace Liveolator.Core.Analysis.Structure;

/// <summary>
/// Offline song-structure segmentation seam (doc 32). The concrete implementation runs a Python +
/// librosa subprocess and lives in Liveolator.Media; Core depends only on this interface so it stays
/// pure managed and unit-tests with a fake. Invoked at import/analysis time only — never on the
/// realtime BASS path.
/// </summary>
public interface ISongStructureAnalyzer
{
    /// <summary>
    /// Detects the musical structure of <paramref name="filePath"/>. Returns <c>null</c> when structure
    /// analysis is unavailable (Python runtime / librosa absent, subprocess failure, or unparsable
    /// output) — callers degrade gracefully to the heuristic path. Never throws on the analysis path.
    /// </summary>
    /// <param name="decoder">The offline decoder for the file format (part of the locked seam contract;
    /// an implementation may use it to materialize PCM, or read the file directly).</param>
    /// <param name="filePath">Path to the audio file to analyze.</param>
    Task<SongStructure?> AnalyzeAsync(
        IAudioDecoder decoder, string filePath, CancellationToken cancellationToken = default);
}
