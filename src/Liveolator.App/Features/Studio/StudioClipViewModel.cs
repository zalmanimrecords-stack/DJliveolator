using System.Collections.Generic;
using System.IO;
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
    }

    public string TrackPath { get; }
    public MusicTrack? Track { get; }
    public string Title => Track?.Title ?? Path.GetFileNameWithoutExtension(TrackPath);

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
        FadeOutSeconds);

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
