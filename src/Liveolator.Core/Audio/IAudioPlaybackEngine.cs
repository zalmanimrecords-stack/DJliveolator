namespace Liveolator.Core.Audio;

/// <summary>
/// Realtime playback engine seam driven by the action layer (doc 04/11): load a track, toggle
/// play/pause, stop. The <see cref="DeckActionHandler"/> calls this; the concrete engine wires the
/// audio source → frame pipeline → beat clock. Kept behind an interface so the handler depends on
/// behaviour, not on a specific backend.
/// </summary>
public interface IAudioPlaybackEngine
{
    /// <summary>True while a loaded track is playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Load a track file into the deck, replacing any current one. Does not auto-play.</summary>
    void Load(string trackPath);

    /// <summary>Start playback, or pause it if already playing. No-op if nothing is loaded.</summary>
    void PlayPause();

    /// <summary>Stop playback (keeps the loaded track).</summary>
    void Stop();
}
