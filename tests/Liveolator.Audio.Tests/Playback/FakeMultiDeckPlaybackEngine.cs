using System;
using System.Collections.Generic;
using Liveolator.Core.Audio;

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

    public FakeMultiDeckPlaybackEngine(int deckCount = 2)
    {
        DeckCount = deckCount;
        _loaded = new string?[deckCount];
        _playing = new bool[deckCount];
    }

    /// <summary>Ordered log of (operation, slot, arg) so a test can assert the exact sequence.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>When set, the path passed to <see cref="Load"/> that should throw (degrade-path test).</summary>
    public string? ThrowOnLoadOf { get; set; }

    public int DeckCount { get; }

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
    public double PitchPosition(int slot) => 0.5;
    public void SetPitch(int slot, double value, bool relative) { }
    public void Cue(int slot) { }
    public double DeckBaseBpm(int slot) => 0;
    public void SetDeckBaseBpm(int slot, double bpm) { }
    public double DeckFirstBeat(int slot) => 0;
    public void SetDeckFirstBeat(int slot, double firstBeatSeconds) { }
    public bool IsSyncLocked(int slot) => false;
    public void SetSyncLock(int slot, bool enabled) { }
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
