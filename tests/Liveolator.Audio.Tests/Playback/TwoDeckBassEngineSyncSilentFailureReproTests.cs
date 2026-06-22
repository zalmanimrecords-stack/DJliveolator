using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Mixer;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// QA repro (2026-06-21) for "SYNC does nothing on the DJ deck even with both decks loaded + playing".
///
/// The DJ tab drives the SAME shared <see cref="TwoDeckBassEngine"/> through <see cref="DeckActionHandler"/>
/// that these tests drive. SYNC is a one-shot beatmatch (<see cref="PerformanceActionKind.DeckSyncOnce"/>);
/// <c>TwoDeckBassEngine.SyncOnce</c> returns SILENTLY (no log, no rate change) whenever EITHER deck's
/// analyzed base BPM is 0. A deck's base BPM is 0 whenever its load action arrived with Value=0 — which
/// happens on a real runtime path: a track loaded onto a PLAYING deck is queued and later loaded by
/// <c>PlaylistAudioPlayer.GoToTrack</c>, which resolves the BPM via <c>MusicLibrary.TryGet(path)</c> — an
/// EXACT path lookup (MediaLibrary._byPath) that misses when the deck-queue/mapped-drive path differs from
/// the scanned catalog path. The deck UI can still SHOW a BPM (its meta line comes from a filename-fallback
/// catalog lookup, decoupled from the engine's base BPM), so the user sees a BPM yet SYNC is a no-op.
///
/// FIX (2026-06-21): two changes landed. (1) The engine load path now resolves BPM via
/// <c>MusicLibrary.TryGetByPathOrName</c> (exact-then-file-name), so a path-mismatched track gets the BPM
/// the UI shows and SYNC works. (2) When the base BPM is still genuinely unknown, <c>SyncOnce</c> now LOGS
/// the skip reason instead of returning silently (global standard #26: "never fail silently").
/// These tests now guard that fixed behaviour: a missing-BPM SYNC is still a rate no-op, but it is no
/// longer silent; and SYNC beatmatches when both decks carry a BPM.
/// </summary>
public class TwoDeckBassEngineSyncSilentFailureReproTests
{
    private const string SyncLogFragment = "one-shot synced to leader";
    private const string SkipLogFragment = "one-shot sync skipped";

    private static (TwoDeckBassEngine engine, FakeBassMixerBackend backend, ListLoggerFactory logs) NewEngine()
    {
        var backend = new FakeBassMixerBackend();
        var mixer = new BassMixer(deckCount: TwoDeckBassEngine.Decks);
        var logs = new ListLoggerFactory();
        var engine = new TwoDeckBassEngine(backend, mixer, loggerFactory: logs);
        return (engine, backend, logs);
    }

    // Mirrors the DJ-tab dispatch path: UI/queue -> DeckLoadTrack(Value=bpm) -> DeckActionHandler -> engine.
    private static void LoadViaHandler(DeckActionHandler handler, int slot, string path, double bpm)
        => handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, ActionInputMode.Absolute, Value: bpm, Slot: slot, Argument: path));

    [Fact]
    public void SyncOnce_LogsTheSkipReason_WhenFollowerHasNoBaseBpm()
    {
        (TwoDeckBassEngine engine, FakeBassMixerBackend backend, ListLoggerFactory logs) = NewEngine();
        var handler = new DeckActionHandler(engine);

        // Leader (deck A) loaded WITH analyzed BPM; follower (deck B) loaded with Value=0 — the queue /
        // path-mismatch case where MusicLibrary.TryGet(exact path) missed, so no BPM threaded to the engine.
        LoadViaHandler(handler, slot: 0, @"\\share\a.flac", bpm: 128.0);
        LoadViaHandler(handler, slot: 1, @"S:\b.flac", bpm: 0.0);
        // Both decks playing — exactly the owner's reported condition.
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 0));
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 1));

        logs.Clear(); // isolate what SYNC itself emits

        // Press SYNC on the follower deck (the DJ tab's ⇄ button -> DeckSyncOnce, Momentary).
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSyncOnce, ActionInputMode.Momentary, Slot: 1));

        // With no base BPM there is nothing to match against, so the rate is still left unchanged...
        Assert.False(backend.Rate.TryGetValue(101, out double rate) && Math.Abs(rate - 1.0) > 1e-9,
            $"Follower rate changed to {rate}; expected SYNC to be a no-op when no base BPM is known.");

        // ...but it must no longer be SILENT: SyncOnce now logs WHY it skipped (global standard #26).
        Assert.Contains(logs.Messages, m => m.Contains(SkipLogFragment, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, m => m.Contains(SyncLogFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Repro_SyncOnce_Works_WhenBothDecksCarryBpm_ProvingZeroBpmIsTheOnlyDifference()
    {
        // Control: identical to the repro above EXCEPT the follower load carries its BPM. SYNC then works,
        // proving the no-op is caused solely by the missing base BPM, not by the dispatch/handler wiring.
        (TwoDeckBassEngine engine, FakeBassMixerBackend backend, ListLoggerFactory logs) = NewEngine();
        var handler = new DeckActionHandler(engine);

        LoadViaHandler(handler, slot: 0, @"\\share\a.flac", bpm: 128.0);
        LoadViaHandler(handler, slot: 1, @"S:\b.flac", bpm: 120.0);
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 0));
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 1));

        logs.Clear();
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSyncOnce, ActionInputMode.Momentary, Slot: 1));

        // Follower rate is matched to the leader (128/120), and the success line is logged.
        Assert.Equal(128.0 / 120.0, backend.Rate[101], precision: 6);
        Assert.Contains(logs.Messages, m => m.Contains(SyncLogFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void SyncOnce_RaisesDeckBpmFeedback_ThatUpdatesTheFollowersOnScreenBpm()
    {
        // The DJ deck's BPM readout binds to the DeckBpm FEEDBACK value. Pressing SYNC must therefore not
        // only change the engine rate but emit a DeckBpm feedback whose Value equals the leader's tempo —
        // otherwise the audible tempo matches yet the on-screen numbers still differ ("SYNC didn't match").
        (TwoDeckBassEngine engine, _, _) = NewEngine();
        var handler = new DeckActionHandler(engine);

        LoadViaHandler(handler, slot: 0, @"\\share\a.flac", bpm: 128.0);
        LoadViaHandler(handler, slot: 1, @"S:\b.flac", bpm: 120.0);

        double followerBpmFeedback = double.NaN;
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckBpm && e.Slot == 1)
                followerBpmFeedback = e.State.Value;
        };

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSyncOnce, ActionInputMode.Momentary, Slot: 1));

        // The follower's reported BPM now equals the leader's — the number the deck shows after SYNC.
        Assert.Equal(128.0, followerBpmFeedback, precision: 3);
        // And GetFeedback (what a late UI subscriber reads) agrees.
        Assert.Equal(128.0, handler.GetFeedback(PerformanceActionKind.DeckBpm, slot: 1).Value, precision: 3);
    }

    /// <summary>Minimal capturing logger factory so the test can assert on what SyncOnce logged (or didn't).</summary>
    private sealed class ListLoggerFactory : ILoggerFactory
    {
        public List<string> Messages { get; } = new();
        public void Clear() => Messages.Clear();
        public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        private sealed class ListLogger : ILogger
        {
            private readonly List<string> _sink;
            public ListLogger(List<string> sink) => _sink = sink;
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _sink.Add(formatter(state, exception));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
