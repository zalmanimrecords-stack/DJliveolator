namespace Liveolator.Audio.Playback;

/// <summary>Raised when a BASS realtime-playback call fails; carries the BASS error context.</summary>
public sealed class BassPlaybackException : Exception
{
    public BassPlaybackException(string message) : base(message) { }
    public BassPlaybackException(string message, Exception inner) : base(message, inner) { }
}
