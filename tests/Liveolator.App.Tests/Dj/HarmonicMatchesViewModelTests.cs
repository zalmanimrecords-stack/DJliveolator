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
/// The DJ PRO harmonic-match widget: suggestions for the track on the chosen source deck, with the
/// key / BPM / genre gates individually switchable, and a per-row send to either deck through the
/// shared load-or-queue policy.
/// </summary>
public sealed class HarmonicMatchesViewModelTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public HarmonicMatchesViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static MusicTrack Track(string name, double bpm, string camelot, string genre)
    {
        var meta = new TrackMetadata(name, "Artist", null, null, genre, 2020, null, null, null, null, null, null);
        return new MusicTrack(
            new ScannedFile($"/music/{name}.mp3", 1000, T), new BpmResult(bpm, 0.9),
            new MusicalKey(0, KeyMode.Minor, camelot, 0.9),
            TimeSpan.FromSeconds(240), TrackCues.None, MediaAnalysisStatus.Ok, null, meta);
    }

    private static readonly MusicTrack[] Catalog =
    {
        Track("seed", 128, "8A", "Techno"),
        Track("nearest", 128.5, "8B", "Techno"),
        Track("close", 127, "9A", "Techno"),
        Track("far", 140, "8A", "Techno"),
        Track("clash", 128, "3B", "Techno"),
        Track("trance", 128, "8A", "Trance"),
    };

    private static HarmonicMatchesViewModel Build(out FakeDispatcher dispatcher, string? onA = "seed", string? onB = null)
    {
        dispatcher = new FakeDispatcher();
        if (onA is not null) SeedLoaded(dispatcher, 0, onA);
        if (onB is not null) SeedLoaded(dispatcher, 1, onB);

        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        library.Restore(Catalog);
        return new HarmonicMatchesViewModel(library, dispatcher, new DeckTrackLoader(dispatcher, _ => true));
    }

    private static void SeedLoaded(FakeDispatcher dispatcher, int slot, string name)
        => dispatcher.SeedFeedback(PerformanceActionKind.DeckLoadTrack, slot,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 128, Argument: $"/music/{name}.mp3"));

    private static string[] Titles(HarmonicMatchesViewModel vm) => vm.Matches.Select(r => r.Title).ToArray();

    [Fact]
    public void AllGatesOn_KeepsCompatibleKeySameGenreInTempo_ClosestBpmFirst()
        => Assert.Equal(new[] { "nearest", "close" }, Titles(Build(out _)));

    [Fact]
    public void BpmOff_LeavesGenreAndKeyOnly()
    {
        HarmonicMatchesViewModel vm = Build(out _);
        vm.MatchBpm = false;
        Assert.Equal(new[] { "nearest", "close", "far" }, Titles(vm));
    }

    [Fact]
    public void GenreOff_LeavesBpmAndKeyOnly()
    {
        HarmonicMatchesViewModel vm = Build(out _);
        vm.MatchGenre = false;
        Assert.Equal(new[] { "trance", "nearest", "close" }, Titles(vm));
    }

    [Fact]
    public void KeyOff_LetsAClashingKeyThrough()
    {
        HarmonicMatchesViewModel vm = Build(out _);
        vm.MatchKey = false;
        Assert.Contains("clash", Titles(vm));
    }

    [Fact]
    public void NoTrackOnSourceDeck_ShowsNothingAndSaysSo()
    {
        HarmonicMatchesViewModel vm = Build(out _, onA: null);
        Assert.Empty(vm.Matches);
        Assert.Contains("No track on deck A", vm.Summary);
    }

    [Fact]
    public void SourceB_MatchesDeckB_AndExcludesTheTrackOnTheOtherDeck()
    {
        HarmonicMatchesViewModel vm = Build(out _, onA: "nearest", onB: "seed");
        vm.IsSourceB = true;
        Assert.Equal(new[] { "close" }, Titles(vm));
    }

    [Fact]
    public void ADeckLoad_RebuildsTheList()
    {
        HarmonicMatchesViewModel vm = Build(out FakeDispatcher dispatcher, onA: null);
        SeedLoaded(dispatcher, 0, "seed");
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            dispatcher.GetFeedback(PerformanceActionKind.DeckLoadTrack, 0));
        Assert.Equal(new[] { "nearest", "close" }, Titles(vm));
    }

    [Fact]
    public void SendingToAPlayingDeck_QueuesInsteadOfLoading()
    {
        HarmonicMatchesViewModel vm = Build(out FakeDispatcher dispatcher);
        dispatcher.SeedFeedback(PerformanceActionKind.DeckPlayPause, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        vm.LoadRowToDeckBCommand.Execute(vm.Matches[0]).Subscribe();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.PlaylistAppendTrack, action.Kind);
        Assert.Equal(1, action.Slot);
        Assert.False(string.IsNullOrEmpty(vm.LoadStatus));
    }
}
