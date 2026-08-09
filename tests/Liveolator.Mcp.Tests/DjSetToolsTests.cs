using Liveolator.Audio.Render;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Media;
using Liveolator.Media.Import;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using Liveolator.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// The agent-facing set-building surface: an agent asks for a set, gets back enough to judge it without
/// listening, and finds the arrangement saved where the app's STUDIO tab lists it. Audio is never
/// rendered here — only <c>render_set_preview</c> needs a decoder, and it is exercised separately.
/// </summary>
public sealed class DjSetToolsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"liveolator-djset-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task BuildDjSet_PlacesTheTracks_AndSavesTheArrangement()
    {
        DjSetSession session = await CreateSessionAsync(
            Track("a.mp3", "8A", 128),
            Track("b.mp3", "8A", 127),
            Track("c.mp3", "9A", 129));

        DjSetResult result = await DjSetTools.BuildDjSet(session, seedPath: FullPath("a.mp3"), length: 3, name: "Test Set");

        Assert.Equal("Test Set", result.ProjectName);
        Assert.Equal(3, result.TrackCount);
        Assert.Equal(2, result.Transitions.Count);
        Assert.True(result.TempoBpm > 0);
        Assert.True(result.TotalSeconds > 0);
        Assert.Contains("Test Set", await DjSetTools.ListDjSets(session));
    }

    [Fact]
    public async Task BuildDjSet_ReportsEveryJoin_WithEnoughToJudgeIt()
    {
        DjSetSession session = await CreateSessionAsync(
            Track("a.mp3", "8A", 128, structure: StandardStructure()),
            Track("b.mp3", "8A", 128, structure: StandardStructure()));

        DjSetResult result = await DjSetTools.BuildDjSet(session, seedPath: FullPath("a.mp3"), length: 2, name: "Judged");

        TransitionInfo transition = Assert.Single(result.Transitions);
        Assert.Equal(0, transition.Index);
        Assert.True(transition.OverlapBars >= 8);
        Assert.True(transition.OverlapSeconds > 0);
        Assert.True(transition.EndSeconds > transition.StartSeconds);
        Assert.True(transition.PhaseLocked);
        Assert.Equal("Structure", transition.OutAnchor.Source);
        Assert.Equal(SongSectionLabel.Outro, transition.OutAnchor.SectionLabel);
        Assert.False(string.IsNullOrWhiteSpace(transition.KeyFrom));
        Assert.False(string.IsNullOrWhiteSpace(transition.FromTitle));
    }

    [Fact]
    public async Task BuildDjSet_ReportsWhatItLeftOut_AndWhy()
    {
        // A track two-thirds of the set tempo is the actionable case: the agent sees the exact stretch it
        // would have needed and can widen the limit or pick differently.
        DjSetSession session = await CreateSessionAsync(
            Track("a.mp3", "8A", 128),
            Track("b.mp3", "8A", 128),
            Track("slow.mp3", "8A", 90));

        DjSetResult result = await DjSetTools.BuildDjSet(
            session, seedPath: FullPath("a.mp3"), length: 3, bpmTolerance: 60, name: "Filtered");

        RejectedTrackInfo rejected = Assert.Single(result.RejectedCandidates, r => r.Path.EndsWith("slow.mp3", StringComparison.Ordinal));
        Assert.Equal("OutsideTempoRange", rejected.Reason);
        Assert.NotNull(rejected.NeededWarpPercent);
        Assert.Equal(1, result.RejectedCount);
    }

    [Fact]
    public async Task BuildDjSet_RestrictedToTrackPaths_BuildsFromThoseTracksOnly()
    {
        // The real failure: a second, unrelated library sits in the same catalog and overlaps on tempo and
        // key, so no tolerance setting can keep it out — only naming the candidates can.
        DjSetSession session = await CreateSessionAsync(
            Track("mine-1.mp3", "8A", 128),
            Track("mine-2.mp3", "8A", 127),
            Track("mine-3.mp3", "9A", 129),
            Track("foreign-1.mp3", "8A", 128),
            Track("foreign-2.mp3", "8A", 128),
            Track("foreign-3.mp3", "9A", 128));

        DjSetResult result = await DjSetTools.BuildDjSet(
            session,
            seedPath: FullPath("mine-1.mp3"),
            trackPaths: new[] { FullPath("mine-1.mp3"), FullPath("mine-2.mp3"), FullPath("mine-3.mp3") },
            name: "Mine Only");

        Assert.Equal(3, result.TrackCount);
        Assert.All(result.Tracks, t => Assert.Contains("mine-", t.Path, StringComparison.Ordinal));
        Assert.DoesNotContain(result.RejectedCandidates, r => r.Path.Contains("foreign-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildDjSet_RestrictedToTrackPaths_DefaultsTheLengthToEveryNamedTrack()
    {
        DjSetSession session = await CreateSessionAsync(
            Track("a.mp3", "8A", 128),
            Track("b.mp3", "8A", 128),
            Track("c.mp3", "8A", 128));

        DjSetResult result = await DjSetTools.BuildDjSet(
            session,
            trackPaths: new[] { FullPath("a.mp3"), FullPath("b.mp3"), FullPath("c.mp3") },
            name: "All Three");

        Assert.Equal(3, result.TrackCount);
    }

    [Fact]
    public async Task BuildDjSet_RestrictedToTrackPaths_RejectsAnUncataloguedPath()
    {
        DjSetSession session = await CreateSessionAsync(Track("a.mp3", "8A", 128), Track("b.mp3", "8A", 128));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => DjSetTools.BuildDjSet(
                session, trackPaths: new[] { FullPath("a.mp3"), FullPath("gone.mp3") }));

        Assert.Contains("gone.mp3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildDjSet_RestrictedToTrackPaths_RejectsASeedOutsideThem()
    {
        // Silently widening the pool to fit the seed is exactly the foreign-material bug this parameter exists
        // to stop, so the contradiction is reported instead.
        DjSetSession session = await CreateSessionAsync(
            Track("a.mp3", "8A", 128), Track("b.mp3", "8A", 128), Track("outsider.mp3", "8A", 128));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => DjSetTools.BuildDjSet(
                session,
                seedPath: FullPath("outsider.mp3"),
                trackPaths: new[] { FullPath("a.mp3"), FullPath("b.mp3") }));

        Assert.Contains("outsider.mp3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDjSet_ReadsBackASavedSet()
    {
        DjSetSession session = await CreateSessionAsync(
            Track("a.mp3", "8A", 128),
            Track("b.mp3", "8A", 128));
        await DjSetTools.BuildDjSet(session, seedPath: FullPath("a.mp3"), length: 2, name: "Saved");

        SavedSetInfo info = await DjSetTools.GetDjSet(session, "Saved");

        Assert.Equal("Saved", info.ProjectName);
        Assert.Equal(2, info.TrackCount);
        SetJoinInfo join = Assert.Single(info.Joins);
        Assert.True(join.OverlapSeconds > 0);
        Assert.Equal(2, info.Tracks.Count);
        Assert.Equal(new[] { 0, 1 }, info.Tracks.Select(t => t.DeckSlot).ToArray());
    }

    [Fact]
    public async Task GetDjSet_SaysSoWhenTheSetIsNotThere()
    {
        DjSetSession session = await CreateSessionAsync(Track("a.mp3", "8A", 128));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => DjSetTools.GetDjSet(session, "nothing here"));

        Assert.Contains("list_dj_sets", error.Message);
    }

    [Fact]
    public async Task BuildDjSet_RejectsAnUncataloguedSeed()
    {
        DjSetSession session = await CreateSessionAsync(Track("a.mp3", "8A", 128));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => DjSetTools.BuildDjSet(session, seedPath: "nowhere.mp3", length: 2));

        Assert.Contains("Scan its folder first", error.Message);
    }

    [Theory]
    [InlineData(1, 16, 6.0, "Any")]          // too few tracks to mix
    [InlineData(8, 4, 6.0, "Any")]           // overlap below the floor
    [InlineData(8, 64, 6.0, "Any")]          // overlap above the ceiling
    [InlineData(8, 16, 40.0, "Any")]         // a stretch nothing survives
    [InlineData(8, 16, 6.0, "Sideways")]     // not a tempo trend
    public async Task BuildDjSet_RejectsUnusableRequests(int length, int overlapBars, double maxWarp, string trend)
    {
        DjSetSession session = await CreateSessionAsync(Track("a.mp3", "8A", 128));

        await Assert.ThrowsAsync<ArgumentException>(() => DjSetTools.BuildDjSet(
            session, seedPath: null, length: length, bpmTolerance: 6.0,
            trend: trend, overlapBars: overlapBars, maxWarpPercent: maxWarp));
    }

    [Fact]
    public async Task RenderSetPreview_RejectsARelativeOutputFolder()
    {
        DjSetSession session = await CreateSessionAsync(Track("a.mp3", "8A", 128));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => DjSetTools.RenderSetPreview(session, "Saved", "previews"));

        Assert.Contains("absolute", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private async Task<DjSetSession> CreateSessionAsync(params MusicTrack[] tracks)
    {
        Directory.CreateDirectory(_directory);
        // The arranger keeps unreachable files off the timeline, so the catalogued paths must really exist.
        foreach (MusicTrack track in tracks)
            await File.WriteAllBytesAsync(track.File.Path, Array.Empty<byte>());

        var store = new JsonCatalogStore(_directory);
        await store.SaveMusicAsync(tracks);
        var importService = new LibraryImportService(
            new JsonHotCueStore(_directory), new JsonPlaylistStore(_directory), p => ImportFileProbe.Stat(p));
        var library = new LibrarySession(
            new EmptyEnumerator(),
            new EmptyDecoder(),
            new TrackAnalyzer(),
            NullTrackMetadataReader.Instance,
            store,
            Array.Empty<ILibraryImporter>(),
            Array.Empty<IFolderLibraryImporter>(),
            importService,
            NullLogger<LibrarySession>.Instance);

        return new DjSetSession(
            library,
            new JsonStudioProjectStore(_directory),
            new OfflineMixRenderer(new EmptyDecoder()),
            NullLogger<DjSetSession>.Instance);
    }

    private string FullPath(string file) => Path.Combine(_directory, file);

    private MusicTrack Track(string file, string camelot, double bpm, SongStructure? structure = null)
        => new(
            new ScannedFile(FullPath(file), 100, DateTime.UtcNow),
            new BpmResult(bpm, 0.9)
            {
                BeatsPerBar = 4,
                DownbeatConfidence = 0.8,
                GridCoherence = 0.9,
                TempoStabilityBpmDelta = 0.1,
            },
            new MusicalKey(0, KeyMode.Minor, camelot, 0.8),
            TimeSpan.FromMinutes(5),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            TrackMetadata.Empty with { Title = Path.GetFileNameWithoutExtension(file), Artist = "Tester" },
            MusicMediaKind.Track,
            TrackAnalyzer.CurrentVersion,
            Structure: structure);

    private static SongStructure StandardStructure()
        => new(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(60.0, SongSectionLabel.BuildUp),
                new SongSection(90.0, SongSectionLabel.Drop),
                new SongSection(150.0, SongSectionLabel.Breakdown),
                new SongSection(180.0, SongSectionLabel.Drop),
                new SongSection(240.0, SongSectionLabel.Outro),
            },
            "test");

    private sealed class EmptyEnumerator : IFileEnumerator
    {
        public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
            => Array.Empty<ScannedFile>();
    }

    /// <summary>Decodes nothing — these tests build and read arrangements, they never render audio.</summary>
    private sealed class EmptyDecoder : IAudioDecoder
    {
        public bool CanDecode(string filePath) => false;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath,
            int targetSampleRate,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
