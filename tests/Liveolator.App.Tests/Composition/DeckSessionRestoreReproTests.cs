using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Composition;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.App.Tests.Composition;

/// <summary>
/// QA repro tests for "loaded deck tracks are not restored / are degraded after an app restart".
/// These document two confirmed runtime defects in the deck-session restore path; they are written
/// to FAIL against current behaviour so they double as regression guards once fixed.
/// </summary>
public sealed class DeckSessionRestoreReproTests
{
    // DEFECT 1 — a deck track whose drive is offline at launch (the user's library lives on the
    // network share \\192.168.68.131\Storage / S:, Unavailable at launch) must NOT be lost. Restore()
    // used to skip any deck whose File.Exists was false and forget it. The fix: never dispatch a doomed
    // load to the engine while offline (BASS cannot tell a failed open from a real one — DeckTrackLoader's
    // invariant), but keep the entry and auto-load it with its saved BPM + downbeat anchor once the share
    // mounts. This test pins that contract.
    [Fact]
    public void OfflineTrack_IsNotFedToTheEngine_ThenAutoLoadsWithSavedAnchorOnMount()
    {
        const string offlineNetworkPath = @"S:\Music\Psytrance\Headroom - 16Bit Masterchef.mp3";
        var dispatcher = new FakeDispatcher();
        var store = new FakeDeckSessionStore(
            [new DeckSessionState(0, offlineNetworkPath, 139.67, 0.29)]);
        bool reachable = false;

        using var persistence = new DeckSessionPersistence(
            dispatcher, store, deckCount: 2, fileExists: _ => reachable, enableRetryTimer: false);

        // While the share is offline the load is deferred, NOT dispatched (a doomed BASS open would
        // throw before the deck ever shows the track, and would masquerade as a successful load).
        Assert.DoesNotContain(
            dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckLoadTrack);

        // When the share mounts, the auto-retry loads the saved track with its saved BPM and first-beat
        // anchor intact — so the deck comes back exactly as it was left.
        reachable = true;
        persistence.RetryPending();

        Assert.Contains(
            dispatcher.Dispatched,
            a => a.Kind == PerformanceActionKind.DeckLoadTrack
                 && a.Slot == 0 && a.Argument == offlineNetworkPath && a.Value == 139.67);
        Assert.Contains(
            dispatcher.Dispatched,
            a => a.Kind == PerformanceActionKind.DeckSetFirstBeat && a.Slot == 0 && a.Value == 0.29);
    }

    // DEFECT 2 — a second startup loader re-loads the same decks AFTER DeckSessionPersistence has
    // subscribed, with Bpm = 0 / FirstBeat = 0 (catalog has no analysis), clobbering the freshly
    // restored values. Verified on disk: deck-session.json Bpm 139.67 / FirstBeat 0.29 BEFORE a
    // launch became Bpm 0 / FirstBeat 0 AFTER. This destroys the saved beat-grid anchor on every run.
    [Fact]
    public async Task Repro_SecondStartupLoad_ClobbersRestoredBpmAndFirstBeat()
    {
        string track = Path.GetTempFileName();
        try
        {
            var dispatcher = new FakeDispatcher();
            var store = new FakeDeckSessionStore(
                [new DeckSessionState(0, track, 139.67, 0.29)]);

            using var persistence = new DeckSessionPersistence(dispatcher, store, deckCount: 2);

            // Simulate the eager PlaylistAudioPlayer (ServiceConfig.cs:717) re-loading the restored
            // "Now" track onto the same slot with no analysis available (Value/Bpm = 0), exactly as
            // GoToTrack does at PlaylistAudioPlayer.cs:130-140 when the catalog lacks a BpmResult.
            dispatcher.RaiseFeedback(
                PerformanceActionKind.DeckLoadTrack, 0,
                new ActionFeedbackState(true, true, Value: 0, Argument: track));
            dispatcher.RaiseFeedback(
                PerformanceActionKind.DeckSetFirstBeat, 0,
                new ActionFeedbackState(false, true, Value: 0));

            await store.WaitForSaveAsync();

            DeckSessionState saved = Assert.Single(store.LastSaved!);
            // The restored anchor must survive a no-analysis re-load; today it is wiped to 0/0.
            Assert.Equal(139.67, saved.Bpm);
            Assert.Equal(0.29, saved.FirstBeatSeconds);
        }
        finally
        {
            File.Delete(track);
        }
    }

    private sealed class FakeDeckSessionStore : IDeckSessionStore
    {
        private readonly IReadOnlyList<DeckSessionState>? _loaded;
        private TaskCompletionSource _saved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeDeckSessionStore(IReadOnlyList<DeckSessionState>? loaded = null) => _loaded = loaded;

        public IReadOnlyList<DeckSessionState>? LastSaved { get; private set; }

        public Task<IReadOnlyList<DeckSessionState>?> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_loaded);

        public Task SaveAsync(IReadOnlyList<DeckSessionState> decks, CancellationToken cancellationToken = default)
        {
            LastSaved = decks;
            _saved.TrySetResult();
            return Task.CompletedTask;
        }

        // Wait for the next save, then arm for a subsequent one (two feedbacks => two saves).
        public async Task WaitForSaveAsync()
        {
            await _saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            _saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
