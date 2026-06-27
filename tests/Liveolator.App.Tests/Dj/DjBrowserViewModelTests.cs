using System;
using System.Linq;
using System.Reactive.Concurrency;
using Liveolator.App.Features.Dj;
using Liveolator.App.Tests.Fakes;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// The DJ-tab track browser: a focused view over the shared catalog with its own search/sort and a
/// load-to-deck path through the shared <see cref="DeckTrackLoader"/>. Verifies it has no scan/import
/// surface (by construction), filters/sorts, the smart "free deck" rule, and that loading dispatches.
/// </summary>
public sealed class DjBrowserViewModelTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DjBrowserViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static MusicTrack Track(string path, string title, string artist, double? bpm, string? camelot)
    {
        var meta = new TrackMetadata(title, artist, null, null, null, 2020, null, null, null, null, null, null);
        BpmResult? bpmResult = bpm is { } b ? new BpmResult(b, 0.9) : null;
        MusicalKey? key = camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9);
        return new MusicTrack(
            new ScannedFile(path, 1000, T), bpmResult, key,
            TimeSpan.FromSeconds(240), TrackCues.None, MediaAnalysisStatus.Ok, null, meta);
    }

    private static readonly MusicTrack[] Catalog =
    {
        Track("/music/a.mp3", "Aurora", "M83", 128, "8A"),
        Track("/music/b.wav", "Bloom", "Deadmau5", 122, "9A"),
        Track("/music/c.flac", "Crystal", "Justice", 140, "1B"),
    };

    private static DjBrowserViewModel Build(out FakeDispatcher dispatcher, MusicTrack[]? catalog = null)
    {
        dispatcher = new FakeDispatcher();
        var available = new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0, Argument: null);
        dispatcher.SeedFeedback(PerformanceActionKind.DeckPlayPause, 0, available);
        dispatcher.SeedFeedback(PerformanceActionKind.DeckPlayPause, 1, available);

        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        library.Restore(catalog ?? Catalog);
        var loader = new DeckTrackLoader(dispatcher, _ => true); // treat every file as reachable in tests
        return new DjBrowserViewModel(library, dispatcher, loader);
    }

    [Fact]
    public void Refresh_populates_tracks_from_the_shared_catalog()
    {
        var browser = Build(out _);
        Assert.Equal(3, browser.Tracks.Count);
    }

    [Fact]
    public void Refresh_picks_up_tracks_added_after_construction()
    {
        var dispatcher = new FakeDispatcher();
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        var browser = new DjBrowserViewModel(library, dispatcher);
        Assert.Empty(browser.Tracks);

        library.Restore(Catalog); // e.g. the LIBRARIES tab scanned while we were on another tab
        browser.Refresh();

        Assert.Equal(3, browser.Tracks.Count);
    }

    [Fact]
    public void Search_filters_by_text()
    {
        var browser = Build(out _);

        browser.SearchText = "bloom";

        Assert.Single(browser.Tracks);
        Assert.Equal("Bloom", browser.Tracks[0].Title);
    }

    [Fact]
    public void Sort_by_bpm_orders_ascending_then_flips_on_a_second_tap()
    {
        var browser = Build(out _);

        browser.SortKey = TrackSortKey.Bpm;
        browser.SortDescending = false;
        Assert.Equal(new[] { "122.0", "128.0", "140.0" }, browser.Tracks.Select(t => t.Bpm));

        browser.SortDescending = true;
        Assert.Equal(new[] { "140.0", "128.0", "122.0" }, browser.Tracks.Select(t => t.Bpm));
    }

    [Fact]
    public void Loading_a_selected_track_dispatches_through_the_deck_loader()
    {
        var browser = Build(out FakeDispatcher dispatcher);
        browser.SelectedTrack = browser.Tracks.First(t => t.Title == "Aurora");

        browser.LoadToDeckACommand.Execute().Subscribe();

        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckLoadTrack && a.Slot == 0);
        Assert.NotEqual(string.Empty, browser.LoadStatus);
    }

    [Theory]
    // exactly one deck playing -> the OTHER (free) deck takes the track
    [InlineData(true, false, 1)]
    [InlineData(false, true, 0)]
    // both stopped (pre-show) or both playing -> ambiguous, no auto-load
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    public void FreeDeckSlot_only_resolves_when_exactly_one_deck_plays(bool aPlaying, bool bPlaying, int? expected)
    {
        Assert.Equal(expected, DjBrowserViewModel.FreeDeckSlot(aPlaying, bPlaying));
    }

    [Fact]
    public void Browser_exposes_no_scan_or_import_surface()
    {
        // The DJ-tab browser must never carry the CPU-heavy setup actions (scan/rescan/import/auto-cue) —
        // those belong to the LIBRARIES tab. Guard it by reflection so a future edit can't quietly add them.
        var members = typeof(DjBrowserViewModel).GetProperties().Select(p => p.Name).ToArray();
        foreach (var forbidden in new[] { "ScanCommand", "RescanAllCommand", "AutoCueLibraryCommand", "ImportCommand" })
            Assert.DoesNotContain(forbidden, members);
    }
}
