using Liveolator.Core.Autopilot;
using Liveolator.Core.Mapping;
using Liveolator.Core.Visuals;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of a saved controller mapping profile (doc 05/13).</summary>
public sealed record MappingProfileSnapshot(int Version, ControllerMappingProfile Profile)
{
    public const int CurrentVersion = 1;
}

/// <summary>Versioned on-disk shape of a saved visual bank and its scenes (doc 08/13).</summary>
public sealed record VisualBankSnapshot(int Version, VisualBank Bank)
{
    public const int CurrentVersion = 1;
}

/// <summary>Versioned on-disk shape of the saved macro definitions (doc 08/13).</summary>
public sealed record VisualMacrosSnapshot(int Version, IReadOnlyList<VisualMacro> Macros)
{
    public const int CurrentVersion = 1;
}

/// <summary>Versioned on-disk shape of a package's controllable generator presets (doc 28).</summary>
public sealed record GeneratorPresetsSnapshot(int Version, IReadOnlyList<GeneratorPreset> Presets)
{
    public const int CurrentVersion = 1;
}

/// <summary>Versioned on-disk shape of a saved autopilot rule-set / show (doc 10/13).</summary>
public sealed record AutopilotRuleSetSnapshot(int Version, AutopilotRuleSet RuleSet)
{
    public const int CurrentVersion = 1;
}
