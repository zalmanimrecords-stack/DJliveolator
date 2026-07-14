using Liveolator.Audio.Render;

namespace Liveolator.Audio.Recording;

/// <summary>
/// The production <see cref="IMasterRecordingSink"/>: writes the captured master to a 16-bit PCM WAV via
/// <see cref="WavStreamWriter"/> (roadmap X2), so a recording is in the same clean format as an offline
/// render.
/// </summary>
internal sealed class WavMasterRecordingSink : IMasterRecordingSink
{
    private readonly WavStreamWriter _writer;

    public WavMasterRecordingSink(string path, int channels, int sampleRate)
        => _writer = new WavStreamWriter(path, channels, sampleRate);

    public void Write(ReadOnlySpan<float> interleaved) => _writer.Write(interleaved);

    public void Dispose() => _writer.Dispose();
}
