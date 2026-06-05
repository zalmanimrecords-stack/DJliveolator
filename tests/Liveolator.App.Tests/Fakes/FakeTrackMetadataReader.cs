using Liveolator.Core.Library.Music;

namespace Liveolator.App.Tests.Fakes;

/// <summary>Returns fixed metadata per path so view-model tests never read real tags.</summary>
public sealed class FakeTrackMetadataReader : ITrackMetadataReader
{
    private readonly IReadOnlyDictionary<string, TrackMetadata> _byPath;

    public FakeTrackMetadataReader(IReadOnlyDictionary<string, TrackMetadata> byPath)
        => _byPath = byPath;

    public TrackMetadata? Read(string filePath)
        => _byPath.TryGetValue(filePath, out TrackMetadata? meta) ? meta : null;
}
