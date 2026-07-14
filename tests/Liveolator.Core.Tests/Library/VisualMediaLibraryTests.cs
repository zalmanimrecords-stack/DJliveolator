using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class VisualMediaLibraryTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static ScannedFile File(string path) => new(path, 1000, T);

    [Fact]
    public async Task Scan_ClassifiesAndProbes_ImagesAndVideos()
    {
        var enumerator = new FakeFileEnumerator(File("pic.jpg"), File("clip.mp4"));
        var library = new VisualMediaLibrary(enumerator, new FakeVisualProbe());

        await library.ScanAsync(new[] { "media" });

        Assert.Equal(2, library.Count);

        VisualAsset pic = library.TryGet("pic.jpg")!;
        Assert.Equal(VisualMediaKind.Image, pic.Kind);
        Assert.Equal(MediaAnalysisStatus.Ok, pic.Status);
        Assert.Null(pic.Info!.Value.Duration); // images have no duration

        VisualAsset clip = library.TryGet("clip.mp4")!;
        Assert.Equal(VisualMediaKind.Video, clip.Kind);
        Assert.NotNull(clip.Info!.Value.Duration);
    }

    [Fact]
    public async Task OfKind_FiltersByKind()
    {
        var enumerator = new FakeFileEnumerator(File("a.png"), File("b.gif"), File("v.webm"));
        var library = new VisualMediaLibrary(enumerator, new FakeVisualProbe());
        await library.ScanAsync(new[] { "media" });

        Assert.Equal(2, library.OfKind(VisualMediaKind.Image).Count);
        Assert.Single(library.OfKind(VisualMediaKind.Video));
    }

    [Fact]
    public async Task Scan_UnreadableFile_MarkedFailed_OthersOk()
    {
        var probe = new FakeVisualProbe();
        probe.FailPaths.Add("bad.png");
        var enumerator = new FakeFileEnumerator(File("good.jpg"), File("bad.png"));
        var library = new VisualMediaLibrary(enumerator, probe);

        await library.ScanAsync(new[] { "media" });

        Assert.Equal(MediaAnalysisStatus.Ok, library.TryGet("good.jpg")!.Status);
        VisualAsset bad = library.TryGet("bad.png")!;
        Assert.Equal(MediaAnalysisStatus.Failed, bad.Status);
        Assert.False(string.IsNullOrEmpty(bad.Error));
    }

    [Fact]
    public async Task Remove_DropsOneAsset_LeavingTheRest()
    {
        var enumerator = new FakeFileEnumerator(File("keep.png"), File("drop.png"));
        var library = new VisualMediaLibrary(enumerator, new FakeVisualProbe());
        await library.ScanAsync(new[] { "media" });

        bool removed = library.Remove("drop.png");

        Assert.True(removed);
        Assert.Equal(1, library.Count);
        Assert.Null(library.TryGet("drop.png"));
        Assert.NotNull(library.TryGet("keep.png"));
    }

    [Fact]
    public async Task Remove_UnknownPath_ReturnsFalse_AndKeepsCatalog()
    {
        var enumerator = new FakeFileEnumerator(File("keep.png"));
        var library = new VisualMediaLibrary(enumerator, new FakeVisualProbe());
        await library.ScanAsync(new[] { "media" });

        Assert.False(library.Remove("ghost.png"));
        Assert.Equal(1, library.Count);
    }

    [Fact]
    public async Task Scan_Incremental_DoesNotReprobeUnchanged()
    {
        var enumerator = new FakeFileEnumerator(File("pic.jpg"));
        var probe = new FakeVisualProbe();
        var library = new VisualMediaLibrary(enumerator, probe);

        await library.ScanAsync(new[] { "media" });
        await library.ScanAsync(new[] { "media" });

        Assert.Equal(1, probe.Calls);
    }
}
