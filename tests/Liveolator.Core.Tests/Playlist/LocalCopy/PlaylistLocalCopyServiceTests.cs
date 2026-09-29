using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist.LocalCopy;
using Xunit;
using PlaylistRecord = Liveolator.Core.Playlist.Playlist;

namespace Liveolator.Core.Tests.Playlist.LocalCopy;

public class PlaylistLocalCopyServiceTests
{
    private static readonly DateTime CopiedAt = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private const string Nas = @"\\nas\music";
    private const string Gig = @"C:\Gig";
    private const string A = @"\\nas\music\a.mp3";
    private const string B = @"\\nas\music\sub\b.mp3";
    private const string LocalA = @"C:\Gig\music\a.mp3";
    private const string LocalB = @"C:\Gig\music\sub\b.mp3";

    private readonly FakeFileCopier _copier = new();
    private readonly FakeCatalogStore _catalogStore = new();
    private readonly FakeHotCueStore _cueStore = new();
    private readonly FakePlaylistStore _playlistStore = new();

    [Fact]
    public async Task Copy_CarriesTheAnalysisToTheLocalFile_StampedWithTheCopiedFilesOwnDate()
    {
        MusicTrack source = LocalCopyPlannerTests.Track(A) with { Rating = 5, AnalysisIsManual = true };

        LocalCopyResult result = await Service().CopyAsync(Playlist(A), new[] { source }, new[] { Nas }, Gig);

        MusicTrack local = Assert.Single(result.LocalTracks);
        Assert.Equal(new ScannedFile(LocalA, 1000, CopiedAt), local.File);
        Assert.Equal(source with { File = local.File }, local);
        Assert.Equal(local, Assert.Single(_catalogStore.Saved));
        Assert.Equal(1, result.Copied);
    }

    [Fact]
    public async Task Copy_SavesALocalPlaylistInTheOriginalOrder_AndLeavesTheOriginalAlone()
    {
        LocalCopyResult result = await Service().CopyAsync(Playlist(B, A), Catalog(A, B), new[] { Nas }, Gig);

        PlaylistRecord local = Assert.Single(_playlistStore.Saved);
        Assert.Equal("Friday (local)", local.Name);
        Assert.Equal(new[] { LocalB, LocalA }, local.TrackPaths);
        Assert.Equal(local, result.LocalPlaylist);
    }

    [Fact]
    public async Task Copy_OfALocalPlaylist_KeepsItsName()
    {
        await Service().CopyAsync(
            new PlaylistRecord("Friday (local)", new[] { A }), Catalog(A), new[] { Nas }, Gig);

        Assert.Equal("Friday (local)", Assert.Single(_playlistStore.Saved).Name);
    }

    [Fact]
    public async Task Copy_WithoutEnoughFreeSpace_CopiesNothing()
    {
        _copier.FreeBytes = 1500; // two 1000-byte tracks

        LocalCopyResult result = await Service().CopyAsync(Playlist(A, B), Catalog(A, B), new[] { Nas }, Gig);

        Assert.True(result.InsufficientSpace);
        Assert.Equal(2000, result.BytesToCopy);
        Assert.Empty(_copier.Copied);
        Assert.Empty(_playlistStore.Saved);
    }

    [Fact]
    public async Task Copy_OneFailedFile_DoesNotStopTheRest_AndIsReported()
    {
        _copier.FailOn = A;

        LocalCopyResult result = await Service().CopyAsync(Playlist(A, B), Catalog(A, B), new[] { Nas }, Gig);

        Assert.Equal(new[] { LocalB }, Assert.Single(_playlistStore.Saved).TrackPaths);
        LocalCopyProblem problem = Assert.Single(result.Problems);
        Assert.Equal(A, problem.SourcePath);
        Assert.Contains("disk on fire", problem.Reason);
    }

    [Fact]
    public async Task Copy_UncataloguedEntry_IsReportedAsAProblem()
    {
        LocalCopyResult result = await Service().CopyAsync(Playlist(A, B), Catalog(A), new[] { Nas }, Gig);

        Assert.Equal(B, Assert.Single(result.Problems).SourcePath);
    }

    [Fact]
    public async Task Copy_CarriesHotCuesToTheLocalPath_AndWritesNoneForATrackWithoutCues()
    {
        var cue = new HotCue(0, 44100, "Drop", null, false);
        _cueStore.Records[A] = new TrackCueRecord(A, 44100, 8, 1000, new[] { cue });

        await Service().CopyAsync(Playlist(A, B), Catalog(A, B), new[] { Nas }, Gig);

        Assert.Equal(new[] { cue }, _cueStore.Records[LocalA].HotCues);
        Assert.False(_cueStore.Records.ContainsKey(LocalB));
    }

    [Fact]
    public async Task Copy_Again_KeepsTheLocalRowAndCuesEditedAtTheGig()
    {
        await Service().CopyAsync(Playlist(A), Catalog(A), new[] { Nas }, Gig);
        MusicTrack editedAtGig = _catalogStore.Saved.Single() with { Rating = 1 };
        var gigCue = new TrackCueRecord(LocalA, 44100, 8, 7, Array.Empty<HotCue>());
        _cueStore.Records[LocalA] = gigCue;
        _catalogStore.Saved.Clear();

        LocalCopyResult again = await Service().CopyAsync(
            Playlist(A), new[] { LocalCopyPlannerTests.Track(A), editedAtGig }, new[] { Nas, Gig }, Gig);

        Assert.Equal(1, again.AlreadyPresent);
        Assert.Empty(again.LocalTracks);
        Assert.Empty(_catalogStore.Saved);
        Assert.Same(gigCue, _cueStore.Records[LocalA]);
        Assert.Single(_copier.Copied); // only the first run copied
    }

    [Fact]
    public async Task Copy_Again_AfterARunCancelledBeforeItsRowWasSaved_SavesTheMissingRow()
    {
        _copier.Files[LocalA] = new ScannedFile(LocalA, 1000, CopiedAt); // file landed, row never saved

        LocalCopyResult result = await Service().CopyAsync(Playlist(A), Catalog(A), new[] { Nas }, Gig);

        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal(LocalA, Assert.Single(result.LocalTracks).File.Path);
        Assert.Empty(_copier.Copied);
    }

    [Fact]
    public async Task Copy_Cancelled_KeepsTheFilesAlreadyFinished()
    {
        using var cts = new CancellationTokenSource();
        _copier.AfterCopy = cts.Cancel; // cancel once the first file lands

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service().CopyAsync(Playlist(A, B), Catalog(A, B), new[] { Nas }, Gig, cancellationToken: cts.Token));

        Assert.Equal(LocalA, Assert.Single(_catalogStore.Saved).File.Path);
        Assert.Empty(_playlistStore.Saved);
    }

    [Fact]
    public async Task Copy_ReportsProgressPerTrack()
    {
        var reports = new List<ScanProgress>();

        await Service().CopyAsync(
            Playlist(A, B), Catalog(A, B), new[] { Nas }, Gig, new SyncProgress(reports.Add));

        Assert.Equal(new[] { 0, 1, 2 }, reports.Select(r => r.Done));
        Assert.All(reports, r => Assert.Equal(2, r.Total));
    }

    private PlaylistLocalCopyService Service() => new(_copier, _catalogStore, _cueStore, _playlistStore);

    private static PlaylistRecord Playlist(params string[] paths) => new("Friday", paths);

    private static MusicTrack[] Catalog(params string[] paths) => paths.Select(LocalCopyPlannerTests.Track).ToArray();

    private sealed class SyncProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }

    private sealed class FakeFileCopier : IFileCopier
    {
        public Dictionary<string, ScannedFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Copied { get; } = new();
        public long FreeBytes { get; set; } = long.MaxValue;
        public string? FailOn { get; set; }
        public Action? AfterCopy { get; set; }

        public Task<ScannedFile> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (sourcePath == FailOn)
                throw new IOException("disk on fire");
            var copied = new ScannedFile(destinationPath, 1000, CopiedAt);
            Files[destinationPath] = copied;
            Copied.Add(destinationPath);
            AfterCopy?.Invoke();
            return Task.FromResult(copied);
        }

        public ScannedFile? TryStat(string path) => Files.TryGetValue(path, out ScannedFile f) ? f : null;

        public long AvailableFreeBytes(string folder) => FreeBytes;
    }

    private sealed class FakeCatalogStore : IMusicCatalogStore
    {
        public List<MusicTrack> Saved { get; } = new();

        public Task SaveTrackAsync(MusicTrack track, CancellationToken ct = default)
        {
            Saved.Add(track);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteTrackAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeHotCueStore : IHotCueStore
    {
        public Dictionary<string, TrackCueRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<TrackCueRecord?> LoadAsync(string trackPath, CancellationToken ct = default)
            => Task.FromResult(Records.TryGetValue(trackPath, out TrackCueRecord? r) ? r : null);

        public Task SaveAsync(TrackCueRecord record, CancellationToken ct = default)
        {
            Records[record.TrackPath] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string trackPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<string>> ListPathsWithCuesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePlaylistStore : IPlaylistStore
    {
        public List<PlaylistRecord> Saved { get; } = new();

        public Task SaveAsync(PlaylistRecord playlist, CancellationToken ct = default)
        {
            Saved.Add(playlist);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PlaylistRecord?> LoadAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
