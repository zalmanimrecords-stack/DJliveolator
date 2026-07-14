using Liveolator.Core.Audio;
using Liveolator.Core.Recording;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Recording;

/// <summary>
/// Records the live master mix to a clean WAV file (roadmap X2) by subscribing to the post-limiter master
/// <see cref="IAudioSource"/> that <c>TwoDeckBassEngine</c> exposes - the SAME buffer the analysis tap and
/// headphone cue read, so the recording matches exactly what the house hears. Capturing only listens to
/// the tap; it never touches the playback path, so toggling REC cannot disturb the audio.
///
/// Tolerant by design (global standards #16/#26): a disk/IO failure while writing stops the recording and
/// logs, and never rethrows onto the audio thread that delivers samples - a failed recording must not
/// crash a performance. The native sample capture lives upstream in the BASS master tap; this class is
/// pure managed and unit-tests with a fake <see cref="IAudioSource"/> and a fake sink.
/// </summary>
public sealed class BassMasterRecorder : IMasterRecorder, IDisposable
{
    private readonly IAudioSource? _master;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly Func<string, int, int, IMasterRecordingSink> _sinkFactory;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private IMasterRecordingSink? _sink;
    private bool _disposed;

    /// <summary>
    /// Production constructor. <paramref name="master"/> is the engine's master <see cref="IAudioSource"/>
    /// (post-limiter) and <paramref name="channels"/>/<paramref name="sampleRate"/> are its fixed output
    /// format (from <c>MasterMixInfo</c>), so the WAV header is exact from the first sample. Pass a null
    /// master on a host without realtime audio so the recorder reports <see cref="IsAvailable"/> = false
    /// and the REC action greys out instead of erroring.
    /// </summary>
    public BassMasterRecorder(IAudioSource? master, int channels, int sampleRate, ILoggerFactory? loggerFactory = null)
        : this(master, channels, sampleRate, DefaultSinkFactory, loggerFactory)
    {
    }

    /// <summary>Test/composition constructor: injects the sink factory so unit tests avoid disk IO.</summary>
    internal BassMasterRecorder(
        IAudioSource? master,
        int channels,
        int sampleRate,
        Func<string, int, int, IMasterRecordingSink> sinkFactory,
        ILoggerFactory? loggerFactory = null)
    {
        _master = master;
        _channels = channels > 0 ? channels : DefaultChannels;
        _sampleRate = sampleRate > 0 ? sampleRate : DefaultSampleRate;
        _sinkFactory = sinkFactory ?? throw new ArgumentNullException(nameof(sinkFactory));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<BassMasterRecorder>();
    }

    /// <inheritdoc />
    public bool IsAvailable => _master is not null && !_disposed;

    /// <inheritdoc />
    public bool IsRecording
    {
        get { lock (_gate) return _sink is not null; }
    }

    /// <inheritdoc />
    public bool Start(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("Master recording not started: no output path supplied.");
            return false;
        }

        lock (_gate)
        {
            if (_master is null || _disposed)
                return false;
            if (_sink is not null)
                return false; // already recording

            try
            {
                _sink = _sinkFactory(path, _channels, _sampleRate);
            }
            catch (Exception ex)
            {
                // Opening the file failed (permissions, disk full, bad path): surface, never throw.
                _logger.LogError(ex, "Master recording could not open {Path}.", path);
                _sink = null;
                return false;
            }

            _master.SamplesAvailable += OnSamples;
            _logger.LogInformation("Master recording started: {Path} ({Channels}ch @ {Rate}Hz).",
                path, _channels, _sampleRate);
            return true;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopLocked();
        }
    }

    private const int DefaultChannels = 2;
    private const int DefaultSampleRate = 48_000;

    private void OnSamples(object? sender, AudioSamplesAvailable e)
    {
        IMasterRecordingSink? sink;
        lock (_gate)
        {
            sink = _sink;
            if (sink is null)
                return;
        }

        try
        {
            sink.Write(e.Interleaved.Span);
        }
        catch (Exception ex)
        {
            // A write failure (disk full, device removed) must stop the recording cleanly and never
            // propagate onto the audio thread (global standards #16/#26).
            _logger.LogError(ex, "Master recording write failed; stopping recording.");
            lock (_gate)
            {
                StopLocked();
            }
        }
    }

    // Must be called under _gate.
    private void StopLocked()
    {
        if (_sink is null)
            return;

        if (_master is not null)
            _master.SamplesAvailable -= OnSamples;

        try
        {
            _sink.Dispose(); // finalizes the WAV header
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Master recording failed to finalize the output file.");
        }
        finally
        {
            _sink = null;
            _logger.LogInformation("Master recording stopped.");
        }
    }

    private static IMasterRecordingSink DefaultSinkFactory(string path, int channels, int sampleRate)
        => new WavMasterRecordingSink(path, channels, sampleRate);
}
