using Liveolator.Core.Mapping;

namespace Liveolator.App.Features.Mappings;

public sealed class MappingBindingViewModel
{
    public MappingBindingViewModel(ControllerBinding binding)
    {
        Binding = binding;
        Control = $"{binding.TriggerType}  ch {binding.Channel + 1}  #{binding.Data1}";
        Target = $"{binding.Action}  slot {binding.Slot + 1}";
        Mode = binding.InputMode.ToString();
    }

    public ControllerBinding Binding { get; }
    public string Control { get; }
    public string Target { get; }
    public string Mode { get; }
}
