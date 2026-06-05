namespace Liveolator.Core.Audio;

/// <summary>
/// A two-deck playback engine seam (doc 11): the same load/play-pause/stop operations as
/// <see cref="IAudioPlaybackEngine"/> but addressed per deck slot (A = 0, B = 1), so a single
/// <see cref="DeckActionHandler"/> can drive both decks from slot-tagged actions. Kept separate
/// from the single-deck seam so the existing single-deck path and its tests are untouched; the
/// concrete two-deck engine (and its BASS routing into the software mixer) is a later increment.
/// </summary>
public interface IMultiDeckPlaybackEngine
{
    /// <summary>Number of addressable deck slots.</summary>
    int DeckCount { get; }

    /// <summary>True while the given deck slot is playing.</summary>
    bool IsPlaying(int slot);

    /// <summary>Load a track into a deck slot, replacing any current one. Does not auto-play.</summary>
    void Load(int slot, string trackPath);

    /// <summary>Start the deck slot, or pause it if already playing. No-op if nothing is loaded.</summary>
    void PlayPause(int slot);

    /// <summary>Stop the deck slot (keeps its loaded track).</summary>
    void Stop(int slot);
}
