using Liveolator.Core.Audio.Sync;

namespace Liveolator.Core.Audio;

/// <summary>
/// Adapts a single-deck <see cref="IAudioPlaybackEngine"/> to the slot-addressed
/// <see cref="IMultiDeckPlaybackEngine"/> shape, exposing exactly one deck slot. Lets
/// <see cref="DeckActionHandler"/> run one slot-aware code path while the existing single-deck
/// composition (and its tests) keep working unchanged.
/// </summary>
internal sealed class SingleDeckEngineAdapter : IMultiDeckPlaybackEngine
{
    private readonly IAudioPlaybackEngine _engine;

    public SingleDeckEngineAdapter(IAudioPlaybackEngine engine)
        => _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public int DeckCount => 1;

    // The legacy single-deck engine (IAudioPlaybackEngine) has no end-of-stream signal, so this never
    // fires; declared to satisfy the seam (the two-deck BASS engine is the path that raises it). The
    // explicit add/remove keep the compiler from warning about an unused event.
    public event EventHandler<int>? DeckEnded
    {
        add { }
        remove { }
    }

    public bool IsPlaying(int slot) => slot == 0 && _engine.IsPlaying;

    public void Load(int slot, string trackPath)
    {
        EnsureSlot(slot);
        _engine.Load(trackPath);
    }

    public void PlayPause(int slot)
    {
        EnsureSlot(slot);
        _engine.PlayPause();
    }

    public void Stop(int slot)
    {
        EnsureSlot(slot);
        _engine.Stop();
    }

    // The legacy single-deck engine (IAudioPlaybackEngine) supports only load/play/stop — it has no
    // position/pitch/cue/sync surface. These adapt to no-ops with neutral readings so the slot-aware
    // DeckActionHandler runs one code path; the two-deck BASS engine is the path that implements them.
    public double Position(int slot) { EnsureSlot(slot); return 0; }

    public void Seek(int slot, double position, bool relative) => EnsureSlot(slot);

    public double PitchPosition(int slot) { EnsureSlot(slot); return 0.5; }

    public void SetPitch(int slot, double value, bool relative) => EnsureSlot(slot);

    public void Cue(int slot) => EnsureSlot(slot);

    // The single-deck engine has no sync — there is no second deck to match against — so base BPM and
    // the first-beat anchor are neither stored nor used.
    public double DeckBaseBpm(int slot) { EnsureSlot(slot); return 0; }

    public void SetDeckBaseBpm(int slot, double bpm) => EnsureSlot(slot);

    public double DeckFirstBeat(int slot) { EnsureSlot(slot); return 0; }

    public void SetDeckFirstBeat(int slot, double firstBeatSeconds) => EnsureSlot(slot);
    public void SyncOnce(int slot) => EnsureSlot(slot);

    public bool IsSyncLocked(int slot) { EnsureSlot(slot); return false; }

    public void SetSyncLock(int slot, bool enabled) => EnsureSlot(slot);

    // A single deck has no second deck to sync to: there is never a master and every slot is Off.
    public int? SyncMaster => null;

    public SyncLockState SyncState(int slot) { EnsureSlot(slot); return SyncLockState.Off; }

    public bool IsQuantizeEnabled(int slot) { EnsureSlot(slot); return false; }

    public void SetQuantize(int slot, bool enabled) => EnsureSlot(slot);

    // The legacy single-deck engine has no hot-cue memory; report zero slots so any index is rejected.
    public int HotCueCount => 0;

    public bool IsHotCueSet(int slot, int cueIndex) { EnsureSlot(slot); return false; }

    public void HotCue(int slot, int cueIndex) => EnsureSlot(slot);

    // The legacy single-deck engine has no loop support; report no active loop and accept no-ops.
    public double LoopBeats(int slot) { EnsureSlot(slot); return 0; }

    public bool IsLooping(int slot) { EnsureSlot(slot); return false; }

    public void SetLoop(int slot, double beats) => EnsureSlot(slot);

    public void ClearLoop(int slot) => EnsureSlot(slot);

    private void EnsureSlot(int slot)
    {
        if (slot != 0)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Single-deck engine has only slot 0.");
    }
}
