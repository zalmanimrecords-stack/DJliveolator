using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;

namespace Liveolator.App.Tests.Fakes;

/// <summary>
/// Yields a 440 Hz sine for any file, so analysis runs deterministically. The clip runs just past
/// the library's 1-minute minimum-visible filter (and the 30 s sample-vs-track threshold) so a
/// scanned track lands in the visible catalog as a real Track — a 1-second clip would be hidden as a
/// sub-minute sample, which is correct but useless here. Kept as short as the filter allows because
/// offline analysis cost scales with clip length.
/// </summary>
public sealed class FakeAudioDecoder : IAudioDecoder
{
    private const int DurationSeconds = 65;

    public bool CanDecode(string filePath) => true;

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new float[targetSampleRate * DurationSeconds];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (float)Math.Sin(2 * Math.PI * 440 * i / targetSampleRate);

        yield return buffer;
        await Task.CompletedTask;
    }
}
