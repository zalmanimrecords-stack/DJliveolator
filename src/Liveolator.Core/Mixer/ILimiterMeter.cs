namespace Liveolator.Core.Mixer;

/// <summary>
/// Read-only master-limiter metering seam (doc 11), the GR-meter twin of <see cref="IDeckLevelMeter"/>:
/// the UI polls the live gain-reduction amount so a meter can show when — and how hard — the brick-wall
/// limiter is working (it is otherwise inaudible by design). The realtime binding
/// (<c>BassMixer</c> → <see cref="Dsp.MasterLimiter"/>) implements it; a headless/no-audio build reports 0.
/// </summary>
public interface ILimiterMeter
{
    /// <summary>Gain reduction the master limiter is applying right now, in dB (0 = not limiting).</summary>
    double CurrentGainReductionDb { get; }
}
