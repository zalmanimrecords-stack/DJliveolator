using Liveolator.Core.Audio;
using Liveolator.Core.Mixer;

namespace Liveolator.Core.Automix;

/// <summary>
/// The standard <see cref="IAutomixDeckReader"/>: composes read-only snapshots from the existing
/// deck engine seam and the mixer handler's authoritative state. Pure composition — no caching, no
/// state of its own — so the auto-mix engine always sees the same truth the rest of the app does.
/// </summary>
public sealed class EngineAutomixDeckReader : IAutomixDeckReader
{
    private readonly IMultiDeckPlaybackEngine _engine;
    private readonly MixerActionHandler _mixer;

    public EngineAutomixDeckReader(IMultiDeckPlaybackEngine engine, MixerActionHandler mixer)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
    }

    /// <inheritdoc />
    public AutomixDeckSnapshot ReadDeck(int slot)
    {
        double length = _engine.LengthSeconds(slot);
        return new AutomixDeckSnapshot(
            IsLoaded: length > 0.0,
            IsPlaying: _engine.IsPlaying(slot),
            BaseBpm: _engine.DeckBaseBpm(slot),
            EffectiveBpm: _engine.DeckBpm(slot),
            FirstBeatSeconds: _engine.DeckFirstBeat(slot),
            PositionSeconds: _engine.Position(slot) * length,
            LengthSeconds: length,
            SyncState: _engine.SyncState(slot),
            SyncLocked: _engine.IsSyncLocked(slot));
    }

    /// <inheritdoc />
    public MixerState Mixer => _mixer.State;
}
