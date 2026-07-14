namespace Liveolator.Core.Analysis;

/// <summary>
/// Decode seam (doc 16): turns an audio file into mono PCM for offline analysis. The concrete
/// implementation (FFmpeg) lives in Liveolator.Audio; Core depends only on this interface so
/// it stays platform- and library-independent and unit-tests with a fake decoder.
/// </summary>
public interface IAudioDecoder
{
    /// <summary>True if this decoder can handle the given file (by extension/probe).</summary>
    bool CanDecode(string filePath);

    /// <summary>
    /// Streams the whole file as mono PCM blocks resampled to <paramref name="targetSampleRate"/>.
    /// Blocks avoid loading large files entirely into memory.
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, CancellationToken cancellationToken = default);
}
