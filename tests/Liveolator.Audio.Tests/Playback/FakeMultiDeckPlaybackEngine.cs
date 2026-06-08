using System;
using System.Collections.Generic;
using Liveolator.Core.Audio;
using Liveolator.Core.Audio.Sync;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// In-memory <see cref="IMultiDeckPlaybackEngine"/> for testing the playlist→audio binding with no
/// native BASS: records the load/play/stop sequence per slot and can be told to throw on load to
/// exercise the tolerant degrade path.
/// </summary>
internal sealed class FakeMultiDeckPlaybackEngine : IMultiDeckPlaybackEngine
{
    private readonly string?[] _loaded;
    private readonly bool[] _playing;
    private readonly double[] _baseBpm;
    private readonly double[] _firstBeat;

    public FakeMultiDeckPlaybackEngine(int deckCount = 2)
    {
        DeckCount = deckCount;
        _loaded = new string?[deckCount];
        _playing = new bool[deckCount];
        _baseBpm = new double[deckCount];
        _firstBeat = new double[deckCount];
    }

    /// <summary>Ordered log of (operation, slot, arg) so a test can assert the exact sequence.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>When set, the path passed to <see cref="Load"/> that should throw (degrade-path test).</summary>
    public string? ThrowOnLoadOf { get; set; }

    public int DeckCount { get; }

    public event EventHandler<int>? DeckEnded;

    /// <summary>Simulate the bound deck's track running out, so the binding's auto-advance path runs.</summary>
    public void RaiseDeckEnded(int slot) => DeckEnded?.Invoke(this, slot);

    public bool IsPlaying(int slot) => _playing[slot];

    public void Load(int slot, string trackPath)
    {
        Calls.Add($"Load({slot},{trackPath})");
        if (trackPath == ThrowOnLoadOf)
            throw new InvalidOperationException($"Simulated load failure for '{trackPath}'.");
        _loaded[slot] = trackPath;
        _playing[slot] = false;
    }

    public void PlayPause(int slot)
    {
        Calls.Add($"PlayPause({slot})");
        if (_loaded[slot] is not null)
            _playing[slot] = !_playing[slot];
    }

    public void Stop(int slot)
    {
        Calls.Add($"Stop({slot})");
        _playing[slot] = false;
    }

    public string? LoadedOn(int slot) => _loaded[slot];

    // --- Unused transport surface for these tests (the binding only loads/plays/stops). ---
    public double Position(int slot) => 0;
    public void Seek(int slot, double position, bool relative) { }
    public void Jog(int slot, double deltaSeconds) { }
    public double PitchPosition(int slot) => 0.5;
    public void SetPitch(int slot, double value, bool relative) { }
    public double DeckBpm(int slot) => _baseBpm[slot];
    public double MinimumDeckBpm(int slot) => _baseBpm[slot] * 0.92;
    public double MaximumDeckBpm(int slot) => _baseBpm[slot] * 1.08;
    public void SetDeckBpm(int slot, double bpm) { }
    public void Cue(int slot) { }
    public double DeckBaseBpm(int slot) => _baseBpm[slot];
    public void SetDeckBaseBpm(int slot, double bpm) => _baseBpm[slot] = bpm;
    public double DeckFirstBeat(int slot) => _firstBeat[slot];
    public void SetDeckFirstBeat(int slot, double firstBeatSeconds) => _firstBeat[slot] = firstBeatSeconds;
    public void SyncOnce(int slot) => Calls.Add($"SyncOnce({slot})");
    public bool IsSyncLocked(int slot) => false;
    public void SetSyncLock(int slot, bool enabled) { }
    public int? SyncMaster => null;
    public SyncLockState SyncState(int slot) => SyncLockState.Off;
    public bool IsQuantizeEnabled(int slot) => false;
    public void SetQuantize(int slot, bool enabled) { }
    public int HotCueCount => 8;
    public bool IsHotCueSet(int slot, int cueIndex) => false;
    public void HotCue(int slot, int cueIndex) { }
    public double LoopBeats(int slot) => 0;
    public bool IsLooping(int slot) => false;
    public void SetLoop(int slot, double beats) { }
    public void ClearLoop(int slot) { }
}
