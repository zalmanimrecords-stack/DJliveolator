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
/// Tests the STUDIO FILE-menu model: the Open/Delete submenu collections track the saved projects, opening
/// a named entry loads that project, and deleting one removes it from the store + the menu. Drives the VM
/// the way the FILE flyout does (the per-name <see cref="MenuActionViewModel"/> commands).
/// </summary>
public sealed class StudioFileMenuTests
{
    public StudioFileMenuTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static StudioViewModel BuildViewModel(FakeStudioProjectStore store)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        return new StudioViewModel(library, store);
    }

    private static StudioProject ProjectWithOneClip(string name) => new(
        name, 124,
        new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, null) },
        Array.Empty<AutomationLane>());

    [Fact]
    public async Task OpenItems_ListsTheSavedProjects()
    {
        var store = new FakeStudioProjectStore();
        store.Saved["Warmup"] = ProjectWithOneClip("Warmup");
        store.Saved["Closing"] = ProjectWithOneClip("Closing");

        StudioViewModel vm = BuildViewModel(store);
        await vm.InitializeAsync();

        Assert.True(vm.HasSavedProjects);
        Assert.Equal(
            new[] { "Closing", "Warmup" },
            vm.OpenItems.Select(i => i.Header).OrderBy(h => h).ToArray());
    }

    [Fact]
    public async Task FreshStore_HasNoSavedProjects()
    {
        StudioViewModel vm = BuildViewModel(new FakeStudioProjectStore());
        await vm.InitializeAsync();

        Assert.False(vm.HasSavedProjects);
        Assert.Empty(vm.OpenItems);
        Assert.Empty(vm.DeleteItems);
    }

    [Fact]
    public async Task OpenItem_LoadsThatProject()
    {
        var store = new FakeStudioProjectStore();
        store.Saved["set"] = ProjectWithOneClip("set");

        StudioViewModel vm = BuildViewModel(store);
        await vm.InitializeAsync();

        MenuActionViewModel item = vm.OpenItems.Single(i => i.Header == "set");
        item.Command.Execute(null);

        Assert.Equal("set", vm.Name);
        Assert.Equal(1, vm.Lanes.Sum(l => l.Clips.Count));
        Assert.Equal("set", vm.SelectedSaved);
    }

    [Fact]
    public async Task DeleteItem_RemovesTheProjectFromStoreAndMenu()
    {
        var store = new FakeStudioProjectStore();
        store.Saved["gone"] = ProjectWithOneClip("gone");
        store.Saved["kept"] = ProjectWithOneClip("kept");

        StudioViewModel vm = BuildViewModel(store);
        await vm.InitializeAsync();

        MenuActionViewModel item = vm.DeleteItems.Single(i => i.Header == "gone");
        item.Command.Execute(null);

        Assert.False(store.Saved.ContainsKey("gone"));
        Assert.True(store.Saved.ContainsKey("kept"));
        Assert.DoesNotContain(vm.OpenItems, i => i.Header == "gone");
        Assert.Contains(vm.OpenItems, i => i.Header == "kept");
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
