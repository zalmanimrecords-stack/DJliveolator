using Liveolator.Core.Analysis;

namespace Liveolator.Audio;

/// <summary>
/// The decoder the application injects: routes <c>.wav</c> to the pure-managed
/// <see cref="WavAudioDecoder"/> (no native dependency) and every other supported format to
/// <see cref="FfmpegAudioDecoder"/>. Keeps the common WAV case dependency-free while still
/// covering compressed formats when FFmpeg is installed.
/// </summary>
public sealed class CompositeAudioDecoder : IAudioDecoder
{
    private readonly IAudioDecoder _wav;
    private readonly IAudioDecoder _ffmpeg;

    public CompositeAudioDecoder(FfmpegOptions? ffmpegOptions = null)
        : this(new WavAudioDecoder(),
               new FfmpegAudioDecoder((ffmpegOptions ?? FfmpegOptions.FromEnvironment()).ExecutablePath)) { }

    public CompositeAudioDecoder(IAudioDecoder wav, IAudioDecoder ffmpeg)
    {
        _wav = wav ?? throw new ArgumentNullException(nameof(wav));
        _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
    }

    public bool CanDecode(string filePath) => Select(filePath).CanDecode(filePath);

    public IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, CancellationToken cancellationToken = default)
        => Select(filePath).DecodeMonoAsync(filePath, targetSampleRate, cancellationToken);

    private IAudioDecoder Select(string filePath)
        => _wav.CanDecode(filePath) ? _wav : _ffmpeg;
}
