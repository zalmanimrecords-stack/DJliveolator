using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;
using ManagedBass;
using ManagedBass.Mix;

namespace Liveolator.Audio;

/// <summary>
/// Offline <see cref="IAudioDecoder"/> over <b>BASS</b> (the same native library the realtime engine
/// already ships): decodes compressed formats — mp3/mp2/ogg/aiff and, when the matching add-on is
/// present, flac/m4a — to mono PCM at the requested rate, with no external <c>ffmpeg</c> dependency.
/// This is the decoder the deck waveform + offline analysis use for everything that isn't a plain WAV,
/// so the strip shows the real waveform out of the box on a BASS install.
/// </summary>
/// <remarks>
/// Native, so (like the rest of the BASS layer) it is verified by running, not in CI. It needs BASS
/// initialised; <see cref="EnsureUsable"/> performs a tolerant one-time no-sound init (device 0) and
/// loads any decode add-ons (<c>bassflac</c>, <c>bass_aac</c>) found next to <c>bass</c>, so it works
/// whether or not the realtime playback device is up. Resampling to the target rate is done by a BASSmix
/// decode mixer (BASS-quality, streamed at the low target rate so a long set never accumulates the full
/// native-rate signal). Failures throw <see cref="BassDecodeException"/>; the caller
/// (<c>DecodedWaveformProvider</c>) degrades to an empty overview (global standards #16/#26).
/// </remarks>
public sealed class BassAudioDecoder : IAudioDecoder
{
    private static readonly object InitGate = new();
    private static bool _initAttempted;
    private static bool _usable;
    private static HashSet<string> _supported = new(StringComparer.OrdinalIgnoreCase);

    // Formats BASS decodes with no add-on. flac/m4a/aac are added at init iff their plugin loads.
    private static readonly string[] CoreExtensions = { ".mp3", ".mp2", ".mp1", ".ogg", ".aiff", ".aif", ".wav" };

    public bool CanDecode(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        EnsureUsable();
        return _usable && _supported.Contains(Path.GetExtension(filePath));
    }

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath must be a non-empty path.", nameof(filePath));
        if (targetSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));

        EnsureUsable();
        if (!_usable)
            throw new BassDecodeException("BASS is not initialised; cannot decode.");

        // Source decode stream (native rate/channels, float) → a 1-channel target-rate BASSmix decode
        // mixer that resamples + downmixes. MixerEnd ends the mixer when the source runs out.
        int source = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (source == 0)
            throw new BassDecodeException($"CreateStream('{filePath}') failed: {Bass.LastError}.");

        int mixer = BassMix.CreateMixerStream(
            targetSampleRate, 1, BassFlags.Decode | BassFlags.Float | BassFlags.MixerEnd);
        if (mixer == 0)
        {
            Bass.StreamFree(source);
            throw new BassDecodeException($"CreateMixerStream failed: {Bass.LastError}.");
        }
        if (!BassMix.MixerAddChannel(mixer, source, BassFlags.Default))
        {
            Bass.StreamFree(mixer);
            Bass.StreamFree(source);
            throw new BassDecodeException($"MixerAddChannel failed: {Bass.LastError}.");
        }

        try
        {
            const int blockFloats = 16_384;
            var buffer = new float[blockFloats];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int bytes = Bass.ChannelGetData(mixer, buffer, blockFloats * sizeof(float));
                if (bytes <= 0)
                {
                    // -1 with Ended is the normal end of stream; any other error stops the decode too.
                    if (bytes == -1 && Bass.LastError != Errors.Ended && Bass.LastError != Errors.OK)
                        throw new BassDecodeException($"ChannelGetData failed: {Bass.LastError}.");
                    break;
                }

                int floats = bytes / sizeof(float);
                // Copy out the exact slice (the buffer is reused next iteration; the consumer may retain it).
                var block = new float[floats];
                Array.Copy(buffer, block, floats);
                yield return new ReadOnlyMemory<float>(block);
                await Task.Yield();
            }
        }
        finally
        {
            // Free the mixer first (it references the source), then the source.
            Bass.StreamFree(mixer);
            Bass.StreamFree(source);
        }
    }

    // One-time, tolerant: make sure BASS can decode on any thread (no-sound device is enough), and load
    // optional decode add-ons so flac/m4a join the supported set when their dll ships next to bass.
    private static void EnsureUsable()
    {
        if (_initAttempted)
            return;
        lock (InitGate)
        {
            if (_initAttempted)
                return;
            _initAttempted = true;

            var supported = new HashSet<string>(CoreExtensions, StringComparer.OrdinalIgnoreCase);
            try
            {
                // Device 0 = "no sound": enough for decode-only streams, and harmless if a real playback
                // device was already initialised elsewhere (Errors.Already is success here).
                bool init = Bass.Init(0) || Bass.LastError == Errors.Already;
                if (!init)
                {
                    // A real device may already be up (the realtime engine) — that also satisfies BASS.
                    init = Bass.LastError == Errors.Already;
                }
                _usable = init;

                if (_usable)
                {
                    if (BassPluginLoader.TryLoad("bassflac"))
                        supported.Add(".flac");
                    if (BassPluginLoader.TryLoad("bass_aac"))
                    {
                        supported.Add(".m4a");
                        supported.Add(".aac");
                        supported.Add(".mp4");
                    }
                }
            }
            catch (DllNotFoundException)
            {
                _usable = false; // native bass absent (e.g. CI / a box without the fetched libs)
            }
            _supported = supported;
        }
    }

}

/// <summary>Thrown when a BASS decode cannot start or read; the waveform/analysis caller degrades to empty.</summary>
public sealed class BassDecodeException : Exception
{
    public BassDecodeException(string message) : base(message) { }
}
