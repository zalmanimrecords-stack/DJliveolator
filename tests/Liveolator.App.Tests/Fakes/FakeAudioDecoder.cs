using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;

namespace Liveolator.App.Tests.Fakes;

/// <summary>Yields one second of a 440 Hz sine for any file, so analysis runs deterministically.</summary>
public sealed class FakeAudioDecoder : IAudioDecoder
{
    public bool CanDecode(string filePath) => true;

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new float[targetSampleRate];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (float)Math.Sin(2 * Math.PI * 440 * i / targetSampleRate);

        yield return buffer;
        await Task.CompletedTask;
    }
}
