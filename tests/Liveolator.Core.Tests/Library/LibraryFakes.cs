using System.IO;
using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Core.Tests.Library;

/// <summary>In-memory file enumerator; mutate <see cref="Files"/> to simulate filesystem changes.</summary>
internal sealed class FakeFileEnumerator : IFileEnumerator
{
    public List<ScannedFile> Files { get; }

    public FakeFileEnumerator(params ScannedFile[] files) => Files = files.ToList();

    public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
        => Files.Where(f => extensions.Contains(Path.GetExtension(f.Path)));
}

/// <summary>Decoder mapping path → PCM; a null mapping throws to simulate a corrupt file. Counts calls.</summary>
internal sealed class MapAudioDecoder : IAudioDecoder
{
    private readonly Dictionary<string, float[]?> _byPath;
    public Dictionary<string, int> DecodeCalls { get; } = new(StringComparer.OrdinalIgnoreCase);

    public MapAudioDecoder(Dictionary<string, float[]?> byPath) => _byPath = byPath;

    public bool CanDecode(string filePath) => true;

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DecodeCalls[filePath] = DecodeCalls.GetValueOrDefault(filePath) + 1;
        if (!_byPath.TryGetValue(filePath, out float[]? pcm) || pcm is null)
            throw new InvalidDataException($"cannot decode '{filePath}'");

        const int block = 4096;
        for (int offset = 0; offset < pcm.Length; offset += block)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int len = Math.Min(block, pcm.Length - offset);
            yield return new ReadOnlyMemory<float>(pcm, offset, len);
            await Task.Yield();
        }
    }
}

/// <summary>Metadata reader mapping path → metadata; paths in <see cref="ThrowPaths"/> throw
/// (to prove the scan survives a misbehaving reader); unmapped paths return null.</summary>
internal sealed class FakeTrackMetadataReader : ITrackMetadataReader
{
    private readonly Dictionary<string, TrackMetadata?> _byPath;
    public HashSet<string> ThrowPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeTrackMetadataReader(Dictionary<string, TrackMetadata?>? byPath = null)
        => _byPath = byPath ?? new(StringComparer.OrdinalIgnoreCase);

    public TrackMetadata? Read(string filePath)
    {
        if (ThrowPaths.Contains(filePath))
            throw new InvalidOperationException($"reader blew up on '{filePath}'");
        return _byPath.TryGetValue(filePath, out TrackMetadata? meta) ? meta : null;
    }
}

/// <summary>Probe returning fixed metadata; paths in <see cref="FailPaths"/> throw. Counts calls.</summary>
internal sealed class FakeVisualProbe : IVisualMediaProbe
{
    public HashSet<string> FailPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Calls { get; private set; }

    public Task<VisualMediaInfo> ProbeAsync(string filePath, VisualMediaKind kind, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (FailPaths.Contains(filePath))
            throw new InvalidDataException($"bad visual '{filePath}'");

        VisualMediaInfo info = kind == VisualMediaKind.Video
            ? new VisualMediaInfo(1920, 1080, TimeSpan.FromSeconds(12))
            : new VisualMediaInfo(1920, 1080, Duration: null);
        return Task.FromResult(info);
    }
}
