using Liveolator.Core.Analysis;

namespace Liveolator.Audio;

/// <summary>
/// The decoder the application injects. Routes by capability, cheapest/most-available first:
/// <c>.wav</c> → the pure-managed <see cref="WavAudioDecoder"/> (no native dependency); every other
/// format → <see cref="BassAudioDecoder"/> (the BASS library the app already ships — mp3/ogg/aiff out
/// of the box, flac/m4a with the add-on); and finally <see cref="FfmpegAudioDecoder"/> as a fallback
/// for anything BASS can't take (when ffmpeg is installed). So the deck waveform + offline analysis
/// work on a stock BASS install with no external ffmpeg.
/// </summary>
public sealed class CompositeAudioDecoder : IAudioDecoder
{
    private readonly IAudioDecoder _wav;
    private readonly IAudioDecoder _bass;
    private readonly IAudioDecoder _ffmpeg;

    public CompositeAudioDecoder(FfmpegOptions? ffmpegOptions = null)
        : this(new WavAudioDecoder(),
               new BassAudioDecoder(),
               new FfmpegAudioDecoder((ffmpegOptions ?? FfmpegOptions.FromEnvironment()).ExecutablePath)) { }

    public CompositeAudioDecoder(IAudioDecoder wav, IAudioDecoder bass, IAudioDecoder ffmpeg)
    {
        _wav = wav ?? throw new ArgumentNullException(nameof(wav));
        _bass = bass ?? throw new ArgumentNullException(nameof(bass));
        _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
    }

    public bool CanDecode(string filePath) => Select(filePath).CanDecode(filePath);

    public IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, CancellationToken cancellationToken = default)
        => Select(filePath).DecodeMonoAsync(filePath, targetSampleRate, cancellationToken);

    private IAudioDecoder Select(string filePath)
        => _wav.CanDecode(filePath) ? _wav
         : _bass.CanDecode(filePath) ? _bass
         : _ffmpeg;
}
