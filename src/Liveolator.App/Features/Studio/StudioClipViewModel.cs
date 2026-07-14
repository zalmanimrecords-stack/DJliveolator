using System.Collections.Generic;
using System.IO;
using System.Reactive;
using Liveolator.App.Shell;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// A clip on the STUDIO timeline: its source track + deck lane + timeline placement and source trim,
/// projected to pixels for the lane canvas (X / Width via the view's pixels-per-second zoom) and
/// carrying the lazily-loaded waveform peaks the block draws. Mutable so timeline editing (drag-move,
/// lane change, in/out trim) updates it in place; <see cref="ToClip"/> projects it back to the
/// immutable Core record for save/play/render.
/// </summary>
public sealed class StudioClipViewModel : ViewModelBase
{
    // A clip with no known source length still needs a visible width; one minute reads sensibly.
    private const double DefaultOpenLengthSeconds = 60;
    private const double MinClipSeconds = 0.1;

    private int _deckSlot;
    private double _timelineStartSeconds;
    private double _sourceInSeconds;
    private double? _sourceOutSeconds;
    private double _pixelsPerSecond;
    private bool _warpEnabled;
    private double _warpTargetBpm;
    private double _gain;
    private double _fadeInSeconds;
    private double _fadeOutSeconds;
    private IReadOnlyList<float>? _peaks;
    private IReadOnlyList<float>? _kickPeaks;
    private IReadOnlyList<float>? _midPeaks;
    private IReadOnlyList<float>? _highPeaks;

    public StudioClipViewModel(StudioClip clip, MusicTrack? track, double pixelsPerSecond)
    {
        TrackPath = clip.TrackPath;
        Track = track;
        _deckSlot = clip.DeckSlot;
        _timelineStartSeconds = clip.TimelineStartSeconds;
        _sourceInSeconds = clip.SourceIn.TotalSeconds;
        _sourceOutSeconds = clip.SourceOut?.TotalSeconds;
        _pixelsPerSecond = pixelsPerSecond;
        _warpEnabled = clip.WarpEnabled;
        _gain = clip.Gain;
        _fadeInSeconds = clip.FadeInSeconds;
        _fadeOutSeconds = clip.FadeOutSeconds;
        SourceBpm = clip.SourceBpm > 0 ? clip.SourceBpm : (track?.Bpm?.Bpm ?? 0);
        // The source bar grid (for sync-to-project-BPM). Prefer the clip's saved values; fall back to the
        // track's analyzed downbeat so clips saved before these fields existed still sync correctly.
        SourceDownbeatSeconds = clip.SourceDownbeatSeconds != 0 ? clip.SourceDownbeatSeconds : (track?.Bpm?.DownbeatSeconds ?? 0);
        SourceBeatsPerBar = clip.SourceBeatsPerBar > 0 ? clip.SourceBeatsPerBar : (track?.Bpm?.BeatsPerBar ?? 4);

        // Trim back to the full source (the clip-local "Reset trim" context action); placement is untouched.
        ResetTrimCommand = ReactiveCommand.Create(ResetTrim);
    }

    public string TrackPath { get; }
    public MusicTrack? Track { get; }
    public string Title => Track?.Title ?? Path.GetFileNameWithoutExtension(TrackPath);

    /// <summary>The source track's analyzed downbeat (beat-1 offset, seconds); 0 when unknown.</summary>
    public double SourceDownbeatSeconds { get; }

    /// <summary>The source track's meter (4 for 4/4).</summary>
    public int SourceBeatsPerBar { get; }

    // Right-click context-menu commands. The clip-local one (Reset trim) is created here; the ones that
    // touch the timeline (Sync to project BPM, Duplicate, Remove) are injected by the timeline VM, which
    // owns the lanes and the project tempo. Bound from the clip's ContextFlyout in StudioView.axaml.
    public ReactiveCommand<Unit, Unit> ResetTrimCommand { get; }
    public ReactiveCommand<Unit, Unit>? SyncToGridCommand { get; set; }
    public ReactiveCommand<Unit, Unit>? DuplicateCommand { get; set; }
    public ReactiveCommand<Unit, Unit>? RemoveCommand { get; set; }

    /// <summary>
    /// Set by the timeline VM to its undo-snapshot push; fired BEFORE a user edit to this clip's
    /// placement/trim/warp (start, source-in/out, warp toggle) so those edits are undoable. Null until
    /// the VM attaches it (e.g. isolated clip tests). Not fired for VM-driven, non-user changes
    /// (zoom/warp-target propagation), which set their own backing fields directly.
    /// </summary>
    public Action? BeforeMutation { get; set; }

    // While a code-behind timeline drag is in progress the per-move TimelineStartSeconds writes must NOT
    // each push a snapshot; the drag pushes one snapshot at its start instead (see BeginDrag/EndDrag).
    private bool _dragging;

    /// <summary>Begin a timeline drag-move: record one undo snapshot, then suppress per-move pushes.</summary>
    public void BeginDrag()
    {
        BeforeMutation?.Invoke();
        _dragging = true;
    }

    /// <summary>End a timeline drag-move (re-enable per-edit undo snapshots).</summary>
    public void EndDrag() => _dragging = false;

    // The shortest a trimmed clip may become, so an edge drag can't collapse it to nothing.
    private const double MinTrimSeconds = 0.05;

    /// <summary>
    /// Drag the clip's left (head) edge by <paramref name="timelineDeltaSeconds"/>: trims the source-in and
    /// shifts the start by the same amount of time so the rest of the clip stays anchored where it sits
    /// (a clip head-trim). The source moves by the warp factor (source seconds per timeline
    /// second). Clamped so the head can't pass the tail or go before the file start. VM-driven (one undo
    /// snapshot is taken at the drag's start), so it writes fields directly.
    /// </summary>
    public void DragStartEdge(double timelineDeltaSeconds)
    {
        double factor = WarpFactor > 0 ? WarpFactor : 1.0;
        double tail = _sourceOutSeconds ?? Track?.Duration?.TotalSeconds ?? (_sourceInSeconds + DefaultOpenLengthSeconds);
        double newIn = Math.Clamp(_sourceInSeconds + timelineDeltaSeconds * factor, 0, tail - MinTrimSeconds);

        double appliedTimelineDelta = (newIn - _sourceInSeconds) / factor;
        _sourceInSeconds = newIn;
        _timelineStartSeconds = Math.Max(0, _timelineStartSeconds + appliedTimelineDelta);
        RaiseTrimGeometry();
    }

    /// <summary>
    /// Drag the clip's right (tail) edge by <paramref name="timelineDeltaSeconds"/>: changes the source-out
    /// (the clip length); the start is untouched. Clamped so the tail can't pass the head or run past the
    /// end of the file. VM-driven (one undo snapshot at the drag's start), so it writes fields directly.
    /// </summary>
    public void DragEndEdge(double timelineDeltaSeconds)
    {
        double factor = WarpFactor > 0 ? WarpFactor : 1.0;
        double currentOut = _sourceOutSeconds ?? Track?.Duration?.TotalSeconds ?? (_sourceInSeconds + DefaultOpenLengthSeconds);
        double newOut = Math.Max(_sourceInSeconds + MinTrimSeconds, currentOut + timelineDeltaSeconds * factor);
        if (Track?.Duration?.TotalSeconds is { } trackLength)
            newOut = Math.Min(newOut, trackLength);

        _sourceOutSeconds = newOut;
        RaiseTrimGeometry();
    }

    // The clip's length along the TIMELINE (fades are timeline-domain, so they're bounded by this, not by
    // the source duration). Equals Width / PixelsPerSecond.
    private double TimelineDurationSeconds => DurationSeconds / (WarpFactor > 0 ? WarpFactor : 1.0);

    /// <summary>
    /// Drag the fade-in handle (top-left corner) by <paramref name="timelineDeltaSeconds"/>: lengthens or
    /// shortens the head fade, clamped to the clip's timeline length. VM-driven (one undo snapshot at the
    /// drag's start), so it writes the field directly.
    /// </summary>
    public void DragFadeIn(double timelineDeltaSeconds)
    {
        _fadeInSeconds = Math.Clamp(_fadeInSeconds + timelineDeltaSeconds, 0, TimelineDurationSeconds);
        this.RaisePropertyChanged(nameof(FadeInSeconds));
    }

    /// <summary>
    /// Drag the fade-out handle (top-right corner) by <paramref name="timelineDeltaSeconds"/>: lengthens or
    /// shortens the tail fade, clamped to the clip's timeline length. VM-driven (one undo snapshot at the
    /// drag's start), so it writes the field directly.
    /// </summary>
    public void DragFadeOut(double timelineDeltaSeconds)
    {
        _fadeOutSeconds = Math.Clamp(_fadeOutSeconds + timelineDeltaSeconds, 0, TimelineDurationSeconds);
        this.RaisePropertyChanged(nameof(FadeOutSeconds));
    }

    private void RaiseTrimGeometry()
    {
        this.RaisePropertyChanged(nameof(SourceInSeconds));
        this.RaisePropertyChanged(nameof(SourceOutSeconds));
        this.RaisePropertyChanged(nameof(TimelineStartSeconds));
        this.RaisePropertyChanged(nameof(DurationSeconds));
        this.RaisePropertyChanged(nameof(X));
        this.RaisePropertyChanged(nameof(Width));
        this.RaisePropertyChanged(nameof(TimelineEndSeconds));
    }

    /// <summary>The clip track's analyzed tempo (0 = unknown). Warp targets the project tempo from here.</summary>
    public double SourceBpm { get; }

    /// <summary>Time-stretch (keylock) this clip to the project tempo.</summary>
    public bool WarpEnabled
    {
        get => _warpEnabled;
        set
        {
            if (value == _warpEnabled)
                return;
            BeforeMutation?.Invoke();
            this.RaiseAndSetIfChanged(ref _warpEnabled, value);
            RaiseWarp();
        }
    }

    /// <summary>The project tempo this clip warps to (set by the timeline; drives the warp factor + width).</summary>
    public double WarpTargetBpm
    {
        get => _warpTargetBpm;
        set
        {
            this.RaiseAndSetIfChanged(ref _warpTargetBpm, value);
            RaiseWarp();
        }
    }

    /// <summary>Read-rate so the clip plays at the project tempo (1.0 when not warped / unknown source BPM).</summary>
    public double WarpFactor
        => WarpEnabled && SourceBpm > 0 && WarpTargetBpm > 0 ? WarpTargetBpm / SourceBpm : 1.0;

    /// <summary>Small badge for the clip ("♪ 120→140" when warped, else the source BPM, else blank).</summary>
    public string WarpBadge => SourceBpm <= 0
        ? string.Empty
        : WarpEnabled && WarpTargetBpm > 0
            ? $"♪ {SourceBpm:0}→{WarpTargetBpm:0}"
            : $"{SourceBpm:0} BPM";

    public int DeckSlot
    {
        get => _deckSlot;
        set => this.RaiseAndSetIfChanged(ref _deckSlot, value);
    }

    public double TimelineStartSeconds
    {
        get => _timelineStartSeconds;
        set
        {
            double clamped = System.Math.Max(0, value);
            // A direct (inspector) edit is undoable; per-move drag writes are covered by one push at
            // drag start (see BeginDrag), so they don't each snapshot.
            if (!_dragging && clamped != _timelineStartSeconds)
                BeforeMutation?.Invoke();
            this.RaiseAndSetIfChanged(ref _timelineStartSeconds, clamped);
            this.RaisePropertyChanged(nameof(X));
            this.RaisePropertyChanged(nameof(TimelineEndSeconds));
        }
    }

    public double SourceInSeconds
    {
        get => _sourceInSeconds;
        set
        {
            double clamped = System.Math.Max(0, value);
            if (clamped != _sourceInSeconds)
                BeforeMutation?.Invoke();
            this.RaiseAndSetIfChanged(ref _sourceInSeconds, clamped);
            RaiseSpan();
        }
    }

    public double? SourceOutSeconds
    {
        get => _sourceOutSeconds;
        set
        {
            if (value != _sourceOutSeconds)
                BeforeMutation?.Invoke();
            this.RaiseAndSetIfChanged(ref _sourceOutSeconds, value);
            RaiseSpan();
        }
    }

    /// <summary>Per-clip linear amplitude multiplier (1.0 = unity). Clamped non-negative; edits are undoable.</summary>
    public double Gain
    {
        get => _gain;
        set
        {
            double clamped = System.Math.Max(0, value);
            if (clamped != _gain)
                BeforeMutation?.Invoke();
            this.RaiseAndSetIfChanged(ref _gain, clamped);
        }
    }

    /// <summary>Linear fade-in ramp length at the clip head, in seconds. Clamped non-negative; edits are undoable.</summary>
    public double FadeInSeconds
    {
        get => _fadeInSeconds;
        set
        {
            double clamped = System.Math.Max(0, value);
            if (clamped != _fadeInSeconds)
                BeforeMutation?.Invoke();
            this.RaiseAndSetIfChanged(ref _fadeInSeconds, clamped);
        }
    }

    /// <summary>Linear fade-out ramp length at the clip tail, in seconds. Clamped non-negative; edits are undoable.</summary>
    public double FadeOutSeconds
    {
        get => _fadeOutSeconds;
        set
        {
            double clamped = System.Math.Max(0, value);
            if (clamped != _fadeOutSeconds)
                BeforeMutation?.Invoke();
            this.RaiseAndSetIfChanged(ref _fadeOutSeconds, clamped);
        }
    }

    /// <summary>The trimmed source length the clip spans (falls back to the full track / a default).</summary>
    public double DurationSeconds
    {
        get
        {
            double end = _sourceOutSeconds ?? Track?.Duration?.TotalSeconds ?? (_sourceInSeconds + DefaultOpenLengthSeconds);
            return System.Math.Max(MinClipSeconds, end - _sourceInSeconds);
        }
    }

    public double TimelineEndSeconds => TimelineStartSeconds + DurationSeconds;

    public double X => TimelineStartSeconds * _pixelsPerSecond;

    /// <summary>Warped on-timeline width: a warped clip occupies <c>DurationSeconds / WarpFactor</c> seconds.</summary>
    public double Width => System.Math.Max(2, (DurationSeconds / WarpFactor) * _pixelsPerSecond);

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set
        {
            this.RaiseAndSetIfChanged(ref _pixelsPerSecond, value);
            this.RaisePropertyChanged(nameof(X));
            this.RaisePropertyChanged(nameof(Width));
        }
    }

    public IReadOnlyList<float>? Peaks { get => _peaks; set => this.RaiseAndSetIfChanged(ref _peaks, value); }
    public IReadOnlyList<float>? KickPeaks { get => _kickPeaks; set => this.RaiseAndSetIfChanged(ref _kickPeaks, value); }
    public IReadOnlyList<float>? MidPeaks { get => _midPeaks; set => this.RaiseAndSetIfChanged(ref _midPeaks, value); }
    public IReadOnlyList<float>? HighPeaks { get => _highPeaks; set => this.RaiseAndSetIfChanged(ref _highPeaks, value); }

    /// <summary>Project back to the immutable Core record for save / playback / render.</summary>
    public StudioClip ToClip() => new(
        DeckSlot,
        TrackPath,
        TimelineStartSeconds,
        TimeSpan.FromSeconds(SourceInSeconds),
        SourceOutSeconds is { } o ? TimeSpan.FromSeconds(o) : null,
        SourceBpm,
        WarpEnabled,
        Gain,
        FadeInSeconds,
        FadeOutSeconds,
        SourceDownbeatSeconds,
        SourceBeatsPerBar);

    // Reset the source trim to the whole file (clears in/out), leaving placement and warp as they are.
    private void ResetTrim()
    {
        SourceInSeconds = 0;
        SourceOutSeconds = null;
    }

    /// <summary>
    /// Apply a sync-to-grid result (warp on + new start) as one VM-driven change. The timeline VM has
    /// already recorded a single undo snapshot, so this writes the backing fields directly (no per-setter
    /// snapshot) and just refreshes the bound geometry.
    /// </summary>
    public void ApplySync(bool warpEnabled, double timelineStartSeconds)
    {
        _warpEnabled = warpEnabled;
        _timelineStartSeconds = System.Math.Max(0, timelineStartSeconds);
        this.RaisePropertyChanged(nameof(WarpEnabled));
        this.RaisePropertyChanged(nameof(TimelineStartSeconds));
        this.RaisePropertyChanged(nameof(X));
        this.RaisePropertyChanged(nameof(TimelineEndSeconds));
        RaiseWarp();
    }

    private void RaiseSpan()
    {
        this.RaisePropertyChanged(nameof(DurationSeconds));
        this.RaisePropertyChanged(nameof(Width));
        this.RaisePropertyChanged(nameof(TimelineEndSeconds));
    }

    private void RaiseWarp()
    {
        this.RaisePropertyChanged(nameof(WarpFactor));
        this.RaisePropertyChanged(nameof(Width));
        this.RaisePropertyChanged(nameof(WarpBadge));
    }
}
