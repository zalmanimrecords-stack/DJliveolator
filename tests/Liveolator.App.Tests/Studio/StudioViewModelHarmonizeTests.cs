using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Integration tests for the STUDIO HARMONIZE auto-arrange wired into <see cref="StudioViewModel"/>:
/// gathering the tracks on the lanes, running <see cref="HarmonicAutoArranger"/>, loading the result as
/// one undoable edit, and gating CanExecute on having at least two resolvable tracks. Drives the VM the
/// way the UI does on the immediate scheduler.
/// </summary>
public sealed class StudioViewModelHarmonizeTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public StudioViewModelHarmonizeTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    // A keyed, analyzed track the harmonic builder can seed/order from.
    private static MusicTrack Track(string path, string camelot, double bpm)
        => new(
            new ScannedFile(path, 1000, T),
            new BpmResult(bpm, 0.9),
            new MusicalKey(0, KeyMode.Major, camelot, 0.9),
            TimeSpan.FromMinutes(4),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null);

    // Build a VM whose library already holds the given tracks (so clip paths resolve to MusicTracks),
    // then initialize the path index the way the shown tab does.
    private static async Task<StudioViewModel> BuildViewModelWithLibraryAsync(params MusicTrack[] tracks)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        library.Restore(tracks);
        var vm = new StudioViewModel(library, new FakeStudioProjectStore());
        await vm.InitializeAsync();
        return vm;
    }

    private static int ClipCount(StudioViewModel vm) => vm.Lanes.Sum(l => l.Clips.Count);

    private static IReadOnlyList<string> OrderedPaths(StudioViewModel vm)
        => vm.Lanes
            .SelectMany(l => l.Clips)
            .OrderBy(c => c.TimelineStartSeconds)
            .Select(c => c.TrackPath)
            .ToList();

    private static async Task<bool> CanHarmonizeAsync(StudioViewModel vm)
        => await vm.HarmonizeCommand.CanExecute.FirstAsync();

    [Fact]
    public async Task CanExecute_IsFalse_WithFewerThanTwoResolvableTracks()
    {
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(Track("a.mp3", "8B", 120));
        vm.AddClipAt("a.mp3", deckSlot: 0, startSeconds: 0);

        Assert.False(await CanHarmonizeAsync(vm));
    }

    [Fact]
    public async Task CanExecute_IsFalse_WhenClipsResolveToTheSameTrack()
    {
        // Two clips, one distinct track => not enough to harmonize.
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(Track("a.mp3", "8B", 120));
        vm.AddClipAt("a.mp3", deckSlot: 0, startSeconds: 0);
        vm.AddClipAt("a.mp3", deckSlot: 1, startSeconds: 16);

        Assert.False(await CanHarmonizeAsync(vm));
    }

    [Fact]
    public async Task CanExecute_IsTrue_WithTwoOrMoreResolvableTracks()
    {
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(
            Track("a.mp3", "8B", 120), Track("b.mp3", "8B", 121));
        vm.AddClipAt("a.mp3", deckSlot: 0, startSeconds: 0);
        vm.AddClipAt("b.mp3", deckSlot: 1, startSeconds: 16);

        Assert.True(await CanHarmonizeAsync(vm));
    }

    [Fact]
    public async Task Harmonize_LoadsTheRearrangedProject_ForTheTracksOnTheLanes()
    {
        var seed = Track("seed.mp3", "8B", 120);
        var a = Track("a.mp3", "8B", 121);
        var b = Track("b.mp3", "9B", 122);
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(seed, a, b);

        // Drop them out of harmonic order at scattered positions.
        vm.AddClipAt("b.mp3", deckSlot: 0, startSeconds: 40);
        vm.AddClipAt("a.mp3", deckSlot: 1, startSeconds: 80);
        vm.AddClipAt("seed.mp3", deckSlot: 0, startSeconds: 120);
        Assert.Equal(3, ClipCount(vm));

        vm.HarmonizeCommand.Execute().Subscribe();

        // The arranger seeds from the FIRST eligible track in the order the VM gathers them — which is
        // timeline order (earliest clip first): b(40s), a(80s), seed(120s). Compare to the arranger's own
        // output over that same ordering so the test pins the wiring, not a guessed harmonic chain.
        StudioProject expected = new HarmonicAutoArranger().Arrange(
            new[] { b, a, seed },
            new HarmonicSetOptions(Length: 3),
            new AutoArrangeOptions(ProjectName: vm.Name));

        Assert.Equal(
            expected.Clips.Select(c => c.TrackPath).ToArray(),
            OrderedPaths(vm).ToArray());
        // The rearranged set starts at the very front of the timeline (back-to-back layout from 0).
        Assert.Equal(0.0, vm.Lanes.SelectMany(l => l.Clips).Min(c => c.TimelineStartSeconds), 1e-9);
    }

    [Fact]
    public async Task Harmonize_IsUndoable_RestoringThePriorArrangement()
    {
        var a = Track("a.mp3", "8B", 120);
        var b = Track("b.mp3", "8B", 121);
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(a, b);

        vm.AddClipAt("b.mp3", deckSlot: 0, startSeconds: 40);
        vm.AddClipAt("a.mp3", deckSlot: 1, startSeconds: 80);
        IReadOnlyList<string> before = OrderedPaths(vm);
        double[] startsBefore = vm.Lanes.SelectMany(l => l.Clips).OrderBy(c => c.TimelineStartSeconds)
            .Select(c => c.TimelineStartSeconds).ToArray();

        vm.HarmonizeCommand.Execute().Subscribe();
        Assert.True(vm.CanUndo);

        vm.Undo();

        // The pre-harmonize arrangement (paths + positions) is restored.
        Assert.Equal(before.ToArray(), OrderedPaths(vm).ToArray());
        double[] startsAfter = vm.Lanes.SelectMany(l => l.Clips).OrderBy(c => c.TimelineStartSeconds)
            .Select(c => c.TimelineStartSeconds).ToArray();
        Assert.Equal(startsBefore, startsAfter);
    }

    [Fact]
    public async Task Harmonize_SkipsUnresolvedAndUnanalyzedClips()
    {
        // a + b are keyed/analyzed; "ghost.mp3" is not in the library, "nokey.mp3" has no key.
        var a = Track("a.mp3", "8B", 120);
        var b = Track("b.mp3", "8B", 121);
        var noKey = new MusicTrack(
            new ScannedFile("nokey.mp3", 1000, T), new BpmResult(120, 0.9), null,
            TimeSpan.FromMinutes(4), TrackCues.None, MediaAnalysisStatus.Ok, null);
        StudioViewModel vm = await BuildViewModelWithLibraryAsync(a, b, noKey);

        vm.AddClipAt("a.mp3", deckSlot: 0, startSeconds: 0);
        vm.AddClipAt("b.mp3", deckSlot: 1, startSeconds: 16);
        vm.AddClipAt("ghost.mp3", deckSlot: 0, startSeconds: 32); // unresolved
        vm.AddClipAt("nokey.mp3", deckSlot: 1, startSeconds: 48); // resolved but unkeyed

        vm.HarmonizeCommand.Execute().Subscribe();

        // Only the keyed tracks survive into the arrangement (the no-key track has no Camelot, so the
        // builder cannot place it; the ghost path resolves to nothing).
        IReadOnlyList<string> arranged = OrderedPaths(vm);
        Assert.DoesNotContain("ghost.mp3", arranged);
        Assert.Contains("a.mp3", arranged);
        Assert.Contains("b.mp3", arranged);
    }

    private sealed class FakeStudioProjectStore : IStudioProjectStore
    {
        public Dictionary<string, StudioProject> Saved { get; } = new();

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Saved.Keys.ToList());

        public Task<StudioProject?> LoadAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(Saved.GetValueOrDefault(name));

        public Task SaveAsync(StudioProject project, CancellationToken cancellationToken = default)
        {
            Saved[project.Name] = project;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            Saved.Remove(name);
            return Task.CompletedTask;
        }
    }
}
