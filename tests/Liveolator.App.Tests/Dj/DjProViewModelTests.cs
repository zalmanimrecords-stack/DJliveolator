using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Fakes;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// DJ PRO surface view-model. Focused on the per-deck TRACK ◀/▶ browse-and-load commands, which step the
/// shared browser's current list onto the matching deck (load-or-queue) — the deck view never learns about
/// the browser; DjProView injects these commands into DjProDeckView.
/// </summary>
public sealed class DjProViewModelTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DjProViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static MusicTrack Track(string path, string title, double bpm)
    {
        var meta = new TrackMetadata(title, "Artist", null, null, null, 2020, null, null, null, null, null, null);
        return new MusicTrack(
            new ScannedFile(path, 1000, T), new BpmResult(bpm, 0.9), null,
            TimeSpan.FromSeconds(240), TrackCues.None, MediaAnalysisStatus.Ok, null, meta);
    }

    private static DjProViewModel Build(out FakeDispatcher dispatcher)
    {
        dispatcher = new FakeDispatcher();
        var available = new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0, Argument: null);
        dispatcher.SeedFeedback(PerformanceActionKind.DeckPlayPause, 0, available);
        dispatcher.SeedFeedback(PerformanceActionKind.DeckPlayPause, 1, available);

        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        library.Restore(new[] { Track("/m/a.mp3", "Aurora", 128), Track("/m/b.wav", "Bloom", 122) });
        var loader = new DeckTrackLoader(dispatcher, _ => true);
        var browser = new DjBrowserViewModel(library, dispatcher, loader);
        var decks = new PerformanceDeckSet(dispatcher);
        return new DjProViewModel(decks, dispatcher, browser);
    }

    [Fact]
    public void BrowseNextA_loads_the_next_browser_track_onto_deck_A()
    {
        var vm = Build(out FakeDispatcher dispatcher);

        vm.BrowseNextACommand.Execute().Subscribe();

        Assert.NotNull(vm.Browser!.SelectedTrack);
        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckLoadTrack && a.Slot == 0);
    }

    [Fact]
    public void BrowseNextB_loads_onto_deck_B()
    {
        var vm = Build(out FakeDispatcher dispatcher);

        vm.BrowseNextBCommand.Execute().Subscribe();

        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckLoadTrack && a.Slot == 1);
    }

    [Fact]
    public void Browse_commands_are_disabled_when_no_browser_is_wired()
    {
        var dispatcher = new FakeDispatcher();
        var decks = new PerformanceDeckSet(dispatcher);
        var vm = new DjProViewModel(decks, dispatcher, browser: null);

        Assert.False(vm.BrowseNextACommand.CanExecute.FirstAsync().Wait());
    }
}
