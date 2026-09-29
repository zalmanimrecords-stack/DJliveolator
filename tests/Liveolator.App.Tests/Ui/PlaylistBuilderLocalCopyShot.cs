using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Liveolator.App.Features.Playlists;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Playlist.LocalCopy;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Renders the playlists window with a set in it, so we can see the "Copy to this computer…" button sits in
/// the top bar and is enabled once the set has tracks (not just that the binding compiles).
/// </summary>
public class PlaylistBuilderLocalCopyShot
{
    [AvaloniaFact]
    public async Task Capture_copy_to_this_computer_button()
    {
        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);

        var library = new MusicLibrary(new FakeFileEnumerator("/music/a.mp3"), new FakeAudioDecoder());
        await library.ScanAsync(new[] { "/music" });
        var playlists = new NullPlaylistStore();
        var wiring = new LocalCopyWiring(
            new PlaylistLocalCopyService(new NullCopier(), new FakeMusicCatalogStore(), new FakeHotCueStore(), playlists),
            () => new[] { "/music" },
            (_, _) => { },
            new NullSettingsStore());
        var vm = new PlaylistBuilderViewModel(library, playlists, localCopy: wiring);
        await vm.InitializeAsync();
        vm.SelectedLibraryTrack = vm.Library.Single();
        vm.AddTrackCommand.Execute().Subscribe();

        var window = new PlaylistBuilderWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "playlists-copy-to-computer.png"));

            Button copy = window.GetVisualDescendants().OfType<Button>()
                .Single(b => (b.Content as string)?.StartsWith("Copy to this computer") == true);
            Assert.True(copy.IsVisible);
            Assert.True(copy.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class NullCopier : IFileCopier
    {
        public Task<ScannedFile> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
            => throw new NotSupportedException();
        public ScannedFile? TryStat(string path) => null;
        public long AvailableFreeBytes(string folder) => long.MaxValue;
    }

    private sealed class NullSettingsStore : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(AppSettings.Default);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullPlaylistStore : IPlaylistStore
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<Playlist?> LoadAsync(string name, CancellationToken ct = default) => Task.FromResult<Playlist?>(null);
        public Task SaveAsync(Playlist playlist, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
