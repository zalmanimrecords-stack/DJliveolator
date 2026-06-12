using System.Collections.Generic;
using Liveolator.App.Shell;
using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// Editable presentation of a <see cref="StudioTransition"/> for the inspector panel: the kind,
/// length (beats), crossfader curve, and anchor. Holds no domain logic — <see cref="ToModel"/>
/// projects it back to the immutable Core record on save.
/// </summary>
public sealed class StudioTransitionViewModel : ViewModelBase
{
    private TransitionKind _kind;
    private double _lengthBeats;
    private CrossfaderCurve _curve;
    private TransitionAnchor _anchor;

    public StudioTransitionViewModel(StudioTransition model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _kind = model.Kind;
        _lengthBeats = model.LengthBeats;
        _curve = model.Curve;
        _anchor = model.Anchor;
    }

    // Bound to the inspector's drop-downs.
    public static IReadOnlyList<TransitionKind> Kinds { get; } = Enum.GetValues<TransitionKind>();
    public static IReadOnlyList<CrossfaderCurve> Curves { get; } = Enum.GetValues<CrossfaderCurve>();
    public static IReadOnlyList<TransitionAnchor> Anchors { get; } = Enum.GetValues<TransitionAnchor>();

    public TransitionKind Kind
    {
        get => _kind;
        set { this.RaiseAndSetIfChanged(ref _kind, value); this.RaisePropertyChanged(nameof(Summary)); }
    }

    public double LengthBeats
    {
        get => _lengthBeats;
        set { this.RaiseAndSetIfChanged(ref _lengthBeats, value); this.RaisePropertyChanged(nameof(Summary)); }
    }

    public CrossfaderCurve Curve
    {
        get => _curve;
        set => this.RaiseAndSetIfChanged(ref _curve, value);
    }

    public TransitionAnchor Anchor
    {
        get => _anchor;
        set { this.RaiseAndSetIfChanged(ref _anchor, value); this.RaisePropertyChanged(nameof(Summary)); }
    }

    /// <summary>Short one-line label shown on the timeline between two lanes.</summary>
    public string Summary => Kind == TransitionKind.Cut
        ? "Cut"
        : $"{Kind} · {LengthBeats:0} beats · {Anchor}";

    public StudioTransition ToModel() => new(Kind, LengthBeats, Curve, Anchor);
}
