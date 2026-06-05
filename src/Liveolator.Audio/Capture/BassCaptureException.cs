namespace Liveolator.Audio.Capture;

/// <summary>Raised when a BASS capture call fails; carries the BASS error context.</summary>
public sealed class BassCaptureException : Exception
{
    public BassCaptureException(string message) : base(message) { }
    public BassCaptureException(string message, Exception inner) : base(message, inner) { }
}
