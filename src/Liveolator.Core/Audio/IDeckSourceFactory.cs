namespace Liveolator.Core.Audio;

/// <summary>
/// Creates an <see cref="IAudioSource"/> deck for a track file. The seam that lets the pure
/// <see cref="LivePlaybackEngine"/> stay platform-independent: the BASS-backed factory
/// (<c>BassAudioEngine</c> in Liveolator.Audio) supplies real decks, a fake supplies test ones.
/// </summary>
public interface IDeckSourceFactory
{
    /// <summary>Create (but do not start) a deck audio source for the given track file.</summary>
    IAudioSource CreateDeck(string filePath);
}
