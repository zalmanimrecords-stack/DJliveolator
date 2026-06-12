using Liveolator.Core.Mixer;

namespace Liveolator.Core.Studio.Render;

/// <summary>
/// The mix parameters for one deck at one output instant during an offline render: whether a clip is
/// sounding (<see cref="HasAudio"/>), where to read its source (<see cref="SourceSeconds"/>), and the
/// per-deck controls (<see cref="Gain"/> 0..1, <see cref="Eq"/>, <see cref="Filter"/> knob 0..1). Pure
/// control values — the renderer turns <see cref="Eq"/>/<see cref="Filter"/> into biquad coefficients
/// via <see cref="MixerMath"/> at the render sample rate and applies them.
/// </summary>
public sealed record DeckMixState(
    bool HasAudio, string? SourcePath, double SourceSeconds, double Gain, EqBands Eq, double Filter)
{
    /// <summary>No clip sounding on this deck: silent, controls neutral.</summary>
    public static DeckMixState Silent { get; } =
        new(HasAudio: false, SourcePath: null, SourceSeconds: 0, Gain: 0, Eq: EqBands.Flat, Filter: DeckChannelState.FilterCenter);
}
