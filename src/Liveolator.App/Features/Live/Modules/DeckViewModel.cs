using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Audio;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Settings;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// A single DJ deck (the mock's Deck A / Deck B, doc 11), parameterized by slot (A = 0, B = 1).
/// Every control is an action source (doc 04): Play·Pause (<see cref="PerformanceActionKind.DeckPlayPause"/>),
/// Cue (<see cref="PerformanceActionKind.DeckCue"/>), Loop (<see cref="PerformanceActionKind.DeckSetLoop"/>),
/// the four hot-cues (<see cref="PerformanceActionKind.DeckHotCue"/>), the continuous sync latch
/// (<see cref="PerformanceActionKind.DeckSyncToggle"/>), Pitch (<see cref="PerformanceActionKind.DeckPitch"/>),
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

    /// <summary>Total hot-cue slots per deck (matches the engine's bank), surfaced as two A/B banks of
    /// <see cref="HotCuesPerBank"/> pads (owner decision 2026-06-19: 4 pads + an A/B toggle).</summary>
    private const int HotCueCount = 8;

    /// <summary>Pads shown at once; the A/B toggle swaps which bank (slots 0–3 vs 4–7) is visible.</summary>
    private const int HotCuesPerBank = 4;

    /// <summary>BPM step per nudge button press (±0.1 BPM — fine enough for manual beat-sync).</summary>
    private const double NudgeBpmStep = 0.1;

    // NUDGE pitch-bend: a tap momentarily bends the deck rate by this fraction (±3%) for PitchBendWindow,
    // then restores — sliding the phase to manually beat-match without a position skip. Rapid taps re-arm
    // the window so a press-and-tap holds the bend. ±3% sits well inside the deck's pitch range.
    private const double PitchBendFraction = 0.03;

    // Fine grid-phase step per tap for manual kick-on-kick alignment (seconds); coarse alignment is the
    // waveform drag. Re-phases the analyzed grid only — never the audible pitch.
    private const double GridNudgeStep = 0.004;
    private static readonly TimeSpan PitchBendWindow = TimeSpan.FromMilliseconds(140);

    /// <summary>
    /// <see cref="PerformanceAction.Origin"/> tag stamped on grid/downbeat actions this deck derives from
    /// automatic track analysis (mirrors <c>StudioArranger.Origin</c>). Never carried by a human gesture,
    /// so session persistence can tell an analyzer downbeat from a manual SET ONE and skip persisting it.
    /// </summary>
    public const string AnalysisOrigin = "analysis";


    private readonly IPerformanceActionDispatcher? _dispatcher;
    // True when the deck-transport actions are actually handled (a deck engine backs this slot). Gates the
    // transport/hot-cue/pitch/grid controls so they disable instead of silently dropping actions (QA S1).
    private readonly bool _transportEnabled;
    private readonly IWaveformProvider? _waveformProvider;
    private readonly Func<string, DeckTrackInfo?>? _trackInfo;
    private readonly Func<string, BpmResult?>? _analysisInfo;
    // Offline auto-cue placement for the AUTO-CUE button; null hides it. Set by the composition root.
    private readonly IAutoCueService? _autoCueService;
    // On-demand background BPM analysis for a load with no analysis anywhere (not even the catalog):
    // decodes + detects tempo off-thread over the same offline-decoder pipeline AUTO-CUE uses and
    // re-emits the self-heal grid actions on completion, so SYNC isn't a dead button on such a load.
    // Null (no decoder wired) leaves the deck grid-less, as before.
    private readonly Func<string, CancellationToken, Task<BpmResult?>>? _bpmAnalysis;
    private CancellationTokenSource? _bpmAnalysisCts;
    // The path the in-flight background analysis is decoding; null when none is running.
    private string? _bpmAnalysisPath;
    // The currently-loaded track path (from DeckLoadTrack feedback) — the file AUTO-CUE analyzes.
    private string? _loadedTrackPath;
    private readonly int _slot;
    private bool _isPlaying;
    private bool _isLooping;
    private bool _isKeyLock;
    private bool _isSyncEngaged;
    private SyncLockState _syncState;
    private bool _isHotCueBankB;
    private string _title = "No track loaded";
    private string? _artist;
    private string _meta = NoMeta;
    private IReadOnlyList<float>? _waveform;
    private IReadOnlyList<float>? _kickPeaks;
    private IReadOnlyList<float>? _midPeaks;
    private IReadOnlyList<float>? _highPeaks;
    private IReadOnlyList<double> _beatGrid = Array.Empty<double>();
    private double _progress;
    private double _trackBpm;
    private double _firstBeatSeconds;
    // The downbeat (bar-1, the musical "one") anchor in seconds: where the BAR starts, distinct from the
    // first-beat anchor (where beats land). Drives which grid line carries the red bar marker. Auto-set from
    // the analyzed downbeat when confident, or placed by the DJ via SET ONE; 0 = unknown → index 0 is the bar.
    private double _downbeatSeconds;
    private int _downbeatBarOffset;
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
    // Restores the deck's normal rate after a momentary NUDGE pitch-bend (see PitchBendTap).
    private readonly DispatcherTimer _pitchBendRestore;
    private decimal _bpm;
    private bool _isBpmMatched;
    private decimal _minimumBpm;
    private decimal _maximumBpm;
    private bool _isBpmEnabled;
    private bool _hasLoadedTrack;
    private bool _applyingBpmFeedback;
    private string _elapsedText = NoTime;
    private string _remainingText = NoTime;
    private string? _trackKey;
    private readonly ObservableAsPropertyHelper<string> _pitchPercentText;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    /// <param name="trackInfo">Resolves a loaded track's catalog facts (title/BPM/key/duration) by path,
    /// so the deck can surface Key · BPM · duration; null leaves the meta line as a placeholder.</param>
    /// <param name="deckTransportEnabled">Whether the deck-transport actions (play/cue/sync/loop/hot-cue/
    /// pitch/grid — owned only by <c>DeckActionHandler</c>) are actually handled. False in catalog-browser
    /// mode (no realtime audio engine), where those actions would otherwise be silently dropped; the EQ/
    /// filter knobs (owned by the always-present mixer handler) stay live regardless.</param>
    public DeckViewModel(
        int slot,
        IPerformanceActionDispatcher? dispatcher = null,
        IWaveformProvider? waveformProvider = null,
        Func<string, DeckTrackInfo?>? trackInfo = null,
        Func<string, BpmResult?>? analysisInfo = null,
        double waveformZoomSeconds = VisualsSettings.DefaultZoomSeconds,
        double nudgeSeconds = VisualsSettings.DefaultNudgeSeconds,
        bool deckTransportEnabled = true,
        IAutoCueService? autoCueService = null,
        Func<string, CancellationToken, Task<BpmResult?>>? bpmAnalysis = null)
    {
        _slot = slot;
        _dispatcher = dispatcher;
        _waveformProvider = waveformProvider;
        _trackInfo = trackInfo;
        _analysisInfo = analysisInfo;
        _autoCueService = autoCueService;
        _bpmAnalysis = bpmAnalysis;
        _zoomSeconds = ClampZoomSeconds(waveformZoomSeconds);
        _nudgeSeconds = ClampNudgeSeconds(nudgeSeconds);
        DeckId = slot == 0 ? "A" : "B";
        bool dispatcherPresent = dispatcher is not null;
        // Deck transport/hot-cue/pitch/grid actions are owned ONLY by DeckActionHandler, which is absent in
        // catalog-browser mode (no realtime audio): a present dispatcher is NOT proof those kinds are handled.
        // Gate them on deckTransportEnabled so they render DISABLED — rather than enabled-but-silently-dropped
        // (QA finding S1) — when no deck engine backs the slot. EQ/filter stay on dispatcherPresent (the mixer
        // handler is always registered, so they route in every mode).
        _transportEnabled = deckTransportEnabled && dispatcherPresent;
        IObservable<bool> canEmit = Observable.Return(_transportEnabled);

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
                PerformanceActionKind.DeckSetLoop, ActionInputMode.Absolute, Value: _loopBeats, Slot: slot)),
            canEmit);
        _isLooping = _dispatcher?.GetFeedback(PerformanceActionKind.DeckSetLoop, slot).IsActive ?? false;

        // Loop release: emit DeckSetLoop with a non-positive beat length, which the engine handler maps
        // to ClearLoop (doc 11). The on-screen LOOP key only ever arms a loop, so this is the deck's only
        // way to exit one from the UI.
        ExitLoopCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckSetLoop, ActionInputMode.Absolute, Value: 0.0, Slot: slot)),
            canEmit);

        // Sync latch: each press toggles the engine's CONTINUOUS phase-lock loop for this deck via
        // DeckSyncToggle, and the button follows the handler's feedback (the LED model) exactly like
        // KEY LOCK. The lock state (Active/Locked/Drifting/OutOfRange) rides on the same feedback.
        // The one-shot DeckSyncOnce stays available to MIDI mappings; the on-screen key is the latch.
        SyncCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckSyncToggle, Slot: slot)),
            canEmit);
        if (_dispatcher?.GetFeedback(PerformanceActionKind.DeckSyncToggle, slot)
            is { IsAvailable: true } syncSeed)
            ApplySyncFeedback(syncSeed);

        // Key-lock (master tempo) toggle: holds the musical key constant while the tempo/pitch fader moves.
        // The VM emits the toggle action and follows the DeckKeyLockToggle active-state feedback (the LED
        // model) exactly like LOOP, so it lights up once the engine reports key-lock is engaged for this deck.
        KeyLockCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckKeyLockToggle, Slot: slot)),
            canEmit);
        _isKeyLock = _dispatcher?.GetFeedback(PerformanceActionKind.DeckKeyLockToggle, slot).IsActive ?? false;

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

        // NUDGE pitch-bend (the platter-push): each tap momentarily bends the deck's rate to slide its
        // phase for manual beat-matching, with NO position skip, then restores on the timer below. ◄ slows
        // (drift back), ► speeds up (drift forward).
        _pitchBendRestore = new DispatcherTimer { Interval = PitchBendWindow };
        _pitchBendRestore.Tick += (_, _) =>
        {
            _pitchBendRestore.Stop();
            _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckPitchBend, ActionInputMode.Absolute, Value: 0, Slot: slot));
        };
        NudgeBendDownCommand = ReactiveCommand.Create(() => PitchBendTap(-PitchBendFraction), canEmit);
        NudgeBendUpCommand = ReactiveCommand.Create(() => PitchBendTap(+PitchBendFraction), canEmit);

        // Grid edit: slide a beat line onto the kick under the playhead
        // (re-phase the BEAT GRID) via DeckSetFirstBeat. Changes the analyzed grid/sync phase only — never
        // the audible pitch — and is a no-op until the track's tempo/duration are known.
        SetGridHereCommand = ReactiveCommand.Create(EmitGridHere, canEmit);
        // Manual beatgrid edit (commercial-parity kick-on-kick): every command reuses an existing grid
        // action and never touches the audible pitch. Nudge re-phases the first beat in fine steps; ½/×2
        // correct a wrong DETECTED tempo; SET-1 marks the down-beat at the playhead.
        NudgeGridBackCommand = ReactiveCommand.Create(() => EmitFirstBeat(-GridNudgeStep), canEmit);
        NudgeGridForwardCommand = ReactiveCommand.Create(() => EmitFirstBeat(+GridNudgeStep), canEmit);
        HalveGridBpmCommand = ReactiveCommand.Create(() => EmitGridBpm(0.5), canEmit);
        DoubleGridBpmCommand = ReactiveCommand.Create(() => EmitGridBpm(2.0), canEmit);
        SetDownbeatHereCommand = ReactiveCommand.Create(EmitDownbeatHere, canEmit);

        var hotCues = new HotCuePadViewModel[HotCueCount];
        for (int index = 0; index < HotCueCount; index++)
        {
            int cueIndex = index; // capture per-pad
            hotCues[index] = new HotCuePadViewModel(
                cueIndex,
                _transportEnabled
                    ? () => _dispatcher?.Dispatch(new PerformanceAction(
                        PerformanceActionKind.DeckHotCue, Slot: slot, Argument: cueIndex.ToString()))
                    : null);
        }
        HotCues = hotCues;
        // Bank B (slots 4–7) holds the auto-cue phrase/outro points; the toggle flips the visible 4 pads
        // so every stored cue is reachable live, not just the first four (audit finding #2).
        ToggleHotCueBankCommand = ReactiveCommand.Create(() => { IsHotCueBankB = !IsHotCueBankB; });

        // AUTO-CUE: analyze the loaded track and apply its suggested hot cues live (doc 11/16). Available
        // only when a decoder-backed auto-cue service is wired, the deck transport is live, AND a track is
        // loaded — so the button isn't a dead control that silently no-ops on an empty deck (same gate the
        // other transport controls use). The canExecute tracks HasLoadedTrack so it follows load/unload.
        AutoCueCommand = ReactiveCommand.CreateFromTask(
            RunAutoCueAsync,
            this.WhenAnyValue(x => x.HasLoadedTrack)
                .Select(loaded => loaded && _transportEnabled && _autoCueService is not null));

        // EQ/filter emit Mixer* actions, owned by the always-present MixerActionHandler — usable whenever a
        // dispatcher exists, even in catalog-browser mode (unlike the deck-transport controls above).
        EqHigh = new ContinuousControlViewModel("Hi", EqBands_Unity, dispatcherPresent ? v => EmitEq("High", v) : null);
        EqMid = new ContinuousControlViewModel("Mid", EqBands_Unity, dispatcherPresent ? v => EmitEq("Mid", v) : null);
        EqLow = new ContinuousControlViewModel("Low", EqBands_Unity, dispatcherPresent ? v => EmitEq("Low", v) : null);
        Filter = new ContinuousControlViewModel(
            "Flt", Seed(PerformanceActionKind.MixerFilter, FilterCentre),
            dispatcherPresent ? v => Emit(PerformanceActionKind.MixerFilter, v) : null);

        // Channel-strip EQ RESET (the small button above the EQ knobs): snaps this channel's three tone
        // bands back to flat. Gated on the same dispatcher presence as the EQ knobs themselves, so the
        // button disables in catalog-browser mode exactly like the knobs it resets.
        ResetEqCommand = ReactiveCommand.Create(ResetEq, Observable.Return(dispatcherPresent));

        // Pitch fader emits DeckPitch (deck-handler-owned), so it follows the transport gate, not the mixer one.
        Pitch = new ContinuousControlViewModel(
            "Pitch", Seed(PerformanceActionKind.DeckPitch, PitchCentre),
            _transportEnabled ? v => Emit(PerformanceActionKind.DeckPitch, v) : null);

        // Signed pitch-percent readout: the normalized fader (0..1, centre 0.5) maps to the engine's real
        // tempo range of +-PitchRangePercent, so the displayed percent = (Value - 0.5) * 2 * 100 * range.
        // Tracks the same Pitch.Value the fader/feedback drive, so a controller move updates the readout too.
        _pitchPercentText = this
            .WhenAnyValue(deck => deck.Pitch.Value)
            .Select(FormatPitchPercent)
            .ToProperty(this, nameof(PitchPercentText), FormatPitchPercent(Pitch.Value));

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
            // A previously-set downbeat ("one") for this slot overrides the auto-resolved one OnTrackLoaded
            // just derived (mirrors the first-beat read above), so re-entering the DJ tab keeps a manual edit.
            ActionFeedbackState downbeat =
                _dispatcher.GetFeedback(PerformanceActionKind.DeckSetDownbeat, slot);
            if (downbeat.IsAvailable && downbeat.Value != 0)
                _downbeatSeconds = downbeat.Value;
        }

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    // EqBands.Unity (0.5) = flat; MixerMath maps 0..1 to boost/cut. Filter/pitch centre likewise.
    private const double EqBands_Unity = 0.5;
    private const double FilterCentre = 0.5;
    private const double PitchCentre = 0.5;

    /// <summary>The +-fraction of the real tempo the pitch fader spans at its extremes (0.5 +- this).
    /// Mirrors <c>TwoDeckBassEngine.PitchRangePercent</c> (0.08 = +-8%); the engine owns the audible rate,
    /// this is only for the on-screen readout. Keep the two in sync if the engine range changes.</summary>
    private const double PitchRangePercent = 0.08;

    /// <summary>Default loop length emitted by the LOOP button, in beats (a 1-bar loop in 4/4).</summary>
    private const double DefaultLoopBeats = 4.0;

    // Selectable auto-loop lengths in BEATS: 1/64 up to 32 (= 8 bars in 4/4). LOOP arms the selected one.
    private static readonly double[] LoopLengthsBeats =
        { 1 / 64.0, 1 / 32.0, 1 / 16.0, 1 / 8.0, 1 / 4.0, 1 / 2.0, 1, 2, 4, 8, 16, 32 };
    private double _loopBeats = DefaultLoopBeats;
    private double _loopLengthKnob = (double)ClosestLoopIndex(DefaultLoopBeats) / (LoopLengthsBeats.Length - 1);

    /// <summary>Deck label, "A" or "B".</summary>
    public string DeckId { get; }

    /// <summary>The loaded track's name, or the no-track placeholder.</summary>
    public string Title
    {
        get => _title;
        private set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    /// <summary>The loaded track's artist, from the catalog facts; null when unknown (or no track).
    /// Shown under the title on both decks.</summary>
    public string? Artist
    {
        get => _artist;
        private set
        {
            this.RaiseAndSetIfChanged(ref _artist, value);
            this.RaisePropertyChanged(nameof(HasArtist));
        }
    }

    /// <summary>True when an artist is known for the loaded track (drives the artist line's visibility).</summary>
    public bool HasArtist => !string.IsNullOrWhiteSpace(_artist);

    /// <summary>Deck meta line — "Key · BPM · duration" from the catalog, or "—" before a track loads.</summary>
    public string Meta
    {
        get => _meta;
        private set => this.RaiseAndSetIfChanged(ref _meta, value);
    }

    /// <summary>True once a track with known catalog facts is loaded (drives the meta line's visibility).</summary>
    public bool HasTrackMeta => _meta != NoMeta;

    private const string NoMeta = "—";

    /// <summary>The loaded track's musical key (e.g. "8A"), from the catalog facts; null when no track is
    /// loaded or the catalog has no key for it. Surfaced as its own labelled readout on both decks.</summary>
    public string? TrackKey
    {
        get => _trackKey;
        private set
        {
            this.RaiseAndSetIfChanged(ref _trackKey, value);
            this.RaisePropertyChanged(nameof(HasTrackKey));
        }
    }

    /// <summary>True when a musical key is known for the loaded track (drives the key readout's visibility).</summary>
    public bool HasTrackKey => !string.IsNullOrWhiteSpace(_trackKey);

    /// <summary>The current pitch offset as a signed percentage of the real tempo range (e.g. "+2.4%"),
    /// derived from <see cref="Pitch"/>.Value and <see cref="PitchRangePercent"/>. Display only.</summary>
    public string PitchPercentText => _pitchPercentText.Value;

    // Map the 0..1 fader (centre 0.5) to a signed percentage of the engine's +-PitchRangePercent range.
    // Centre -> "0.0%", full up -> "+8.0%", full down -> "-8.0%" (with the default +-8% range). The value
    // is kept as a fraction (0.08 at full up) so the "%" format specifier scales it by 100 for display.
    private static string FormatPitchPercent(double normalized)
    {
        double fraction = (normalized - PitchCentre) * 2.0 * PitchRangePercent;
        return fraction.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture);
    }

    /// <summary>The loaded track's waveform peaks (0..1), or null when none is decoded (placeholder).</summary>
    public IReadOnlyList<float>? Waveform
    {
        get => _waveform;
        private set => this.RaiseAndSetIfChanged(ref _waveform, value);
    }

    /// <summary>The loaded track's low-frequency (kick) band peaks (0..1), aligned 1:1 with
    /// <see cref="Waveform"/>; null when none is decoded. The strip draws these as the FRONT layer,
    /// bright, so the kick transients are visible for beat-sync alignment.</summary>
    public IReadOnlyList<float>? KickPeaks
    {
        get => _kickPeaks;
        private set => this.RaiseAndSetIfChanged(ref _kickPeaks, value);
    }

    /// <summary>The mid band peaks (0..1), aligned 1:1 with <see cref="Waveform"/>; null when none is
    /// decoded. With <see cref="HighPeaks"/> they drive the strip's layered 3-band render (the body).</summary>
    public IReadOnlyList<float>? MidPeaks
    {
        get => _midPeaks;
        private set => this.RaiseAndSetIfChanged(ref _midPeaks, value);
    }

    /// <summary>The high band peaks (0..1), aligned 1:1 with <see cref="Waveform"/>; null when none is
    /// decoded. Drawn as pale caps behind the body.</summary>
    public IReadOnlyList<float>? HighPeaks
    {
        get => _highPeaks;
        private set => this.RaiseAndSetIfChanged(ref _highPeaks, value);
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
    /// Which beat of the bar the <see cref="BeatGrid"/> starts on (0..3 for 4/4): the strip marks comb line
    /// <c>i</c> as a bar downbeat when <c>((i - DownbeatBarOffset) mod 4) == 0</c>, so the red bars sit on the
    /// analyzed/edited downbeat (the "one") rather than on an arbitrary beat. 0 until a downbeat is known.
    /// </summary>
    public int DownbeatBarOffset
    {
        get => _downbeatBarOffset;
        private set => this.RaiseAndSetIfChanged(ref _downbeatBarOffset, value);
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
        private set
        {
            this.RaiseAndSetIfChanged(ref _progress, value);
            UpdateTimeTexts();
        }
    }

    /// <summary>Time elapsed in the loaded track ("m:ss"), or the placeholder until the duration decodes.</summary>
    public string ElapsedText
    {
        get => _elapsedText;
        private set => this.RaiseAndSetIfChanged(ref _elapsedText, value);
    }

    /// <summary>Time remaining in the loaded track ("-m:ss"), or the placeholder until the duration decodes.</summary>
    public string RemainingText
    {
        get => _remainingText;
        private set => this.RaiseAndSetIfChanged(ref _remainingText, value);
    }

    private const string NoTime = "--:--";

    // Elapsed/remaining derive from the playhead fraction × the decoded duration; recomputed on every
    // playhead/seek update and when the duration becomes known. Unknown duration → placeholders, so the
    // readout never shows a guessed time.
    private void UpdateTimeTexts()
    {
        if (_durationSeconds <= 0.0)
        {
            ElapsedText = NoTime;
            RemainingText = NoTime;
            return;
        }

        double elapsed = Math.Clamp(_progress, 0.0, 1.0) * _durationSeconds;
        ElapsedText = FormatTime(elapsed);
        RemainingText = "-" + FormatTime(Math.Max(0.0, _durationSeconds - elapsed));
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1.0
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    /// <summary>True when the deck-transport controls (play/cue/sync/loop/hot-cue/pitch/grid) can be driven;
    /// the UI disables them otherwise. False in catalog-browser mode where no deck engine backs the slot, so
    /// those actions would be silently dropped — the mixer EQ/filter knobs stay live independently.</summary>
    public bool IsEnabled => _transportEnabled;

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
    /// True while this deck's audible BPM is beatmatched to the other deck's (both playing, within
    /// <see cref="Liveolator.Core.Beat.BpmMatch.DefaultToleranceBpm"/>). Drives the green "matched"
    /// highlight on the BPM readout. Set by <see cref="PerformanceDeckSet"/>, which owns both decks.
    /// </summary>
    public bool IsBpmMatched
    {
        get => _isBpmMatched;
        private set => this.RaiseAndSetIfChanged(ref _isBpmMatched, value);
    }

    /// <summary>Applies the cross-deck beatmatch result computed by <see cref="PerformanceDeckSet"/>.</summary>
    internal void SetBpmMatched(bool matched) => IsBpmMatched = matched;

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
        private set
        {
            if (_isBpmEnabled == value)
                return;
            this.RaiseAndSetIfChanged(ref _isBpmEnabled, value);
            // SYNC needs this deck's analyzed tempo to beatmatch against, so its availability tracks the
            // BPM's: an empty / un-analyzed deck shows SYNC disabled instead of as a dead button (the
            // owner's "SYNC does nothing" report — there was no base BPM to match).
            this.RaisePropertyChanged(nameof(CanSync));
            this.RaisePropertyChanged(nameof(CanGridEdit));
        }
    }

    /// <summary>True once a track has successfully loaded onto this deck (from <see
    /// cref="PerformanceActionKind.DeckLoadTrack"/> feedback, which the handler raises only after the
    /// engine load succeeds). Gates the transport controls so they disable on an empty deck — and, crucially,
    /// stay disabled when a load FAILED (a failed load never raises the feedback), instead of presenting
    /// dead buttons that silently do nothing.</summary>
    public bool HasLoadedTrack
    {
        get => _hasLoadedTrack;
        private set
        {
            if (_hasLoadedTrack == value)
                return;
            this.RaiseAndSetIfChanged(ref _hasLoadedTrack, value);
            this.RaisePropertyChanged(nameof(CanCue));
            this.RaisePropertyChanged(nameof(CanLoop));
            this.RaisePropertyChanged(nameof(CanHotCue));
            this.RaisePropertyChanged(nameof(CanNudgeSeek));
            this.RaisePropertyChanged(nameof(CanPitchBend));
            this.RaisePropertyChanged(nameof(CanAutoCue));
        }
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

        // Sync-lock state (Active→Locked→Drifting) now arrives by push: the engine raises SyncStateChanged
        // on every transition and the handler re-emits DeckSyncToggle feedback, handled in OnFeedback. No
        // per-tick poll (which took the audio _gate 3x and allocated per frame) is needed here.
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
    {
        // Which grid line carries the red bar marker — folds the downbeat (bar phase) against the grid's
        // first-beat anchor (beat phase). Set BEFORE the grid so any consumer reacting to the BeatGrid change
        // sees the matching offset already in place (they are one logical update).
        DownbeatBarOffset = BeatGridCalculator.DownbeatBarOffset(_trackBpm, _firstBeatSeconds, _downbeatSeconds);
        BeatGrid = _durationSeconds > 0
            ? BeatGridCalculator.BeatFractions(_trackBpm, _durationSeconds, _firstBeatSeconds)
            : Array.Empty<double>();
    }

    // Slide the grid so a beat line lands on the kick under the playhead: emit DeckSetFirstBeat with the
    // within-beat anchor derived from the current position. No-op until tempo + duration are known.
    private void EmitGridHere()
    {
        if (_trackBpm <= 0 || _durationSeconds <= 0)
            return;
        double anchor = GridAnchorAtPlayhead(_progress, _durationSeconds, _trackBpm);
        _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat, ActionInputMode.Absolute, Value: anchor, Slot: _slot));
    }

    private static int ClosestLoopIndex(double beats)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < LoopLengthsBeats.Length; i++)
        {
            double d = Math.Abs(LoopLengthsBeats[i] - beats);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>A loop length (beats) as a deck label: "1/8", "2", "1 BAR", "8 BAR" (4 beats = 1 bar). Pure.</summary>
    public static string FormatLoopLength(double beats)
    {
        if (beats <= 0)
            return "—";
        if (beats < 1)
            return $"1/{(int)Math.Round(1.0 / beats)}";
        if (beats < 4)
            return ((int)beats).ToString();
        return $"{(int)Math.Round(beats / 4.0)} BAR";
    }

    // Re-phase the grid by a small +/- step (reuses DeckSetFirstBeat). No-op before a track's tempo is known.
    private void EmitFirstBeat(double deltaSeconds)
    {
        if (_trackBpm <= 0 || _durationSeconds <= 0)
            return;
        double anchor = NudgedFirstBeat(_firstBeatSeconds, deltaSeconds, _trackBpm);
        _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat, ActionInputMode.Absolute, Value: anchor, Slot: _slot));
    }

    // Halve/double the GRID tempo (reuses DeckSetGridBpm) for a wrong detected octave — pitch is untouched.
    private void EmitGridBpm(double factor)
    {
        if (_trackBpm <= 0)
            return;
        _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetGridBpm, ActionInputMode.Absolute, Value: _trackBpm * factor, Slot: _slot));
    }

    // Mark the down-beat (bar 1) at the current playhead (reuses DeckSetDownbeat). Display/grid-only.
    private void EmitDownbeatHere()
    {
        if (_trackBpm <= 0 || _durationSeconds <= 0)
            return;
        double t = Math.Clamp(_progress, 0.0, 1.0) * _durationSeconds;
        _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetDownbeat, ActionInputMode.Absolute, Value: t, Slot: _slot));
    }

    /// <summary>
    /// The first-beat anchor after shifting it by <paramref name="deltaSeconds"/>, folded back into
    /// [0, 60/bpm). Pure so the nudge math unit-tests without a VM.
    /// </summary>
    public static double NudgedFirstBeat(double currentFirstBeat, double deltaSeconds, double bpm)
    {
        if (bpm <= 0)
            return currentFirstBeat;
        double beat = 60.0 / bpm;
        double v = (currentFirstBeat + deltaSeconds) % beat;
        return v < 0 ? v + beat : v;
    }

    /// <summary>
    /// The within-beat first-beat anchor (seconds, in [0, 60/bpm)) that puts a beat line on the playhead:
    /// the playhead time folded into one beat. Pure so the "set grid here" math unit-tests without a VM.
    /// </summary>
    public static double GridAnchorAtPlayhead(double progress, double durationSeconds, double bpm)
    {
        if (bpm <= 0 || durationSeconds <= 0 || double.IsNaN(progress))
            return 0.0;
        double beatSeconds = 60.0 / bpm;
        double t = Math.Clamp(progress, 0.0, 1.0) * durationSeconds;
        double anchor = t % beatSeconds;
        return anchor < 0 ? anchor + beatSeconds : anchor;
    }

    /// <summary>True while this deck has an active loop (drives the LOOP key's active state), from feedback.</summary>
    public bool IsLooping
    {
        get => _isLooping;
        private set => this.RaiseAndSetIfChanged(ref _isLooping, value);
    }

    /// <summary>True while key-lock (master tempo) is engaged for this deck (drives the KEY LOCK key's
    /// active state), from <see cref="PerformanceActionKind.DeckKeyLockToggle"/> feedback.</summary>
    public bool IsKeyLock
    {
        get => _isKeyLock;
        private set => this.RaiseAndSetIfChanged(ref _isKeyLock, value);
    }

    /// <summary>True while the continuous sync latch is engaged for this deck (drives the SYNC key's
    /// active state), from <see cref="PerformanceActionKind.DeckSyncToggle"/> feedback.</summary>
    public bool IsSyncEngaged
    {
        get => _isSyncEngaged;
        private set => this.RaiseAndSetIfChanged(ref _isSyncEngaged, value);
    }

    /// <summary>The engine's live phase-lock state for this deck (Off / Active / Locked / Drifting /
    /// OutOfRange), parsed from the <see cref="PerformanceActionKind.DeckSyncToggle"/> feedback and
    /// refreshed by <see cref="UpdatePlayhead"/> while the latch is engaged (the engine loop moves it
    /// without dispatching any action). Drives the SYNC key's label, tooltip, and indicator classes.</summary>
    public SyncLockState SyncState
    {
        get => _syncState;
        private set
        {
            if (_syncState == value)
                return;
            this.RaiseAndSetIfChanged(ref _syncState, value);
            this.RaisePropertyChanged(nameof(IsSyncLocked));
            this.RaisePropertyChanged(nameof(IsSyncSettling));
            this.RaisePropertyChanged(nameof(IsSyncOutOfRange));
            this.RaisePropertyChanged(nameof(SyncStateLabel));
            this.RaisePropertyChanged(nameof(SyncStateTip));
        }
    }

    /// <summary>Beat-locked to the master within tolerance.</summary>
    public bool IsSyncLocked => _syncState == SyncLockState.Locked;

    /// <summary>Engaged but still pulling into lock (Active) or recovering from a slip (Drifting).</summary>
    public bool IsSyncSettling => _syncState is SyncLockState.Active or SyncLockState.Drifting;

    /// <summary>Engaged but the tempo gap is too wide to beatmatch — the deck holds its own tempo.</summary>
    public bool IsSyncOutOfRange => _syncState == SyncLockState.OutOfRange;

    /// <summary>The SYNC key's face text — the lock state is carried in text, never color alone.</summary>
    public string SyncStateLabel => _syncState switch
    {
        SyncLockState.Locked => "LOCKED",
        SyncLockState.Active or SyncLockState.Drifting => "SYNC…",
        SyncLockState.OutOfRange => "SYNC ⚠",
        _ => "SYNC",
    };

    /// <summary>The SYNC key's tooltip, spelling the current lock state out in words.</summary>
    public string SyncStateTip => _syncState switch
    {
        SyncLockState.Locked => "Sync lock: beat-locked to the other deck",
        SyncLockState.Active or SyncLockState.Drifting => "Sync lock engaged — pulling into beat lock",
        SyncLockState.OutOfRange => "Can't sync — tempo gap too wide for the sync range",
        _ => "Sync lock: continuously match tempo + phase to the other deck",
    };

    // The handler's SyncFeedback: IsActive = latch engaged, Value = (double)SyncLockState ordinal. Read the
    // ordinal directly (it arrived numerically); fall back to the engaged flag if it is ever out of range,
    // so a malformed echo can't mislight the indicator.
    private void ApplySyncFeedback(ActionFeedbackState state)
    {
        IsSyncEngaged = state.IsActive;
        var ordinal = (SyncLockState)(int)state.Value;
        SyncState = Enum.IsDefined(ordinal) ? ordinal
            : state.IsActive ? SyncLockState.Active : SyncLockState.Off;
    }

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> CueCommand { get; }
    public ReactiveCommand<Unit, Unit> LoopCommand { get; }

    /// <summary>Releases (clears) any active loop on this deck — the flank's loop-exit key. Emits
    /// <see cref="PerformanceActionKind.DeckSetLoop"/> with a non-positive beat length (engine → ClearLoop).</summary>
    public ReactiveCommand<Unit, Unit> ExitLoopCommand { get; }

    public ReactiveCommand<Unit, Unit> SyncCommand { get; }

    /// <summary>Analyzes the loaded track and applies its auto hot cues live (the deck AUTO-CUE button).</summary>
    public ReactiveCommand<Unit, Unit> AutoCueCommand { get; }

    /// <summary>True when AUTO-CUE is available (a decoder-backed auto-cue service is wired, the deck
    /// transport is live, AND a track is loaded); the view hides the button otherwise so it's never a dead
    /// control on an empty deck.</summary>
    public bool CanAutoCue => _autoCueService is not null && _transportEnabled && _hasLoadedTrack;
    /// <summary>Toggles key-lock (master tempo) for this deck via <see cref="PerformanceActionKind.DeckKeyLockToggle"/>.</summary>
    public ReactiveCommand<Unit, Unit> KeyLockCommand { get; }
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

    /// <summary>NUDGE ◄ — momentary pitch-bend DOWN: briefly slows the deck so its beats drift back, to
    /// manually beat-match without skipping the playhead (the platter-push). Restores on its own.</summary>
    public ReactiveCommand<Unit, Unit> NudgeBendDownCommand { get; }

    /// <summary>NUDGE ► — momentary pitch-bend UP: briefly speeds the deck so its beats drift forward.</summary>
    public ReactiveCommand<Unit, Unit> NudgeBendUpCommand { get; }

    /// <summary>True when pitch-bend can be emitted (the realtime engine is wired and a track is loaded).</summary>
    public bool CanPitchBend => IsEnabled && _hasLoadedTrack;

    /// <summary>Grid edit: slide the grid so a beat line lands on the kick under the playhead (sets the
    /// within-beat first-beat anchor from the current position).</summary>
    /// <summary>The armed loop length as a deck label (e.g. "1/8", "1 BAR", "8 BAR"); shown on the LOOP key.</summary>
    public string LoopLengthLabel => FormatLoopLength(_loopBeats);

    /// <summary>
    /// The loop-length KNOB position (0..1, two-way). The knob carries 12 detents (1/64 … 8 bars); turning
    /// it selects the armed length the LOOP key uses. Pure UI state — an active loop is not resized underfoot.
    /// </summary>
    public double LoopLengthKnob
    {
        get => _loopLengthKnob;
        set
        {
            double v = Math.Clamp(value, 0.0, 1.0);
            this.RaiseAndSetIfChanged(ref _loopLengthKnob, v);
            int i = Math.Clamp((int)Math.Round(v * (LoopLengthsBeats.Length - 1)), 0, LoopLengthsBeats.Length - 1);
            if (_loopBeats != LoopLengthsBeats[i])
            {
                _loopBeats = LoopLengthsBeats[i];
                this.RaisePropertyChanged(nameof(LoopLengthLabel));
            }
        }
    }
    public ReactiveCommand<Unit, Unit> SetGridHereCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeGridBackCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeGridForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> HalveGridBpmCommand { get; }
    public ReactiveCommand<Unit, Unit> DoubleGridBpmCommand { get; }
    public ReactiveCommand<Unit, Unit> SetDownbeatHereCommand { get; }

    /// <summary>All eight hot-cue pads (two banks of four). Indexed by absolute cue index for feedback.</summary>
    public IReadOnlyList<HotCuePadViewModel> HotCues { get; }

    /// <summary>The four pads of the currently selected bank (A = slots 0–3, B = slots 4–7).</summary>
    public IReadOnlyList<HotCuePadViewModel> VisibleHotCues =>
        new ArraySegment<HotCuePadViewModel>(
            (HotCuePadViewModel[])HotCues, (_isHotCueBankB ? 1 : 0) * HotCuesPerBank, HotCuesPerBank);

    /// <summary>True when bank B (slots 4–7) is shown; false for bank A (slots 0–3).</summary>
    public bool IsHotCueBankB
    {
        get => _isHotCueBankB;
        set
        {
            this.RaiseAndSetIfChanged(ref _isHotCueBankB, value);
            this.RaisePropertyChanged(nameof(VisibleHotCues));
            this.RaisePropertyChanged(nameof(HotCueBankLabel));
        }
    }

    /// <summary>The active bank's letter for the toggle face ("A"/"B").</summary>
    public string HotCueBankLabel => _isHotCueBankB ? "B" : "A";

    /// <summary>Flips between hot-cue bank A and bank B.</summary>
    public ReactiveCommand<Unit, Unit> ToggleHotCueBankCommand { get; }

    public ContinuousControlViewModel EqHigh { get; }
    public ContinuousControlViewModel EqMid { get; }
    public ContinuousControlViewModel EqLow { get; }
    public ContinuousControlViewModel Filter { get; }
    public ContinuousControlViewModel Pitch { get; }

    /// <summary>Resets this channel's three EQ bands (HI/MID/LOW) to flat — the RESET button seated above
    /// the channel-strip EQ knobs. The filter knob is intentionally left untouched.</summary>
    public ReactiveCommand<Unit, Unit> ResetEqCommand { get; }

    /// <summary>Cue/Loop/Hot-cues/Nudge are drivable when a deck engine backs this view AND a track has
    /// loaded (doc 11). Gating on the loaded track keeps these from being dead buttons on an empty or
    /// failed-to-load deck — pressing them on nothing was the owner's "the button does nothing" report.</summary>
    public bool CanCue => IsEnabled && _hasLoadedTrack;
    public bool CanLoop => IsEnabled && _hasLoadedTrack;
    public bool CanHotCue => IsEnabled && _hasLoadedTrack;
    /// <summary>SYNC additionally needs this deck's analyzed tempo (<see cref="IsBpmEnabled"/>): with no
    /// base BPM there is nothing to beatmatch, so SYNC stays disabled rather than silently no-op.</summary>
    public bool CanSync => IsEnabled && IsBpmEnabled;
    /// <summary>Manual grid edit needs a loaded, analyzed track (a tempo to re-phase), same gate as SYNC.</summary>
    public bool CanGridEdit => IsEnabled && IsBpmEnabled;
    public bool CanNudgeSeek => IsEnabled && _hasLoadedTrack;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        CancelBackgroundBpmAnalysis();
        _pitchPercentText.Dispose();
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

    // Setting each knob's Value re-emits its MixerEqBand only when the band actually moved (the
    // ContinuousControlViewModel emit guard), so a reset of an already-flat channel dispatches nothing.
    private void ResetEq()
    {
        EqHigh.Value = EqBands_Unity;
        EqMid.Value = EqBands_Unity;
        EqLow.Value = EqBands_Unity;
    }

    private void Emit(PerformanceActionKind kind, double value)
        => _dispatcher?.Dispatch(new PerformanceAction(kind, ActionInputMode.Absolute, Value: value, Slot: _slot));

    // Shift the playhead by a signed number of seconds via a RELATIVE DeckSeek. Converts seconds to a
    // 0..1 fraction using the decoded duration; a no-op until the duration is known (engine clamps to [0,1]).
    // Emit a momentary pitch-bend and (re)arm the restore window: the deck bends its rate by bendFraction
    // now and snaps back to its normal rate when the window elapses. Rapid taps re-arm the window, so a
    // press-and-tap holds the bend; this slides the deck's phase with no position skip (manual beat-match).
    private void PitchBendTap(double bendFraction)
    {
        if (_dispatcher is null)
            return;
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckPitchBend, ActionInputMode.Absolute, Value: bendFraction, Slot: _slot));
        _pitchBendRestore.Stop();
        _pitchBendRestore.Start();
    }

    private void NudgeSeek(double seconds)
    {
        if (_dispatcher is null || _durationSeconds <= 0.0)
            return;
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSeek, ActionInputMode.Relative, Value: seconds / _durationSeconds, Slot: _slot));
    }

    // Analyze the loaded track and apply its suggested hot cues live. Decode + structural analysis is
    // CPU-bound, so it runs off the UI thread; it writes the cues to the store (preserving any manual
    // cues), then dispatches DeckApplyAutoCues so the engine reloads this deck's bank and the pads light
    // up without reloading the track. A no-op on an empty deck; a failure is logged, never thrown (#16/#26).
    private async Task RunAutoCueAsync()
    {
        string? path = _loadedTrackPath;
        if (_autoCueService is null || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            await Task.Run(() => _autoCueService.RunAsync(new[] { path! })).ConfigureAwait(false);
            _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckApplyAutoCues, Slot: _slot));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Auto-cue for deck {DeckId} failed: {ex.Message}");
        }
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
                case PerformanceActionKind.DeckKeyLockToggle:
                    IsKeyLock = e.State.IsActive;
                    break;
                case PerformanceActionKind.DeckSyncToggle:
                    ApplySyncFeedback(e.State);
                    break;
                case PerformanceActionKind.DeckHotCue:
                    UpdateHotCue(e.State);
                    break;
                case PerformanceActionKind.DeckSeek when e.State.IsAvailable:
                    Progress = e.State.Value; // playhead follows seek/cue position
                    break;
                case PerformanceActionKind.DeckLoadTrack
                    when e.State.IsAvailable && !string.IsNullOrEmpty(e.State.Argument):
                    OnTrackLoaded(e.State.Argument!, e.State.Value);
                    break;
                case PerformanceActionKind.DeckLoadTrack
                    when !e.State.IsAvailable && !string.IsNullOrEmpty(e.State.Argument):
                    // The engine reported the load FAILED (missing/offline file, or the audio engine could
                    // not create the deck stream). Show it instead of leaving a silently empty deck — and
                    // keep transport disabled so SYNC/CUE can't read as dead buttons.
                    OnTrackLoadFailed(e.State.Argument!);
                    break;
                case PerformanceActionKind.DeckSetFirstBeat:
                    // The analyzed downbeat anchor (seconds), echoed right after the load — anchor the
                    // beat/bar grid on it so the lines fall on the kicks (and match what Sync aligns to).
                    // Ignore a stale 0 from a no-analysis restore so it can't wipe a catalog/auto anchor
                    // (the self-heal in OnTrackLoaded may have already set a real one).
                    if (e.State.Value != 0 || _firstBeatSeconds == 0)
                    {
                        _firstBeatSeconds = e.State.Value;
                        RecomputeBeatGrid();
                        this.RaisePropertyChanged(nameof(KickAnchorFraction));
                    }
                    break;
                case PerformanceActionKind.DeckSetDownbeat:
                    // The bar-1 ("one") anchor in seconds: a manual SET ONE (or a session restore re-applying
                    // one) overrides the auto-resolved downbeat. A reset to 0 (no-analysis re-load) must not
                    // erase a real anchor, so only apply a non-zero value or the very first 0.
                    if (e.State.Value != 0 || _downbeatSeconds == 0)
                    {
                        _downbeatSeconds = e.State.Value;
                        RecomputeBeatGrid();
                    }
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

    // The cue index + display metadata (label/color/auto) ride encoded in the feedback Argument (the deck
    // is addressed by slot); update the matching pad's lit state and its label/color so it can show the
    // cue's name. A missing/unparseable Argument is ignored — never throw on a feedback echo.
    private void UpdateHotCue(ActionFeedbackState state)
    {
        if (!HotCueFeedback.TryDecode(state.Argument, out int index, out HotCueInfo info)
            || index < 0 || index >= HotCues.Count)
            return;
        // The lit state is the feedback's IsActive (an unset slot relights as not-lit); the label/color/auto
        // come from the decoded cue metadata.
        HotCues[index].SetState(state.IsActive, info.Label, info.Color, info.IsAuto);
    }

    // A load that the engine could not complete: present a clear failure on the deck and keep the
    // transport disabled (no playable track), so the controls can't read as broken (global #26).
    private void OnTrackLoadFailed(string trackPath)
    {
        _loadedTrackPath = null;
        CancelBackgroundBpmAnalysis(); // nothing playable to grid — a late result must not re-enable SYNC
        HasLoadedTrack = false;     // there is no playable track — transport stays disabled
        IsBpmEnabled = false;       // and SYNC stays disabled (nothing to beatmatch)
        Title = $"⚠ Couldn't load {Path.GetFileNameWithoutExtension(trackPath)}";
        Artist = null;
        Meta = NoMeta;
        this.RaisePropertyChanged(nameof(HasTrackMeta));
        TrackKey = null;
        Progress = 0;
        Waveform = null;
        KickPeaks = null;
        MidPeaks = null;
        HighPeaks = null;
        BeatGrid = Array.Empty<double>();
        _trackBpm = 0;
        _durationSeconds = 0;
        UpdateTimeTexts();
        this.RaisePropertyChanged(nameof(KickAnchorFraction));
        ClearHotCues();
    }

    private void OnTrackLoaded(string trackPath, double bpm)
    {
        _loadedTrackPath = trackPath; // the file the AUTO-CUE button analyzes
        HasLoadedTrack = true;        // a successful load arrived — enable the transport controls

        // A different track supersedes any in-flight background BPM analysis: its late result must never
        // re-grid the newly loaded track (a quick A→B→A swap would otherwise apply a stale grid).
        if (_bpmAnalysisPath is not null
            && !string.Equals(_bpmAnalysisPath, trackPath, StringComparison.OrdinalIgnoreCase))
            CancelBackgroundBpmAnalysis();

        DeckTrackInfo? info = _trackInfo?.Invoke(trackPath);
        Title = !string.IsNullOrWhiteSpace(info?.Title)
            ? info!.Title
            : Path.GetFileNameWithoutExtension(trackPath);
        // Artist comes only from the catalog facts (the load action carries no artist); null when the
        // track isn't in the catalog so the line hides rather than showing a stale artist from a prior load.
        Artist = string.IsNullOrWhiteSpace(info?.Artist) ? null : info!.Artist;
        // Prefer the full catalog facts (Key · BPM · duration); if the track isn't in the catalog, still
        // show at least the analyzed BPM that rides on the load action so a deck never hides its tempo.
        Meta = info is { } i
            ? $"{i.Key} · {i.Bpm} BPM · {i.Duration}"
            : bpm > 0 ? $"{bpm:0.0} BPM" : NoMeta;
        this.RaisePropertyChanged(nameof(HasTrackMeta));
        // Key comes only from the catalog facts (the load action carries no key); clear it when the track
        // isn't in the catalog so the dedicated readout never shows a stale key from a previous load.
        TrackKey = string.IsNullOrWhiteSpace(info?.Key) ? null : info!.Key;
        Progress = 0;
        Waveform = null;          // empty state while the new overview decodes (no fake waveform)
        KickPeaks = null;
        MidPeaks = null;
        HighPeaks = null;
        BeatGrid = Array.Empty<double>();
        _trackBpm = bpm;          // analyzed tempo from the load (0 = unknown); grid waits on the duration
        _firstBeatSeconds = 0;    // re-anchored when the DeckSetFirstBeat feedback arrives for this load
        _downbeatSeconds = 0;     // re-resolved below from analysis (or set by SET ONE / a restore)
        _durationSeconds = 0;     // unknown until the overview decodes; re-zoom then
        UpdateTimeTexts();        // back to placeholders until the new track's duration is known
        this.RaisePropertyChanged(nameof(KickAnchorFraction));
        ZoomWindow = ComputeZoomWindow();
        ClearHotCues();           // hot-cues belong to the track and clear on load (doc 18)

        BpmResult? analysis = _analysisInfo?.Invoke(trackPath);

        // Self-heal a load that arrived without analysis (e.g. a deck restored from a saved session whose
        // BPM predates analysis, or a queue load): pull the CURRENT catalog BPM + first-beat and apply them
        // through the same grid actions a manual edit uses, so the grid/BPM appear instead of staying blank.
        if (bpm <= 0 && analysis is { Bpm: > 0 })
        {
            _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckSetGridBpm, ActionInputMode.Absolute, Value: analysis.Bpm, Slot: _slot));
            if (analysis.FirstBeatSeconds > 0)
                _dispatcher?.Dispatch(new PerformanceAction(
                    PerformanceActionKind.DeckSetFirstBeat, ActionInputMode.Absolute,
                    Value: analysis.FirstBeatSeconds, Slot: _slot));
        }
        else if (bpm <= 0)
        {
            // No analysis ANYWHERE — the load carried no BPM and the catalog has none. Analyze the file in
            // the background and re-emit the same grid actions on completion, so SYNC comes alive instead
            // of staying a dead button on a real-world (uncatalogued / unanalyzed) load.
            StartBackgroundBpmAnalysis(trackPath);
        }

        // Auto-anchor the bar markers on the analyzed downbeat (the musical "one") only when the analysis is
        // confident; a low-confidence bar (four-on-the-floor is genuinely ambiguous) would just jump the red
        // bars onto a guess, so we leave them at the default (index 0) for the DJ to place with SET ONE. A
        // manual SET ONE / a restored anchor arrives later via DeckSetDownbeat feedback and overrides this.
        if (analysis is { DownbeatSeconds: > 0 } && analysis.DownbeatConfidence >= DownbeatEstimate.ConfidenceFloor)
        {
            _downbeatSeconds = analysis.DownbeatSeconds;
            // Also push it through the action seam so the engine's bar-level sync snap can engage without
            // a manual SET ONE on both decks — guarded so a restored/manual anchor always wins.
            DispatchAnalyzedDownbeat(analysis);
        }

        LoadWaveform(trackPath);
    }

    private void ClearHotCues()
    {
        foreach (HotCuePadViewModel pad in HotCues)
            pad.Clear();
    }

    // Fire-and-forget background BPM analysis at the event boundary (mirrors LoadWaveform): decode +
    // detect off the UI thread, then re-emit the SAME grid actions the catalog self-heal path uses. A
    // newer load cancels it; a failure is logged, never thrown (global standards #16/#26).
    private async void StartBackgroundBpmAnalysis(string trackPath)
    {
        if (_bpmAnalysis is null)
            return;
        // Already analyzing this very file (e.g. a duplicate load feedback) — don't decode it twice.
        if (_bpmAnalysisCts is not null
            && string.Equals(_bpmAnalysisPath, trackPath, StringComparison.OrdinalIgnoreCase))
            return;

        CancelBackgroundBpmAnalysis();
        var cts = new CancellationTokenSource();
        _bpmAnalysisCts = cts;
        _bpmAnalysisPath = trackPath;

        try
        {
            BpmResult? analysis = await Task.Run(() => _bpmAnalysis(trackPath, cts.Token), cts.Token);
            // Superseded by a newer load — a stale result must never re-grid a different track.
            if (cts.IsCancellationRequested
                || !string.Equals(_loadedTrackPath, trackPath, StringComparison.OrdinalIgnoreCase))
                return;
            if (analysis is { Bpm: > 0 })
            {
                _dispatcher?.Dispatch(new PerformanceAction(
                    PerformanceActionKind.DeckSetGridBpm, ActionInputMode.Absolute,
                    Value: analysis.Bpm, Slot: _slot, Origin: AnalysisOrigin));
                if (analysis.FirstBeatSeconds > 0)
                    _dispatcher?.Dispatch(new PerformanceAction(
                        PerformanceActionKind.DeckSetFirstBeat, ActionInputMode.Absolute,
                        Value: analysis.FirstBeatSeconds, Slot: _slot, Origin: AnalysisOrigin));
                DispatchAnalyzedDownbeat(analysis);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load — ignore.
        }
        catch (Exception ex)
        {
            // Best-effort enrichment: the deck stays grid-less (SYNC disabled) but the UI never crashes.
            System.Diagnostics.Trace.TraceWarning(
                $"Background BPM analysis of '{trackPath}' for deck {DeckId} failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_bpmAnalysisCts, cts))
            {
                _bpmAnalysisCts = null;
                _bpmAnalysisPath = null;
            }
            cts.Dispose();
        }
    }

    private void CancelBackgroundBpmAnalysis()
    {
        _bpmAnalysisCts?.Cancel();
        _bpmAnalysisCts = null; // the analysis' own finally disposes its cts
        _bpmAnalysisPath = null;
    }

    // Anchor the bars on a confident analyzed downbeat THROUGH the action seam (not just the local
    // display field) so the engine's bar-level snap sees it. Skipped when an anchor already sits in the
    // engine for this load (a load resets it to 0; a restore/manual SET ONE re-applies one afterwards):
    // the DJ's "one" must always beat the analyzer's guess.
    private void DispatchAnalyzedDownbeat(BpmResult analysis)
    {
        if (_dispatcher is null
            || analysis is not { DownbeatSeconds: > 0 }
            || analysis.DownbeatConfidence < DownbeatEstimate.ConfidenceFloor)
            return;
        if (_dispatcher.GetFeedback(PerformanceActionKind.DeckSetDownbeat, _slot).Value != 0)
            return;
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetDownbeat, ActionInputMode.Absolute,
            Value: analysis.DownbeatSeconds, Slot: _slot, Origin: AnalysisOrigin));
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
            MidPeaks = overview.IsEmpty ? null : overview.MidPeaks;
            HighPeaks = overview.IsEmpty ? null : overview.HighPeaks;
            // Now the duration is known: build the (first-beat-anchored) grid and size the zoom window in
            // real time (so the follow view shows a consistent ~PlayingZoomSeconds regardless of length).
            _durationSeconds = overview.IsEmpty ? 0 : overview.DurationSeconds;
            UpdateTimeTexts(); // the elapsed/remaining readout can resolve now
            this.RaisePropertyChanged(nameof(KickAnchorFraction));
            // Size the zoom window from the now-known duration BEFORE signalling the grid. RecomputeBeatGrid
            // raises BeatGrid, which any awaiter (e.g. the UI / tests) treats as "the load has settled"; if
            // the zoom were sized after, that observer could read a stale (pre-duration) window.
            ZoomWindow = ComputeZoomWindow();
            RecomputeBeatGrid();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load — ignore.
        }
        catch (Exception)
        {
            Waveform = null; // belt-and-braces around the await boundary
            KickPeaks = null;
            MidPeaks = null;
            HighPeaks = null;
            BeatGrid = Array.Empty<double>();
        }
    }
}

/// <summary>Pre-formatted catalog facts for a deck's loaded track (title, artist, and BPM/key/duration
/// strings). <paramref name="Artist"/> is null when the catalog has no artist for the track.</summary>
public sealed record DeckTrackInfo(string Title, string Bpm, string Key, string Duration, string? Artist = null);
