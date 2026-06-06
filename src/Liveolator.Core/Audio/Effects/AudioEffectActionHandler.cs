using Liveolator.Core.Actions;

namespace Liveolator.Core.Audio.Effects;

public sealed class AudioEffectActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.AudioFxLoad,
        PerformanceActionKind.AudioFxUnload,
        PerformanceActionKind.AudioFxMove,
        PerformanceActionKind.AudioFxToggleBypass,
        PerformanceActionKind.AudioFxSetParameter,
        PerformanceActionKind.AudioFxLoadPreset,
    };

    private readonly IAudioEffectRackProvider _racks;
    private readonly Action? _onChanged;

    public AudioEffectActionHandler(IAudioEffectRackProvider racks, Action? onChanged = null)
    {
        _racks = racks ?? throw new ArgumentNullException(nameof(racks));
        _onChanged = onChanged;
    }

    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        IAudioEffectRack rack = _racks.GetRack(action.Slot);

        switch (action.Kind)
        {
            case PerformanceActionKind.AudioFxLoad:
                rack.Load(Required(action.Argument, "plugin UID"), action.Target);
                break;
            case PerformanceActionKind.AudioFxUnload:
                rack.Unload(Required(action.Target, "effect target"));
                break;
            case PerformanceActionKind.AudioFxMove:
                rack.Move(Required(action.Target, "effect target"), checked((int)Math.Round(action.Value)));
                break;
            case PerformanceActionKind.AudioFxToggleBypass:
                rack.ToggleBypass(Required(action.Target, "effect target"));
                break;
            case PerformanceActionKind.AudioFxSetParameter:
                rack.SetParameter(
                    Required(action.Target, "effect target"),
                    Required(action.Argument, "parameter id"),
                    action.Value);
                break;
            case PerformanceActionKind.AudioFxLoadPreset:
                rack.LoadPreset(
                    Required(action.Target, "effect target"),
                    Convert.FromBase64String(Required(action.Argument, "base64 preset state")));
                break;
        }

        _onChanged?.Invoke();
        RaiseFeedback(
            action.Kind,
            action.Slot,
            new ActionFeedbackState(
                IsActive: action.Kind == PerformanceActionKind.AudioFxToggleBypass,
                IsAvailable: true,
                Value: action.Value,
                Argument: action.Target));
    }

    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
    {
        if (!Kinds.Contains(kind) || slot < 0 || slot >= AudioEffectRackSlot.Count)
            return ActionFeedbackState.Unavailable;
        return new ActionFeedbackState(false, true, 0);
    }

    private static string Required(string? value, string name)
        => !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Audio effect action requires {name}.");
}
