using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// A single DJ deck (the mock's Deck A / Deck B, doc 11), parameterized by slot (A = 0, B = 1).
/// Wired now: Play·Pause (<see cref="PerformanceActionKind.DeckPlayPause"/>), the 3-band EQ
/// (<see cref="PerformanceActionKind.MixerEqBand"/>), the single-knob filter
/// (<see cref="PerformanceActionKind.MixerFilter"/>), and the track <see cref="Waveform"/> overview +
/// playhead — all through the dispatcher (doc 04). The deck learns its loaded track from
/// <see cref="PerformanceActionKind.DeckLoadTrack"/> feedback (which carries the path) and renders the
/// waveform via <see cref="IWaveformProvider"/>. Sync Lock (tempo match, doc 11) is wired through
/// <see cref="PerformanceActionKind.DeckSyncLockToggle"/>; Cue/Loop/hot-cues/pitch are not yet surfaced
/// in this view (a later UI increment) and stay disabled.
/// </summary>
public sealed class DeckViewModel : ViewModelBase, IDisposable
{
    /// <summary>Overview resolution — plenty of buckets for the strip; the control samples to its width.</summary>
    private const int WaveformBuckets = 1_000;

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IWaveformProvider? _waveformProvider;
    private readonly int _slot;
    private bool _isPlaying;
    private bool _isSyncLocked;
    private string _title = "No track loaded";
    private IReadOnlyList<float>? _waveform;
    private double _progress;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    public DeckViewModel(
        int slot,
        IPerformanceActionDispatcher? dispatcher = null,
        IWaveformProvider? waveformProvider = null)
    {
        _slot = slot;
        _dispatcher = dispatcher;
        _waveformProvider = waveformProvider;
        DeckId = slot == 0 ? "A" : "B";
        bool enabled = dispatcher is not null;

        PlayPauseCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: slot)),
            Observable.Return(enabled));

        // Sync Lock = tempo match (beatmatch by BPM, doc 11). The toggle's active state follows the
        // handler's DeckSyncLockToggle feedback (the LED model), seeded from the current engine state.
        SyncCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckSyncLockToggle, Slot: slot)),
            Observable.Return(enabled));
        _isSyncLocked = _dispatcher?.GetFeedback(PerformanceActionKind.DeckSyncLockToggle, slot).IsActive ?? false;

        EqHigh = new ContinuousControlViewModel("Hi", EqBands_Unity, enabled ? v => EmitEq("High", v) : null);
        EqMid = new ContinuousControlViewModel("Mid", EqBands_Unity, enabled ? v => EmitEq("Mid", v) : null);
        EqLow = new ContinuousControlViewModel("Low", EqBands_Unity, enabled ? v => EmitEq("Low", v) : null);
        Filter = new ContinuousControlViewModel(
            "Flt", Seed(PerformanceActionKind.MixerFilter, FilterCentre),
            enabled ? v => Emit(PerformanceActionKind.MixerFilter, v) : null);

        // Disabled-but-labeled: not surfaced in this view yet (doc 18). A null callback disables the control.
        Pitch = new ContinuousControlViewModel("Pitch", PitchCentre, onUserChanged: null);

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    // EqBands.Unity (0.5) = flat; MixerMath maps 0..1 to boost/cut. Filter/pitch centre likewise.
    private const double EqBands_Unity = 0.5;
    private const double FilterCentre = 0.5;
    private const double PitchCentre = 0.5;

    /// <summary>Deck label, "A" or "B".</summary>
    public string DeckId { get; }

    /// <summary>The loaded track's name, or the no-track placeholder.</summary>
    public string Title
    {
        get => _title;
        private set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    /// <summary>Placeholder deck meta line (key · pitch · time) until a track is loaded.</summary>
    public string Meta => "—";

    /// <summary>The loaded track's waveform peaks (0..1), or null when none is decoded (placeholder).</summary>
    public IReadOnlyList<float>? Waveform
    {
        get => _waveform;
        private set => this.RaiseAndSetIfChanged(ref _waveform, value);
    }

    /// <summary>
    /// Playhead position as a 0..1 fraction of the track. Updated from <c>DeckSeek</c> feedback (raised by
    /// the deck handler on seek/cue/load); a continuously advancing playhead during playback is a follow-up
    /// that needs a render-loop tick (the Live tab's <c>ILiveBeatTimer</c> seam), kept out of the VM ctor so
    /// it can't block a unit-test scheduler.
    /// </summary>
    public double Progress
    {
        get => _progress;
        private set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    /// <summary>True when transport/EQ can be driven; the UI disables those controls otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    /// <summary>True while this deck is playing (drives the Play key's active state), from dispatcher feedback.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
    }

    /// <summary>True while this deck is sync-locked (tempo-matched to the other deck), from feedback.</summary>
    public bool IsSyncLocked
    {
        get => _isSyncLocked;
        private set => this.RaiseAndSetIfChanged(ref _isSyncLocked, value);
    }

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncCommand { get; }
    public ContinuousControlViewModel EqHigh { get; }
    public ContinuousControlViewModel EqMid { get; }
    public ContinuousControlViewModel EqLow { get; }
    public ContinuousControlViewModel Filter { get; }
    public ContinuousControlViewModel Pitch { get; }

    /// <summary>Cue/Loop/Hot-cues are not surfaced in this view yet — disabled (doc 18).</summary>
    public bool CanCue => false;
    public bool CanLoop => false;
    public bool CanHotCue => false;

    /// <summary>Sync Lock is drivable whenever the deck engine backs this view (tempo match, doc 11).</summary>
    public bool CanSync => IsEnabled;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }

    private double Seed(PerformanceActionKind kind, double fallback)
    {
        ActionFeedbackState? feedback = _dispatcher?.GetFeedback(kind, _slot);
        return feedback is { IsAvailable: true } ? feedback.Value : fallback;
    }

    private void EmitEq(string band, double value)
        => _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, Value: value, Slot: _slot, Argument: band));

    private void Emit(PerformanceActionKind kind, double value)
        => _dispatcher?.Dispatch(new PerformanceAction(kind, ActionInputMode.Absolute, Value: value, Slot: _slot));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Slot != _slot)
            return;
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            switch (e.Kind)
            {
                case PerformanceActionKind.MixerFilter:
                    Filter.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.DeckPlayPause:
                    IsPlaying = e.State.IsActive;
                    break;
                case PerformanceActionKind.DeckSyncLockToggle:
                    IsSyncLocked = e.State.IsActive;
                    break;
                case PerformanceActionKind.DeckSeek when e.State.IsAvailable:
                    Progress = e.State.Value; // playhead follows seek/cue position
                    break;
                case PerformanceActionKind.DeckLoadTrack when !string.IsNullOrEmpty(e.State.Argument):
                    OnTrackLoaded(e.State.Argument!);
                    break;
            }
        });
    }

    private void OnTrackLoaded(string trackPath)
    {
        Title = Path.GetFileNameWithoutExtension(trackPath);
        Progress = 0;
        Waveform = null; // show the placeholder while the new overview decodes
        LoadWaveform(trackPath);
    }

    // Fire-and-forget waveform decode at the event boundary; cancels any prior in-flight load so a quick
    // A→B→A swap can't paint a stale overview. The provider already degrades on failure (returns Empty).
    private async void LoadWaveform(string trackPath)
    {
        if (_waveformProvider is null)
            return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        try
        {
            WaveformOverview overview = await Task.Run(
                () => _waveformProvider.GetOverviewAsync(trackPath, WaveformBuckets, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested)
                Waveform = overview.IsEmpty ? null : overview.Peaks;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load — ignore.
        }
        catch (Exception)
        {
            Waveform = null; // belt-and-braces around the await boundary
        }
    }
}
