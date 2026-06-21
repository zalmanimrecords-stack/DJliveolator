using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Xunit;
using PlaylistRecord = Liveolator.Core.Playlist.Playlist;

namespace Liveolator.Core.Tests.Library.Import;

public class ImportKeyParserTests
{
    [Theory]
    [InlineData("8A", "A Minor")]
    [InlineData("8B", "C Major")]
    [InlineData("Am", "A Minor")]
    [InlineData("C", "C Major")]
    [InlineData("F#m", "F# Minor")]
    [InlineData("1m", "A Minor")]   // Open Key 1m = Camelot 8A = A minor
    [InlineData("1d", "C Major")]   // Open Key 1d = Camelot 8B = C major
    public void Parse_RecognizesEveryNotation(string raw, string expectedName)
        => Assert.Equal(expectedName, ImportKeyParser.Parse(raw)!.Name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Parse_UnknownNotation_IsNull(string? raw) => Assert.Null(ImportKeyParser.Parse(raw));
}

public class ImportCueMapperTests
{
    [Fact]
    public void Map_ConvertsSecondsToSamplesAtCanonicalRate_AsCommittedManualCue()
    {
        var track = new ImportedTrack("x", Cues: new[] { new ImportedCue(0, 5.0, "Drop", 0xFF0000) });

        TrackCueSet set = ImportCueMapper.Map(track, out int dropped);

        Assert.Equal(0, dropped);
        HotCue cue = set.HotCues.Single();
        Assert.Equal(5L * ImportCueMapper.SampleRate, cue.PositionSamples);
        Assert.Equal("Drop", cue.Label);
        Assert.Equal(0xFF0000, cue.Color);
        Assert.False(cue.IsAuto); // imported = committed, not a suggestion
    }

    [Fact]
    public void Map_MemoryCue_BecomesPrimaryCue_FirstWins()
    {
        var track = new ImportedTrack("x", Cues: new[]
        {
            new ImportedCue(ImportedCue.MemoryCue, 1.0),
            new ImportedCue(ImportedCue.MemoryCue, 2.0),
        });

        TrackCueSet set = ImportCueMapper.Map(track, out int dropped);

        Assert.Equal(1L * ImportCueMapper.SampleRate, set.PrimaryCueSamples);
        Assert.Equal(1, dropped); // the second memory cue is discarded
    }

    [Fact]
    public void Map_DropsOutOfRangeAndCollidingSlots()
    {
        var track = new ImportedTrack("x", Cues: new[]
        {
            new ImportedCue(0, 1.0, "first"),
            new ImportedCue(0, 2.0, "collision"),  // slot 0 already taken -> dropped
            new ImportedCue(99, 3.0),              // out of range -> dropped
        });

        TrackCueSet set = ImportCueMapper.Map(track, out int dropped);

        Assert.Equal("first", set.HotCues.Single().Label);
        Assert.Equal(2, dropped);
    }
}

public class ImportTrackMapperTests
{
    private static MusicTrack Existing(string path, double? bpm) => new(
        new ScannedFile(path, 1000, DateTime.UnixEpoch),
        bpm is { } b ? new BpmResult(b, 1.0) : null,
        Key: null, Duration: null, Cues: Liveolator.Core.Analysis.TrackCues.None,
        Status: MediaAnalysisStatus.Ok, Error: null);

    [Fact]
    public void Map_NewTrack_CarriesImportedAnalysis_AndIsManual()
    {
        var src = new ImportedTrack(@"C:\m\a.mp3", Title: "A", Bpm: 128, Key: "8A");
        var file = new ScannedFile(@"C:\m\a.mp3", 2000, DateTime.UnixEpoch);

        MusicTrack track = ImportTrackMapper.Map(src, file, existing: null, ImportMergePolicy.FillGaps);

        Assert.Equal(128, track.Bpm!.Bpm);
        Assert.Equal("A Minor", track.Key!.Name);
        Assert.Equal("A", track.Title);
        Assert.True(track.AnalysisIsManual); // protected from re-analysis (global #7)
    }

    [Fact]
    public void Map_FillGaps_KeepsExistingBpm()
    {
        MusicTrack existing = Existing(@"C:\m\a.mp3", bpm: 120);
        var src = new ImportedTrack(@"C:\m\a.mp3", Bpm: 128);

        MusicTrack merged = ImportTrackMapper.Map(src, existing.File, existing, ImportMergePolicy.FillGaps);

        Assert.Equal(120, merged.Bpm!.Bpm); // existing analysis preserved
    }

    [Fact]
    public void Map_FillGaps_FillsMissingBpm()
    {
        MusicTrack existing = Existing(@"C:\m\a.mp3", bpm: null);
        var src = new ImportedTrack(@"C:\m\a.mp3", Bpm: 128);

        MusicTrack merged = ImportTrackMapper.Map(src, existing.File, existing, ImportMergePolicy.FillGaps);

        Assert.Equal(128, merged.Bpm!.Bpm);
    }

    [Fact]
    public void Map_Overwrite_ReplacesExistingBpm()
    {
        MusicTrack existing = Existing(@"C:\m\a.mp3", bpm: 120);
        var src = new ImportedTrack(@"C:\m\a.mp3", Bpm: 128);

        MusicTrack merged = ImportTrackMapper.Map(src, existing.File, existing, ImportMergePolicy.Overwrite);

        Assert.Equal(128, merged.Bpm!.Bpm);
    }
}

public class ImportPathResolverTests
{
    private static MusicTrack Cataloged(string path, double? durationSeconds = null) => new(
        new ScannedFile(path, 1, DateTime.UnixEpoch), Bpm: null, Key: null,
        Duration: durationSeconds is { } d ? TimeSpan.FromSeconds(d) : null,
        Cues: Liveolator.Core.Analysis.TrackCues.None, Status: MediaAnalysisStatus.Ok, Error: null);

    [Fact]
    public void Resolve_LiteralPathThatExists_IsUsed()
    {
        var resolver = new ImportPathResolver(
            Array.Empty<MusicTrack>(),
            p => p == @"C:\m\a.mp3" ? new ScannedFile(p, 10, DateTime.UnixEpoch) : null);

        Assert.Equal(@"C:\m\a.mp3", resolver.Resolve(@"C:\m\a.mp3", null)!.Value.Path);
    }

    [Fact]
    public void Resolve_RemapsByFilename_WhenLiteralMissing()
    {
        var resolver = new ImportPathResolver(
            new[] { Cataloged(@"S:\Music\a.mp3") },
            _ => null); // nothing exists at the source path

        Assert.Equal(@"S:\Music\a.mp3", resolver.Resolve(@"/Users/dj/Music/a.mp3", null)!.Value.Path);
    }

    [Fact]
    public void Resolve_AmbiguousFilename_NeedsDurationToCommit()
    {
        var resolver = new ImportPathResolver(
            new[] { Cataloged(@"S:\A\a.mp3", 100), Cataloged(@"S:\B\a.mp3", 200) },
            _ => null);

        Assert.Null(resolver.Resolve(@"/x/a.mp3", null));                 // ambiguous, no duration -> unresolved
        Assert.Equal(@"S:\B\a.mp3", resolver.Resolve(@"/x/a.mp3", 200)!.Value.Path); // duration disambiguates
    }

    [Fact]
    public void Resolve_NoMatch_IsNull()
    {
        var resolver = new ImportPathResolver(Array.Empty<MusicTrack>(), _ => null);
        Assert.Null(resolver.Resolve(@"/x/missing.mp3", null));
    }
}

public class LibraryImportServiceTests
{
    private sealed class FakeHotCueStore : IHotCueStore
    {
        public readonly Dictionary<string, TrackCueRecord> Records = new(StringComparer.OrdinalIgnoreCase);
        public Task<TrackCueRecord?> LoadAsync(string trackPath, CancellationToken ct = default)
            => Task.FromResult(Records.TryGetValue(trackPath, out TrackCueRecord? r) ? r : null);
        public Task SaveAsync(TrackCueRecord record, CancellationToken ct = default)
        {
            Records[record.TrackPath] = record;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string trackPath, CancellationToken ct = default)
        {
            Records.Remove(trackPath);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlaylistStore : IPlaylistStore
    {
        public readonly List<PlaylistRecord> Saved = new();
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<string>)Saved.Select(p => p.Name).ToList());
        public Task<PlaylistRecord?> LoadAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Saved.FirstOrDefault(p => p.Name == name));
        public Task SaveAsync(PlaylistRecord playlist, CancellationToken ct = default)
        {
            Saved.Add(playlist);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static MusicTrack Cataloged(string path, double? bpm = null) => new(
        new ScannedFile(path, 1, DateTime.UnixEpoch), bpm is { } b ? new BpmResult(b, 1.0) : null,
        Key: null, Duration: null, Cues: Liveolator.Core.Analysis.TrackCues.None,
        Status: MediaAnalysisStatus.Ok, Error: null);

    private static Func<string, ScannedFile?> Exists(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(p) ? new ScannedFile(p, 10, DateTime.UnixEpoch) : null;
    }

    [Fact]
    public async Task Import_AddsNew_EnrichesExisting_WritesCues_AndPlaylists()
    {
        var cues = new FakeHotCueStore();
        var playlists = new FakePlaylistStore();
        var service = new LibraryImportService(cues, playlists, Exists(@"C:\m\a.mp3", @"C:\m\b.mp3"));

        var import = new LibraryImport(
            new[]
            {
                new ImportedTrack(@"C:\m\a.mp3", Bpm: 128, Key: "8A",
                    Cues: new[] { new ImportedCue(0, 5.0, "Drop", 0x00FF00) }),
                new ImportedTrack(@"C:\m\b.mp3", Bpm: 130),
                new ImportedTrack(@"C:\m\missing.mp3", Bpm: 124), // unresolved
            },
            new[] { new ImportedPlaylist("Set", new[] { @"C:\m\a.mp3", @"C:\m\missing.mp3" }) });

        // a.mp3 already catalogued without analysis; b.mp3 is new.
        var catalog = new[] { Cataloged(@"C:\m\a.mp3") };

        LibraryImportResult result = await service.ImportAsync(import, catalog, ImportMergePolicy.FillGaps);

        Assert.Equal(1, result.Summary.TracksAdded);       // b
        Assert.Equal(1, result.Summary.TracksUpdated);     // a enriched
        Assert.Equal(1, result.Summary.TracksUnresolved);  // missing
        Assert.Equal(1, result.Summary.CuesImported);
        Assert.Equal(1, result.Summary.PlaylistsImported);
        Assert.Equal(1, result.Summary.PlaylistTrackRefsDropped); // missing.mp3 dropped from the set

        MusicTrack a = result.TracksToUpsert.Single(t => t.File.Path == @"C:\m\a.mp3");
        Assert.Equal(128, a.Bpm!.Bpm);
        Assert.Equal("A Minor", a.Key!.Name);

        TrackCueRecord savedCues = cues.Records[@"C:\m\a.mp3"];
        Assert.Equal(5L * ImportCueMapper.SampleRate, savedCues.HotCues.Single().PositionSamples);
        Assert.Equal(new[] { @"C:\m\a.mp3" }, playlists.Saved.Single().TrackPaths);
    }

    [Fact]
    public async Task FillGaps_DoesNotOverwriteExistingCues()
    {
        var cues = new FakeHotCueStore();
        var seeded = TrackCueRecord.FromCueSet(@"C:\m\a.mp3",
            new TrackCueSet(48_000, 8).SetHotCue(0, 99_999, "Mine"));
        cues.Records[@"C:\m\a.mp3"] = seeded;
        var service = new LibraryImportService(cues, new FakePlaylistStore(), Exists(@"C:\m\a.mp3"));

        var import = new LibraryImport(
            new[] { new ImportedTrack(@"C:\m\a.mp3", Cues: new[] { new ImportedCue(0, 5.0, "Imported") }) },
            Array.Empty<ImportedPlaylist>());

        LibraryImportResult result = await service.ImportAsync(
            import, new[] { Cataloged(@"C:\m\a.mp3") }, ImportMergePolicy.FillGaps);

        Assert.Equal(1, result.Summary.CuesSkipped);
        Assert.Equal(0, result.Summary.CuesImported);
        Assert.Equal("Mine", cues.Records[@"C:\m\a.mp3"].HotCues.Single().Label); // existing cues kept
    }

    [Fact]
    public async Task Overwrite_ReplacesExistingCues()
    {
        var cues = new FakeHotCueStore();
        cues.Records[@"C:\m\a.mp3"] = TrackCueRecord.FromCueSet(@"C:\m\a.mp3",
            new TrackCueSet(48_000, 8).SetHotCue(0, 99_999, "Mine"));
        var service = new LibraryImportService(cues, new FakePlaylistStore(), Exists(@"C:\m\a.mp3"));

        var import = new LibraryImport(
            new[] { new ImportedTrack(@"C:\m\a.mp3", Cues: new[] { new ImportedCue(0, 5.0, "Imported") }) },
            Array.Empty<ImportedPlaylist>());

        await service.ImportAsync(import, new[] { Cataloged(@"C:\m\a.mp3") }, ImportMergePolicy.Overwrite);

        Assert.Equal("Imported", cues.Records[@"C:\m\a.mp3"].HotCues.Single().Label);
    }
}
