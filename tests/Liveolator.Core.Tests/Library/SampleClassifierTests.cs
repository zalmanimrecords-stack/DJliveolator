using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class SampleClassifierTests
{
    private static readonly HashSet<string> None = new();

    [Fact]
    public void ShortFile_IsSample()
    {
        var kind = SampleClassifier.Classify("/m/stab.wav", TimeSpan.FromSeconds(2), None);
        Assert.Equal(MusicMediaKind.Sample, kind);
    }

    [Fact]
    public void LongFile_IsTrack()
    {
        var kind = SampleClassifier.Classify("/m/song.mp3", TimeSpan.FromMinutes(5), None);
        Assert.Equal(MusicMediaKind.Track, kind);
    }

    [Fact]
    public void UnknownDuration_IsTrack()
    {
        var kind = SampleClassifier.Classify("/m/mystery.mp3", duration: null, None);
        Assert.Equal(MusicMediaKind.Track, kind);
    }

    [Fact]
    public void DurationExactlyAtThreshold_IsTrack()
    {
        // The rule is strictly "< threshold", so a file exactly 30s long is a Track.
        var kind = SampleClassifier.Classify("/m/edge.wav", SampleClassifier.DefaultMaxSampleLength, None);
        Assert.Equal(MusicMediaKind.Track, kind);
    }

    [Fact]
    public void FolderDesignation_OverridesDuration_ForLongFile()
    {
        var sampleFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/m/loops" };
        // A 5-minute file under a designated samples folder is still a Sample (folder override wins).
        var kind = SampleClassifier.Classify("/m/loops/long-loop.wav", TimeSpan.FromMinutes(5), sampleFolders);
        Assert.Equal(MusicMediaKind.Sample, kind);
    }

    [Fact]
    public void FolderDesignation_MatchesOnlyAtPathBoundary()
    {
        var sampleFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/m/loop" };
        // "/m/loops" is a sibling of the designated "/m/loop" — not under it.
        var kind = SampleClassifier.Classify("/m/loops/song.mp3", TimeSpan.FromMinutes(5), sampleFolders);
        Assert.Equal(MusicMediaKind.Track, kind);
    }
}
