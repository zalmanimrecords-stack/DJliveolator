namespace Liveolator.Core.Mixer;

/// <summary>
/// Pure math for the headphone-cue (PFL) bus (doc 11): which decks feed the cue, and the per-source
/// gains the realtime binding multiplies to build the headphone output. No state, no native code —
/// just the numbers, so the cue routing and the cue/master blend unit-test without BASS.
/// </summary>
/// <remarks>
/// PFL means <em>pre-fade listen</em>: a deck's contribution to the cue bus is taken before the
/// crossfader and channel fader, so the DJ hears the cued track at a consistent level regardless of
/// where the crossfader sits. The cue send therefore ignores <see cref="MixerMath.DeckOutputGain"/>;
/// it is purely whether the deck is cue-enabled. The cue/master blend uses an equal-power
/// (constant-loudness) crossfade so the perceived level stays steady as the knob sweeps.
/// </remarks>
public static class CueMixMath
{
    /// <summary>
    /// The pre-fade send gain of one deck slot into the cue bus: 1 when the deck is routed to cue,
    /// 0 otherwise. Independent of the crossfader/channel fader — that is what makes it pre-fade.
    /// </summary>
    public static double DeckCueSendGain(MixerState state, int slot)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Channel(slot).CueEnabled ? 1.0 : 0.0;
    }

    /// <summary>True when at least one deck is routed to the cue bus.</summary>
    public static bool AnyDeckCued(MixerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (int slot = 0; slot < state.Channels.Count; slot++)
        {
            if (state.Channel(slot).CueEnabled)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The two output gains for the headphone bus given the cue/master blend knob: how much of the
    /// summed cued decks (<c>CueGain</c>) and how much of the master mix (<c>MasterGain</c>) to mix
    /// into the headphones, before the headphone level. Equal-power so loudness stays constant as the
    /// knob sweeps: knob 0 = all cue, knob 1 = all master, knob 0.5 = equal blend.
    /// </summary>
    public static (double CueGain, double MasterGain) BlendGains(double mix)
    {
        double m = Math.Clamp(mix, 0.0, 1.0);
        // Equal-power crossfade: cos/sin keep cueGain^2 + masterGain^2 == 1 across the sweep.
        double cueGain = Math.Cos(m * Math.PI / 2.0);
        double masterGain = Math.Sin(m * Math.PI / 2.0);
        return (cueGain, masterGain);
    }

    /// <summary>
    /// The final per-source headphone-output gains for the whole cue bus: the blend gains scaled by
    /// the headphone <see cref="CueBusState.Level"/>. The binding multiplies the summed cued decks by
    /// <c>CueGain</c> and the master mix by <c>MasterGain</c>, sums them, and sends to the cue output.
    /// </summary>
    public static (double CueGain, double MasterGain) HeadphoneOutputGains(CueBusState bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        double level = Math.Clamp(bus.Level, 0.0, 1.0);
        (double cueGain, double masterGain) = BlendGains(bus.Mix);
        return (cueGain * level, masterGain * level);
    }

    /// <summary>
    /// The scalar one deck's pre-fade samples are multiplied by before they are summed into the
    /// headphone-cue mix (A2): the deck's pre-fade cue send (1 when cue-enabled, else 0) times the
    /// bus <paramref name="cueGain"/> (the level-scaled cue leg of the blend from
    /// <see cref="HeadphoneOutputGains"/>). 0 when the deck is not cued, so a non-cued deck never
    /// bleeds into the headphones regardless of the blend knob.
    /// </summary>
    public static double DeckCueContributionGain(bool deckCueEnabled, double cueGain)
        => deckCueEnabled ? Math.Max(0.0, cueGain) : 0.0;
}
