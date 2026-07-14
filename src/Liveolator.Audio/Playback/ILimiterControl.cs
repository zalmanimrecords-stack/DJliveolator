using Liveolator.Core.Dsp;

namespace Liveolator.Audio.Playback;

/// <summary>
/// The master smart-limiter control seam (doc 11): the realtime binding's path for applying the
/// Core-owned <see cref="LimiterSettings"/> (SAFE↔SMART, character, true-peak ceiling) to the running
/// master-bus <see cref="MasterLimiter"/>. <see cref="BassMixer"/> forwards <see cref="IMixer.SetLimiter"/>
/// here; the concrete implementation (<see cref="BassMixerBackend"/>) owns the limiter instance.
/// </summary>
/// <remarks>
/// Kept as a binding-internal seam, mirroring <see cref="ICueOutput"/>, so the routing skeleton in
/// <see cref="BassMixer"/> stays testable and degrades gracefully (logs + drops) when no realtime
/// backend is wired. Applying settings never changes the limiter's latency, so the shared audio↔visual
/// clock alignment is preserved (the look-ahead window is fixed).
/// </remarks>
internal interface ILimiterControl
{
    /// <summary>Apply the master limiter controls to the running limiter on the realtime path.</summary>
    void ApplyLimiterSettings(LimiterSettings settings);

    /// <summary>Gain reduction the running limiter is applying right now, in dB (0 = not limiting); read
    /// by the UI GR meter (<see cref="Liveolator.Core.Mixer.ILimiterMeter"/>).</summary>
    double CurrentGainReductionDb { get; }
}
