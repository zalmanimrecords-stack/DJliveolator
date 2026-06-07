using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Playlists;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Playlists;

public sealed class PlaylistBuilderViewModelTests
{
    public PlaylistBuilderViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class FakePlaylistStore : IPlaylistStore
    {
        public Dictionary<string, Playlist> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Saved.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList());

        public Task<Playlist?> LoadAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Saved.GetValueOrDefault(name));

        public Task SaveAsync(Playlist playlist, CancellationToken ct = default)
        {
            Saved[playlist.Name] = playlist;
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

    private static async Task<(PlaylistBuilderViewModel vm, FakePlaylistStore store, FakeLivePlaylist live)>
        BuildViewModel(params string[] files)
    {
        MusicLibrary library = await BuildLibrary(files);
        var store = new FakePlaylistStore();
        var live = new FakeLivePlaylist();
        var vm = new PlaylistBuilderViewModel(library, store, live);
        await vm.InitializeAsync();
        return (vm, store, live);
    }

    [Fact]
    public async Task AddTrack_appends_and_dedupes()
    {
        var (vm, _, _) = await BuildViewModel("/music/A.wav", "/music/B.wav");

        vm.SelectedLibraryTrack = vm.Library[0];
        await vm.AddTrackCommand.Execute().ToTask();
        await vm.AddTrackCommand.Execute().ToTask(); // same track again → no duplicate
        vm.SelectedLibraryTrack = vm.Library[1];
        await vm.AddTrackCommand.Execute().ToTask();

        Assert.Equal(2, vm.Current.Count);
    }

    [Fact]
    public async Task Save_persists_via_store_and_lists_it()
    {
        var (vm, store, _) = await BuildViewModel("/music/A.wav", "/music/B.wav");
        vm.Name = "Warmup";
        vm.SelectedLibraryTrack = vm.Library[0];
        await vm.AddTrackCommand.Execute().ToTask();

        await vm.SaveCommand.Execute().ToTask();

        Assert.True(store.Saved.ContainsKey("Warmup"));
        Assert.Single(store.Saved["Warmup"].TrackPaths);
        Assert.Contains("Warmup", vm.SavedPlaylists);
    }

    [Fact]
    public async Task Open_loads_a_saved_set()
    {
        var (vm, store, _) = await BuildViewModel("/music/A.wav", "/music/B.wav");
        store.Saved["Peak"] = new Playlist("Peak", new[] { "/music/B.wav", "/music/A.wav" });
        await vm.InitializeAsync(); // refresh SavedPlaylists from the store

        vm.SelectedSaved = "Peak";
        await vm.OpenCommand.Execute().ToTask();

        Assert.Equal("Peak", vm.Name);
        Assert.Equal(new[] { "/music/B.wav", "/music/A.wav" }, vm.Current.Select(e => e.Path));
    }

    [Fact]
    public async Task AutoFill_from_seed_populates_a_set()
    {
        var (vm, _, _) = await BuildViewModel("/music/A.wav", "/music/B.wav", "/music/C.wav");
        vm.SelectedLibraryTrack = vm.Library[0];

        await vm.AutoFillCommand.Execute().ToTask();

        Assert.True(vm.Current.Count >= 1);
        Assert.Equal(vm.Library[0].Track.File.Path, vm.Current[0].Path); // seed leads the set
    }

    [Fact]
    public async Task SendToLiveSet_loads_the_live_playlist_with_the_paths()
    {
        var (vm, _, live) = await BuildViewModel("/music/A.wav", "/music/B.wav");
        vm.SelectedLibraryTrack = vm.Library[0];
        await vm.AddTrackCommand.Execute().ToTask();
        vm.SelectedLibraryTrack = vm.Library[1];
        await vm.AddTrackCommand.Execute().ToTask();

        await vm.SendToLiveSetCommand.Execute().ToTask();

        Assert.NotNull(live.LoadedPaths);
        Assert.Equal(2, live.LoadedPaths!.Count);
    }

    [Fact]
    public async Task Remove_and_move_reorder_the_set()
    {
        var (vm, _, _) = await BuildViewModel("/music/A.wav", "/music/B.wav", "/music/C.wav");
        foreach (var row in vm.Library.ToList())
        {
            vm.SelectedLibraryTrack = row;
            await vm.AddTrackCommand.Execute().ToTask();
        }
        Assert.Equal(3, vm.Current.Count);

        // move the last entry up one, then remove the first
        vm.SelectedCurrent = vm.Current[2];
        await vm.MoveUpCommand.Execute().ToTask();
        Assert.Equal(vm.Current[1], vm.SelectedCurrent);

        vm.SelectedCurrent = vm.Current[0];
        await vm.RemoveCommand.Execute().ToTask();
        Assert.Equal(2, vm.Current.Count);
    }
}
