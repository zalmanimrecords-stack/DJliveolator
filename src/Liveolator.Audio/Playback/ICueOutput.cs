namespace Liveolator.Audio.Playback;

/// <summary>
/// The headphone-cue (PFL) output seam (doc 11): the realtime binding's second audio path. The
/// <see cref="BassMixer"/> forwards the Core-computed cue/master output gains here; the concrete
/// implementation (<see cref="BassMixerBackend"/>) builds the headphone mix — summed cued (PFL)
/// decks plus the master — and sends it to the cue output device/channel (the CMD STUDIO 2A
/// channels 3/4). Kept as a seam so the routing skeleton in <see cref="BassMixer"/> stays testable
/// and degrades gracefully when no cue output has been configured.
/// </summary>
/// <remarks>
/// New seam introduced with the PFL increment. It is owned by this binding (not the forbidden
/// <c>IBassMixerBackend</c>) so the cue-bus work does not edit deck-loop–owned interfaces; the
/// final integrator may fold the per-deck cue-send routing into the backend — see the integration
/// note. Gains are the equal-power, level-scaled values from <see cref="Liveolator.Core.Mixer.CueMixMath"/>.
/// </remarks>
internal interface ICueOutput
{
    /// <summary>
    /// Set how much of the summed cued (PFL) decks (<paramref name="cueGain"/>) and of the master mix
    /// (<paramref name="masterGain"/>) feed the headphone-cue output, already scaled by headphone level.
    /// </summary>
    void SetCueOutputGains(double cueGain, double masterGain);
}
