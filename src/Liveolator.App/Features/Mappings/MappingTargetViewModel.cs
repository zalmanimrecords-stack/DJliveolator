using Liveolator.Core.Actions;

namespace Liveolator.App.Features.Mappings;

public sealed record MappingTargetViewModel(
    string Label,
    PerformanceActionKind Action,
    int Slot,
    ActionInputMode? PreferredInputMode = null,
    double RelativeTicksPerRevolution = 1.0,
    bool Invert = false,
    string? Argument = null);
