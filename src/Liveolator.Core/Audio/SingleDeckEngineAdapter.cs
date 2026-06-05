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

    private void EnsureSlot(int slot)
    {
        if (slot != 0)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Single-deck engine has only slot 0.");
    }
}
