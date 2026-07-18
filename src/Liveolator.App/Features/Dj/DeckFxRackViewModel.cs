using System;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Effects;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// The DJ PRO "effects outside" rack for one deck: dedicated, always-visible knobs for the deck's built-in
/// FX chain — Moog ladder low-pass (cutoff + resonance), Phaser mix, Reverb mix. Each knob drives the
/// deck's realtime rack instance through <see cref="PerformanceActionKind.AudioFxSetParameter"/> (the doc 04
/// seam) — the SAME rack the DJ tab's FX-mode button drives, only surfaced as its own knobs instead of
/// hijacking the EQ. A null dispatcher (headless / no realtime engine) leaves every knob disabled.
/// </summary>
public sealed class DeckFxRackViewModel : ViewModelBase
{
    /// <param name="dispatcher">The one action layer; null disables the rack.</param>
    /// <param name="slot">The deck slot the rack belongs to (A = 0, B = 1).</param>
    public DeckFxRackViewModel(IPerformanceActionDispatcher? dispatcher, int slot)
    {
        Action<double>? Emit(string instanceId, string parameterId) =>
            dispatcher is null
                ? null
                : value => dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.AudioFxSetParameter, ActionInputMode.Absolute,
                    Value: value, Slot: slot, Argument: parameterId, Target: instanceId));

        // Seeded to the neutral (fully dry / open) value so the rack passes audio through unchanged until
        // the DJ turns a knob up — matching how leaving the DJ tab's FX mode forces the rack dry.
        Cutoff = new ContinuousControlViewModel("CUT",
            BuiltInAudioEffects.Neutral(BuiltInAudioEffects.Cutoff),
            Emit(BuiltInAudioEffects.MoogInstance, BuiltInAudioEffects.Cutoff));
        Resonance = new ContinuousControlViewModel("RES",
            BuiltInAudioEffects.Neutral(BuiltInAudioEffects.Resonance),
            Emit(BuiltInAudioEffects.MoogInstance, BuiltInAudioEffects.Resonance));
        Phaser = new ContinuousControlViewModel("PHASE",
            BuiltInAudioEffects.Neutral(BuiltInAudioEffects.Wet),
            Emit(BuiltInAudioEffects.PhaserInstance, BuiltInAudioEffects.Wet));
        Reverb = new ContinuousControlViewModel("VERB",
            BuiltInAudioEffects.Neutral(BuiltInAudioEffects.Wet),
            Emit(BuiltInAudioEffects.ReverbInstance, BuiltInAudioEffects.Wet));
    }

    /// <summary>Moog ladder low-pass cutoff (1 = fully open / transparent).</summary>
    public ContinuousControlViewModel Cutoff { get; }

    /// <summary>Moog ladder resonance (0 = none).</summary>
    public ContinuousControlViewModel Resonance { get; }

    /// <summary>Phaser dry↔wet mix (0 = dry).</summary>
    public ContinuousControlViewModel Phaser { get; }

    /// <summary>Reverb dry↔wet mix (0 = dry).</summary>
    public ContinuousControlViewModel Reverb { get; }
}
