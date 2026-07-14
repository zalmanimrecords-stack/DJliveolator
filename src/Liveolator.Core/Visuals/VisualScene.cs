namespace Liveolator.Core.Visuals;

/// <summary>
/// A saved layer stack plus macro values, the transition used to move into it, and its beat
/// behavior. Loading a scene applies all of it atomically (on the next quantum boundary if
/// quantized) (doc 08).
/// </summary>
public sealed record VisualScene
{
    public VisualScene(
        string name,
        IReadOnlyList<VisualLayer> layers,
        IReadOnlyDictionary<string, double> macroValues,
        TransitionStyle transition,
        BeatBehavior beatBehavior)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Layers = layers ?? throw new ArgumentNullException(nameof(layers));
        MacroValues = macroValues ?? throw new ArgumentNullException(nameof(macroValues));
        BeatBehavior = beatBehavior ?? throw new ArgumentNullException(nameof(beatBehavior));
        Transition = transition;
    }

    public string Name { get; init; }

    /// <summary>Layers ordered bottom→top.</summary>
    public IReadOnlyList<VisualLayer> Layers { get; init; }

    public IReadOnlyDictionary<string, double> MacroValues { get; init; }

    public TransitionStyle Transition { get; init; }

    public BeatBehavior BeatBehavior { get; init; }
}
