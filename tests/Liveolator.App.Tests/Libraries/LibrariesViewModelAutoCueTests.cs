using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Library.Music;
using ReactiveUI;

namespace Liveolator.App.Tests.Libraries;

public sealed class LibrariesViewModelAutoCueTests
{
    public LibrariesViewModelAutoCueTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class FakeAutoCueService : IAutoCueService
    {
        private readonly int _cued;
        public FakeAutoCueService(int cued) => _cued = cued;
        public List<string> Requested { get; } = new();

        public Task<AutoCueOutcome> RunAsync(
            IReadOnlyList<string> trackPaths,
            IProgress<AutoCueProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requested.AddRange(trackPaths);
            progress?.Report(new AutoCueProgress(trackPaths.Count, trackPaths.Count, _cued));
            return Task.FromResult(new AutoCueOutcome(trackPaths.Count, _cued));
        }
    }

    private static LibrariesViewModel BuildViewModel(
        IAutoCueService? service, Func<string, bool>? isLocallyDecodable = null, params string[] files)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(files), new FakeAudioDecoder());
        var vm = new LibrariesViewModel(
            library, autoCueService: service, isLocallyDecodable: isLocallyDecodable ?? (_ => true));
        vm.AddFolder("/music");
        return vm;
    }

    private static LibrariesViewModel BuildViewModel(IAutoCueService? service, params string[] files)
        => BuildViewModel(service, isLocallyDecodable: null, files);

    [Fact]
    public void CanAutoCueLibrary_reflects_whether_a_service_was_wired()
    {
        Assert.False(BuildViewModel(service: null, "/music/A.wav").CanAutoCueLibrary);
        Assert.True(BuildViewModel(new FakeAutoCueService(cued: 1), "/music/A.wav").CanAutoCueLibrary);
    }

    [Fact]
    public async Task AutoCueLibrary_runs_the_service_over_every_catalogued_track()
    {
        var service = new FakeAutoCueService(cued: 2);
        LibrariesViewModel vm = BuildViewModel(service, "/music/A.wav", "/music/B.wav");
        await vm.ScanCommand.Execute().ToTask();

        await vm.AutoCueLibraryCommand.Execute().ToTask();

        Assert.Equal(2, service.Requested.Count);
        Assert.Contains("/music/A.wav", service.Requested);
        Assert.Contains("/music/B.wav", service.Requested);
        Assert.False(vm.IsAutoCueing);
        Assert.Contains("Auto-cue complete", vm.ScanStatus);
    }

    [Fact]
    public async Task AutoCueLibrary_withNoTracks_reportsAndDoesNotCallService()
    {
        var service = new FakeAutoCueService(cued: 0);
        LibrariesViewModel vm = BuildViewModel(service); // no files, no scan

        await vm.AutoCueLibraryCommand.Execute().ToTask();

        Assert.Empty(service.Requested);
        Assert.Contains("No tracks", vm.ScanStatus);
    }

    // The single LIBRARIES "Scan" button (owner request, 2026-06-30): one click scans new/changed files
    // (which analyzes them) then places auto cues — WITHOUT a force re-decode of the whole catalog.
    [Fact]
    public async Task ScanAll_scans_and_autoCues_in_one_command()
    {
        var service = new FakeAutoCueService(cued: 2);
        LibrariesViewModel vm = BuildViewModel(service, "/music/A.wav", "/music/B.wav");

        await vm.ScanAllCommand.Execute().ToTask();

        Assert.Equal(2, vm.Tracks.Count);                         // folders were scanned
        Assert.All(vm.Tracks, t => Assert.NotNull(t.Track.Bpm));  // scan produced fresh analysis
        Assert.Equal(2, service.Requested.Count);                 // auto-cue ran over the catalog
        Assert.False(vm.IsScanning);
        Assert.False(vm.IsAutoCueing);
        Assert.Contains("Auto-cue complete", vm.ScanStatus);
    }

    [Fact]
    public async Task ScanAll_withoutAutoCueService_stillScans()
    {
        LibrariesViewModel vm = BuildViewModel(service: null, "/music/A.wav");

        await vm.ScanAllCommand.Execute().ToTask();

        Assert.Single(vm.Tracks);
        Assert.False(vm.IsScanning);
        Assert.False(vm.IsAutoCueing);
    }

    // The bug: the one-click flow used to force-re-decode the whole catalog, which hangs on un-downloaded
    // OneDrive/online-only placeholders, so it never reached auto-cue. Now unreachable files are skipped
    // before the decode and the pass finishes, cueing only the reachable tracks (and reporting the skip).
    [Fact]
    public async Task ScanAll_skips_unreachable_tracks_before_autoCue()
    {
        var service = new FakeAutoCueService(cued: 1);
        LibrariesViewModel vm = BuildViewModel(
            service,
            isLocallyDecodable: path => path != "/music/Offline.wav", // simulate an online-only placeholder
            "/music/Local.wav", "/music/Offline.wav");

        await vm.ScanAllCommand.Execute().ToTask();

        Assert.Single(service.Requested);                              // only the reachable track was cued
        Assert.Contains("/music/Local.wav", service.Requested);
        Assert.DoesNotContain("/music/Offline.wav", service.Requested);
        Assert.False(vm.IsAutoCueing);
        Assert.Contains("skipped", vm.ScanStatus, StringComparison.OrdinalIgnoreCase);
    }

    // Regression: the detail-panel "Auto Hot-Cue" button appeared to "do nothing" because it rebuilt the
    // Tracks list on completion, which drops the ListBox selection in the UI and clears the cues just
    // placed. The command must leave the row instances (and the selection) intact.
    [Fact]
    public async Task AutoCueSelected_keeps_the_track_rows_so_the_selection_survives()
    {
        var service = new FakeAutoCueService(cued: 1);
        LibrariesViewModel vm = BuildViewModel(service, "/music/A.wav");
        await vm.ScanCommand.Execute().ToTask();
        vm.SelectedTrack = vm.Tracks.Single();
        TrackRowViewModel rowBefore = vm.Tracks.Single();

        await vm.AutoCueSelectedCommand.Execute().ToTask();

        Assert.Same(rowBefore, vm.Tracks.Single()); // the list was NOT rebuilt
        Assert.Same(rowBefore, vm.SelectedTrack);    // so the selection (and its cues) survive
    }

    [Fact]
    public async Task AutoCue_withAllTracksUnreachable_reportsAndDoesNotCallService()
    {
        var service = new FakeAutoCueService(cued: 0);
        LibrariesViewModel vm = BuildViewModel(
            service, isLocallyDecodable: _ => false, "/music/A.wav", "/music/B.wav");
        await vm.ScanCommand.Execute().ToTask();

        await vm.AutoCueLibraryCommand.Execute().ToTask();

        Assert.Empty(service.Requested);
        Assert.Contains("No reachable tracks", vm.ScanStatus);
    }
}
