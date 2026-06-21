using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using ReactiveUI;

namespace Liveolator.App.Tests.Libraries;

public sealed class LibrariesViewModelHotCueTests
{
    public LibrariesViewModelHotCueTests()
    {
        // Run ReactiveCommand and the VM's UI-marshalling synchronously so the async cue load resolves
        // inline (the fake store completes synchronously).
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static (LibrariesViewModel Vm, FakeHotCueStore Cues) BuildViewModel(params string[] files)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(files), new FakeAudioDecoder());
        var cues = new FakeHotCueStore();
        var vm = new LibrariesViewModel(library, hotCueStore: cues);
        vm.AddFolder("/music");
        return (vm, cues);
    }

    [Fact]
    public async Task Selecting_a_track_loads_its_stored_hot_cues()
    {
        (LibrariesViewModel vm, FakeHotCueStore cues) = BuildViewModel("/music/Alpha.wav");
        cues.Seed(new TrackCueRecord(
            "/music/Alpha.wav", SampleRate: 44_100, SlotCount: 8, PrimaryCueSamples: null,
            HotCues: new[]
            {
                new HotCue(0, 88_200, "Intro"),
                new HotCue(2, 0, Label: null, Color: null, IsAuto: true),
            }));
        await vm.ScanCommand.Execute().ToTask();

        vm.SelectedTrack = vm.Tracks.Single();

        Assert.True(vm.HasHotCues);
        Assert.Equal(2, vm.HotCues.Count);
        Assert.Equal(new[] { "1", "3" }, vm.HotCues.Select(c => c.Number));
        Assert.Equal("0:02.00", vm.HotCues[0].Time);
        Assert.Equal("Intro", vm.HotCues[0].Label);
        Assert.Equal("auto", vm.HotCues[1].Tag);
    }

    [Fact]
    public async Task Selecting_a_track_without_cues_leaves_the_list_empty()
    {
        (LibrariesViewModel vm, _) = BuildViewModel("/music/Alpha.wav");
        await vm.ScanCommand.Execute().ToTask();

        vm.SelectedTrack = vm.Tracks.Single();

        Assert.False(vm.HasHotCues);
        Assert.Empty(vm.HotCues);
    }

    [Fact]
    public async Task Changing_selection_replaces_the_shown_cues()
    {
        (LibrariesViewModel vm, FakeHotCueStore cues) = BuildViewModel("/music/Alpha.wav", "/music/Beta.wav");
        cues.Seed(new TrackCueRecord(
            "/music/Alpha.wav", 44_100, 8, null, new[] { new HotCue(0, 44_100, "A-cue") }));
        await vm.ScanCommand.Execute().ToTask();

        vm.SelectedTrack = vm.Tracks.First(t => t.Title == "Alpha");
        Assert.Single(vm.HotCues);

        vm.SelectedTrack = vm.Tracks.First(t => t.Title == "Beta");

        Assert.False(vm.HasHotCues);
        Assert.Empty(vm.HotCues);
    }

    [Fact]
    public async Task Confirming_a_suggested_cue_commits_it_to_manual()
    {
        (LibrariesViewModel vm, FakeHotCueStore cues) = BuildViewModel("/music/Alpha.wav");
        cues.Seed(new TrackCueRecord(
            "/music/Alpha.wav", 44_100, 8, null,
            new[] { new HotCue(1, 88_200, "Drop", 0xFF3B30, IsAuto: true) }));
        await vm.ScanCommand.Execute().ToTask();
        vm.SelectedTrack = vm.Tracks.Single();
        Assert.True(vm.HotCues.Single().CanConfirm);

        await vm.HotCues.Single().ConfirmCommand.Execute().ToTask();

        HotCue committed = cues.Get("/music/Alpha.wav")!.HotCues.Single(c => c.Index == 1);
        Assert.False(committed.IsAuto);            // now manual
        Assert.Equal("Drop", committed.Label);     // keeps label/color/position
        Assert.Equal(0xFF3B30, committed.Color);
        Assert.Equal(88_200L, committed.PositionSamples);
        // The shown row refreshes: no longer a suggestion, so Confirm is gone.
        Assert.False(vm.HotCues.Single().CanConfirm);
        Assert.Equal(string.Empty, vm.HotCues.Single().Tag);
    }

    [Fact]
    public async Task Deleting_a_cue_removes_it_from_the_store_and_the_list()
    {
        (LibrariesViewModel vm, FakeHotCueStore cues) = BuildViewModel("/music/Alpha.wav");
        cues.Seed(new TrackCueRecord(
            "/music/Alpha.wav", 44_100, 8, null,
            new[] { new HotCue(0, 44_100, "Intro"), new HotCue(2, 88_200, "Drop", IsAuto: true) }));
        await vm.ScanCommand.Execute().ToTask();
        vm.SelectedTrack = vm.Tracks.Single();
        Assert.Equal(2, vm.HotCues.Count);

        await vm.HotCues.First(c => c.Number == "3").DeleteCommand.Execute().ToTask();

        Assert.Single(vm.HotCues);
        Assert.Equal("1", vm.HotCues.Single().Number);
        Assert.DoesNotContain(cues.Get("/music/Alpha.wav")!.HotCues, c => c.Index == 2);
    }

    [Fact]
    public async Task A_cue_store_load_failure_surfaces_without_crashing()
    {
        (LibrariesViewModel vm, FakeHotCueStore cues) = BuildViewModel("/music/Alpha.wav");
        await vm.ScanCommand.Execute().ToTask();
        cues.ThrowOnLoad = true;

        vm.SelectedTrack = vm.Tracks.Single();

        Assert.False(vm.HasHotCues);
        Assert.Contains("hot cues", vm.ScanStatus, StringComparison.OrdinalIgnoreCase);
    }
}
