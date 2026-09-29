using System.Reactive.Concurrency;
using Liveolator.App.Features.Playlists;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Playlist.LocalCopy;
using Liveolator.Core.Settings;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Playlists;

public sealed class PlaylistBuilderViewModelLocalCopyTests
{
    private readonly FakeFileCopier _copier = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly FakePlaylistStore _playlists = new();
    private readonly List<(string Folder, IReadOnlyList<MusicTrack> Tracks)> _adopted = new();

    public PlaylistBuilderViewModelLocalCopyTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public async Task CopyToComputer_CopiesTheSet_HandsTheCopiesToTheLibrary_AndRemembersTheFolder()
    {
        PlaylistBuilderViewModel vm = await BuildWithSet("/music/a.mp3", "/music/b.mp3");

        await vm.CopyToComputerAsync("/gig");

        (string folder, IReadOnlyList<MusicTrack> tracks) = Assert.Single(_adopted);
        Assert.Equal("/gig", folder);
        Assert.Equal(new[] { "/gig/music/a.mp3", "/gig/music/b.mp3" }, tracks.Select(t => t.File.Path));
        Assert.Equal("/gig", _settings.Current.LocalCopyFolder);
        Assert.Equal("/gig", await vm.LastLocalCopyFolderAsync());
        Assert.Contains("Friday (local)", vm.SavedPlaylists);
        Assert.Contains("Copied 2", vm.Status);
        Assert.False(vm.IsCopying);
    }

    [Fact]
    public async Task CanCopyToComputer_NeedsTheWiringAndAtLeastOneTrack()
    {
        PlaylistBuilderViewModel empty = await Build(wired: true, "/music/a.mp3");
        PlaylistBuilderViewModel unwired = await Build(wired: false, "/music/a.mp3");
        AddAll(unwired);

        Assert.False(empty.CanCopyToComputer);
        Assert.False(unwired.CanCopyToComputer);
        AddAll(empty);
        Assert.True(empty.CanCopyToComputer);
    }

    [Fact]
    public async Task CopyToComputer_WithoutEnoughSpace_SaysSo_AndChangesNothing()
    {
        _copier.FreeBytes = 10;
        PlaylistBuilderViewModel vm = await BuildWithSet("/music/a.mp3");

        await vm.CopyToComputerAsync("/gig");

        Assert.Contains("Not enough space", vm.Status);
        Assert.Empty(_adopted);
        Assert.Null(_settings.Current.LocalCopyFolder);
    }

    [Fact]
    public async Task CopyToComputer_NamesTheTracksLeftOut()
    {
        _copier.FailOn = "/music/b.mp3";
        PlaylistBuilderViewModel vm = await BuildWithSet("/music/a.mp3", "/music/b.mp3");

        await vm.CopyToComputerAsync("/gig");

        Assert.Contains("Copied 1", vm.Status);
        Assert.Contains("1 left out", vm.Status);
        Assert.Contains("b.mp3", vm.Status);
    }

    [Fact]
    public async Task Cancel_KeepsWhatWasCopied_AndStillRegistersTheFolder()
    {
        PlaylistBuilderViewModel vm = await BuildWithSet("/music/a.mp3", "/music/b.mp3");
        _copier.AfterCopy = () => vm.CancelCopyCommand.Execute().Subscribe();

        await vm.CopyToComputerAsync("/gig");

        Assert.Contains("cancelled", vm.Status);
        Assert.Equal("/gig", Assert.Single(_adopted).Folder);
        Assert.False(vm.IsCopying);
    }

    private async Task<PlaylistBuilderViewModel> BuildWithSet(params string[] files)
    {
        PlaylistBuilderViewModel vm = await Build(wired: true, files);
        AddAll(vm);
        vm.Name = "Friday";
        return vm;
    }

    private async Task<PlaylistBuilderViewModel> Build(bool wired, params string[] files)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(files), new FakeAudioDecoder());
        await library.ScanAsync(new[] { "/music" });
        LocalCopyWiring? wiring = wired
            ? new LocalCopyWiring(
                new PlaylistLocalCopyService(_copier, new FakeMusicCatalogStore(), new FakeHotCueStore(), _playlists),
                () => new[] { "/music" },
                (folder, tracks) => _adopted.Add((folder, tracks)),
                _settings)
            : null;
        var vm = new PlaylistBuilderViewModel(library, _playlists, localCopy: wiring);
        await vm.InitializeAsync();
        return vm;
    }

    private static void AddAll(PlaylistBuilderViewModel vm)
    {
        foreach (var row in vm.Library.OrderBy(r => r.Track.File.Path).ToList())
        {
            vm.SelectedLibraryTrack = row;
            vm.AddTrackCommand.Execute().Subscribe();
        }
    }

    private sealed class FakeFileCopier : IFileCopier
    {
        private readonly Dictionary<string, ScannedFile> _files = new(StringComparer.OrdinalIgnoreCase);
        public long FreeBytes { get; set; } = long.MaxValue;
        public string? FailOn { get; set; }
        public Action? AfterCopy { get; set; }

        public Task<ScannedFile> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (sourcePath == FailOn)
                throw new IOException("read error");
            var copied = new ScannedFile(destinationPath, 1000, DateTime.UtcNow);
            _files[destinationPath] = copied;
            AfterCopy?.Invoke();
            return Task.FromResult(copied);
        }

        public ScannedFile? TryStat(string path) => _files.TryGetValue(path, out ScannedFile f) ? f : null;

        public long AvailableFreeBytes(string folder) => FreeBytes;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;

        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlaylistStore : IPlaylistStore
    {
        private readonly Dictionary<string, Playlist> _saved = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_saved.Keys.ToList());

        public Task<Playlist?> LoadAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_saved.GetValueOrDefault(name));

        public Task SaveAsync(Playlist playlist, CancellationToken ct = default)
        {
            _saved[playlist.Name] = playlist;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string name, CancellationToken ct = default)
        {
            _saved.Remove(name);
            return Task.CompletedTask;
        }
    }
}
