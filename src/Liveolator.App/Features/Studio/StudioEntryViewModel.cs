using System.Collections.Generic;
using System.IO;
using Liveolator.App.Shell;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// One lane on the STUDIO timeline: a track in the planned set with its display facts, the
/// transition that leads into it (null for the first lane), and the lazily-loaded waveform peaks
/// the lane draws. <see cref="Track"/> is null when the saved path is no longer in the library.
/// </summary>
public sealed class StudioEntryViewModel : ViewModelBase
{
    private const string None = "—";

    private StudioTransitionViewModel? _transitionIn;
    private IReadOnlyList<float>? _peaks;
    private IReadOnlyList<float>? _kickPeaks;
    private IReadOnlyList<float>? _midPeaks;
    private IReadOnlyList<float>? _highPeaks;

    public StudioEntryViewModel(string path, MusicTrack? track, StudioTransitionViewModel? transitionIn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Track = track;
        _transitionIn = transitionIn;
    }

    public string Path { get; }
    public MusicTrack? Track { get; }

    /// <summary>The blend leading into this lane from the previous one; null on the first lane.</summary>
    public StudioTransitionViewModel? TransitionIn
    {
        get => _transitionIn;
        set
        {
            this.RaiseAndSetIfChanged(ref _transitionIn, value);
            this.RaisePropertyChanged(nameof(HasTransition));
        }
    }

    public bool HasTransition => TransitionIn is not null;

    public string Title => Track?.Title ?? System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Bpm => Track?.Bpm is { } b ? b.Bpm.ToString("0.0") : None;
    public string Key => Track?.Key?.Camelot ?? None;
    public string Duration => Track?.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : None;

    // 3-band waveform peaks for the lane (filled asynchronously after the set is built/opened).
    public IReadOnlyList<float>? Peaks { get => _peaks; set => this.RaiseAndSetIfChanged(ref _peaks, value); }
    public IReadOnlyList<float>? KickPeaks { get => _kickPeaks; set => this.RaiseAndSetIfChanged(ref _kickPeaks, value); }
    public IReadOnlyList<float>? MidPeaks { get => _midPeaks; set => this.RaiseAndSetIfChanged(ref _midPeaks, value); }
    public IReadOnlyList<float>? HighPeaks { get => _highPeaks; set => this.RaiseAndSetIfChanged(ref _highPeaks, value); }

    public StudioEntry ToModel() => new(Path, TransitionIn: TransitionIn?.ToModel());
}
