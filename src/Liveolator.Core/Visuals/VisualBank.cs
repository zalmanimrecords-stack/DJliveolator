namespace Liveolator.Core.Visuals;

/// <summary>
/// A group of scenes mapped to Push pads / the Scene Grid (doc 08/12). Indexing matches the pad
/// layout, so a pad press selects <c>Scenes[slot]</c>.
/// </summary>
public sealed record VisualBank
{
    public VisualBank(string name, IReadOnlyList<VisualScene> scenes)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
    }

    public string Name { get; init; }

    public IReadOnlyList<VisualScene> Scenes { get; init; }

    /// <summary>The scene at <paramref name="index"/>, or null when out of range (e.g. an empty pad).</summary>
    public VisualScene? Scene(int index)
        => index >= 0 && index < Scenes.Count ? Scenes[index] : null;
}
