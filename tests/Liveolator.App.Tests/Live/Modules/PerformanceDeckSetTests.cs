using System.Reactive.Concurrency;
using Liveolator.App.Features.Dj;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Settings;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

/// <summary>
/// Verifies the shared performance modules holder (doc 11): it exposes the two decks + mixer, and — the
/// point of the type — when one instance is handed to both the DJ tab and the Live tab they drive the
/// SAME deck/mixer instances rather than look-alike copies (one source of truth, doc 12).
/// </summary>
public sealed class PerformanceDeckSetTests
{
    [Fact]
    public void ExposesTwoDecksAndMixer()
    {
        var decks = new PerformanceDeckSet();

        Assert.Equal("A", decks.DeckA.DeckId);
        Assert.Equal("B", decks.DeckB.DeckId);
        Assert.NotNull(decks.Mixer);
    }

    [Fact]
    public void DeckTransportEnabled_IsForwardedToBothDecks()
    {
        // The composition root passes deckTransportEnabled: realtimeUp so that, with no realtime audio
        // engine, both decks present their transport controls disabled instead of silently dropping
        // actions (QA finding S1). A dispatcher is still supplied for the always-present mixer handler.
        using var catalogOnly = new PerformanceDeckSet(
            new FakeDispatcher(), deckTransportEnabled: false);

        Assert.False(catalogOnly.DeckA.IsEnabled);
        Assert.False(catalogOnly.DeckB.IsEnabled);
        Assert.True(catalogOnly.DeckA.EqHigh.IsEnabled); // mixer-owned knobs stay live

        using var realtime = new PerformanceDeckSet(new FakeDispatcher()); // default = transport enabled

        Assert.True(realtime.DeckA.IsEnabled);
        Assert.True(realtime.DeckB.IsEnabled);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var decks = new PerformanceDeckSet();

        decks.Dispose();
        decks.Dispose(); // second call must be a safe no-op
    }

    [Fact]
    public void DjAndLive_DriveTheSameDeckInstances_WhenSharedSetInjected()
    {
        var shared = new PerformanceDeckSet();

        var dj = new DjViewModel(decks: shared);
        var live = new LiveViewModel(decks: shared);

        Assert.Same(shared.DeckA, dj.DeckA);
        Assert.Same(shared.DeckB, dj.DeckB);
        Assert.Same(shared.Mixer, dj.Mixer);

        Assert.Same(dj.DeckA, live.DeckA);
        Assert.Same(dj.DeckB, live.DeckB);
        Assert.Same(dj.Mixer, live.Mixer);
    }

    // --- Shared waveform ZOOM knob (one control zooms both decks in seconds → kicks stack on A and B) ---

    [Fact]
    public void WaveformZoom_Zero_SetsBothDecksToWholeTrackOverview()
    {
        using var decks = new PerformanceDeckSet();

        decks.WaveformZoom = 0.0;

        Assert.Equal(0.0, decks.DeckA.ZoomWindow, precision: 6);
        Assert.Equal(0.0, decks.DeckB.ZoomWindow, precision: 6);
    }

    [Fact]
    public void WaveformZoom_ZoomedIn_AppliesEquallyToBothDecks()
    {
        using var decks = new PerformanceDeckSet();

        decks.WaveformZoom = 1.0; // most zoomed in

        Assert.True(decks.DeckA.ZoomWindow > 0.0);                                  // no longer the overview
        Assert.Equal(decks.DeckA.ZoomWindow, decks.DeckB.ZoomWindow, precision: 6); // same time-scale on A and B
    }

    [Fact]
    public void SetWaveformZoom_TightestSeconds_PutsKnobAtMax()
    {
        using var decks = new PerformanceDeckSet();

        decks.SetWaveformZoom(VisualsSettings.MinZoomSeconds); // the most magnified window

        Assert.Equal(1.0, decks.WaveformZoom, precision: 6);
    }

    [Fact]
    public void SetWaveformZoom_WidestSeconds_PutsKnobAtZero()
    {
        using var decks = new PerformanceDeckSet();

        decks.SetWaveformZoom(VisualsSettings.MaxZoomSeconds); // the widest supported window

        Assert.Equal(0.0, decks.WaveformZoom, precision: 6);
    }

    [Fact]
    public void Ctor_SeedsTheKnobFromTheInitialZoomSeconds()
    {
        using var decks = new PerformanceDeckSet(waveformZoomSeconds: VisualsSettings.MinZoomSeconds);

        Assert.Equal(1.0, decks.WaveformZoom, precision: 6);
    }

    // --- Cross-deck beatmatch highlight (both decks light green when playing at the same tempo) ---

    public PerformanceDeckSetTests()
    {
        // Feedback echoes apply synchronously so the cross-deck recompute runs within the test.
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static void PlayAt(FakeDispatcher dispatcher, int slot, double bpm)
    {
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, slot,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: bpm, Argument: "60|200"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, slot,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
    }

    [Fact]
    public void BothDecksMatched_WhenPlayingAtTheSameTempo()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        PlayAt(dispatcher, slot: 0, bpm: 128.0);
        PlayAt(dispatcher, slot: 1, bpm: 128.05); // within the 0.1 BPM beatmatch window

        Assert.True(decks.DeckA.IsBpmMatched);
        Assert.True(decks.DeckB.IsBpmMatched);
    }

    [Fact]
    public void NotMatched_WhenTemposDiffer()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        PlayAt(dispatcher, slot: 0, bpm: 128.0);
        PlayAt(dispatcher, slot: 1, bpm: 130.0);

        Assert.False(decks.DeckA.IsBpmMatched);
        Assert.False(decks.DeckB.IsBpmMatched);
    }

    [Fact]
    public void SyncedFollower_MakesTheOtherDeckMaster()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        // Deck B engages Sync Lock (becomes the follower) → deck A is the MASTER it locks onto.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSyncToggle, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        Assert.True(decks.DeckA.IsSyncMaster);
        Assert.False(decks.DeckB.IsSyncMaster); // the follower is not the master

        // Release → no master.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSyncToggle, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0));

        Assert.False(decks.DeckA.IsSyncMaster);
        Assert.False(decks.DeckB.IsSyncMaster);
    }

    [Fact]
    public void TempoSyncedFollower_AlsoMakesTheOtherDeckMaster()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        // Deck A engages Tempo Sync (tempo-only follower) → deck B is the master.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckTempoSyncToggle, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        Assert.True(decks.DeckB.IsSyncMaster);
        Assert.False(decks.DeckA.IsSyncMaster);
    }

    [Fact]
    public void NotMatched_WhenOneDeckIsNotPlaying()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        PlayAt(dispatcher, slot: 0, bpm: 128.0);
        // Deck B has the same tempo but is only cued (not playing) — a parked deck is not a beatmatch.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 128.0, Argument: "60|200"));

        Assert.False(decks.DeckA.IsBpmMatched);
        Assert.False(decks.DeckB.IsBpmMatched);
    }

    [Fact]
    public void Match_ClearsWhenADeckStops()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        PlayAt(dispatcher, slot: 0, bpm: 128.0);
        PlayAt(dispatcher, slot: 1, bpm: 128.0);
        Assert.True(decks.DeckA.IsBpmMatched);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0));

        Assert.False(decks.DeckA.IsBpmMatched);
        Assert.False(decks.DeckB.IsBpmMatched);
    }

    // --- Octave (half/double-time) tag: each deck shows how its counter relates to the other's ---

    [Fact]
    public void OctaveTag_TagsHalfAndDoubleTime_WhenMatchedAtAnOctave()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        PlayAt(dispatcher, slot: 0, bpm: 140.0);
        PlayAt(dispatcher, slot: 1, bpm: 70.0); // exact half-time — a genuine beatmatch

        Assert.True(decks.DeckA.IsBpmMatched);
        Assert.Equal("2×", decks.DeckA.BpmOctaveLabel);  // A runs at double the other deck
        Assert.Equal("½×", decks.DeckB.BpmOctaveLabel);  // B runs at half
        Assert.True(decks.DeckA.HasBpmOctaveLabel);
        Assert.True(decks.DeckB.HasBpmOctaveLabel);
    }

    [Fact]
    public void OctaveTag_IsEmpty_AtUnison()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        PlayAt(dispatcher, slot: 0, bpm: 128.0);
        PlayAt(dispatcher, slot: 1, bpm: 128.05); // matched at unison — no octave tag

        Assert.True(decks.DeckA.IsBpmMatched);
        Assert.Equal("", decks.DeckA.BpmOctaveLabel);
        Assert.False(decks.DeckA.HasBpmOctaveLabel);
        Assert.False(decks.DeckB.HasBpmOctaveLabel);
    }

    [Fact]
    public void OctaveTag_Clears_WhenNoLongerMatched()
    {
        var dispatcher = new FakeDispatcher();
        using var decks = new PerformanceDeckSet(dispatcher);

        PlayAt(dispatcher, slot: 0, bpm: 140.0);
        PlayAt(dispatcher, slot: 1, bpm: 70.0);
        Assert.True(decks.DeckB.HasBpmOctaveLabel);

        // The DJ pitches deck B off the half-time grid — no longer an octave lock, so the tag drops.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 75.0, Argument: "60|200"));

        Assert.False(decks.DeckB.IsBpmMatched);
        Assert.Equal("", decks.DeckA.BpmOctaveLabel);
        Assert.Equal("", decks.DeckB.BpmOctaveLabel);
    }
}
