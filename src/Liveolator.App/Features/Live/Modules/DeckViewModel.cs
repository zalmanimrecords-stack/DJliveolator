using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Settings;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// A single DJ deck (the mock's Deck A / Deck B, doc 11), parameterized by slot (A = 0, B = 1).
/// Every control is an action source (doc 04): Play·Pause (<see cref="PerformanceActionKind.DeckPlayPause"/>),
/// Cue (<see cref="PerformanceActionKind.DeckCue"/>), Loop (<see cref="PerformanceActionKind.DeckSetLoop"/>),
/// the four hot-cues (<see cref="PerformanceActionKind.DeckHotCue"/>), one-shot Sync
/// (<see cref="PerformanceActionKind.DeckSyncOnce"/>), Pitch (<see cref="PerformanceActionKind.DeckPitch"/>),
/// the 3-band EQ (<see cref="PerformanceActionKind.MixerEqBand"/>), the filter knob
/// (<see cref="PerformanceActionKind.MixerFilter"/>), and click-to-seek on the waveform
/// (<see cref="PerformanceActionKind.DeckSeek"/>). The deck learns its loaded track from
/// <see cref="PerformanceActionKind.DeckLoadTrack"/> feedback (path + analyzed BPM), renders the
/// <see cref="Waveform"/> overview via <see cref="IWaveformProvider"/>, and derives a <see cref="BeatGrid"/>
/// from the BPM and the decoded duration. Toggle controls follow their handler feedback (the LED model).
/// </summary>
public sealed class DeckViewModel : ViewModelBase, IDisposable
{
    /// <summary>Overview resolution — high enough that a zoomed-in window still resolves individual kicks
    /// (the strip samples down to its pixel width when showing the whole track).</summary>
    private const int WaveformBuckets = 6_000;

    private const double MinZoomWindow = 0.01;
    private const double DefaultZoomWindow = 0.04; // fallback when the duration is unknown

    /// <summary>Hot-cue pad count shown on the deck (the mock's 1·2·3·4 row).</summary>
    private const int HotCueCount = 4;

    /// <summary>BPM step per nudge button press (±0.1 BPM — fine enough for manual beat-sync).</summary>
    private const double NudgeBpmStep = 0.1;


    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IWaveformProvider? _waveformProvider;
    private readonly Func<string, DeckTrackInfo?>? _trackInfo;
    private readonly int _slot;
    private bool _isPlaying;
    private bool _isLooping;
    private string _title = "No track loaded";
    private string _meta = NoMeta;
    private IReadOnlyList<float>? _waveform;
    private IReadOnlyList<float>? _kickPeaks;
    private IReadOnlyList<double> _beatGrid = Array.Empty<double>();
    private double _progress;
    private double _trackBpm;
    private double _firstBeatSeconds;
    private double _durationSeconds;
    private double _zoomWindow;
    // Seconds of audio the waveform shows around the playhead — the configurable zoom level (doc 12
    // Settings + the deck ZOOM knob). Lower = more magnified; 0 = whole-track overview. Applied whether
    // the deck is playing or paused, so kicks can be inspected and lined up while cued. Seeded from
    // VisualsSettings; updated live via SetWaveformZoomSeconds.
    private double _zoomSeconds = VisualsSettings.DefaultZoomSeconds;
    // Seconds the track-nudge buttons (◄ / ►) move the playhead per press — the configurable cueing step
    // (doc 12 Settings). Seeded from VisualsSettings; updated live via SetNudgeSeconds.
    private double _nudgeSeconds = VisualsSettings.DefaultNudgeSeconds;
    private decimal _bpm;
    private decimal _minimumBpm;
    private decimal _maximumBpm;
    private bool _isBpmEnabled;
    private bool _applyingBpmFeedback;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    /// <param name="trackInfo">Resolves a loaded track's catalog facts (title/BPM/key/duration) by path,
    /// so the deck can surface Key · BPM · duration; null leaves the meta line as a placeholder.</param>
    public DeckViewModel(
        int slot,
        IPerformanceActionDispatcher? dispatcher = null,
        IWaveformProvider? waveformProvider = null,
        Func<string, DeckTrackInfo?>? trackInfo = null,
        double waveformZoomSeconds = VisualsSettings.DefaultZoomSeconds,
        double nudgeSeconds = VisualsSettings.DefaultNudgeSeconds)
    {
        _slot = slot;
        _dispatcher = dispatcher;
        _waveformProvider = waveformProvider;
        _trackInfo = trackInfo;
        _zoomSeconds = ClampZoomSeconds(waveformZoomSeconds);
        _nudgeSeconds = ClampNudgeSeconds(nudgeSeconds);
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

        // One-shot Sync beatmatches and phase-aligns this deck without engaging a persistent latch.
        SyncCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckSyncOnce, Slot: slot)),
            canEmit);

        // Nudge buttons: ±0.1 BPM relative delta via DeckBpmNudge — manual beat-sync fine-tuning.
        // Emitting Relative mode lets the controller-mapping layer use the same action from a jog wheel.
        NudgeLeftCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: -NudgeBpmStep, Slot: slot)),
            canEmit);
        NudgeRightCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: +NudgeBpmStep, Slot: slot)),
            canEmit);
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

        // Track nudge: shift the playhead ±NudgeSeekSeconds via a RELATIVE DeckSeek. The deck knows the
        // track length, so it converts the half-second into a 0..1 fraction delta; until the duration is
        // known (waveform still decoding) it is a no-op rather than a guessed jump (the engine clamps to [0,1]).
        SeekBackCommand = ReactiveCommand.Create(() => NudgeSeek(-_nudgeSeconds), canEmit);
        SeekForwardCommand = ReactiveCommand.Create(() => NudgeSeek(+_nudgeSeconds), canEmit);

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

        if (_dispatcher?.GetFeedback(PerformanceActionKind.DeckBpm, slot) is { } bpmFeedback)
            ApplyBpmFeedback(bpmFeedback);

        if (_dispatcher?.GetFeedback(PerformanceActionKind.DeckLoadTrack, slot)
            is { IsAvailable: true, Argument: { Length: > 0 } trackPath } loadedTrack)
        {
            OnTrackLoaded(trackPath, loadedTrack.Value);
            ActionFeedbackState firstBeat =
                _dispatcher.GetFeedback(PerformanceActionKind.DeckSetFirstBeat, slot);
            if (firstBeat.IsAvailable)
                _firstBeatSeconds = firstBeat.Value;
        }

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

    public double? KickAnchorFraction =>
        _durationSeconds > 0 ? Math.Clamp(_firstBeatSeconds / _durationSeconds, 0.0, 1.0) : null;

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

    /// <summary>True while this deck is playing (drives the Play key's active state), from dispatcher feedback.
    /// Toggling play also flips the waveform between the whole-track overview and the zoomed follow view.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isPlaying, value);
            ZoomWindow = ComputeZoomWindow();
        }
    }

    /// <summary>
    /// The waveform zoom as a fraction of the track shown centred on the playhead: 0 = whole-track
    /// overview (stopped/paused); on play it becomes a small window (the configured zoom window of
    /// audio) so the strip zooms in and follows the playhead, letting both decks' kicks be aligned by eye.
    /// </summary>
    public double ZoomWindow
    {
        get => _zoomWindow;
        private set => this.RaiseAndSetIfChanged(ref _zoomWindow, value);
    }

    /// <summary>The deck's current audible BPM. User edits emit <see cref="PerformanceActionKind.DeckBpm"/>.</summary>
    public decimal Bpm
    {
        get => _bpm;
        set
        {
            decimal clamped = _isBpmEnabled && !_applyingBpmFeedback
                ? Math.Clamp(value, _minimumBpm, _maximumBpm)
                : value;
            decimal previous = _bpm;
            this.RaiseAndSetIfChanged(ref _bpm, clamped);
            if (_bpm != previous)
                this.RaisePropertyChanged(nameof(BpmFaderValue));
            if (!_applyingBpmFeedback && _isBpmEnabled && clamped != previous)
            {
                _dispatcher?.Dispatch(new PerformanceAction(
                    PerformanceActionKind.DeckBpm,
                    ActionInputMode.Absolute,
                    Value: decimal.ToDouble(clamped),
                    Slot: _slot));
            }
        }
    }

    /// <summary>
    /// BPM expressed as a 0..1 fader position (0 = MinimumBpm, 1 = MaximumBpm).
    /// Used by the horizontal <c>Fader</c> control; writing it back dispatches a
    /// <see cref="PerformanceActionKind.DeckBpm"/> action via the <see cref="Bpm"/> setter.
    /// Returns 0.5 (centre) when no track is loaded or the range is degenerate.
    /// </summary>
    public double BpmFaderValue
    {
        get
        {
            decimal range = _maximumBpm - _minimumBpm;
            if (range <= 0) return 0.5;
            return (double)((_bpm - _minimumBpm) / range);
        }
        set
        {
            decimal range = _maximumBpm - _minimumBpm;
            if (range <= 0) return;
            Bpm = _minimumBpm + (decimal)Math.Clamp(value, 0.0, 1.0) * range;
        }
    }

    public decimal MinimumBpm
    {
        get => _minimumBpm;
        private set => this.RaiseAndSetIfChanged(ref _minimumBpm, value);
    }

    public decimal MaximumBpm
    {
        get => _maximumBpm;
        private set => this.RaiseAndSetIfChanged(ref _maximumBpm, value);
    }

    public bool IsBpmEnabled
    {
        get => _isBpmEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isBpmEnabled, value);
    }

    /// <summary>
    /// Advances the playhead from the engine's live position while playing — called by the Live render-loop
    /// timer (the decks are shared, so both tabs follow). Reads the position through the dispatcher feedback
    /// seam (no direct engine call); a no-op when stopped or when no deck backs this slot.
    /// </summary>
    public void UpdatePlayhead()
    {
        if (_dispatcher is null)
            return;

        if (!_isPlaying)
            return;
        ActionFeedbackState position = _dispatcher.GetFeedback(PerformanceActionKind.DeckSeek, _slot);
        if (position.IsAvailable)
            Progress = position.Value;
    }

    private double ComputeZoomWindow()
    {
        if (_zoomSeconds <= 0.0)
            return 0.0; // knob fully out → whole-track overview (and full-track click-seek)
        if (_durationSeconds <= 0.0)
            return DefaultZoomWindow; // zoomed, but the duration isn't decoded yet → a sane default window
        // Window as a fraction of the track. Defined in SECONDS (not a fixed fraction) so both decks at the
        // same zoom show the same time-scale — a beat is the same width on A and B, so kicks line up by eye.
        return Math.Clamp(_zoomSeconds / _durationSeconds, MinZoomWindow, 1.0);
    }

    /// <summary>
    /// Updates the waveform zoom level (seconds of audio shown) at runtime — driven by the ZOOM knob and
    /// the Settings value. Re-zooms immediately (playing or paused) so kicks can be inspected/aligned while
    /// cued; lower seconds = more magnified, and <c>0</c> (or below) = whole-track overview.
    /// </summary>
    public void SetWaveformZoomSeconds(double seconds)
    {
        _zoomSeconds = ClampZoomSeconds(seconds);
        ZoomWindow = ComputeZoomWindow();
    }

    // Clamp to the supported zoom range, but let 0 (or below) pass through as the overview sentinel.
    private static double ClampZoomSeconds(double seconds)
        => seconds <= 0.0 ? 0.0
            : double.IsNaN(seconds) ? VisualsSettings.DefaultZoomSeconds
            : Math.Clamp(seconds, VisualsSettings.MinZoomSeconds, VisualsSettings.MaxZoomSeconds);

    /// <summary>Updates the track-nudge step (seconds per ◄/► press) at runtime — from the Settings value.</summary>
    public void SetNudgeSeconds(double seconds) => _nudgeSeconds = ClampNudgeSeconds(seconds);

    private static double ClampNudgeSeconds(double seconds)
        => double.IsNaN(seconds)
            ? VisualsSettings.DefaultNudgeSeconds
            : Math.Clamp(seconds, VisualsSettings.MinNudgeSeconds, VisualsSettings.MaxNudgeSeconds);

    // The beat/bar grid needs the BPM (from the load), the decoded duration, and the first-beat anchor
    // (from the DeckSetFirstBeat feedback); empty until the duration is known.
    private void RecomputeBeatGrid()
        => BeatGrid = _durationSeconds > 0
            ? BeatGridCalculator.BeatFractions(_trackBpm, _durationSeconds, _firstBeatSeconds)
            : Array.Empty<double>();

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
    /// <summary>Nudges the deck BPM down by <see cref="NudgeBpmStep"/> — manual beat-sync fine-tuning.</summary>
    public ReactiveCommand<Unit, Unit> NudgeLeftCommand { get; }
    /// <summary>Nudges the deck BPM up by <see cref="NudgeBpmStep"/> — manual beat-sync fine-tuning.</summary>
    public ReactiveCommand<Unit, Unit> NudgeRightCommand { get; }

    /// <summary>Click-to-seek: invoked by the waveform strip with the clicked 0..1 fraction.</summary>
    public ReactiveCommand<double, Unit> SeekCommand { get; }

    /// <summary>Nudges the track playhead 0.5 s back (relative seek) — fine cueing / manual line-up.</summary>
    public ReactiveCommand<Unit, Unit> SeekBackCommand { get; }

    /// <summary>Nudges the track playhead 0.5 s forward (relative seek) — fine cueing / manual line-up.</summary>
    public ReactiveCommand<Unit, Unit> SeekForwardCommand { get; }

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
    public bool CanNudgeSeek => IsEnabled;

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

    // Shift the playhead by a signed number of seconds via a RELATIVE DeckSeek. Converts seconds to a
    // 0..1 fraction using the decoded duration; a no-op until the duration is known (engine clamps to [0,1]).
    private void NudgeSeek(double seconds)
    {
        if (_dispatcher is null || _durationSeconds <= 0.0)
            return;
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSeek, ActionInputMode.Relative, Value: seconds / _durationSeconds, Slot: _slot));
    }

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Slot != _slot)
            return;
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            switch (e.Kind)
            {
                case PerformanceActionKind.MixerEqBand:
                    ApplyEqFeedback(e.State);
                    break;
                case PerformanceActionKind.MixerFilter:
                    Filter.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.DeckPitch:
                    Pitch.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.DeckBpm:
                    ApplyBpmFeedback(e.State);
                    break;
                case PerformanceActionKind.DeckPlayPause:
                    IsPlaying = e.State.IsActive;
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
                case PerformanceActionKind.DeckSetFirstBeat:
                    // The analyzed downbeat anchor (seconds), echoed right after the load — anchor the
                    // beat/bar grid on it so the lines fall on the kicks (and match what Sync aligns to).
                    _firstBeatSeconds = e.State.Value;
                    RecomputeBeatGrid();
                    this.RaisePropertyChanged(nameof(KickAnchorFraction));
                    break;
            }
        });
    }

    private void ApplyEqFeedback(ActionFeedbackState state)
    {
        switch (state.Argument)
        {
            case "High":
                EqHigh.SetFromFeedback(state.Value);
                break;
            case "Mid":
                EqMid.SetFromFeedback(state.Value);
                break;
            case "Low":
                EqLow.SetFromFeedback(state.Value);
                break;
        }
    }

    private void ApplyBpmFeedback(ActionFeedbackState state)
    {
        decimal minimum = 0;
        decimal maximum = 0;
        string[] range = state.Argument?.Split('|', StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        if (range.Length == 2)
        {
            decimal.TryParse(range[0], NumberStyles.Float, CultureInfo.InvariantCulture, out minimum);
            decimal.TryParse(range[1], NumberStyles.Float, CultureInfo.InvariantCulture, out maximum);
        }

        _applyingBpmFeedback = true;
        try
        {
            MinimumBpm = minimum;
            MaximumBpm = maximum;
            IsBpmEnabled = state.IsAvailable && state.Value > 0.0 && maximum >= minimum;
            Bpm = state.Value > 0.0 ? (decimal)state.Value : 0;
            if (_title != "No track loaded" && state.Value > 0.0)
            {
                _trackBpm = state.Value;
                Meta = ReplaceDisplayedBpm(Meta, state.Value);
                RecomputeBeatGrid();
            }
        }
        finally
        {
            _applyingBpmFeedback = false;
            // Min/Max changed inside feedback; notify the fader so it repositions its thumb.
            this.RaisePropertyChanged(nameof(BpmFaderValue));
        }
    }

    private static string ReplaceDisplayedBpm(string meta, double bpm)
    {
        int suffix = meta.IndexOf(" BPM", StringComparison.Ordinal);
        if (suffix < 0)
            return $"{bpm:0.0} BPM";

        int start = suffix;
        while (start > 0 && (char.IsDigit(meta[start - 1]) || meta[start - 1] == '.'))
            start--;
        return $"{meta[..start]}{bpm:0.0}{meta[suffix..]}";
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
        // Prefer the full catalog facts (Key · BPM · duration); if the track isn't in the catalog, still
        // show at least the analyzed BPM that rides on the load action so a deck never hides its tempo.
        Meta = info is { } i
            ? $"{i.Key} · {i.Bpm} BPM · {i.Duration}"
            : bpm > 0 ? $"{bpm:0.0} BPM" : NoMeta;
        this.RaisePropertyChanged(nameof(HasTrackMeta));
        Progress = 0;
        Waveform = null;          // empty state while the new overview decodes (no fake waveform)
        KickPeaks = null;
        BeatGrid = Array.Empty<double>();
        _trackBpm = bpm;          // analyzed tempo from the load (0 = unknown); grid waits on the duration
        _firstBeatSeconds = 0;    // re-anchored when the DeckSetFirstBeat feedback arrives for this load
        _durationSeconds = 0;     // unknown until the overview decodes; re-zoom then
        this.RaisePropertyChanged(nameof(KickAnchorFraction));
        ZoomWindow = ComputeZoomWindow();
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
            // Now the duration is known: build the (first-beat-anchored) grid and size the zoom window in
            // real time (so the follow view shows a consistent ~PlayingZoomSeconds regardless of length).
            _durationSeconds = overview.IsEmpty ? 0 : overview.DurationSeconds;
            RecomputeBeatGrid();
            this.RaisePropertyChanged(nameof(KickAnchorFraction));
            ZoomWindow = ComputeZoomWindow();
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
