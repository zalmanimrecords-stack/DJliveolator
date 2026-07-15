using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;

namespace Liveolator.App.Features.Mappings;

public sealed record MappingTargetViewModel(
    string Label,
    PerformanceActionKind Action,
    int Slot,
    ActionInputMode? PreferredInputMode = null,
    double RelativeTicksPerRevolution = 1.0,
    bool Invert = false,
    string? Argument = null,
    // Encoding the learn picker defaults to for this target. Jog wheels are offset-binary around 64;
    // most other relative encoders are two's-complement.
    RelativeEncoding RelativeEncoding = RelativeEncoding.TwosComplement);
