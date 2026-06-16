using Liveolator.Core.Mixer;

namespace Liveolator.Core.Studio.Render;

/// <summary>
/// The mix parameters for one deck at one output instant during an offline render: whether a clip is
/// sounding (<see cref="HasAudio"/>), where to read its source (<see cref="SourceSeconds"/>), the warp
/// (<see cref="WarpFactor"/> + the active clip's <see cref="ClipStartSeconds"/>/<see cref="SourceInSeconds"/>
/// so the renderer can index a pitch-preserved, time-stretched buffer), and the per-deck controls
/// (<see cref="Gain"/> 0..1, <see cref="Eq"/>, <see cref="Filter"/> knob 0..1). Pure control values — the
/// renderer turns <see cref="Eq"/>/<see cref="Filter"/> into biquad coefficients via <see cref="MixerMath"/>.
/// </summary>
public sealed record DeckMixState(
    bool HasAudio, string? SourcePath, double SourceSeconds,
    double WarpFactor, double ClipStartSeconds, double SourceInSeconds,
    double Gain, EqBands Eq, double Filter)
{
    /// <summary>No clip sounding on this deck: silent, controls neutral, unwarped.</summary>
    public static DeckMixState Silent { get; } = new(
        HasAudio: false, SourcePath: null, SourceSeconds: 0,
        WarpFactor: 1.0, ClipStartSeconds: 0, SourceInSeconds: 0,
        Gain: 0, Eq: EqBands.Flat, Filter: DeckChannelState.FilterCenter);
}
