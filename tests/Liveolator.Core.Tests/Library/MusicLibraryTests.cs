using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class MusicLibraryTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int Sr = 44100;

    private static ScannedFile File(string path) => new(path, 1000, T);

    [Fact]
    public async Task Scan_AnalyzesEachTrack()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"), File("b.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["b.mp3"] = TestSignals.ClickTrain(128, Sr, 8),
        });
        var library = new MusicLibrary(enumerator, decoder);

        await library.ScanAsync(new[] { "music" });

        Assert.Equal(2, library.Count);
        MusicTrack a = library.TryGet("a.mp3")!;
        Assert.NotEqual(MediaAnalysisStatus.Failed, a.Status); // a beat-only click train has no key
        Assert.InRange(a.Bpm!.Bpm, 117.0, 123.0);
        Assert.NotNull(a.Duration);
        Assert.Equal("a", a.Title);
    }

    [Fact]
    public async Task Scan_Incremental_DoesNotReanalyzeUnchangedFiles()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);

        await library.ScanAsync(new[] { "music" });
        await library.ScanAsync(new[] { "music" }); // same fingerprints → skip

        Assert.Equal(1, decoder.DecodeCalls["a.mp3"]);
    }

    [Fact]
    public async Task Scan_CorruptFile_MarkedFailed_OthersStillAnalyzed()
    {
        var enumerator = new FakeFileEnumerator(File("good.mp3"), File("bad.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["good.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["bad.mp3"] = null, // decoder throws
        });
        var library = new MusicLibrary(enumerator, decoder);

        await library.ScanAsync(new[] { "music" });

        Assert.Equal(2, library.Count);
        Assert.Equal(MediaAnalysisStatus.Ok, library.TryGet("good.mp3")!.Status);
        MusicTrack bad = library.TryGet("bad.mp3")!;
        Assert.Equal(MediaAnalysisStatus.Failed, bad.Status);
        Assert.False(string.IsNullOrEmpty(bad.Error));
    }

    [Fact]
    public async Task Restore_SeedsCatalog_SoScanSkipsUnchangedFiles()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);

        await library.ScanAsync(new[] { "music" });
        MusicTrack cached = library.TryGet("a.mp3")!;

        // Simulate a fresh process: a new library + decoder, restored from the persisted snapshot.
        var freshDecoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var reloaded = new MusicLibrary(enumerator, freshDecoder);
        reloaded.Restore(new[] { cached });
        await reloaded.ScanAsync(new[] { "music" });

        Assert.Equal(1, reloaded.Count);
        Assert.Equal(0, freshDecoder.DecodeCalls.GetValueOrDefault("a.mp3")); // restored fingerprint → no re-decode
    }

    [Fact]
    public async Task Scan_PopulatesMetadata_AndTagTitleOverridesFilename()
    {
        var enumerator = new FakeFileEnumerator(File("track01.mp3"));
        var decoder = new MapAudioDecoder(new() { ["track01.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var meta = new TrackMetadata("Real Title", "M83", "Album", null, "Electronic", 2011, 3, null, 320, 44100, 2, "MP3");
        var reader = new FakeTrackMetadataReader(new() { ["track01.mp3"] = meta });
        var library = new MusicLibrary(enumerator, decoder, metadataReader: reader);

        await library.ScanAsync(new[] { "music" });

        MusicTrack t = library.TryGet("track01.mp3")!;
        Assert.Equal(meta, t.Metadata);
        Assert.Equal("Real Title", t.Title);   // tag title wins over the "track01" filename
        Assert.Equal("M83", t.Artist);
    }

    [Fact]
    public async Task Scan_NoMetadataReader_LeavesMetadataNull_AndTitleFallsBackToFilename()
    {
        var enumerator = new FakeFileEnumerator(File("song.mp3"));
        var decoder = new MapAudioDecoder(new() { ["song.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder); // null reader → no metadata

        await library.ScanAsync(new[] { "music" });

        MusicTrack t = library.TryGet("song.mp3")!;
        Assert.Null(t.Metadata);
        Assert.Equal("song", t.Title);
        Assert.Null(t.Artist);
    }

    [Fact]
    public async Task Scan_FailedDecode_StillCapturesMetadata()
    {
        var enumerator = new FakeFileEnumerator(File("bad.mp3"));
        var decoder = new MapAudioDecoder(new() { ["bad.mp3"] = null }); // decode throws → Failed
        var meta = new TrackMetadata(null, "Some Artist", null, null, null, null, null, null, null, null, null, null);
        var reader = new FakeTrackMetadataReader(new() { ["bad.mp3"] = meta });
        var library = new MusicLibrary(enumerator, decoder, metadataReader: reader);

        await library.ScanAsync(new[] { "music" });

        MusicTrack t = library.TryGet("bad.mp3")!;
        Assert.Equal(MediaAnalysisStatus.Failed, t.Status);
        Assert.Equal("Some Artist", t.Artist);  // tags survive a decode failure
    }

    [Fact]
    public async Task Scan_MisbehavingReaderThatThrows_DoesNotAbortScan()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var reader = new FakeTrackMetadataReader();
        reader.ThrowPaths.Add("a.mp3");
        var library = new MusicLibrary(enumerator, decoder, metadataReader: reader);

        await library.ScanAsync(new[] { "music" });

        MusicTrack t = library.TryGet("a.mp3")!;
        Assert.Null(t.Metadata);                            // reader failure degrades to null
        Assert.NotEqual(MediaAnalysisStatus.Failed, t.Status); // analysis still succeeded
    }

    [Fact]
    public async Task HarmonicMatches_ReturnsCompatibleKeys_ExcludingSeed()
    {
        // C major triad (8B) and A minor triad (8A) are relative-key compatible.
        var cMajor = TestSignals.Chord(new[] { (261.63, 1.0), (329.63, 0.6), (392.00, 0.8) }, Sr, 2.0);
        var aMinor = TestSignals.Chord(new[] { (440.00, 1.0), (523.25, 0.6), (659.25, 0.8) }, Sr, 2.0);

        var enumerator = new FakeFileEnumerator(File("c.mp3"), File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["c.mp3"] = cMajor, ["a.mp3"] = aMinor });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        MusicTrack seed = library.TryGet("c.mp3")!;
        var matches = library.HarmonicMatches(seed);

        Assert.Contains(matches, m => m.File.Path == "a.mp3");
        Assert.DoesNotContain(matches, m => m.File.Path == "c.mp3"); // never the seed itself
    }
}
