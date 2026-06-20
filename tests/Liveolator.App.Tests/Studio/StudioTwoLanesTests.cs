using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Shared;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// STUDIO is a two-channel arranger (lanes A/B). Guards the lane set and the back-compat fold of an old
/// four-lane project: clips/automation saved on a C/D lane collapse onto their paired primary lane
/// (C→A, D→B) so their audio is preserved rather than silently dropped when the project is opened.
/// </summary>
public sealed class StudioTwoLanesTests
{
    public StudioTwoLanesTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static StudioViewModel BuildViewModel(FakeStudioProjectStore store)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        return new StudioViewModel(library, store);
    }

    [Fact]
    public void StudioHasTwoLanes_LabelledAAndB()
    {
        StudioViewModel vm = BuildViewModel(new FakeStudioProjectStore());

        Assert.Equal(2, vm.Lanes.Count);
        Assert.Equal(new[] { "A", "B" }, vm.Lanes.Select(l => l.Label).ToArray());
    }

    [Fact]
    public void AddClipAt_OutsideTheTwoLanes_IsIgnored()
    {
        StudioViewModel vm = BuildViewModel(new FakeStudioProjectStore());

        vm.AddClipAt("/m/a.wav", deckSlot: 2, startSeconds: 0); // old C lane no longer exists

        Assert.Equal(0, vm.Lanes.Sum(l => l.Clips.Count));
    }

    [Fact]
    public async Task OpeningAnOldFourLaneProject_FoldsCAndDOntoAAndB()
    {
        var store = new FakeStudioProjectStore();
        store.Saved["legacy"] = new StudioProject(
            "legacy", 120,
            new[]
            {
                new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, null),  // A stays A
                new StudioClip(2, "/m/c.wav", 4, TimeSpan.Zero, null),  // C folds onto A
                new StudioClip(3, "/m/d.wav", 8, TimeSpan.Zero, null),  // D folds onto B
            },
            Array.Empty<AutomationLane>());

        StudioViewModel vm = BuildViewModel(store);
        await vm.InitializeAsync();
        vm.OpenItems.Single(i => i.Header == "legacy").Command.Execute(null);

        // All three clips survive (none dropped), and they land only on the two existing lanes.
        Assert.Equal(3, vm.Lanes.Sum(l => l.Clips.Count));
        Assert.All(vm.Lanes.SelectMany(l => l.Clips), c => Assert.InRange(c.DeckSlot, 0, 1));
        Assert.Equal(
            new[] { "/m/a.wav", "/m/c.wav" },                 // A + folded C (C→A)
            vm.Lanes[0].Clips.Select(c => c.TrackPath).OrderBy(p => p).ToArray());
        Assert.Equal(
            new[] { "/m/d.wav" },                             // folded D (D→B)
            vm.Lanes[1].Clips.Select(c => c.TrackPath).ToArray());
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
