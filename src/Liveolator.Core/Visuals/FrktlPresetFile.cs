namespace Liveolator.Core.Visuals;

/// <summary>
/// One controllable parameter declared by a <c>.frktl</c> preset file (doc 28/29): a shader uniform that
/// also becomes a labelled performer knob. Carries everything both sides need — the descriptor parameter
/// (<see cref="Uniform"/> + range + default) and the controllable label.
/// </summary>
public sealed record FrktlPresetParameter
{
    public string Id { get; init; } = string.Empty;
    public string Uniform { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public double Min { get; init; }
    public double Max { get; init; } = 1.0;
    public double Default { get; init; }
}

/// <summary>
/// The on-disk shape of a self-contained <c>.frktl</c> preset (doc 29): a name, optional metadata, up to
/// five controllable parameters, and the full GLSL fragment shader that draws the look (frame-feedback
/// via <c>uPreviousFrame</c>, audio/beat uniforms, plus each parameter's uniform). A folder of these
/// files is loaded by <c>FrktlPresetFolderLoader</c>; each becomes a generator effect + a controllable
/// preset. Kept a plain record (no throwing constructor) so tolerant loading can validate then skip a
/// bad file rather than crash — see <c>FrktlPresetValidator</c>.
/// </summary>
public sealed record FrktlPresetFile
{
    public string Name { get; init; } = string.Empty;
    public string? Author { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<FrktlPresetParameter> Parameters { get; init; } = Array.Empty<FrktlPresetParameter>();
    public string Shader { get; init; } = string.Empty;
}
