using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Enrichment;
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
    public async Task Scan_StampsLastAnalyzed()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);

        await library.ScanAsync(new[] { "music" });

        Assert.NotNull(library.TryGet("a.mp3")!.LastAnalyzedUtc);
    }

    [Fact]
    public async Task ForceReanalyze_RefreshesLastAnalyzed()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });
        DateTime? afterScan = library.TryGet("a.mp3")!.LastAnalyzedUtc;

        await library.ForceReanalyzeAsync("a.mp3");

        DateTime? afterForce = library.TryGet("a.mp3")!.LastAnalyzedUtc;
        Assert.NotNull(afterForce);
        Assert.True(afterForce >= afterScan);
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
    public async Task Scan_ReportsEachNewEntry_ForIncrementalPersistence()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"), File("b.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["b.mp3"] = TestSignals.ClickTrain(128, Sr, 8),
        });
        var library = new MusicLibrary(enumerator, decoder);
        var processed = new List<string>();

        await library.ScanAsync(
            new[] { "music" }, progress: null, cancellationToken: default,
            onEntryProcessed: (entry, _) => { processed.Add(entry.File.Path); return Task.CompletedTask; });

        // Every analyzed track is reported once, as it lands — the hook the incremental scan persists on.
        Assert.Equal(new[] { "a.mp3", "b.mp3" }, processed.OrderBy(p => p));
    }

    [Fact]
    public async Task Scan_ReportsRemovedFile_SoPersistenceCanDropIt()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"), File("gone.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["gone.mp3"] = TestSignals.ClickTrain(128, Sr, 8),
        });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        enumerator.Files.RemoveAll(f => f.Path == "gone.mp3"); // the file vanished from disk
        var removed = new List<string>();

        await library.ScanAsync(
            new[] { "music" }, progress: null, cancellationToken: default,
            onEntryRemoved: (path, _) => { removed.Add(path); return Task.CompletedTask; });

        Assert.Equal(new[] { "gone.mp3" }, removed);
        Assert.Null(library.TryGet("gone.mp3"));
    }

    [Fact]
    public async Task Scan_StampsDateAdded_AndRatingPlayCountWork()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        Assert.NotNull(library.TryGet("a.mp3")!.DateAdded); // a new track is stamped when it enters the catalog

        MusicTrack? rated = library.SetRating("a.mp3", 4);
        Assert.Equal(4, rated!.Rating);

        MusicTrack? played = library.MarkPlayed("a.mp3");
        Assert.Equal(1, played!.PlayCount);
        Assert.NotNull(played.LastPlayed);
        Assert.Equal(2, library.MarkPlayed("a.mp3")!.PlayCount); // increments
    }

    [Fact]
    public async Task Rescan_And_ForceReanalyze_PreserveUserLibraryFields()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        library.SetRating("a.mp3", 5);
        library.MarkPlayed("a.mp3");
        DateTime? addedAt = library.TryGet("a.mp3")!.DateAdded;

        // The file changes on disk (new fingerprint) → a rescan rebuilds it from the decoder.
        enumerator.Files[0] = new ScannedFile("a.mp3", 2000, T.AddMinutes(1));
        await library.ScanAsync(new[] { "music" });

        MusicTrack afterRescan = library.TryGet("a.mp3")!;
        Assert.Equal(5, afterRescan.Rating);         // rating survives a re-decode (it's user data, not analysis)
        Assert.Equal(1, afterRescan.PlayCount);       // play history survives
        Assert.Equal(addedAt, afterRescan.DateAdded); // date-added is stable across rescans

        await library.ForceReanalyzeAsync("a.mp3");
        MusicTrack afterForce = library.TryGet("a.mp3")!;
        Assert.Equal(5, afterForce.Rating);
        Assert.Equal(1, afterForce.PlayCount);
        Assert.Equal(addedAt, afterForce.DateAdded);
    }

    [Fact]
    public async Task ForceReanalyze_RebuildsAnAlreadyAnalyzedTrack()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        bool analyzed = await library.ForceReanalyzeAsync("a.mp3");

        Assert.True(analyzed);
        Assert.Equal(2, decoder.DecodeCalls["a.mp3"]);
        Assert.False(library.TryGet("a.mp3")!.AnalysisIsManual);
    }

    [Fact]
    public async Task Scan_ModifiedFile_PreservesManualBeatGrid_AndReStampsFingerprint()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        // The DJ hand-corrects the grid; this locks the analysis (AnalysisIsManual = true).
        library.SetManualBeatGrid("a.mp3", bpm: 138.5, firstBeatSeconds: 0.25);

        // The file is re-tagged in an external app → same path, new size/mtime → classified Modified.
        enumerator.Files[0] = new ScannedFile("a.mp3", 2000, T.AddMinutes(5));
        await library.ScanAsync(new[] { "music" });

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.True(track.AnalysisIsManual);            // the manual lock survives the re-scan
        Assert.Equal(138.5, track.Bpm!.Bpm);            // hand-set BPM not clobbered by re-analysis
        Assert.Equal(0.25, track.Bpm.FirstBeatSeconds); // hand-set first beat not clobbered
        Assert.Equal(1, decoder.DecodeCalls["a.mp3"]);  // manual entry kept → not re-decoded

        // The fingerprint was re-stamped to the new file, so a further scan sees it Unchanged
        // (no perpetual "Modified" that would keep trying to rebuild the locked track).
        await library.ScanAsync(new[] { "music" });
        Assert.Equal(1, decoder.DecodeCalls["a.mp3"]);
    }

    [Fact]
    public async Task UpdateManualDetails_PersistsBpmKeyGenreAndNotes()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        bool updated = library.UpdateManualDetails(
            "a.mp3", 138.5, "8A", "Psytrance", "Long intro");

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.True(updated);
        Assert.Equal(138.5, track.Bpm!.Bpm);
        Assert.Equal("8A", track.Key!.Camelot);
        Assert.Equal("A Minor", track.Key.Name);
        Assert.Equal("Psytrance", track.Metadata!.Genre);
        Assert.Equal("Long intro", track.Metadata.Comment);
        Assert.True(track.AnalysisIsManual);
    }

    [Fact]
    public async Task ApplyOnlineDetails_CrossChecksBpmAndUpdatesGenre()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        bool updated = library.ApplyOnlineDetails(
            "a.mp3",
            new OnlineTrackMetadata(121, "8A", null, "Psytrance", "GetSongBPM"));

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.True(updated);
        Assert.InRange(track.Bpm!.Bpm, 117, 123); // local value stays authoritative
        Assert.Equal(0.95, track.Bpm.Confidence, 2);
        Assert.Equal("Psytrance", track.Metadata!.Genre);
    }

    [Fact]
    public async Task ApplyOnlineDetails_FillsAKeylessTrackFromAnOnlineKeyName()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["a.mp3"] = null }); // fails to decode → Failed, no analyzed key
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });
        Assert.Null(library.TryGet("a.mp3")!.Key); // keyless to start

        // GetSongBPM reports a key NAME ("Am") and never a Camelot code; the online key must still apply.
        bool updated = library.ApplyOnlineDetails(
            "a.mp3", new OnlineTrackMetadata(null, Camelot: null, KeyName: "Am", null, "GetSongBPM"));

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.True(updated);
        Assert.Equal("8A", track.Key!.Camelot);
        Assert.Equal("A Minor", track.Key.Name);
    }

    [Fact]
    public void ApplyOnlineDetails_PreservesDownbeatAnchorOnCrossCheck()
    {
        // An already-analyzed track has a detected downbeat (analyzer v4). A cross-check keeps the local
        // BPM, so re-running it must NOT discard the bar/downbeat anchor (doc 31 L1 — silent data loss).
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        var bpm = new Liveolator.Core.Analysis.Bpm.BpmResult(128, 0.9, FirstBeatSeconds: 0.1)
        {
            DownbeatSeconds = 0.5,
            BeatsPerBar = 4,
            DownbeatConfidence = 0.8,
        };
        library.Restore(new[]
        {
            new MusicTrack(File("a.mp3"), bpm, null, TimeSpan.FromMinutes(5), TrackCues.None,
                MediaAnalysisStatus.Ok, null, AnalyzerVersion: TrackAnalyzer.CurrentVersion),
        });

        bool updated = library.ApplyOnlineDetails(
            "a.mp3", new OnlineTrackMetadata(128, "8A", null, null, "GetSongBPM"));

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.True(updated);
        Assert.Equal(0.5, track.Bpm!.DownbeatSeconds);     // downbeat anchor survives
        Assert.Equal(0.1, track.Bpm.FirstBeatSeconds);     // first-beat anchor survives
        Assert.Equal(0.8, track.Bpm.DownbeatConfidence);
    }

    [Fact]
    public void ApplyOnlineDetails_StampsAnalyzerVersion_SoEnrichedFailedTrackLeavesThePendingQueue()
    {
        // A track that Failed locally (no decoder) but got a usable BPM/key from online enrichment must
        // stop being flagged for re-analysis, or a later pass re-decodes and overwrites it (doc 31 L2).
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(new[]
        {
            new MusicTrack(File("a.mp3"), null, null, null, TrackCues.None,
                MediaAnalysisStatus.Failed, "no decoder"), // AnalyzerVersion defaults to 0
        });
        Assert.Contains("a.mp3", library.PathsNeedingAnalysis());

        bool updated = library.ApplyOnlineDetails(
            "a.mp3", new OnlineTrackMetadata(140, "8A", null, null, "GetSongBPM"));

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.True(updated);
        Assert.Equal(TrackAnalyzer.CurrentVersion, track.AnalyzerVersion);
        Assert.DoesNotContain("a.mp3", library.PathsNeedingAnalysis());
    }

    [Fact]
    public void ApplyOnlineDetails_Conflict_StampsOnlineBpmAndProvenance()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(new[]
        {
            new MusicTrack(File("a.mp3"), new Liveolator.Core.Analysis.Bpm.BpmResult(128, 0.9), null,
                TimeSpan.FromMinutes(5), TrackCues.None, MediaAnalysisStatus.Ok, null,
                AnalyzerVersion: TrackAnalyzer.CurrentVersion),
        });

        bool updated = library.ApplyOnlineDetails(
            "a.mp3", new OnlineTrackMetadata(174, null, null, null, "GetSongBPM"));

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.True(updated);
        Assert.Equal(128, track.Bpm!.Bpm);            // offline-first: local stays authoritative
        Assert.Equal(174, track.OnlineBpm);           // the disagreeing value is kept for display
        Assert.Equal("GetSongBPM", track.OnlineBpmSource);
        Assert.Equal(BpmProvenance.Conflicted, track.BpmProvenance);
    }

    [Fact]
    public void ApplyOnlineDetails_Agreement_StampsCrossCheckedProvenance()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(new[]
        {
            new MusicTrack(File("a.mp3"), new Liveolator.Core.Analysis.Bpm.BpmResult(128, 0.7), null,
                TimeSpan.FromMinutes(5), TrackCues.None, MediaAnalysisStatus.Ok, null,
                AnalyzerVersion: TrackAnalyzer.CurrentVersion),
        });

        library.ApplyOnlineDetails("a.mp3", new OnlineTrackMetadata(128, null, null, null, "GetSongBPM"));

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.Equal(128, track.OnlineBpm);
        Assert.Equal(BpmProvenance.CrossChecked, track.BpmProvenance);
    }

    [Fact]
    public void ApplyOnlineDetails_ManualOrConfirmedBpm_IsNeverReFlagged()
    {
        // The user's decision (a manually-set BPM, or a dismissed conflict) outranks the online
        // cross-check — it cannot tell an extended mix from the radio edit.
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(new[]
        {
            new MusicTrack(File("manual.mp3"), new Liveolator.Core.Analysis.Bpm.BpmResult(128, 1.0), null,
                TimeSpan.FromMinutes(5), TrackCues.None, MediaAnalysisStatus.Ok, null,
                AnalyzerVersion: TrackAnalyzer.CurrentVersion, AnalysisIsManual: true),
            new MusicTrack(File("confirmed.mp3"), new Liveolator.Core.Analysis.Bpm.BpmResult(128, 0.9), null,
                TimeSpan.FromMinutes(5), TrackCues.None, MediaAnalysisStatus.Ok, null,
                AnalyzerVersion: TrackAnalyzer.CurrentVersion,
                BpmProvenance: BpmProvenance.LocalConfirmed),
        });

        library.ApplyOnlineDetails("manual.mp3", new OnlineTrackMetadata(174, null, null, null, "GetSongBPM"));
        library.ApplyOnlineDetails("confirmed.mp3", new OnlineTrackMetadata(174, null, null, null, "GetSongBPM"));

        foreach (string path in new[] { "manual.mp3", "confirmed.mp3" })
        {
            MusicTrack track = library.TryGet(path)!;
            Assert.Equal(BpmProvenance.LocalConfirmed, track.BpmProvenance);
            Assert.Equal(MediaAnalysisStatus.Ok, track.Status); // a suppressed conflict never demotes the status
        }
    }

    [Fact]
    public void ConfirmLocalBpm_DismissesTheConflict()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(new[]
        {
            new MusicTrack(File("a.mp3"), new Liveolator.Core.Analysis.Bpm.BpmResult(128, 0.9), null,
                TimeSpan.FromMinutes(5), TrackCues.None, MediaAnalysisStatus.Ok, null,
                AnalyzerVersion: TrackAnalyzer.CurrentVersion,
                OnlineBpm: 174, OnlineBpmSource: "GetSongBPM", BpmProvenance: BpmProvenance.Conflicted),
        });

        Assert.True(library.ConfirmLocalBpm("a.mp3"));
        Assert.Equal(BpmProvenance.LocalConfirmed, library.TryGet("a.mp3")!.BpmProvenance);

        Assert.False(library.ConfirmLocalBpm("a.mp3"));       // already resolved — nothing to change
        Assert.False(library.ConfirmLocalBpm("missing.mp3")); // unknown path
    }

    [Fact]
    public void MarkOnlineLookup_StampsTimestampWithoutTouchingAnalysis()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(new[]
        {
            new MusicTrack(File("a.mp3"), new Liveolator.Core.Analysis.Bpm.BpmResult(128, 0.9), null,
                TimeSpan.FromMinutes(5), TrackCues.None, MediaAnalysisStatus.Ok, null,
                AnalyzerVersion: TrackAnalyzer.CurrentVersion),
        });

        Assert.True(library.MarkOnlineLookup("a.mp3", T));

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.Equal(T, track.OnlineLookupUtc);
        Assert.Equal(128, track.Bpm!.Bpm); // analysis untouched
        Assert.False(library.MarkOnlineLookup("missing.mp3", T));
    }

    [Fact]
    public async Task Rescan_CarriesOnlineLookup_AndRecomputesProvenanceAgainstTheNewLocalBpm()
    {
        // A re-decode must not drop the online data (or the next scan re-burns the free API), and the
        // conflict flag must stay honest against the NEW locally detected tempo.
        var enumerator = new FakeFileEnumerator(File("a.mp3"));
        var pcm = new Dictionary<string, float[]?> { ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) };
        var library = new MusicLibrary(enumerator, new MapAudioDecoder(pcm));
        await library.ScanAsync(new[] { "music" });

        // Online said 121 — agrees with the detected ~120.
        library.ApplyOnlineDetails("a.mp3", new OnlineTrackMetadata(121, null, null, null, "GetSongBPM"));
        library.MarkOnlineLookup("a.mp3", T);
        Assert.Equal(BpmProvenance.CrossChecked, library.TryGet("a.mp3")!.BpmProvenance);

        // The file changes on disk and now detects ~100 (121 conflicts with 100 under every octave/3:2
        // relation, whichever octave the detector lands on) → the carried online value no longer agrees.
        pcm["a.mp3"] = TestSignals.ClickTrain(100, Sr, 8);
        enumerator.Files[0] = new ScannedFile("a.mp3", 2000, T.AddMinutes(1));
        await library.ScanAsync(new[] { "music" });

        MusicTrack track = library.TryGet("a.mp3")!;
        Assert.Equal(121, track.OnlineBpm);                        // online data survives the re-decode
        Assert.Equal("GetSongBPM", track.OnlineBpmSource);
        Assert.Equal(T, track.OnlineLookupUtc);                    // no API re-burn on the next pass
        Assert.Equal(BpmProvenance.Conflicted, track.BpmProvenance); // recomputed vs the new local value
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
    public async Task SummarizeFolders_CountsTracksAndStatusPerFolder()
    {
        var enumerator = new FakeFileEnumerator(
            File("/music/rock/a.mp3"), File("/music/rock/b.mp3"), File("/music/rock/bad.mp3"),
            File("/music/jazz/c.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["/music/rock/a.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["/music/rock/b.mp3"] = TestSignals.ClickTrain(128, Sr, 8),
            ["/music/rock/bad.mp3"] = null, // decode fails → Failed
            ["/music/jazz/c.mp3"] = TestSignals.ClickTrain(90, Sr, 8),
        });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "/music" });

        var summaries = library.SummarizeFolders(new[] { "/music/rock", "/music/jazz" });

        FolderCatalogSummary rock = summaries.Single(s => s.Folder == "/music/rock");
        Assert.Equal(3, rock.TrackCount);
        Assert.Equal(1, rock.Failed);
        Assert.Equal(2, rock.Ok + rock.PartiallyAnalyzed); // click trains have no key → Ok or Partial

        FolderCatalogSummary jazz = summaries.Single(s => s.Folder == "/music/jazz");
        Assert.Equal(1, jazz.TrackCount);
        Assert.Equal(0, jazz.Failed);
    }

    [Fact]
    public async Task SummarizeFolders_PrefixMatchesOnlyAtPathBoundary()
    {
        var enumerator = new FakeFileEnumerator(
            File("/music/rock/a.mp3"), File("/music/rockabilly/b.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["/music/rock/a.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["/music/rockabilly/b.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
        });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "/music" });

        var summaries = library.SummarizeFolders(new[] { "/music/rock" });

        Assert.Equal(1, summaries.Single().TrackCount); // not 2 — rockabilly is a sibling, not a child
    }

    [Fact]
    public async Task SummarizeFolders_EmptyFolder_YieldsZeroSummary()
    {
        var enumerator = new FakeFileEnumerator(File("/music/rock/a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["/music/rock/a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "/music" });

        var summaries = library.SummarizeFolders(new[] { "/music/empty" });

        FolderCatalogSummary empty = summaries.Single();
        Assert.Equal("/music/empty", empty.Folder);
        Assert.Equal(0, empty.TrackCount);
    }

    [Fact]
    public async Task PruneToFolders_DropsTracksOutsideRetainedFolders()
    {
        var enumerator = new FakeFileEnumerator(
            File("/music/rock/a.mp3"), File("/music/jazz/c.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["/music/rock/a.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["/music/jazz/c.mp3"] = TestSignals.ClickTrain(90, Sr, 8),
        });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "/music" });

        int dropped = library.PruneToFolders(new[] { "/music/rock" });

        Assert.Equal(1, dropped);
        Assert.NotNull(library.TryGet("/music/rock/a.mp3"));
        Assert.Null(library.TryGet("/music/jazz/c.mp3")); // jazz folder no longer retained → dropped
    }

    [Fact]
    public async Task PruneToFolders_KeepsTrackStillCoveredByANestedRetainedFolder()
    {
        var enumerator = new FakeFileEnumerator(File("/music/rock/a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["/music/rock/a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "/music" });

        // Removing the broad "/music" root still leaves the track covered by the kept "/music/rock" root.
        int dropped = library.PruneToFolders(new[] { "/music/rock" });

        Assert.Equal(0, dropped);
        Assert.NotNull(library.TryGet("/music/rock/a.mp3"));
    }

    [Fact]
    public async Task PruneToFolders_Empty_ClearsCatalog()
    {
        var enumerator = new FakeFileEnumerator(File("/music/rock/a.mp3"));
        var decoder = new MapAudioDecoder(new() { ["/music/rock/a.mp3"] = TestSignals.ClickTrain(120, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "/music" });

        int dropped = library.PruneToFolders(Array.Empty<string>());

        Assert.Equal(1, dropped);
        Assert.Equal(0, library.Count);
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

    [Fact]
    public async Task TryGetByPathOrName_FallsBackToFileName_WhenTheExactPathDiffers()
    {
        // The track is catalogued under the scanned UNC-style path; a deck loads it under a mapped-drive
        // path with the SAME file name. An exact lookup misses, but the file-name fallback recovers it —
        // this is what threads the analyzed BPM to the engine so SYNC/beatmatch works.
        var enumerator = new FakeFileEnumerator(File(@"\\nas\music\track.mp3"));
        var decoder = new MapAudioDecoder(new() { [@"\\nas\music\track.mp3"] = TestSignals.ClickTrain(128, Sr, 8) });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { @"\\nas\music" });

        Assert.Null(library.TryGet(@"S:\track.mp3"));                  // exact lookup misses
        MusicTrack? recovered = library.TryGetByPathOrName(@"S:\track.mp3");
        Assert.NotNull(recovered);                                     // file-name fallback finds it
        Assert.InRange(recovered!.Bpm!.Bpm, 125.0, 131.0);
    }

    [Fact]
    public async Task TryGetByPathOrName_PrefersTheExactPath_AndReturnsNullOnNoMatch()
    {
        var enumerator = new FakeFileEnumerator(File("a.mp3"), File("b.mp3"));
        var decoder = new MapAudioDecoder(new()
        {
            ["a.mp3"] = TestSignals.ClickTrain(120, Sr, 8),
            ["b.mp3"] = TestSignals.ClickTrain(128, Sr, 8),
        });
        var library = new MusicLibrary(enumerator, decoder);
        await library.ScanAsync(new[] { "music" });

        Assert.Equal("a.mp3", library.TryGetByPathOrName("a.mp3")!.File.Path); // exact wins
        Assert.Null(library.TryGetByPathOrName("nowhere.mp3"));                // genuine miss → null
        Assert.Null(library.TryGetByPathOrName(""));                           // empty → null, never throws
    }
}
