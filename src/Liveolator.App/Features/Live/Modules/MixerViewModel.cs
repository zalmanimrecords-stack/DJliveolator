using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The Mixer module (the mock's centre column / doc 11): the A↔B crossfader and the two per-deck
/// channel-gain faders. Each fader drives the <see cref="MixerActionHandler"/> through the dispatcher
/// (doc 04). VU meter levels have no live-input source yet (doc 18) and are rendered as a static
/// placeholder in the view. Initial fader positions are seeded from dispatcher feedback so the UI
/// reflects the authoritative mixer state.
/// </summary>
public sealed class MixerViewModel : ViewModelBase, IDisposable
{
    private const double DefaultCrossfader = 0.5;
    private const double DefaultGain = 1.0;

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private bool _disposed;

    public MixerViewModel(IPerformanceActionDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
        bool enabled = dispatcher is not null;

        Crossfader = new ContinuousControlViewModel(
            "A / B", Seed(PerformanceActionKind.MixerCrossfade, slot: 0, DefaultCrossfader),
            enabled ? v => Emit(PerformanceActionKind.MixerCrossfade, v, slot: 0) : null);

        ChannelGainA = new ContinuousControlViewModel(
            "A", Seed(PerformanceActionKind.MixerChannelGain, slot: 0, DefaultGain),
            enabled ? v => Emit(PerformanceActionKind.MixerChannelGain, v, slot: 0) : null);

        ChannelGainB = new ContinuousControlViewModel(
            "B", Seed(PerformanceActionKind.MixerChannelGain, slot: 1, DefaultGain),
            enabled ? v => Emit(PerformanceActionKind.MixerChannelGain, v, slot: 1) : null);

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    /// <summary>True when the mixer handler is wired; the UI disables the faders otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    public ContinuousControlViewModel Crossfader { get; }
    public ContinuousControlViewModel ChannelGainA { get; }
    public ContinuousControlViewModel ChannelGainB { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }

    private double Seed(PerformanceActionKind kind, int slot, double fallback)
    {
        ActionFeedbackState? feedback = _dispatcher?.GetFeedback(kind, slot);
        return feedback is { IsAvailable: true } ? feedback.Value : fallback;
    }

    private void Emit(PerformanceActionKind kind, double value, int slot)
        => _dispatcher?.Dispatch(new PerformanceAction(kind, ActionInputMode.Absolute, Value: value, Slot: slot));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
        => RxApp.MainThreadScheduler.Schedule(() =>
        {
            switch (e.Kind)
            {
                case PerformanceActionKind.MixerCrossfade:
                    Crossfader.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.MixerChannelGain when e.Slot == 0:
                    ChannelGainA.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.MixerChannelGain when e.Slot == 1:
                    ChannelGainB.SetFromFeedback(e.State.Value);
                    break;
            }
        });
}
