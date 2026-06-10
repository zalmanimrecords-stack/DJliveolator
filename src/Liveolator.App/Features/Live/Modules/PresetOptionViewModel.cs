namespace Liveolator.App.Features.Live.Modules;

/// <summary>One selectable entry in the controllable-preset picker (doc 28): the stable preset id plus
/// the human name shown in the list.</summary>
public sealed class PresetOptionViewModel
{
    public PresetOptionViewModel(string presetId, string name)
    {
        PresetId = presetId;
        Name = name;
    }

    public string PresetId { get; }
    public string Name { get; }

    public override string ToString() => Name;
}
