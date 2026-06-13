using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Shared;
using Liveolator.App.Shell;
using Liveolator.Audio.Render;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;
using Liveolator.Core.Waveform;
using Avalonia.Threading;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// The STUDIO tab: a basic-DAW timeline. Clips are placed on four deck lanes (A/B live + C/D hidden);
/// Play drives the real decks live via <see cref="StudioTransport"/> (through the dispatcher), and
/// Render mixes the arrangement down to a WAV via <see cref="OfflineMixRenderer"/>. Holds no Avalonia
/// types; store/transport/render calls are guarded and surfaced on <see cref="Status"/>.
/// </summary>
/// <remarks>Automation-curve editing is not yet exposed in the UI; loaded automation is preserved
/// across edits and honoured by playback/render, so it round-trips even though this MVP only edits clips.</remarks>
public sealed class StudioViewModel : ViewModelBase, IDisposable
{
    private const int WaveformBuckets = 512;
    private const double DefaultPixelsPerSecond = 8.0;

    private readonly MusicLibrary _library;
    private readonly IStudioProjectStore _store;
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IHostClock? _clock;
    private readonly IWaveformProvider? _waveforms;
    private readonly IAudioDecoder? _decoder;
    private readonly TrackContextActions? _contextActions;
    private readonly string _renderDirectory;
    private readonly Dictionary<string, MusicTrack> _byPath = new(StringComparer.OrdinalIgnoreCase);

    private List<TrackRowViewModel> _allLibrary = new();
    private readonly List<AutomationLaneViewModel> _automationLanes = new();
    private int _automationDeck;
    private AutomationTarget _automationTarget = AutomationTarget.DeckGain;
    private StudioTransport? _transport;

    private string _name = "New project";
    private double _bpm = StudioProject.DefaultBpm;
    private double _pixelsPerSecond = DefaultPixelsPerSecond;
    private string? _librarySearch;
    private string? _selectedSaved;
    private TrackRowViewModel? _selectedLibraryTrack;
    private StudioClipViewModel? _selectedClip;
    private int _targetDeck;
    private bool _isPlaying;
    private double _playheadSeconds;
    private readonly DispatcherTimer _playheadTimer;
    private string _status = string.Empty;

    // The lane label gutter (40px label + 6px margin) the timeline clip canvases sit behind; the
    // playhead overlay is offset by it so the line aligns with the clips, not the labels.
    public const double LaneGutterPx = 46;

    public StudioViewModel(
        MusicLibrary library,
        IStudioProjectStore store,
        IPerformanceActionDispatcher? dispatcher = null,
        IHostClock? clock = null,
        IWaveformProvider? waveforms = null,
        IAudioDecoder? decoder = null,
        TrackContextActions? contextActions = null,
        string? renderDirectory = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dispatcher = dispatcher;
        _clock = clock;
        _waveforms = waveforms;
        _decoder = decoder;
        _contextActions = contextActions;
        _renderDirectory = renderDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator", "renders");

        Lanes = new ObservableCollection<StudioLaneViewModel>
        {
            new(0, "A"), new(1, "B"), new(2, "C"), new(3, "D"),
        };

        var hasName = this.WhenAnyValue(x => x.Name).Select(n => !string.IsNullOrWhiteSpace(n));
        var hasSaved = this.WhenAnyValue(x => x.SelectedSaved).Select(s => !string.IsNullOrWhiteSpace(s));
        var hasLibrarySelection = this.WhenAnyValue(x => x.SelectedLibraryTrack).Select(t => t is not null);
        var hasClipSelection = this.WhenAnyValue(x => x.SelectedClip).Select(c => c is not null);

        NewCommand = ReactiveCommand.Create(NewProject);
        AddClipCommand = ReactiveCommand.Create(AddClip, hasLibrarySelection);
        RemoveClipCommand = ReactiveCommand.Create(RemoveSelectedClip, hasClipSelection);
        PlayCommand = ReactiveCommand.Create(TogglePlay, this.WhenAnyValue(x => x.CanPlay));
        StopCommand = ReactiveCommand.Create(StopPlayback);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasName);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync, hasSaved);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync, hasSaved);
        RenderCommand = ReactiveCommand.CreateFromTask(RenderAsync, this.WhenAnyValue(x => x.CanRender));

        this.WhenAnyValue(x => x.LibrarySearch).Subscribe(_ => ApplyLibraryFilter());
        this.WhenAnyValue(x => x.PixelsPerSecond).Subscribe(PropagateZoom);

        // ~20 fps playhead follow while playing — reads the transport's position on the UI thread.
        _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _playheadTimer.Tick += (_, _) =>
        {
            if (_transport is { } transport)
                PlayheadSeconds = transport.PositionSeconds;
        };
    }

    public ObservableCollection<TrackRowViewModel> Library { get; } = new();
    public ObservableCollection<StudioLaneViewModel> Lanes { get; }
    public ObservableCollection<string> SavedProjects { get; } = new();

    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> AddClipCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveClipCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> RenderCommand { get; }

    /// <summary>Live preview is available only when the realtime engine (dispatcher + clock) is wired.</summary>
    public bool CanPlay => _dispatcher is not null && _clock is not null;

    /// <summary>Offline render is available only when a decoder is wired.</summary>
    public bool CanRender => _decoder is not null;

    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public double Bpm { get => _bpm; set => this.RaiseAndSetIfChanged(ref _bpm, value); }
    public double PixelsPerSecond { get => _pixelsPerSecond; set => this.RaiseAndSetIfChanged(ref _pixelsPerSecond, value); }
    public string? LibrarySearch { get => _librarySearch; set => this.RaiseAndSetIfChanged(ref _librarySearch, value); }
    public string? SelectedSaved { get => _selectedSaved; set => this.RaiseAndSetIfChanged(ref _selectedSaved, value); }
    public TrackRowViewModel? SelectedLibraryTrack { get => _selectedLibraryTrack; set => this.RaiseAndSetIfChanged(ref _selectedLibraryTrack, value); }
    public StudioClipViewModel? SelectedClip
    {
        get => _selectedClip;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedClip, value);
            this.RaisePropertyChanged(nameof(SelectedClipDeck));
            this.RaisePropertyChanged(nameof(HasSelectedClip));
        }
    }

    /// <summary>True when a clip is selected (drives the inspector's visibility).</summary>
    public bool HasSelectedClip => _selectedClip is not null;

    /// <summary>The deck lane (0-3) a newly added clip lands on.</summary>
    public int TargetDeck { get => _targetDeck; set => this.RaiseAndSetIfChanged(ref _targetDeck, value); }

    /// <summary>The selected clip's deck (0-3), bound to the inspector — moving it relocates the clip's lane.</summary>
    public int SelectedClipDeck
    {
        get => SelectedClip?.DeckSlot ?? 0;
        set
        {
            if (SelectedClip is { } clip)
                MoveClipToDeck(clip, value);
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Move a clip to another deck lane (relocates it between the lane collections).</summary>
    public void MoveClipToDeck(StudioClipViewModel clip, int targetDeck)
    {
        targetDeck = Math.Clamp(targetDeck, 0, Lanes.Count - 1);
        if (clip.DeckSlot == targetDeck)
            return;
        foreach (StudioLaneViewModel lane in Lanes)
            if (lane.Clips.Remove(clip))
                break;
        clip.DeckSlot = targetDeck;
        Lanes[targetDeck].Clips.Add(clip);
    }

    public bool IsPlaying { get => _isPlaying; private set => this.RaiseAndSetIfChanged(ref _isPlaying, value); }

    public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }

    /// <summary>The playhead position in timeline seconds (follows the transport while playing).</summary>
    public double PlayheadSeconds
    {
        get => _playheadSeconds;
        set
        {
            this.RaiseAndSetIfChanged(ref _playheadSeconds, Math.Max(0, value));
            this.RaisePropertyChanged(nameof(PlayheadX));
            this.RaisePropertyChanged(nameof(PositionText));
            this.RaisePropertyChanged(nameof(ScrubSeconds));
        }
    }

    /// <summary>Two-way scrub binding for the transport slider: reads the playhead, and on set seeks
    /// the playhead + the running transport. Separate from <see cref="PlayheadSeconds"/> so the timer's
    /// updates (which only set PlayheadSeconds) still move the slider without re-seeking every tick.</summary>
    public double ScrubSeconds
    {
        get => PlayheadSeconds;
        set => SeekTo(value);
    }

    /// <summary>The playhead's x-pixel on the timeline overlay (offset past the lane label gutter).</summary>
    public double PlayheadX => LaneGutterPx + (PlayheadSeconds * PixelsPerSecond);

    /// <summary>m:ss readout of the playhead for the transport bar.</summary>
    public string PositionText => TimeSpan.FromSeconds(PlayheadSeconds).ToString(@"m\:ss");

    /// <summary>The arrangement length (latest clip end), the scrub slider's range; minimum 1s.</summary>
    public double ProjectDurationSeconds =>
        Math.Max(1, Lanes.SelectMany(l => l.Clips).Select(c => c.TimelineEndSeconds).DefaultIfEmpty(0).Max());

    /// <summary>Move the playhead (and the running transport, if any) to <paramref name="seconds"/>.</summary>
    public void SeekTo(double seconds)
    {
        PlayheadSeconds = seconds;
        _transport?.Seek(PlayheadSeconds);
    }

    /// <summary>The automation targets the editor can select (bound to the target drop-down).</summary>
    public static IReadOnlyList<AutomationTarget> AutomationTargets { get; } = Enum.GetValues<AutomationTarget>();

    /// <summary>Which deck's automation the editor shows (0-3).</summary>
    public int SelectedAutomationDeck
    {
        get => _automationDeck;
        set
        {
            this.RaiseAndSetIfChanged(ref _automationDeck, Math.Clamp(value, 0, Lanes.Count - 1));
            this.RaisePropertyChanged(nameof(CurrentAutomationLane));
        }
    }

    /// <summary>Which control's automation the editor shows.</summary>
    public AutomationTarget SelectedAutomationTarget
    {
        get => _automationTarget;
        set
        {
            this.RaiseAndSetIfChanged(ref _automationTarget, value);
            this.RaisePropertyChanged(nameof(CurrentAutomationLane));
        }
    }

    /// <summary>The editable lane for the selected (deck, target) — created on first edit.</summary>
    public AutomationLaneViewModel CurrentAutomationLane => GetOrCreateAutomationLane(_automationDeck, _automationTarget);

    private AutomationLaneViewModel GetOrCreateAutomationLane(int deck, AutomationTarget target)
    {
        AutomationLaneViewModel? existing = _automationLanes
            .FirstOrDefault(l => l.DeckSlot == deck && l.Target == target);
        if (existing is not null)
            return existing;

        var lane = new AutomationLaneViewModel(target, deck);
        _automationLanes.Add(lane);
        return lane;
    }

    /// <summary>Loads the library snapshot for the picker + the list of saved projects. Called when shown.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _byPath.Clear();
        foreach (MusicTrack track in _library.All)
            _byPath[track.File.Path] = track;

        _allLibrary = _library.All
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TrackRowViewModel(t, _contextActions))
            .ToList();
        ApplyLibraryFilter();
        await RefreshSavedAsync(cancellationToken).ConfigureAwait(false);
    }

    private void NewProject()
    {
        StopPlayback();
        foreach (StudioLaneViewModel lane in Lanes)
            lane.Clips.Clear();
        _automationLanes.Clear();
        this.RaisePropertyChanged(nameof(CurrentAutomationLane));
        SelectedClip = null;
        Name = "New project";
        Bpm = StudioProject.DefaultBpm;
        Status = "New project — add clips to the deck lanes, then Play or Render.";
    }

    private void AddClip()
    {
        if (SelectedLibraryTrack is not { } row)
            return;
        StudioLaneViewModel lane = Lanes[Math.Clamp(TargetDeck, 0, Lanes.Count - 1)];

        // Drop the clip after whatever already sits on this lane, so additions don't overlap.
        double start = lane.Clips.Count == 0 ? 0 : lane.Clips.Max(c => c.TimelineEndSeconds);
        TimeSpan? outPoint = row.Track.Duration;
        var clip = new StudioClip(lane.Slot, row.Track.File.Path, start, TimeSpan.Zero, outPoint);

        var vm = new StudioClipViewModel(clip, row.Track, PixelsPerSecond);
        lane.Clips.Add(vm);
        SelectedClip = vm;
        LoadWaveform(vm);
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
        Status = $"Added \"{vm.Title}\" to deck {lane.Label}.";
    }

    private void RemoveSelectedClip()
    {
        if (SelectedClip is not { } clip)
            return;
        foreach (StudioLaneViewModel lane in Lanes)
            if (lane.Clips.Remove(clip))
                break;
        SelectedClip = null;
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
    }

    private void TogglePlay()
    {
        if (IsPlaying)
        {
            StopPlayback();
            return;
        }
        if (_dispatcher is null || _clock is null)
            return;

        _transport?.Dispose();
        _transport = new StudioTransport(new StudioArranger(BuildProject()), _dispatcher, _clock);
        _transport.Seek(PlayheadSeconds); // start from where the playhead sits
        _transport.Play();
        _playheadTimer.Start();
        IsPlaying = true;
        Status = "Playing arrangement…";
    }

    private void StopPlayback()
    {
        _playheadTimer.Stop();
        _transport?.Stop();
        _transport?.Dispose();
        _transport = null;
        IsPlaying = false;
        PlayheadSeconds = 0; // Stop rewinds to the start (there is no separate pause)
    }

    private async Task SaveAsync()
    {
        try
        {
            StudioProject project = BuildProject();
            await _store.SaveAsync(project).ConfigureAwait(false);
            await RefreshSavedAsync().ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Saved \"{project.Name}\" ({project.Clips.Count} clips).");
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Save failed: {ex.Message}");
        }
    }

    private async Task OpenAsync()
    {
        if (SelectedSaved is not { } name)
            return;
        try
        {
            StudioProject? project = await _store.LoadAsync(name).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (project is null)
                {
                    Status = $"Could not open \"{name}\".";
                    return;
                }
                LoadProject(project);
                Status = $"Opened \"{project.Name}\" ({project.Clips.Count} clips).";
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Open failed: {ex.Message}");
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedSaved is not { } name)
            return;
        try
        {
            await _store.DeleteAsync(name).ConfigureAwait(false);
            await RefreshSavedAsync().ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Deleted \"{name}\".");
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Delete failed: {ex.Message}");
        }
    }

    private async Task RenderAsync()
    {
        if (_decoder is null)
            return;
        try
        {
            StudioProject project = BuildProject();
            Directory.CreateDirectory(_renderDirectory);
            string outputPath = Path.Combine(_renderDirectory, Sanitize(project.Name) + ".wav");
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Rendering \"{project.Name}\"…");
            await new OfflineMixRenderer(_decoder).RenderAsync(project, outputPath).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Rendered to {outputPath}");
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Render failed: {ex.Message}");
        }
    }

    private StudioProject BuildProject()
    {
        var clips = Lanes
            .SelectMany(lane => lane.Clips.Select(c => c.ToClip()))
            .OrderBy(c => c.TimelineStartSeconds)
            .ToList();
        var automation = _automationLanes
            .Where(l => l.Points.Count > 0)
            .Select(l => l.ToLane())
            .ToList();
        return new StudioProject(Name.Trim(), Bpm, clips, automation);
    }

    private void LoadProject(StudioProject project)
    {
        StopPlayback();
        foreach (StudioLaneViewModel lane in Lanes)
            lane.Clips.Clear();

        foreach (StudioClip clip in project.Clips)
        {
            if (clip.DeckSlot < 0 || clip.DeckSlot >= Lanes.Count)
                continue;
            MusicTrack? track = _byPath.GetValueOrDefault(clip.TrackPath);
            var vm = new StudioClipViewModel(clip, track, PixelsPerSecond);
            Lanes[clip.DeckSlot].Clips.Add(vm);
            LoadWaveform(vm);
        }

        _automationLanes.Clear();
        foreach (AutomationLane lane in project.Automation)
            _automationLanes.Add(new AutomationLaneViewModel(lane.Target, lane.DeckSlot, lane.Keyframes));
        this.RaisePropertyChanged(nameof(CurrentAutomationLane));
        Name = project.Name;
        Bpm = project.Bpm;
        SelectedClip = null;
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
    }

    private async void LoadWaveform(StudioClipViewModel clip)
    {
        if (_waveforms is null)
            return;
        try
        {
            WaveformOverview overview = await Task.Run(
                () => _waveforms.GetOverviewAsync(clip.TrackPath, WaveformBuckets)).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                clip.Peaks = overview.IsEmpty ? null : overview.Peaks;
                clip.KickPeaks = overview.IsEmpty ? null : overview.LowPeaks;
                clip.MidPeaks = overview.IsEmpty ? null : overview.MidPeaks;
                clip.HighPeaks = overview.IsEmpty ? null : overview.HighPeaks;
            });
        }
        catch (Exception)
        {
            // A single clip failing to decode must not break the timeline.
        }
    }

    private async Task RefreshSavedAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> names = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            SavedProjects.Clear();
            foreach (string name in names)
                SavedProjects.Add(name);
        });
    }

    private void ApplyLibraryFilter()
    {
        Library.Clear();
        string? query = LibrarySearch?.Trim();
        foreach (TrackRowViewModel row in _allLibrary)
            if (string.IsNullOrEmpty(query) || row.Matches(query))
                Library.Add(row);
    }

    private void PropagateZoom(double pps)
    {
        foreach (StudioLaneViewModel lane in Lanes)
            foreach (StudioClipViewModel clip in lane.Clips)
                clip.PixelsPerSecond = pps;
        this.RaisePropertyChanged(nameof(PlayheadX));
    }

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "render" : cleaned;
    }

    public void Dispose() => StopPlayback();
}
