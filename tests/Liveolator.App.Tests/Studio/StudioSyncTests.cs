using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Integration tests for the STUDIO right-click "Sync to project BPM" wired into <see cref="StudioViewModel"/>:
/// it warps the clip and snaps its downbeat onto the project grid as one undoable edit, gates bar vs beat
/// alignment on the analyzed downbeat confidence, and reports clearly when a clip's tempo isn't analyzed.
/// </summary>
public sealed class StudioSyncTests
{
    private const double Tol = 1e-9;
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public StudioSyncTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static MusicTrack Track(string path, double bpm, double downbeat, double downbeatConfidence)
        => new(
            new ScannedFile(path, 1000, T),
            new BpmResult(bpm, 0.9) { DownbeatSeconds = downbeat, BeatsPerBar = 4, DownbeatConfidence = downbeatConfidence },
            null,
            TimeSpan.FromMinutes(4),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null);

    private static async Task<StudioViewModel> BuildViewModelWithLibraryAsync(params MusicTrack[] tracks)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        library.Restore(tracks);
        var vm = new StudioViewModel(library, new FakeStudioProjectStore());
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task Sync_WarpsTheClipAndSnapsItToTheNearestBar()
    {
        // 100 BPM source into a 120 BPM project (bars at 0,2,4…). A clip nudged to 2.1s snaps back to 2.0.
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(Track("/m/a.wav", bpm: 100, downbeat: 0, downbeatConfidence: 0.9));
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        StudioClipViewModel clip = vm.Lanes[0].Clips.Single();
        clip.TimelineStartSeconds = 2.1; // off the grid

        vm.SyncClipToProjectGrid(clip);

        Assert.True(clip.WarpEnabled);
        Assert.Equal(2.0, clip.TimelineStartSeconds, Tol);
    }

    [Fact]
    public async Task Sync_IsASingleUndoableEdit()
    {
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(Track("/m/a.wav", bpm: 100, downbeat: 0, downbeatConfidence: 0.9));
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        vm.Lanes[0].Clips.Single().TimelineStartSeconds = 2.1;

        vm.SyncClipToProjectGrid(vm.Lanes[0].Clips.Single());
        vm.Undo();

        // Undo rebuilds the lane, so re-fetch: one step restores BOTH the placement and the warp toggle.
        StudioClipViewModel restored = vm.Lanes[0].Clips.Single();
        Assert.False(restored.WarpEnabled);
        Assert.Equal(2.1, restored.TimelineStartSeconds, Tol);
    }

    [Fact]
    public async Task Sync_LowDownbeatConfidence_FallsBackToBeatAlignment()
    {
        // Ambiguous downbeat (confidence below the floor): snap to the nearest beat (0.5s grid), not bar.
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(Track("/m/b.wav", bpm: 120, downbeat: 0, downbeatConfidence: 0.1));
        vm.AddClipAt("/m/b.wav", deckSlot: 0, startSeconds: 0);
        StudioClipViewModel clip = vm.Lanes[0].Clips.Single();
        clip.TimelineStartSeconds = 1.1;

        vm.SyncClipToProjectGrid(clip);

        Assert.Equal(1.0, clip.TimelineStartSeconds, Tol); // nearest beat (bar mode would jump to 2.0)
        Assert.Contains("beat-aligned", vm.Status);
    }

    [Fact]
    public async Task Sync_UnanalyzedClip_DoesNotWarp_AndSaysSo()
    {
        StudioViewModel vm = await BuildViewModelWithLibraryAsync();
        vm.AddClipAt("/m/unknown.wav", deckSlot: 0, startSeconds: 0); // not in library → no tempo
        StudioClipViewModel clip = vm.Lanes[0].Clips.Single();

        vm.SyncClipToProjectGrid(clip);

        Assert.False(clip.WarpEnabled);
        Assert.Contains("isn't analyzed", vm.Status);
    }

    [Fact]
    public void ToClip_RoundTripsTheSourceDownbeatGrid()
    {
        var clip = new StudioClipViewModel(
            new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, null, SourceBpm: 128,
                SourceDownbeatSeconds: 0.25, SourceBeatsPerBar: 3),
            track: null, pixelsPerSecond: 8);

        StudioClip back = clip.ToClip();

        Assert.Equal(0.25, back.SourceDownbeatSeconds, Tol);
        Assert.Equal(3, back.SourceBeatsPerBar);
    }

    [Fact]
    public async Task Duplicate_PlacesACopyBackToBackOnTheSameLane()
    {
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(Track("/m/a.wav", bpm: 120, downbeat: 0, downbeatConfidence: 0.9));
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        StudioClipViewModel original = vm.Lanes[0].Clips.Single();
        double end = original.TimelineEndSeconds;

        vm.DuplicateClip(original);

        Assert.Equal(2, vm.Lanes[0].Clips.Count);
        StudioClipViewModel copy = vm.Lanes[0].Clips.Last();
        Assert.Equal(end, copy.TimelineStartSeconds, Tol);
        Assert.Equal(original.TrackPath, copy.TrackPath);
    }

    private sealed class FakeStudioProjectStore : IStudioProjectStore
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());

        public Task<StudioProject?> LoadAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<StudioProject?>(null);

        public Task SaveAsync(StudioProject project, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
