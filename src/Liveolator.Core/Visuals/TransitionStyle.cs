namespace Liveolator.Core.Visuals;

/// <summary>How the compositor moves into a scene (doc 08).</summary>
public enum TransitionStyle
{
    /// <summary>Hard cut, no blend.</summary>
    Cut,

    /// <summary>Opacity crossfade between outgoing and incoming stacks.</summary>
    Crossfade,

    /// <summary>Directional wipe.</summary>
    Wipe,

    /// <summary>Noise/threshold dissolve.</summary>
    Dissolve,
}
