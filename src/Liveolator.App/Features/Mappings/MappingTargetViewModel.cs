using Liveolator.Core.Actions;

namespace Liveolator.App.Features.Mappings;

public sealed record MappingTargetViewModel(
    string Label,
    PerformanceActionKind Action,
    int Slot);
