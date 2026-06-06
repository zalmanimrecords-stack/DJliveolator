using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class MusicLibraryKindTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int Sr = 44100;

    private static ScannedFile File(string path) => new(path, 1000, T);

    [Fact]
    public async Task Scan_ClassifiesShortAsSample_AndUndecodableAsTrack()
    {
        var enumerator = new FakeFileEnumerator(File("/songs/good.mp3"), File("/songs/bad.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["/songs/good.mp3"] = TestSignals.ClickTrain(120, Sr, 8), // ~4s → Sample (under threshold)
            ["/songs/bad.mp3"] = null,                                // decode throws → Failed, no duration → Track
        });
        var library = new MusicLibrary(enumerator, decoder);

        await library.ScanAsync(new[] { "/songs" });

        Assert.Equal(MusicMediaKind.Sample, library.TryGet("/songs/good.mp3")!.Kind);
        Assert.Equal(MusicMediaKind.Track, library.TryGet("/songs/bad.mp3")!.Kind);

        Assert.Single(library.OfKind(MusicMediaKind.Sample));
        Assert.Single(library.OfKind(MusicMediaKind.Track));
    }

    [Fact]
    public async Task SetSampleFolders_ReclassifiesInPlace_WithoutReDecoding()
    {
        var enumerator = new FakeFileEnumerator(File("/songs/bad.mp3"));
        var decoder = new MapAudioDecoder(new() { ["/songs/bad.mp3"] = null }); // null duration → Track
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "/songs" });

        Assert.Equal(MusicMediaKind.Track, library.TryGet("/songs/bad.mp3")!.Kind);
        int decodesAfterScan = decoder.DecodeCalls.GetValueOrDefault("/songs/bad.mp3");

        // Designate the folder as samples → the file flips to Sample with no extra decode.
        library.SetSampleFolders(new[] { "/songs" });

        Assert.Equal(MusicMediaKind.Sample, library.TryGet("/songs/bad.mp3")!.Kind);
        Assert.Equal(decodesAfterScan, decoder.DecodeCalls.GetValueOrDefault("/songs/bad.mp3"));
    }
}
