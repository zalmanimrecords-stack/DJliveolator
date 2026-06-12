using System.Collections.Generic;
using System.IO;
using Liveolator.App.Shell;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// A clip on the STUDIO timeline: its source track + deck lane + timeline placement, projected to
/// pixels for the lane canvas (X / Width via the view's pixels-per-second zoom) and carrying the
/// lazily-loaded waveform peaks the block draws.
/// </summary>
public sealed class StudioClipViewModel : ViewModelBase
{
    private double _pixelsPerSecond;
    private IReadOnlyList<float>? _peaks;
    private IReadOnlyList<float>? _kickPeaks;
    private IReadOnlyList<float>? _midPeaks;
    private IReadOnlyList<float>? _highPeaks;

    public StudioClipViewModel(StudioClip clip, MusicTrack? track, double pixelsPerSecond)
    {
        Clip = clip;
        Track = track;
        _pixelsPerSecond = pixelsPerSecond;
    }

    public StudioClip Clip { get; }
    public MusicTrack? Track { get; }

    public int DeckSlot => Clip.DeckSlot;
    public string Title => Track?.Title ?? Path.GetFileNameWithoutExtension(Clip.TrackPath);

    /// <summary>Seconds of source the clip spans (falls back to a default block when open-ended).</summary>
    public double DurationSeconds =>
        Clip.SourceDuration?.TotalSeconds ?? Track?.Duration?.TotalSeconds ?? DefaultOpenLengthSeconds;

    public double X => Clip.TimelineStartSeconds * _pixelsPerSecond;
    public double Width => System.Math.Max(2, DurationSeconds * _pixelsPerSecond);

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

    // A clip with no known length still needs a visible width; one minute reads sensibly on the lane.
    private const double DefaultOpenLengthSeconds = 60;
}
