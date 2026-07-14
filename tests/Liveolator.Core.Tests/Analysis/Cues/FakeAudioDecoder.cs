using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis;

namespace Liveolator.Core.Tests.Analysis.Cues;

/// <summary>
/// In-memory <see cref="IAudioDecoder"/> for auto-cue tests: streams a fixed PCM buffer in blocks. Can be
/// told it cannot decode (to exercise the unsupported-file path) or to throw mid-decode for a chosen path
/// (to exercise per-track failure isolation in a batch).
/// </summary>
internal sealed class FakeAudioDecoder : IAudioDecoder
{
    private readonly float[] _pcm;
    private readonly bool _canDecode;
    private readonly string? _throwForPath;

    public FakeAudioDecoder(float[] pcm, bool canDecode = true, string? throwForPath = null)
    {
        _pcm = pcm;
        _canDecode = canDecode;
        _throwForPath = throwForPath;
    }

    public bool CanDecode(string filePath) => _canDecode;

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_throwForPath is not null && string.Equals(filePath, _throwForPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"Simulated decode failure for '{filePath}'.");

        const int block = 16_384;
        for (int offset = 0; offset < _pcm.Length; offset += block)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int len = Math.Min(block, _pcm.Length - offset);
            yield return new ReadOnlyMemory<float>(_pcm, offset, len);
        }
    }
}
