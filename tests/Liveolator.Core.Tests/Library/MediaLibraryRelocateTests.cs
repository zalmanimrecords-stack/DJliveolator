using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class MediaLibraryRelocateTests
{
    private static readonly DateTime Old = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string OldPath = @"S:\music\song.mp3";
    private const string NewPath = @"D:\local\song.mp3";

    private static MusicLibrary SeededMusicLibrary(out MusicTrack original)
    {
        // A user-locked manual beat grid: the analysis we must not lose when relocating.
        original = new MusicTrack(
            new ScannedFile(OldPath, 1000, Old),
            new BpmResult(124.0, Confidence: 1.0, FirstBeatSeconds: 0.5),
            Key: null,
            Duration: TimeSpan.FromMinutes(4),
            Cues: TrackCues.None,
            Status: MediaAnalysisStatus.Ok,
            Error: null,
            AnalysisIsManual: true);

        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(new[] { original });
        return library;
    }

    [Fact]
    public void Relocate_ReKeysEntryToNewPath_PreservingAnalysis()
    {
        MusicLibrary library = SeededMusicLibrary(out MusicTrack original);
        var newFile = new ScannedFile(NewPath, 1000, New);

        bool relocated = library.Relocate(OldPath, newFile);

        Assert.True(relocated);
        Assert.Null(library.TryGet(OldPath));

        MusicTrack moved = library.TryGet(NewPath)!;
        Assert.Equal(newFile, moved.File);
        // All analysis is carried over untouched.
        Assert.Equal(original.Bpm, moved.Bpm);
        Assert.Equal(original.Duration, moved.Duration);
        Assert.Equal(original.Status, moved.Status);
        Assert.True(moved.AnalysisIsManual);
        Assert.Equal(1, library.Count);
    }

    [Fact]
    public void Relocate_OldPathLookupIsCaseInsensitive()
    {
        MusicLibrary library = SeededMusicLibrary(out _);

        bool relocated = library.Relocate(@"s:\MUSIC\SONG.mp3", new ScannedFile(NewPath, 1000, New));

        Assert.True(relocated);
        Assert.NotNull(library.TryGet(NewPath));
    }

    [Fact]
    public void Relocate_UnknownOldPath_ReturnsFalseAndDoesNotMutate()
    {
        MusicLibrary library = SeededMusicLibrary(out _);

        bool relocated = library.Relocate(@"S:\music\nope.mp3", new ScannedFile(NewPath, 1000, New));

        Assert.False(relocated);
        Assert.NotNull(library.TryGet(OldPath));
        Assert.Null(library.TryGet(NewPath));
        Assert.Equal(1, library.Count);
    }

    [Fact]
    public void Relocate_NullOrEmptyOldPath_Throws()
    {
        MusicLibrary library = SeededMusicLibrary(out _);
        Assert.Throws<ArgumentException>(() => library.Relocate("", new ScannedFile(NewPath, 1000, New)));
        // ThrowIfNullOrWhiteSpace raises ArgumentNullException (an ArgumentException subtype) for null.
        Assert.Throws<ArgumentNullException>(() => library.Relocate(null!, new ScannedFile(NewPath, 1000, New)));
    }

    [Fact]
    public void Relocate_NewFileWithEmptyPath_Throws()
    {
        MusicLibrary library = SeededMusicLibrary(out _);
        Assert.Throws<ArgumentException>(() => library.Relocate(OldPath, new ScannedFile("", 1000, New)));
    }

    [Fact]
    public void Relocate_VisualAsset_PreservesProbedInfo()
    {
        var original = new VisualAsset(
            new ScannedFile(OldPath, 2000, Old),
            VisualMediaKind.Video,
            new VisualMediaInfo(1920, 1080, TimeSpan.FromSeconds(30)),
            MediaAnalysisStatus.Ok,
            Error: null);

        var library = new VisualMediaLibrary(new FakeFileEnumerator(), new FakeVisualProbe());
        library.Restore(new[] { original });

        var newFile = new ScannedFile(@"D:\local\clip.mp4", 2000, New);
        bool relocated = library.Relocate(OldPath, newFile);

        Assert.True(relocated);
        VisualAsset moved = library.TryGet(newFile.Path)!;
        Assert.Equal(newFile, moved.File);
        Assert.Equal(original.Info, moved.Info);
        Assert.Equal(original.Kind, moved.Kind);
    }
}
