using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Mixer;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

public sealed class StudioViewModelTests
{
    public StudioViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class FakeStudioSetStore : IStudioSetStore
    {
        public Dictionary<string, StudioSet> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Saved.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList());

        public Task<StudioSet?> LoadAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Saved.GetValueOrDefault(name));

        public Task SaveAsync(StudioSet set, CancellationToken ct = default)
        {
            Saved[set.Name] = set;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string name, CancellationToken ct = default)
        {
            Saved.Remove(name);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLivePlaylist : ILivePlaylist
    {
        public IReadOnlyList<string>? LoadedPaths { get; private set; }
        public QueueEntry? Now => null;
        public IReadOnlyList<QueueEntry> Upcoming => Array.Empty<QueueEntry>();
        public bool AutoAdvance => false;
        public void Load(IEnumerable<string> trackPaths) => LoadedPaths = trackPaths.ToList();
        public void Append(string trackPath) { }
        public void InsertNext(string trackPath) { }
        public void Move(Guid id, int toIndex) { }
        public void RemoveFuture(Guid id) { }
        public void SetAutoAdvance(bool on) { }
        public void SkipNow() { }
        public void SkipOn(Quantize when, int everyN = 1) { }
        public void NotifyTrackEnded() { }
        public event EventHandler<QueueEntry?>? NowChanged { add { } remove { } }
        public event EventHandler? Changed { add { } remove { } }
    }

    private static async Task<MusicLibrary> BuildLibrary(params string[] files)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(files), new FakeAudioDecoder());
        await library.ScanAsync(new[] { "/music" });
        return library;
    }

    private static async Task<(StudioViewModel vm, FakeStudioSetStore store, FakeLivePlaylist live)>
        BuildViewModel(params string[] files)
    {
        MusicLibrary library = await BuildLibrary(files);
        var store = new FakeStudioSetStore();
        var live = new FakeLivePlaylist();
        var vm = new StudioViewModel(library, store, planner: null, waveforms: null, live: live);
        await vm.InitializeAsync();
        return (vm, store, live);
    }

    [Fact]
    public async Task AddTrack_appends_dedupes_and_normalizes_transitions()
    {
        var (vm, _, _) = await BuildViewModel("/music/A.wav", "/music/B.wav");

        vm.SelectedLibraryTrack = vm.Library[0];
        await vm.AddTrackCommand.Execute().ToTask();
        await vm.AddTrackCommand.Execute().ToTask(); // same track again → no duplicate
        vm.SelectedLibraryTrack = vm.Library[1];
        await vm.AddTrackCommand.Execute().ToTask();

        Assert.Equal(2, vm.Entries.Count);
        Assert.Null(vm.Entries[0].TransitionIn);     // first lane: nothing blends in
        Assert.NotNull(vm.Entries[1].TransitionIn);  // later lanes: a default transition
    }

    [Fact]
    public async Task AutoBuild_from_seed_populates_set_with_seed_first()
    {
        var (vm, _, _) = await BuildViewModel("/music/A.wav", "/music/B.wav", "/music/C.wav");
        vm.SelectedLibraryTrack = vm.Library[0];

        await vm.AutoBuildCommand.Execute().ToTask();

        Assert.True(vm.Entries.Count >= 1);
        Assert.Equal(vm.Library[0].Track.File.Path, vm.Entries[0].Path);
        Assert.Null(vm.Entries[0].TransitionIn);
    }

    [Fact]
    public async Task Save_persists_set_with_transitions_via_store()
    {
        var (vm, store, _) = await BuildViewModel("/music/A.wav", "/music/B.wav");
        vm.Name = "Warmup";
        vm.SelectedLibraryTrack = vm.Library[0];
        await vm.AddTrackCommand.Execute().ToTask();
        vm.SelectedLibraryTrack = vm.Library[1];
        await vm.AddTrackCommand.Execute().ToTask();

        await vm.SaveCommand.Execute().ToTask();

        Assert.True(store.Saved.ContainsKey("Warmup"));
        StudioSet saved = store.Saved["Warmup"];
        Assert.Equal(2, saved.Entries.Count);
        Assert.Null(saved.Entries[0].TransitionIn);
        Assert.NotNull(saved.Entries[1].TransitionIn);
        Assert.Contains("Warmup", vm.SavedSets);
    }

    [Fact]
    public async Task Open_loads_a_saved_set_and_round_trips_transition()
    {
        var (vm, store, _) = await BuildViewModel("/music/A.wav", "/music/B.wav");
        store.Saved["Peak"] = new StudioSet("Peak", new[]
        {
            new StudioEntry("/music/A.wav"),
            new StudioEntry("/music/B.wav",
                TransitionIn: new StudioTransition(TransitionKind.BassSwap, 32, CrossfaderCurve.Smooth, TransitionAnchor.TailOverlap)),
        });
        await vm.InitializeAsync(); // refresh SavedSets

        vm.SelectedSaved = "Peak";
        await vm.OpenCommand.Execute().ToTask();

        Assert.Equal("Peak", vm.Name);
        Assert.Equal(new[] { "/music/A.wav", "/music/B.wav" }, vm.Entries.Select(e => e.Path));
        Assert.Null(vm.Entries[0].TransitionIn);
        Assert.Equal(TransitionKind.BassSwap, vm.Entries[1].TransitionIn!.Kind);
        Assert.Equal(32, vm.Entries[1].TransitionIn.LengthBeats);
    }

    [Fact]
    public async Task SendToLiveSet_loads_the_live_playlist_with_the_paths()
    {
        var (vm, _, live) = await BuildViewModel("/music/A.wav", "/music/B.wav");
        foreach (var row in vm.Library.ToList())
        {
            vm.SelectedLibraryTrack = row;
            await vm.AddTrackCommand.Execute().ToTask();
        }

        await vm.SendToLiveSetCommand.Execute().ToTask();

        Assert.NotNull(live.LoadedPaths);
        Assert.Equal(2, live.LoadedPaths!.Count);
    }

    [Fact]
    public async Task Move_then_remove_keeps_first_lane_transitionless()
    {
        var (vm, _, _) = await BuildViewModel("/music/A.wav", "/music/B.wav", "/music/C.wav");
        foreach (var row in vm.Library.ToList())
        {
            vm.SelectedLibraryTrack = row;
            await vm.AddTrackCommand.Execute().ToTask();
        }
        Assert.Equal(3, vm.Entries.Count);

        // Move the last lane up, then remove the first: the (new) first lane must shed its transition.
        vm.SelectedEntry = vm.Entries[2];
        await vm.MoveUpCommand.Execute().ToTask();
        Assert.Equal(vm.Entries[1], vm.SelectedEntry);

        vm.SelectedEntry = vm.Entries[0];
        await vm.RemoveCommand.Execute().ToTask();

        Assert.Equal(2, vm.Entries.Count);
        Assert.Null(vm.Entries[0].TransitionIn);
        Assert.NotNull(vm.Entries[1].TransitionIn);
    }
}
