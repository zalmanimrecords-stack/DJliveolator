using System.Reactive;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using ReactiveUI;

namespace Liveolator.App.Features.Mappings;

public sealed class GlobalMidiLearnCoordinator : ViewModelBase, IDisposable
{
    private readonly IMidiControlSession _session;
    private bool _isEnabled;
    private bool _isWaitingForMidi;
    private string _status = "MIDI Learn off";

    public GlobalMidiLearnCoordinator(IMidiControlSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ToggleCommand = ReactiveCommand.Create(Toggle);
        _session.MappingChanged += OnMappingChanged;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    public bool IsWaitingForMidi
    {
        get => _isWaitingForMidi;
        private set => this.RaiseAndSetIfChanged(ref _isWaitingForMidi, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public bool TryCaptureUiAction(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsEnabled)
            return false;
        if (IsWaitingForMidi)
            return true;

        try
        {
            _session.BeginLearn(action.Kind, action.Slot, action.Argument, action.InputMode);
            IsWaitingForMidi = true;
            Status = $"Now use the controller for {Describe(action)}. Esc exits.";
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }

        return true;
    }

    public void Toggle()
    {
        if (IsEnabled)
            Cancel();
        else
            Enable();
    }

    public void Enable()
    {
        IsEnabled = true;
        IsWaitingForMidi = false;
        Status = "Click a control in Liveolator, then use the controller. Esc exits.";
    }

    public void Cancel()
    {
        _session.CancelLearn();
        IsEnabled = false;
        IsWaitingForMidi = false;
        Status = "MIDI Learn off";
    }

    private void OnMappingChanged(object? sender, ControllerMappingProfile profile)
    {
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            if (!IsEnabled)
                return;

            IsWaitingForMidi = false;
            Status = "Mapped. Click another control, or press Esc to exit.";
        });
    }

    private static string Describe(PerformanceAction action)
        => action.Argument is null
            ? $"{action.Kind} slot {action.Slot + 1}"
            : $"{action.Kind} {action.Argument} slot {action.Slot + 1}";

    public void Dispose() => _session.MappingChanged -= OnMappingChanged;
}
