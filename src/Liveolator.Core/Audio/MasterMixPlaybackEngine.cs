using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Audio;

/// <summary>
/// Composes the analysis half of the realtime chain (doc 02/03/11) over a <b>fixed master mix</b>:
/// a single <see cref="IAudioSource"/> — the post-crossfader master bus produced by the two-deck
/// engine — feeds one <see cref="AudioFramePipeline"/> feeding one <see cref="AudioBeatClock"/>, and
/// the live <see cref="BeatClock"/> is exposed for the UI/visuals. This is the doc 11 requirement
/// that the beat engine sees the audible mix directly (no loopback). Pure managed — no native — so it
/// unit-tests with a fake master source.
/// </summary>
/// <remarks>
/// Unlike <see cref="LivePlaybackEngine"/> (which swaps a single deck per track behind a
/// <see cref="SwitchableAudioSource"/>), the master source here is stable for the engine's lifetime —
/// track loading happens per deck on the two-deck engine, below the mix. Ownership of the master
/// source stays with the caller: <see cref="Dispose"/> tears down the pipeline + clock but never the
/// master (mirroring <see cref="SwitchableAudioSource"/>'s ownership rule).
/// </remarks>
public sealed class MasterMixPlaybackEngine : IDisposable
{
    private readonly AudioFramePipeline _pipeline;
    private readonly AudioBeatClock _beatClock;
    private bool _disposed;

    public MasterMixPlaybackEngine(
        IAudioSource masterSource,
        IHostClock hostClock,
        SpectrumAnalyzer? analyzer = null,
        int hop = 512,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(masterSource);
        ArgumentNullException.ThrowIfNull(hostClock);

        loggerFactory ??= NullLoggerFactory.Instance;
        _pipeline = new AudioFramePipeline(
            masterSource, analyzer ?? new SpectrumAnalyzer(), hop, loggerFactory.CreateLogger<AudioFramePipeline>());
        _beatClock = new AudioBeatClock(
            _pipeline, hostClock, logger: loggerFactory.CreateLogger<AudioBeatClock>());
    }

    /// <summary>The live beat clock fed by the master mix; stable for the engine's lifetime.</summary>
    public IBeatClock BeatClock => _beatClock;

    /// <summary>
    /// The shared analysis frames feeding the clock — the same master-mix frames a visual audio-level
    /// meter (doc 26) subscribes to, so the metered level matches the audible signal the visuals lock to.
    /// </summary>
    public IAudioFrameProvider FrameProvider => _pipeline;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _beatClock.Dispose();
        _pipeline.Dispose();
    }
}
