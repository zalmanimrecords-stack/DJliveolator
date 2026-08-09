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
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using Liveolator.Core.Waveform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// The STUDIO tab: a basic-DAW timeline. Clips are placed on the two deck lanes (A/B);
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

    // Zoom bounds for the VIEW → Zoom in/out commands. Kept in sync with the zoom slider's Minimum/Maximum
    // in StudioView.axaml so menu zoom and the slider share one range; each step is a fixed ratio.
    private const double MinPixelsPerSecond = 2.0;
    private const double MaxPixelsPerSecond = 200.0;
    private const double ZoomStep = 1.25;

    private readonly MusicLibrary _library;
    private readonly IStudioProjectStore _store;
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IHostClock? _clock;
    private readonly IWaveformProvider? _waveforms;
    private readonly IAudioDecoder? _decoder;
    private readonly TrackContextActions? _contextActions;
    private readonly ILogger? _log;
    private readonly string _renderDirectory;
    private readonly Dictionary<string, MusicTrack> _byPath = new(StringComparer.OrdinalIgnoreCase);

    private List<TrackRowViewModel> _allLibrary = new();
    private bool _automationDrawMode;
    private bool _automationEditMode;
    private StudioTransport? _transport;

    // Snapshot-based undo/redo: each edit pushes the pre-mutation ToProject() snapshot; Undo/Redo
    // restore via the existing LoadProject rebuild. Suppresses re-entrant pushes while we are the one
    // rebuilding the timeline (LoadProject) so a restore is not mistaken for a fresh user edit.
    private readonly StudioEditHistory _history = new();
    private bool _restoring;

    private string _name = "New project";
    private double _bpm = StudioProject.DefaultBpm;
    private double _pixelsPerSecond = DefaultPixelsPerSecond;
    private string? _librarySearch;
    private string? _selectedSaved;
    private TrackRowViewModel? _selectedLibraryTrack;
    private StudioClipViewModel? _selectedClip;
    private bool _isPlaying;
    private double _playheadSeconds;
    private readonly DispatcherTimer _playheadTimer;
    private string _status = string.Empty;

    // The lane-header gutter width is now a pure UI-layout concern (the fixed header column in
    // StudioView.axaml) — the playhead and clips live in the content scroller and use a time-0 origin,
    // so no engine/VM math depends on it. It therefore lives in the view, not here.

    public StudioViewModel(
        MusicLibrary library,
        IStudioProjectStore store,
        IPerformanceActionDispatcher? dispatcher = null,
        IHostClock? clock = null,
        IWaveformProvider? waveforms = null,
        IAudioDecoder? decoder = null,
        TrackContextActions? contextActions = null,
        string? renderDirectory = null,
        ILoggerFactory? loggerFactory = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dispatcher = dispatcher;
        _clock = clock;
        _waveforms = waveforms;
        _decoder = decoder;
        _contextActions = contextActions;
        _log = loggerFactory?.CreateLogger<StudioViewModel>();
        _renderDirectory = renderDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator", "renders");

        // STUDIO arranges on the two supported channels A/B.
        Lanes = new ObservableCollection<StudioLaneViewModel>
        {
            new(0, "A"), new(1, "B"),
        };

        // Make automation/tempo curve edits undoable: every curve pushes the pre-edit snapshot before it
        // mutates (the lanes apply this hook to each automation curve they create/load).
        TempoLane.BeforeMutation = BeginEdit;
        foreach (StudioLaneViewModel lane in Lanes)
            lane.AutomationMutationHook = BeginEdit;

        var hasName = this.WhenAnyValue(x => x.Name).Select(n => !string.IsNullOrWhiteSpace(n));
        var hasSaved = this.WhenAnyValue(x => x.SelectedSaved).Select(s => !string.IsNullOrWhiteSpace(s));
        var hasClipSelection = this.WhenAnyValue(x => x.SelectedClip).Select(c => c is not null);

        var canUndo = this.WhenAnyValue(x => x.CanUndo);
        var canRedo = this.WhenAnyValue(x => x.CanRedo);

        NewCommand = ReactiveCommand.Create(NewProject);
        UndoCommand = ReactiveCommand.Create(Undo, canUndo);
        RedoCommand = ReactiveCommand.Create(Redo, canRedo);
        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        ResetZoomCommand = ReactiveCommand.Create(ResetZoom);
        RemoveClipCommand = ReactiveCommand.Create(RemoveSelectedClip, hasClipSelection);
        PlayCommand = ReactiveCommand.Create(TogglePlay, this.WhenAnyValue(x => x.CanPlay));
        StopCommand = ReactiveCommand.Create(StopPlayback);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasName);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync, hasSaved);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync, hasSaved);
        RenderCommand = ReactiveCommand.CreateFromTask(RenderAsync, this.WhenAnyValue(x => x.CanRender));
        HarmonizeCommand = ReactiveCommand.Create(Harmonize, this.WhenAnyValue(x => x.CanHarmonize));

        this.WhenAnyValue(x => x.LibrarySearch).Subscribe(_ => ApplyLibraryFilter());
        this.WhenAnyValue(x => x.PixelsPerSecond).Subscribe(PropagateZoom);
        this.WhenAnyValue(x => x.Bpm).Subscribe(_ => PropagateWarpTarget());

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

    /// <summary>The project tempo curve (BPM over time) — warped clips follow it.</summary>
    public TempoLaneViewModel TempoLane { get; } = new();

    /// <summary>True when there is at least one saved project (gates the FILE → Open / Delete submenus).</summary>
    public bool HasSavedProjects => SavedProjects.Count > 0;

    /// <summary>FILE → Open submenu: one entry per saved project that opens it directly. Rebuilt whenever the
    /// saved list changes (see <see cref="RefreshSavedAsync"/>); mirrors the library's "Add to playlist" menu.</summary>
    public IReadOnlyList<MenuActionViewModel> OpenItems =>
        SavedProjects.Select(name => new MenuActionViewModel(
            name, ReactiveCommand.CreateFromTask(() => OpenNamedAsync(name)))).ToList();

    /// <summary>FILE → Delete submenu: one entry per saved project that deletes it directly.</summary>
    public IReadOnlyList<MenuActionViewModel> DeleteItems =>
        SavedProjects.Select(name => new MenuActionViewModel(
            name, ReactiveCommand.CreateFromTask(() => DeleteNamedAsync(name)))).ToList();

    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetZoomCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveClipCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> RenderCommand { get; }
    public ReactiveCommand<Unit, Unit> HarmonizeCommand { get; }

    /// <summary>Live preview is available only when the realtime engine (dispatcher + clock) is wired.</summary>
    public bool CanPlay => _dispatcher is not null && _clock is not null;

    /// <summary>True when at least two distinct, analyzed+keyed tracks are placed on the lanes — the
    /// minimum the harmonic arranger needs to produce a reordered set (drives <see cref="HarmonizeCommand"/>).</summary>
    public bool CanHarmonize => ResolveTimelineTracks().Count >= 2;

    /// <summary>Offline render is available only when a decoder is wired.</summary>
    public bool CanRender => _decoder is not null;

    /// <summary>True when there is an edit to undo (drives <see cref="UndoCommand"/> + Ctrl+Z).</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>True when there is an undone edit to redo (drives <see cref="RedoCommand"/> + Ctrl+Y).</summary>
    public bool CanRedo => _history.CanRedo;

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
        BeginEdit();
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

    /// <summary>The playhead's x-pixel inside the timeline content scroller. The lane headers are a
    /// separate fixed column, so the content's origin is time-0 (x = 0) and the playhead shares the same
    /// coordinate space as the clips (whose X is also seconds * zoom).</summary>
    public double PlayheadX => PlayheadSeconds * PixelsPerSecond;

    /// <summary>m:ss readout of the playhead for the transport bar.</summary>
    public string PositionText => TimeSpan.FromSeconds(PlayheadSeconds).ToString(@"m\:ss");

    /// <summary>The arrangement length (latest clip end), the scrub slider's range; minimum 1s.</summary>
    public double ProjectDurationSeconds =>
        Math.Max(1, Lanes.SelectMany(l => l.Clips).Select(c => c.TimelineEndSeconds).DefaultIfEmpty(0).Max());

    // A short trailing run of empty timeline past the last clip, so a clip at the very end is still
    // draggable and the scroll does not stop flush against it.
    private const double TrailingMarginSeconds = 8;

    // The shortest the timeline content may be, so a near-empty project still shows a usable strip.
    private const double MinTimelineContentPx = 600;

    /// <summary>The pixel width of the timeline content (clip canvases + automation editors): the
    /// arrangement duration plus a short trailing margin, scaled by the current zoom. Binding the
    /// scrollable content widths to this makes the scroll extent track the actual material at every zoom
    /// instead of a hardcoded constant.</summary>
    public double TimelineContentWidth =>
        Math.Max(MinTimelineContentPx, (ProjectDurationSeconds + TrailingMarginSeconds) * PixelsPerSecond);

    /// <summary>Move the playhead (and the running transport, if any) to <paramref name="seconds"/>.</summary>
    public void SeekTo(double seconds)
    {
        PlayheadSeconds = seconds;
        _transport?.Seek(PlayheadSeconds);
    }

    /// <summary>When on, the per-lane automation overlays become interactive (edit envelopes); off,
    /// they are display-only and clips are draggable. Like Ableton's automation-mode toggle.</summary>
    public bool AutomationEditMode
    {
        get => _automationEditMode;
        set => this.RaiseAndSetIfChanged(ref _automationEditMode, value);
    }

    /// <summary>Within automation edit mode, dragging paints the curve freehand (Ableton pencil).</summary>
    public bool AutomationDrawMode
    {
        get => _automationDrawMode;
        set => this.RaiseAndSetIfChanged(ref _automationDrawMode, value);
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

    /// <summary>
    /// Record the current arrangement onto the undo stack BEFORE a user mutation (add/move/trim/remove
    /// a clip, change a clip's deck, toggle warp, edit automation/tempo). Call this immediately before
    /// the edit. Suppressed while we are restoring a snapshot so an Undo/Redo rebuild is not recorded as
    /// a new edit. Identical consecutive states are de-duplicated by the history.
    /// </summary>
    public void BeginEdit()
    {
        if (_restoring)
            return;
        _history.Push(BuildProject());
        RaiseHistoryChanged();
    }

    /// <summary>Restore the previous arrangement snapshot (Ctrl+Z / Undo).</summary>
    public void Undo()
    {
        StudioProject? previous = _history.Undo(BuildProject());
        if (previous is null)
            return;
        RestoreSnapshot(previous);
        Status = "Undo.";
    }

    /// <summary>Reapply the most recently undone arrangement snapshot (Ctrl+Y / Redo).</summary>
    public void Redo()
    {
        StudioProject? next = _history.Redo(BuildProject());
        if (next is null)
            return;
        RestoreSnapshot(next);
        Status = "Redo.";
    }

    // Rebuild the timeline from a history snapshot via the same LoadProject seam Open uses, guarding the
    // re-entrant push so the rebuild's mutations are not themselves recorded.
    private void RestoreSnapshot(StudioProject snapshot)
    {
        _restoring = true;
        try
        {
            LoadProject(snapshot);
        }
        finally
        {
            _restoring = false;
        }
        RaiseHistoryChanged();
    }

    private void RaiseHistoryChanged()
    {
        this.RaisePropertyChanged(nameof(CanUndo));
        this.RaisePropertyChanged(nameof(CanRedo));
    }

    // VIEW → Zoom: step the timeline scale by a fixed ratio within the slider's range, or reset to default.
    private void ZoomIn() => PixelsPerSecond = Math.Min(MaxPixelsPerSecond, PixelsPerSecond * ZoomStep);

    private void ZoomOut() => PixelsPerSecond = Math.Max(MinPixelsPerSecond, PixelsPerSecond / ZoomStep);

    private void ResetZoom() => PixelsPerSecond = DefaultPixelsPerSecond;

    private void NewProject()
    {
        StopPlayback();
        foreach (StudioLaneViewModel lane in Lanes)
        {
            lane.Clips.Clear();
            lane.ClearAutomation();
        }
        TempoLane.Points.Clear();
        SelectedClip = null;
        Name = "New project";
        Bpm = StudioProject.DefaultBpm;
        _history.Clear();
        RaiseHistoryChanged();
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
        this.RaisePropertyChanged(nameof(TimelineContentWidth));
        this.RaisePropertyChanged(nameof(CanHarmonize));
        Status = "New project — drag tracks from the library onto the deck lanes, then Play or Render.";
    }

    /// <summary>
    /// Place a library track as a clip on <paramref name="deckSlot"/> at <paramref name="startSeconds"/>
    /// (beat-snapped) — the drop target of a library→lane drag-and-drop.
    /// </summary>
    public void AddClipAt(string trackPath, int deckSlot, double startSeconds)
    {
        if (string.IsNullOrWhiteSpace(trackPath) || deckSlot < 0 || deckSlot >= Lanes.Count)
            return;

        BeginEdit();
        MusicTrack? track = _byPath.GetValueOrDefault(trackPath);
        double start = TimelineMath.Snap(Math.Max(0, startSeconds), TimelineMath.BeatSeconds(Bpm));
        var clip = new StudioClip(
            deckSlot, trackPath, start, TimeSpan.Zero, track?.Duration,
            SourceBpm: track?.Bpm?.Bpm ?? 0.0,
            SourceDownbeatSeconds: track?.Bpm?.DownbeatSeconds ?? 0.0,
            SourceBeatsPerBar: track?.Bpm?.BeatsPerBar ?? 4);

        var vm = new StudioClipViewModel(clip, track, PixelsPerSecond);
        AttachClip(vm);
        Lanes[deckSlot].Clips.Add(vm);
        SelectedClip = vm;
        LoadWaveform(vm);
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
        this.RaisePropertyChanged(nameof(TimelineContentWidth));
        this.RaisePropertyChanged(nameof(CanHarmonize));
        Status = $"Dropped \"{vm.Title}\" on deck {Lanes[deckSlot].Label}.";
    }

    private void RemoveSelectedClip()
    {
        if (SelectedClip is { } clip)
            RemoveClip(clip);
    }

    // Conservative floor on the analyzed downbeat confidence: above it we trust the bar and snap clips to
    // the project's downbeats (phrase-locked); below it the bar is genuinely ambiguous (e.g. four-on-the-
    // floor), so we snap to the nearest beat instead of trusting a guessed downbeat (doc 03). Shared with
    // the DJ deck's bar-marker anchor so both gate on the same threshold.
    private const double DownbeatConfidenceFloor = DownbeatEstimate.ConfidenceFloor;

    /// <summary>
    /// Wire a freshly created clip VM into the timeline: point its warp target at the project tempo, make
    /// its edits undoable, and attach the right-click context commands that need the timeline (sync to the
    /// project BPM, duplicate, remove). Used by both drop (<see cref="AddClipAt"/>) and load.
    /// </summary>
    private void AttachClip(StudioClipViewModel vm)
    {
        vm.WarpTargetBpm = Bpm;
        vm.BeforeMutation = BeginEdit;
        vm.SyncToGridCommand = ReactiveCommand.Create(() => SyncClipToProjectGrid(vm));
        vm.DuplicateCommand = ReactiveCommand.Create(() => DuplicateClip(vm));
        vm.RemoveCommand = ReactiveCommand.Create(() => RemoveClip(vm));
    }

    /// <summary>
    /// Warp a clip to the project tempo and snap its first audible downbeat onto the project grid, in phase
    /// with the rest of the set — the right-click "Sync to project BPM". Bar-locked when the track's downbeat
    /// is confident; otherwise beat-aligned (never trusting a guessed bar). Lands as close as possible to the
    /// clip's current position. One undoable edit.
    /// </summary>
    public void SyncClipToProjectGrid(StudioClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.SourceBpm <= 0)
        {
            Status = $"Can't sync \"{clip.Title}\" — its tempo isn't analyzed.";
            return;
        }

        double confidence = clip.Track?.Bpm?.DownbeatConfidence ?? 0.0;
        GridSnapMode mode = confidence >= DownbeatConfidenceFloor
            ? GridSnapMode.NearestDownbeat
            : GridSnapMode.NearestBeat;

        StudioClip synced = WarpSync.SnapClipToProjectGrid(clip.ToClip(), Bpm, mode);

        BeginEdit(); // one undo step covers the warp toggle + the move
        clip.ApplySync(synced.WarpEnabled, synced.TimelineStartSeconds);
        SelectedClip = clip;
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
        this.RaisePropertyChanged(nameof(TimelineContentWidth));
        Status = mode == GridSnapMode.NearestDownbeat
            ? $"Synced \"{clip.Title}\" to {Bpm:0} BPM (bar-locked)."
            : $"Synced \"{clip.Title}\" to {Bpm:0} BPM (beat-aligned — low downbeat confidence).";
    }

    /// <summary>Place a copy of <paramref name="clip"/> back-to-back after it on the same lane.</summary>
    public void DuplicateClip(StudioClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        int lane = clip.DeckSlot;
        if (lane < 0 || lane >= Lanes.Count)
            return;

        BeginEdit();
        StudioClip copy = clip.ToClip() with { TimelineStartSeconds = clip.TimelineEndSeconds };
        var vm = new StudioClipViewModel(copy, clip.Track, PixelsPerSecond);
        AttachClip(vm);
        Lanes[lane].Clips.Add(vm);
        SelectedClip = vm;
        LoadWaveform(vm);
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
        this.RaisePropertyChanged(nameof(TimelineContentWidth));
        this.RaisePropertyChanged(nameof(CanHarmonize));
        Status = $"Duplicated \"{vm.Title}\".";
    }

    /// <summary>Remove a clip from whichever lane holds it (one undoable edit).</summary>
    public void RemoveClip(StudioClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        BeginEdit();
        foreach (StudioLaneViewModel lane in Lanes)
            if (lane.Clips.Remove(clip))
                break;
        if (ReferenceEquals(SelectedClip, clip))
            SelectedClip = null;
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
        this.RaisePropertyChanged(nameof(TimelineContentWidth));
        this.RaisePropertyChanged(nameof(CanHarmonize));
    }

    /// <summary>
    /// Re-arranges the tracks currently on the timeline into a harmonic set (Camelot + BPM trend) and
    /// lays them back-to-back on alternating decks with crossfade overlaps, via
    /// <see cref="HarmonicAutoArranger"/>. Replaces the current arrangement as one undoable edit.
    /// </summary>
    private void Harmonize()
    {
        IReadOnlyList<MusicTrack> tracks = ResolveTimelineTracks();
        if (tracks.Count < 2)
            return;

        StudioProject arranged = new HarmonicAutoArranger().Arrange(
            tracks,
            new HarmonicSetOptions(Length: tracks.Count),
            new AutoArrangeOptions(ProjectName: Name.Trim()));
        if (arranged.Clips.Count == 0)
        {
            // No keyed seed among the placed tracks — nothing the harmonic builder can order.
            Status = "Harmonize: no analyzed, keyed tracks to arrange.";
            return;
        }

        BeginEdit(); // one undoable step: Undo restores the pre-harmonize arrangement
        LoadProject(arranged);
        Status = $"Harmonized {arranged.Clips.Count} tracks.";
    }

    /// <summary>
    /// The DISTINCT analyzed tracks currently placed on the lanes, resolved from each clip's path against
    /// the library snapshot, in first-seen timeline order. Unresolved (not in the library) clips are
    /// skipped; the harmonic builder itself drops unkeyed tracks, so they need no special handling here.
    /// </summary>
    private IReadOnlyList<MusicTrack> ResolveTimelineTracks()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracks = new List<MusicTrack>();
        foreach (StudioClipViewModel clip in Lanes
                     .SelectMany(l => l.Clips)
                     .OrderBy(c => c.TimelineStartSeconds))
        {
            if (!seen.Add(clip.TrackPath))
                continue;
            if (_byPath.GetValueOrDefault(clip.TrackPath) is { } track)
                tracks.Add(track);
        }

        return tracks;
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

    // The OpenCommand/DeleteCommand operate on the picker selection; the FILE-menu submenus open/delete a
    // named project directly (see OpenItems/DeleteItems). Both funnel through the *Named helpers below.
    private Task OpenAsync() => OpenNamedAsync(SelectedSaved);

    private Task DeleteAsync() => DeleteNamedAsync(SelectedSaved);

    /// <summary>Open a saved project by name (the FILE → Open submenu entry, or the picker's Open button).</summary>
    public async Task OpenNamedAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
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
                SelectedSaved = name;
                LoadProject(project);
                _history.Clear(); // opening a project starts a fresh edit history
                RaiseHistoryChanged();
                Status = $"Opened \"{project.Name}\" ({project.Clips.Count} clips).";
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Open failed: {ex.Message}");
        }
    }

    /// <summary>Delete a saved project by name (the FILE → Delete submenu entry, or the picker's Delete button).</summary>
    public async Task DeleteNamedAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
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
        var automation = Lanes.SelectMany(l => l.NonEmptyAutomation()).ToList();
        return new StudioProject(Name.Trim(), Bpm, clips, automation, TempoLane.ToTempoCurve());
    }

    private void LoadProject(StudioProject project)
    {
        StopPlayback();
        foreach (StudioLaneViewModel lane in Lanes)
        {
            lane.Clips.Clear();
            lane.ClearAutomation();
        }

        foreach (StudioClip clip in project.Clips)
        {
            // STUDIO now has two lanes (A/B). A clip saved on an old C/D lane is folded onto its paired
            // primary lane (C→A, D→B) so its audio is preserved rather than silently dropped on load.
            int slot = clip.DeckSlot < 0 ? 0 : clip.DeckSlot % Lanes.Count;
            MusicTrack? track = _byPath.GetValueOrDefault(clip.TrackPath);
            var vm = new StudioClipViewModel(clip with { DeckSlot = slot }, track, PixelsPerSecond);
            AttachClip(vm); // WarpTargetBpm is corrected to project.Bpm when Bpm is assigned below
            Lanes[slot].Clips.Add(vm);
            LoadWaveform(vm);
        }

        foreach (AutomationLane lane in project.Automation)
        {
            // Fold an old C/D automation lane onto its paired primary lane, matching the clip remap above.
            int laneSlot = lane.DeckSlot < 0 ? 0 : lane.DeckSlot % Lanes.Count;
            Lanes[laneSlot].SetAutomation(lane);
        }

        TempoLane.Load(project.EffectiveTempo);
        Name = project.Name;
        Bpm = project.Bpm;
        SelectedClip = null;
        this.RaisePropertyChanged(nameof(ProjectDurationSeconds));
        this.RaisePropertyChanged(nameof(TimelineContentWidth));
        this.RaisePropertyChanged(nameof(CanHarmonize));
    }

    private async void LoadWaveform(StudioClipViewModel clip)
    {
        if (_waveforms is null)
            return;
        try
        {
            // No ConfigureAwait(false): resume on the UI thread (like DeckViewModel) so the peak
            // properties update the bound WaveformStrip directly.
            WaveformOverview overview = await Task.Run(() => _waveforms.GetOverviewAsync(clip.TrackPath, WaveformBuckets));

            clip.Peaks = overview.IsEmpty ? null : overview.Peaks;
            clip.KickPeaks = overview.IsEmpty ? null : overview.LowPeaks;
            clip.MidPeaks = overview.IsEmpty ? null : overview.MidPeaks;
            clip.HighPeaks = overview.IsEmpty ? null : overview.HighPeaks;

            if (overview.IsEmpty)
                _log?.LogWarning("STUDIO: waveform overview empty for {Path} (undecodable / unreachable file?).", clip.TrackPath);
        }
        catch (Exception ex)
        {
            // A single clip failing to decode must not break the timeline — but surface it (no silent failure).
            _log?.LogWarning(ex, "STUDIO: waveform load failed for {Path}.", clip.TrackPath);
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
            this.RaisePropertyChanged(nameof(HasSavedProjects));
            this.RaisePropertyChanged(nameof(OpenItems));
            this.RaisePropertyChanged(nameof(DeleteItems));
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
        this.RaisePropertyChanged(nameof(TimelineContentWidth));
    }

    // Push the project tempo to every clip as its warp target, so warped clip widths follow the tempo.
    private void PropagateWarpTarget()
    {
        foreach (StudioLaneViewModel lane in Lanes)
            foreach (StudioClipViewModel clip in lane.Clips)
                clip.WarpTargetBpm = Bpm;
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
