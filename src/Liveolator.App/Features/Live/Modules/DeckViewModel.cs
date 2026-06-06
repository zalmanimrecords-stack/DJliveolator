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
/// Every control is an action source (doc 04): Play·Pause (<see cref="PerformanceActionKind.DeckPlayPause"/>),
/// Cue (<see cref="PerformanceActionKind.DeckCue"/>), Loop (<see cref="PerformanceActionKind.DeckSetLoop"/>),
/// the four hot-cues (<see cref="PerformanceActionKind.DeckHotCue"/>), Sync Lock
/// (<see cref="PerformanceActionKind.DeckSyncLockToggle"/>), Pitch (<see cref="PerformanceActionKind.DeckPitch"/>),
/// the 3-band EQ (<see cref="PerformanceActionKind.MixerEqBand"/>), the filter knob
/// (<see cref="PerformanceActionKind.MixerFilter"/>), and click-to-seek on the waveform
/// (<see cref="PerformanceActionKind.DeckSeek"/>). The deck learns its loaded track from
/// <see cref="PerformanceActionKind.DeckLoadTrack"/> feedback (path + analyzed BPM), renders the
/// <see cref="Waveform"/> overview via <see cref="IWaveformProvider"/>, and derives a <see cref="BeatGrid"/>
/// from the BPM and the decoded duration. Toggle controls follow their handler feedback (the LED model).
/// </summary>
public sealed class DeckViewModel : ViewModelBase, IDisposable
{
    /// <summary>Overview resolution — plenty of buckets for the strip; the control samples to its width.</summary>
    private const int WaveformBuckets = 1_000;

    /// <summary>Hot-cue pad count shown on the deck (the mock's 1·2·3·4 row).</summary>
    private const int HotCueCount = 4;

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IWaveformProvider? _waveformProvider;
    private readonly Func<string, DeckTrackInfo?>? _trackInfo;
    private readonly int _slot;
    private bool _isPlaying;
    private bool _isSyncLocked;
    private bool _isLooping;
    private string _title = "No track loaded";
    private string _meta = NoMeta;
    private IReadOnlyList<float>? _waveform;
    private IReadOnlyList<float>? _kickPeaks;
    private IReadOnlyList<double> _beatGrid = Array.Empty<double>();
    private double _progress;
    private double _trackBpm;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    /// <param name="trackInfo">Resolves a loaded track's catalog facts (title/BPM/key/duration) by path,
    /// so the deck can surface Key · BPM · duration; null leaves the meta line as a placeholder.</param>
    public DeckViewModel(
        int slot,
        IPerformanceActionDispatcher? dispatcher = null,
        IWaveformProvider? waveformProvider = null,
        Func<string, DeckTrackInfo?>? trackInfo = null)
    {
        _slot = slot;
        _dispatcher = dispatcher;
        _waveformProvider = waveformProvider;
        _trackInfo = trackInfo;
        DeckId = slot == 0 ? "A" : "B";
        bool enabled = dispatcher is not null;
        IObservable<bool> canEmit = Observable.Return(enabled);

        PlayPauseCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: slot)),
            canEmit);

        // Cue = jump to the cue point / track start (momentary, doc 11). No active latch — it's a jump.
        CueCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckCue, Slot: slot)),
            canEmit);

        // Loop toggle. The engine handler is being built in parallel; the VM emits the action and follows
        // the DeckSetLoop active-state feedback (the LED model) exactly like Sync, so it lights up once the
        // engine reports a loop is active. Value carries a default loop length in beats (doc 11).
        LoopCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckSetLoop, ActionInputMode.Absolute, Value: DefaultLoopBeats, Slot: slot)),
            canEmit);
        _isLooping = _dispatcher?.GetFeedback(PerformanceActionKind.DeckSetLoop, slot).IsActive ?? false;

        // Sync Lock = tempo match (beatmatch by BPM, doc 11). The toggle's active state follows the
        // handler's DeckSyncLockToggle feedback (the LED model), seeded from the current engine state.
        SyncCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckSyncLockToggle, Slot: slot)),
            canEmit);
        _isSyncLocked = _dispatcher?.GetFeedback(PerformanceActionKind.DeckSyncLockToggle, slot).IsActive ?? false;

        // Click-to-seek: the strip computes the clicked 0..1 fraction and passes it here; we emit an
        // absolute DeckSeek for this slot. The fraction is clamped at the seam (defence against a bad value).
        SeekCommand = ReactiveCommand.Create<double>(fraction =>
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction))
                return;
            double clamped = Math.Clamp(fraction, 0.0, 1.0);
            _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckSeek, ActionInputMode.Absolute, Value: clamped, Slot: slot));
        }, canEmit);

        var hotCues = new HotCuePadViewModel[HotCueCount];
        for (int index = 0; index < HotCueCount; index++)
        {
            int cueIndex = index; // capture per-pad
            hotCues[index] = new HotCuePadViewModel(
                cueIndex,
                enabled
                    ? () => _dispatcher?.Dispatch(new PerformanceAction(
                        PerformanceActionKind.DeckHotCue, Slot: slot, Argument: cueIndex.ToString()))
                    : null);
        }
        HotCues = hotCues;

        EqHigh = new ContinuousControlViewModel("Hi", EqBands_Unity, enabled ? v => EmitEq("High", v) : null);
        EqMid = new ContinuousControlViewModel("Mid", EqBands_Unity, enabled ? v => EmitEq("Mid", v) : null);
        EqLow = new ContinuousControlViewModel("Low", EqBands_Unity, enabled ? v => EmitEq("Low", v) : null);
        Filter = new ContinuousControlViewModel(
            "Flt", Seed(PerformanceActionKind.MixerFilter, FilterCentre),
            enabled ? v => Emit(PerformanceActionKind.MixerFilter, v) : null);

        // Pitch fader: absolute 0..1 (0.5 = no pitch change); follows DeckPitch feedback like the filter.
        Pitch = new ContinuousControlViewModel(
            "Pitch", Seed(PerformanceActionKind.DeckPitch, PitchCentre),
            enabled ? v => Emit(PerformanceActionKind.DeckPitch, v) : null);

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    // EqBands.Unity (0.5) = flat; MixerMath maps 0..1 to boost/cut. Filter/pitch centre likewise.
    private const double EqBands_Unity = 0.5;
    private const double FilterCentre = 0.5;
    private const double PitchCentre = 0.5;

    /// <summary>Default loop length emitted by the LOOP button, in beats (a 1-bar loop in 4/4).</summary>
    private const double DefaultLoopBeats = 4.0;

    /// <summary>Deck label, "A" or "B".</summary>
    public string DeckId { get; }

    /// <summary>The loaded track's name, or the no-track placeholder.</summary>
    public string Title
    {
        get => _title;
        private set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    /// <summary>Deck meta line — "Key · BPM · duration" from the catalog, or "—" before a track loads.</summary>
    public string Meta
    {
        get => _meta;
        private set => this.RaiseAndSetIfChanged(ref _meta, value);
    }

    /// <summary>True once a track with known catalog facts is loaded (drives the meta line's visibility).</summary>
    public bool HasTrackMeta => _meta != NoMeta;

    private const string NoMeta = "—";

    /// <summary>The loaded track's waveform peaks (0..1), or null when none is decoded (placeholder).</summary>
    public IReadOnlyList<float>? Waveform
    {
        get => _waveform;
        private set => this.RaiseAndSetIfChanged(ref _waveform, value);
    }

    /// <summary>The loaded track's low-frequency (kick) band peaks (0..1), aligned 1:1 with
    /// <see cref="Waveform"/>; null when none is decoded. The strip draws these as a distinct overlay so
    /// the kick transients are visible for beat-sync alignment.</summary>
    public IReadOnlyList<float>? KickPeaks
    {
        get => _kickPeaks;
        private set => this.RaiseAndSetIfChanged(ref _kickPeaks, value);
    }

    /// <summary>
    /// Beat-line positions as 0..1 track fractions for the strip's grid overlay, derived from the loaded
    /// track's BPM and decoded duration. Empty when either is unknown (the strip then draws no grid).
    /// </summary>
    public IReadOnlyList<double> BeatGrid
    {
        get => _beatGrid;
        private set => this.RaiseAndSetIfChanged(ref _beatGrid, value);
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

    /// <summary>True while this deck has an active loop (drives the LOOP key's active state), from feedback.</summary>
    public bool IsLooping
    {
        get => _isLooping;
        private set => this.RaiseAndSetIfChanged(ref _isLooping, value);
    }

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> CueCommand { get; }
    public ReactiveCommand<Unit, Unit> LoopCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncCommand { get; }

    /// <summary>Click-to-seek: invoked by the waveform strip with the clicked 0..1 fraction.</summary>
    public ReactiveCommand<double, Unit> SeekCommand { get; }

    /// <summary>The four hot-cue pads (the mock's 1·2·3·4 row).</summary>
    public IReadOnlyList<HotCuePadViewModel> HotCues { get; }

    public ContinuousControlViewModel EqHigh { get; }
    public ContinuousControlViewModel EqMid { get; }
    public ContinuousControlViewModel EqLow { get; }
    public ContinuousControlViewModel Filter { get; }
    public ContinuousControlViewModel Pitch { get; }

    /// <summary>Cue/Loop/Hot-cues/Sync are drivable whenever a deck engine backs this view (doc 11).</summary>
    public bool CanCue => IsEnabled;
    public bool CanLoop => IsEnabled;
    public bool CanHotCue => IsEnabled;
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
                case PerformanceActionKind.DeckPitch:
                    Pitch.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.DeckPlayPause:
                    IsPlaying = e.State.IsActive;
                    break;
                case PerformanceActionKind.DeckSyncLockToggle:
                    IsSyncLocked = e.State.IsActive;
                    break;
                case PerformanceActionKind.DeckSetLoop:
                    IsLooping = e.State.IsActive;
                    break;
                case PerformanceActionKind.DeckHotCue:
                    UpdateHotCue(e.State);
                    break;
                case PerformanceActionKind.DeckSeek when e.State.IsAvailable:
                    Progress = e.State.Value; // playhead follows seek/cue position
                    break;
                case PerformanceActionKind.DeckLoadTrack when !string.IsNullOrEmpty(e.State.Argument):
                    OnTrackLoaded(e.State.Argument!, e.State.Value);
                    break;
            }
        });
    }

    // The hot-cue index rides in the feedback Argument (the deck is addressed by slot); update only the
    // matching pad's lit state. A missing/unparseable index is ignored — never throw on a feedback echo.
    private void UpdateHotCue(ActionFeedbackState state)
    {
        if (!int.TryParse(state.Argument, out int index) || index < 0 || index >= HotCues.Count)
            return;
        HotCues[index].IsSet = state.IsActive;
    }

    private void OnTrackLoaded(string trackPath, double bpm)
    {
        DeckTrackInfo? info = _trackInfo?.Invoke(trackPath);
        Title = !string.IsNullOrWhiteSpace(info?.Title)
            ? info!.Title
            : Path.GetFileNameWithoutExtension(trackPath);
        Meta = info is { } i ? $"{i.Key} · {i.Bpm} BPM · {i.Duration}" : NoMeta;
        this.RaisePropertyChanged(nameof(HasTrackMeta));
        Progress = 0;
        Waveform = null;          // show the placeholder while the new overview decodes
        KickPeaks = null;
        BeatGrid = Array.Empty<double>();
        _trackBpm = bpm;          // analyzed tempo from the load (0 = unknown); grid waits on the duration
        ClearHotCues();           // hot-cues belong to the track and clear on load (doc 18)
        LoadWaveform(trackPath);
    }

    private void ClearHotCues()
    {
        foreach (HotCuePadViewModel pad in HotCues)
            pad.IsSet = false;
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
            if (cts.IsCancellationRequested)
                return;
            Waveform = overview.IsEmpty ? null : overview.Peaks;
            KickPeaks = overview.IsEmpty ? null : overview.LowPeaks;
            // The grid needs both the BPM (from the load) and the decoded duration (from the overview).
            BeatGrid = overview.IsEmpty
                ? Array.Empty<double>()
                : BeatGridCalculator.BeatFractions(_trackBpm, overview.DurationSeconds);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load — ignore.
        }
        catch (Exception)
        {
            Waveform = null; // belt-and-braces around the await boundary
            KickPeaks = null;
            BeatGrid = Array.Empty<double>();
        }
    }
}

/// <summary>Pre-formatted catalog facts for a deck's loaded track (title + BPM/key/duration strings).</summary>
public sealed record DeckTrackInfo(string Title, string Bpm, string Key, string Duration);
