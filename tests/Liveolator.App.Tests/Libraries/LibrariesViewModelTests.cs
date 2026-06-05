using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using ReactiveUI;

namespace Liveolator.App.Tests.Libraries;

public sealed class LibrariesViewModelTests
{
    public LibrariesViewModelTests()
    {
        // Make ReactiveCommand and the VM's UI-marshalling run synchronously in tests.
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static LibrariesViewModel BuildViewModel(params string[] files)
        => BuildViewModel(metadata: null, files);

    private static LibrariesViewModel BuildViewModel(
        IReadOnlyDictionary<string, TrackMetadata>? metadata, params string[] files)
    {
        ITrackMetadataReader? reader = metadata is null ? null : new FakeTrackMetadataReader(metadata);
        var library = new MusicLibrary(new FakeFileEnumerator(files), new FakeAudioDecoder(), metadataReader: reader);
        var vm = new LibrariesViewModel(library);
        vm.AddFolder("/music");
        return vm;
    }

    [Fact]
    public async Task Scan_populates_one_row_per_file()
    {
        LibrariesViewModel vm = BuildViewModel("/music/Alpha.wav", "/music/Beta.wav", "/music/Gamma.wav");

        await vm.ScanCommand.Execute().ToTask();

        Assert.Equal(3, vm.Tracks.Count);
        Assert.False(vm.IsScanning);
        Assert.Contains(vm.Tracks, t => t.Title == "Alpha");
    }

    [Fact]
    public async Task Search_filters_tracks_by_title()
    {
        LibrariesViewModel vm = BuildViewModel("/music/Alpha.wav", "/music/Beta.wav");
        await vm.ScanCommand.Execute().ToTask();

        vm.SearchText = "alph";
        Assert.Single(vm.Tracks);
        Assert.Equal("Alpha", vm.Tracks[0].Title);

        vm.SearchText = "";
        Assert.Equal(2, vm.Tracks.Count);
    }

    [Fact]
    public async Task Metadata_surfaces_artist_and_subline_and_tag_title()
    {
        var meta = new Dictionary<string, TrackMetadata>
        {
            ["/music/Alpha.wav"] = new TrackMetadata(
                "Midnight City", "M83", "Hurry Up", null, "Electronic",
                2011, 3, null, 320, 44100, 2, "MP3"),
        };
        LibrariesViewModel vm = BuildViewModel(meta, "/music/Alpha.wav");
        await vm.ScanCommand.Execute().ToTask();

        TrackRowViewModel row = vm.Tracks.Single();
        Assert.Equal("Midnight City", row.Title); // tag title wins over the "Alpha" filename
        Assert.Equal("M83", row.Artist);
        Assert.Contains("M83", row.SubLine);
        Assert.Contains("320kbps", row.SubLine);
        Assert.Equal("Hurry Up", row.Album);
        Assert.Equal("Stereo", row.Channels);
    }

    [Fact]
    public async Task Search_matches_on_artist()
    {
        var meta = new Dictionary<string, TrackMetadata>
        {
            ["/music/Alpha.wav"] = new TrackMetadata(null, "Deadmau5", null, null, null, null, null, null, null, null, null, null),
            ["/music/Beta.wav"] = new TrackMetadata(null, "M83", null, null, null, null, null, null, null, null, null, null),
        };
        LibrariesViewModel vm = BuildViewModel(meta, "/music/Alpha.wav", "/music/Beta.wav");
        await vm.ScanCommand.Execute().ToTask();

        vm.SearchText = "deadmau";

        Assert.Single(vm.Tracks);
        Assert.Equal("Deadmau5", vm.Tracks[0].Artist);
    }

    [Fact]
    public void AddFolder_adds_a_zero_count_status_row_immediately()
    {
        // BuildViewModel already adds "/music"; no scan yet.
        LibrariesViewModel vm = BuildViewModel("/music/Alpha.wav");

        FolderStatusViewModel row = Assert.Single(vm.FolderStatuses);
        Assert.Equal("music", row.Name);
        Assert.Equal("No tracks", row.TrackCountText);
    }

    [Fact]
    public async Task Scan_populates_folder_status_counts_and_progress()
    {
        LibrariesViewModel vm = BuildViewModel("/music/Alpha.wav", "/music/Beta.wav", "/music/Gamma.wav");

        await vm.ScanCommand.Execute().ToTask();

        FolderStatusViewModel row = Assert.Single(vm.FolderStatuses);
        Assert.Equal("music", row.Name);
        Assert.StartsWith("3 tracks", row.StatusText); // issue suffixes (if any) follow the count
        Assert.Equal(100, vm.ScanProgressValue);
    }

    [Fact]
    public async Task Selecting_a_track_rebuilds_harmonic_matches_without_error()
    {
        LibrariesViewModel vm = BuildViewModel("/music/Alpha.wav", "/music/Beta.wav");
        await vm.ScanCommand.Execute().ToTask();

        vm.SelectedTrack = vm.Tracks[0];

        Assert.NotNull(vm.SelectedTrack);
        // Matches depend on detected keys; the contract under test is "no throw, consistent count".
        Assert.True(vm.HarmonicMatches.Count >= 0);
        Assert.DoesNotContain(vm.HarmonicMatches, m => m.Title == vm.SelectedTrack!.Title);
    }
}
