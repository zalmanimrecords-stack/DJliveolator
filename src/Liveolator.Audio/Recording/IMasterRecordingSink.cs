namespace Liveolator.Audio.Recording;

/// <summary>
/// Internal seam over the file format a master recording is written to (roadmap X2). The default
/// implementation wraps <see cref="Liveolator.Audio.Render.WavStreamWriter"/>; tests inject a fake to
/// verify <see cref="BassMasterRecorder"/>'s subscribe/write/finalize and IO-tolerance behaviour without
/// touching disk. Frames arrive interleaved at the master's channel/rate.
/// </summary>
internal interface IMasterRecordingSink : IDisposable
{
    /// <summary>Append a block of interleaved float samples (-1..1). May throw on an IO failure; the
    /// recorder catches it, stops, and logs (a recording must never crash playback).</summary>
    void Write(ReadOnlySpan<float> interleaved);
}
