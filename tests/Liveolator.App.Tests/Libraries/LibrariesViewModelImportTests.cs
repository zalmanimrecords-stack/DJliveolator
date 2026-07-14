using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Media.Import;
using ReactiveUI;
using Xunit;
using PlaylistRecord = Liveolator.Core.Playlist.Playlist;

namespace Liveolator.App.Tests.Libraries;

public sealed class LibrariesViewModelImportTests
{
    public LibrariesViewModelImportTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class FakeFolderImporter : IFolderLibraryImporter
    {
        private readonly LibraryImport _result;
        public string? LastFolder { get; private set; }
        public FakeFolderImporter(LibraryImport result) => _result = result;
        public string FormatName => "Serato";
        public LibraryImport Parse(string rootFolderPath)
        {
            LastFolder = rootFolderPath;
            return _result;
        }
    }

    private sealed class FakePlaylistStore : IPlaylistStore
    {
        public readonly List<PlaylistRecord> Saved = new();
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<string>)Saved.Select(p => p.Name).ToList());
        public Task<PlaylistRecord?> LoadAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Saved.FirstOrDefault(p => p.Name == name));
        public Task SaveAsync(PlaylistRecord playlist, CancellationToken ct = default)
        {
            Saved.Add(playlist);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    private const string RekordboxXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <DJ_PLAYLISTS Version="1.0.0">
          <COLLECTION Entries="1">
            <TRACK TrackID="1" Name="Imported One" Artist="DJ A" AverageBpm="128.00" Tonality="8A"
                   TotalTime="300" Location="file://localhost/C:/Music/one.mp3">
              <TEMPO Inizio="0.150" Bpm="128.00"/>
              <POSITION_MARK Name="Drop" Type="0" Start="64.0" Num="0" Red="255" Green="0" Blue="0"/>
            </TRACK>
          </COLLECTION>
          <PLAYLISTS>
            <NODE Type="0" Name="ROOT" Count="1">
              <NODE Name="My Set" Type="1" KeyType="0" Entries="1"><TRACK Key="1"/></NODE>
            </NODE>
          </PLAYLISTS>
        </DJ_PLAYLISTS>
        """;

    [Fact]
    public async Task ImportFromFile_AddsTracks_WritesCues_AndPlaylist_FromRekordboxXml()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        var cues = new FakeHotCueStore();
        var playlists = new FakePlaylistStore();
        // The source files don't physically exist in the test; the stat probe reports them present so the
        // resolver uses the literal path (the by-filename remap is covered by the Core resolver tests).
        var service = new LibraryImportService(
            cues, playlists, p => new ScannedFile(p, 10, System.DateTime.UnixEpoch));
        var vm = new LibrariesViewModel(
            library, hotCueStore: cues, importService: service,
            importers: new ILibraryImporter[] { new RekordboxXmlImporter() });

        Assert.True(vm.CanImportLibrary);
        Assert.Contains("Rekordbox", vm.ImportFormatNames);

        string path = Path.Combine(Path.GetTempPath(), $"rb-{System.Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, RekordboxXml);
        try
        {
            await vm.ImportFromFileAsync("Rekordbox", path);
        }
        finally
        {
            File.Delete(path);
        }

        MusicTrack imported = Assert.Single(library.All);
        Assert.Equal("Imported One", imported.Title);
        Assert.Equal(128, imported.Bpm!.Bpm);
        Assert.Equal("A Minor", imported.Key!.Name);

        TrackCueRecordAssert(cues, imported.File.Path);
        Assert.Equal("My Set", playlists.Saved.Single().Name);
        Assert.Contains("Rekordbox", vm.ScanStatus);
    }

    [Fact]
    public async Task ImportFromFolder_RunsTheFolderImporter_AndAppliesTheResult()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        var cues = new FakeHotCueStore();
        var playlists = new FakePlaylistStore();
        var service = new LibraryImportService(
            cues, playlists, p => new ScannedFile(p, 10, System.DateTime.UnixEpoch));
        var serato = new FakeFolderImporter(new LibraryImport(
            new[]
            {
                new ImportedTrack(@"C:\Music\x.mp3", Bpm: 128,
                    Cues: new[] { new ImportedCue(0, 8.0, "Drop", 0xFF3B30) }),
            },
            new[] { new ImportedPlaylist("Crate", new[] { @"C:\Music\x.mp3" }) }));
        var vm = new LibrariesViewModel(
            library, hotCueStore: cues, importService: service,
            importers: System.Array.Empty<ILibraryImporter>(),
            folderImporters: new IFolderLibraryImporter[] { serato });

        Assert.True(vm.CanImportLibrary);
        Assert.Contains("Serato", vm.FolderImportFormatNames);

        await vm.ImportFromFolderAsync("Serato", @"C:\SeratoLib");

        Assert.Equal(@"C:\SeratoLib", serato.LastFolder);
        MusicTrack track = Assert.Single(library.All);
        Assert.Equal(128, track.Bpm!.Bpm);
        Assert.Equal("Drop", cues.Get(track.File.Path)!.HotCues.Single().Label);
        Assert.Equal("Crate", playlists.Saved.Single().Name);
        Assert.Contains("Serato", vm.ScanStatus);
    }

    private static void TrackCueRecordAssert(FakeHotCueStore cues, string trackPath)
    {
        Core.Persistence.TrackCueRecord? record = cues.Get(trackPath);
        Assert.NotNull(record);
        Liveolator.Core.Analysis.Cues.HotCue drop = record!.HotCues.Single();
        Assert.Equal("Drop", drop.Label);
        Assert.False(drop.IsAuto); // imported cues are committed, not suggestions
    }
}
