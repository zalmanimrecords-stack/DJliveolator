using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// QA repro (2026-06-21) for the owner's report: "When I press SYNC, the BPM between the two decks does
/// NOT equalize." The runtime evidence in <c>%APPDATA%\Liveolator\logs\liveolator.log</c> shows EVERY
/// <see cref="PerformanceActionKind.DeckLoadTrack"/> failing with
/// <c>System.DllNotFoundException: Unable to load DLL 'bass_fx'</c> thrown from
/// <c>BassMixerBackend.PlugDeck -&gt; BassFx.TempoCreate</c> (the realtime engine wraps every deck in a
/// BASS_FX tempo stream for key-lock; see Liveolator.Audio.csproj lines 22-26: "without it every deck
/// load throws").
///
/// This test proves the FULL causal chain that produces the symptom, independent of the missing native
/// library: when <c>PlugDeck</c> throws during load, <see cref="TwoDeckBassEngine.Load"/> aborts BEFORE
/// it records the deck — so the slot stays empty and its base BPM stays 0. The subsequent
/// <c>SetDeckBaseBpm</c> in <see cref="DeckActionHandler.LoadTrack"/> therefore lands on an empty slot,
/// and <c>SyncOnce</c> hits the <c>s.Deck is not {} deck</c> guard and no-ops. The deck rate never
/// changes, so the two decks' BPM never equalize — exactly what the owner saw.
///
/// NOTE: this is a runtime/environment defect (a stale build / missing bass_fx deployment), NOT a Core
/// logic bug — the SYNC math itself is correct (TwoDeckBassEngineSyncTests, 22/22 green). The current dev
/// build ships a working x64 bass_fx.dll and a probe confirms TempoCreate succeeds against it, so the
/// failure reproduces only when the deployed bass_fx is missing/incompatible (the 2026-06-17 sessions).
/// This test guards the invariant that such a load failure can never silently masquerade as a healthy,
/// SYNC-able deck.
/// </summary>
public class TwoDeckBassEngineSyncMissingFxLibReproTests
{
    // Wraps the working fake backend but throws from PlugDeck exactly as the real backend does when
    // bass_fx is unloadable (BassFx.TempoCreate -> DllNotFoundException).
    private sealed class FxLibMissingBackend : IBassMixerBackend
    {
        private readonly FakeBassMixerBackend _inner;
        public FxLibMissingBackend(FakeBassMixerBackend inner) => _inner = inner;

        public IBassMixerChannel PlugDeck(int deckHandle, int slot)
            => throw new DllNotFoundException("Unable to load DLL 'bass_fx' or one of its dependencies.");

        // The FX library is the missing piece, so the startup probe reports it unavailable.
        public bool IsEffectsLibraryAvailable() => false;

        // Everything else delegates to the working fake.
        public MasterMixInfo CreateMaster() => _inner.CreateMaster();
        public int OpenDeckStream(string filePath) => _inner.OpenDeckStream(filePath);
        public int OpenStemDeck(Liveolator.Core.Analysis.Stems.StemSet stems) => _inner.OpenStemDeck(stems);
        public void SetDeckPlaying(int deckHandle, bool playing) => _inner.SetDeckPlaying(deckHandle, playing);
        public void UnplugDeck(int deckHandle) => _inner.UnplugDeck(deckHandle);
        public double GetDeckPositionFraction(int deckHandle) => _inner.GetDeckPositionFraction(deckHandle);
        public void SetDeckPositionFraction(int deckHandle, double fraction) => _inner.SetDeckPositionFraction(deckHandle, fraction);
        public void SetDeckRate(int deckHandle, double rateMultiplier) => _inner.SetDeckRate(deckHandle, rateMultiplier);
        public void SetDeckKeyLock(int deckHandle, bool enabled) => _inner.SetDeckKeyLock(deckHandle, enabled);
        public void SetStemEnabled(int deckHandle, Liveolator.Core.Analysis.Stems.StemKind kind, bool enabled)
            => _inner.SetStemEnabled(deckHandle, kind, enabled);
        public double GetDeckPositionSeconds(int deckHandle) => _inner.GetDeckPositionSeconds(deckHandle);
        public double GetDeckLengthSeconds(int deckHandle) => _inner.GetDeckLengthSeconds(deckHandle);
        public void SetDeckLoop(int deckHandle, double startSeconds, double endSeconds) => _inner.SetDeckLoop(deckHandle, startSeconds, endSeconds);
        public void ClearDeckLoop(int deckHandle) => _inner.ClearDeckLoop(deckHandle);
        public void SetDeckEndCallback(int deckHandle, Action onEnded) => _inner.SetDeckEndCallback(deckHandle, onEnded);
        public void StartMaster(Action<float[]> onMasterSamples) => _inner.StartMaster(onMasterSamples);
        public bool ReinitOutput(BassInitOptions options) => _inner.ReinitOutput(options);
        public void Dispose() => _inner.Dispose();
    }

    [Fact]
    public void Repro_SyncDoesNotEqualizeBpm_WhenDeckLoadThrowsBecauseBassFxIsMissing()
    {
        var inner = new FakeBassMixerBackend();
        var backend = new FxLibMissingBackend(inner);
        var mixer = new BassMixer(deckCount: TwoDeckBassEngine.Decks);
        var engine = new TwoDeckBassEngine(backend, mixer);
        var handler = new DeckActionHandler(engine);

        // The owner's exact flow: load both decks from the Libraries tab WITH their analyzed BPMs
        // (Value carries the catalog Bpm). The load DISPATCHES fine; it throws deep in the engine.
        Assert.Throws<DllNotFoundException>(() => engine.Load(0, @"S:\a.flac"));
        Assert.Throws<DllNotFoundException>(() => engine.Load(1, @"S:\b.flac"));

        // The deck-load throw aborts BEFORE the base-BPM/feedback is recorded, so each slot is empty and
        // its base BPM is 0 even though the UI passed real BPMs into the action.
        Assert.Equal(0.0, engine.DeckBpm(0));
        Assert.Equal(0.0, engine.DeckBpm(1));

        // Press SYNC on deck B. With an empty deck / base BPM 0 there is nothing to match, so the rate is
        // never set — the two decks' BPM cannot equalize. This is the owner's reported symptom.
        engine.SetDeckBaseBpm(0, 128.0); // even if a later code path tried to thread BPM in, the deck is empty
        engine.SetDeckBaseBpm(1, 120.0);
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSyncOnce, ActionInputMode.Momentary, Slot: 1));

        // No rate was ever applied to deck B (handle 101 never plugged), proving SYNC could not equalize.
        Assert.False(inner.Rate.ContainsKey(101),
            "Deck B rate was set; expected SYNC to be unable to change rate on a deck that failed to load.");
    }
}
