using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Integration tests for STUDIO timeline undo/redo wired into <see cref="StudioViewModel"/>: an edit is
/// undoable, redo reapplies it, the flags track the stacks, and New/Open reset the history. Drives the
/// VM the way the UI does (AddClipAt / clip mutations / commands) on the immediate scheduler.
/// </summary>
public sealed class StudioViewModelUndoTests
{
    public StudioViewModelUndoTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static StudioViewModel BuildViewModel(FakeStudioProjectStore? store = null)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        return new StudioViewModel(library, store ?? new FakeStudioProjectStore());
    }

    private static int ClipCount(StudioViewModel vm) => vm.Lanes.Sum(l => l.Clips.Count);

    [Fact]
    public void FreshViewModel_HasNothingToUndoOrRedo()
    {
        StudioViewModel vm = BuildViewModel();
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void AddClip_IsUndoable_RestoresThePriorClipSet()
    {
        StudioViewModel vm = BuildViewModel();
        Assert.Equal(0, ClipCount(vm));

        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        Assert.Equal(1, ClipCount(vm));
        Assert.True(vm.CanUndo);

        vm.Undo();
        Assert.Equal(0, ClipCount(vm)); // back to empty
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);
    }

    [Fact]
    public void Redo_ReappliesTheUndoneAdd()
    {
        StudioViewModel vm = BuildViewModel();
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        vm.Undo();
        Assert.Equal(0, ClipCount(vm));

        vm.Redo();
        Assert.Equal(1, ClipCount(vm)); // the clip is back
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Undo_RestoresAClipsTimelinePosition()
    {
        StudioViewModel vm = BuildViewModel();
        vm.Bpm = 120;
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        StudioClipViewModel clip = vm.Lanes[0].Clips[0];
        Assert.Equal(0, clip.TimelineStartSeconds, 1e-9);

        clip.TimelineStartSeconds = 16; // an inspector-style move (undoable)
        Assert.Equal(16, vm.Lanes[0].Clips[0].TimelineStartSeconds, 1e-9);

        vm.Undo();
        Assert.Equal(0, vm.Lanes[0].Clips[0].TimelineStartSeconds, 1e-9); // moved back
    }

    [Fact]
    public void Undo_RestoresAutomationKeyframes()
    {
        StudioViewModel vm = BuildViewModel();
        AutomationLaneViewModel curve = vm.Lanes[0].CurrentAutomation;
        Assert.Empty(curve.Points);

        curve.AddPoint(2.0, 0.75); // a user automation edit
        Assert.Single(vm.Lanes[0].CurrentAutomation.Points);
        Assert.True(vm.CanUndo);

        vm.Undo();
        Assert.Empty(vm.Lanes[0].CurrentAutomation.Points); // the keyframe is gone
    }

    [Fact]
    public void NewProject_ClearsTheHistory()
    {
        StudioViewModel vm = BuildViewModel();
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        Assert.True(vm.CanUndo);

        vm.NewCommand.Execute().Subscribe();
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public async Task Open_ClearsTheHistory()
    {
        var store = new FakeStudioProjectStore();
        store.Saved["set"] = new StudioProject(
            "set", 124,
            new[] { new StudioClip(0, "/m/a.wav", 0, System.TimeSpan.Zero, null) },
            System.Array.Empty<AutomationLane>());

        StudioViewModel vm = BuildViewModel(store);
        vm.AddClipAt("/m/b.wav", deckSlot: 1, startSeconds: 4); // some pre-open edit history
        Assert.True(vm.CanUndo);

        vm.SelectedSaved = "set";
        await vm.OpenCommand.Execute().FirstAsync();

        Assert.False(vm.CanUndo); // opening starts a fresh history
        Assert.False(vm.CanRedo);
        Assert.Equal(1, ClipCount(vm)); // the opened project's single clip
    }

    [Fact]
    public void MultipleEdits_UndoUnwindsInReverseOrder()
    {
        StudioViewModel vm = BuildViewModel();
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0);
        vm.AddClipAt("/m/b.wav", deckSlot: 1, startSeconds: 8);
        Assert.Equal(2, ClipCount(vm));

        vm.Undo();
        Assert.Equal(1, ClipCount(vm));
        vm.Undo();
        Assert.Equal(0, ClipCount(vm));
        Assert.False(vm.CanUndo);
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
