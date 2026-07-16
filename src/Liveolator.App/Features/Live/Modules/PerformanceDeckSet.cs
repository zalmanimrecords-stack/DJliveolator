using System;
using System.ComponentModel;
using System.Linq;
using Liveolator.App.Shell;
using Liveolator.Core;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Beat;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Mixer;
using Liveolator.Core.Settings;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The shared performance modules — Deck A, Deck B and the crossfader/mixer (doc 11) — owned as a single
/// instance so both the DJ tab and the Live tab drive the <em>same</em> decks, not look-alike copies. The
/// instances are constructed once (in the composition root) and handed to every screen that hosts them, so
/// a track loaded or a knob moved on one tab is reflected on the other (one source of truth, doc 12).
/// All controls remain pure action sources through the one dispatcher (doc 04).
/// </summary>
public sealed class PerformanceDeckSet : ViewModelBase, IDisposable
{
    private readonly MusicLibrary? _library;
    private double _waveformZoom;
    private bool _disposed;

    /// <param name="dispatcher">The one action layer for every deck/mixer control; null disables them.</param>
    /// <param name="waveformProvider">Decodes the deck waveform overview; null leaves the placeholder strip.</param>
    /// <param name="library">Catalog used to surface a loaded track's Key · BPM · duration; null omits the meta.</param>
    /// <param name="waveformZoomSeconds">Initial deck waveform zoom (seconds shown) from the user's settings.</param>
    /// <param name="deckTransportEnabled">Whether a realtime deck engine backs the decks (so transport
    /// actions are handled). False in catalog-browser mode, where the decks disable their transport controls
    /// instead of silently dropping actions; the mixer EQ/filter knobs stay live (mixer handler is always on).</param>
    /// <param name="bpmAnalysis">On-demand background BPM analysis of a loaded file, for loads with no
    /// analysis anywhere (not even the catalog) — see <see cref="DeckViewModel"/>; null disables it.</param>
    public PerformanceDeckSet(
        IPerformanceActionDispatcher? dispatcher = null,
        IWaveformProvider? waveformProvider = null,
        MusicLibrary? library = null,
        IDeckLevelMeter? levelMeter = null,
        ILimiterMeter? limiterMeter = null,
        double waveformZoomSeconds = VisualsSettings.DefaultZoomSeconds,
        double nudgeSeconds = VisualsSettings.DefaultNudgeSeconds,
        bool deckTransportEnabled = true,
        IAutoCueService? autoCueService = null,
        Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<BpmResult?>>? bpmAnalysis = null)
    {
        _library = library;
        DeckA = new DeckViewModel(slot: 0, dispatcher, waveformProvider, ResolveTrackInfo, ResolveAnalysis, waveformZoomSeconds, nudgeSeconds, deckTransportEnabled, autoCueService, bpmAnalysis);
        DeckB = new DeckViewModel(slot: 1, dispatcher, waveformProvider, ResolveTrackInfo, ResolveAnalysis, waveformZoomSeconds, nudgeSeconds, deckTransportEnabled, autoCueService, bpmAnalysis);
        // The mixer hosts the per-channel EQ/filter knobs (the DJ mixer renders them as channel strips), so
        // it is given both decks — the knobs already emit per-slot Mixer* actions, this just relocates them.
        Mixer = new MixerViewModel(dispatcher, levelMeter, DeckA, DeckB, limiterMeter);
        _waveformZoom = ZoomKnobFromSeconds(waveformZoomSeconds); // reflect the initial zoom on the knob

        // Cross-deck beatmatch highlight: when both decks are playing at the same audible tempo, light the
        // BPM readout on BOTH. Computed here because this is the one place that sees both decks; the result
        // is pushed into each deck VM so its own readout can bind a simple flag (no deck↔deck coupling).
        DeckA.PropertyChanged += OnDeckPropertyChanged;
        DeckB.PropertyChanged += OnDeckPropertyChanged;
    }

    private void OnDeckPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeckViewModel.Bpm) or nameof(DeckViewModel.IsPlaying))
            RefreshBpmMatch();
        if (e.PropertyName is nameof(DeckViewModel.IsPlaying))
            this.RaisePropertyChanged(nameof(AnyDeckPlaying));
    }

    /// <summary>True while either deck is playing. The shell uses this to hold discrete responsive
    /// reflows until the set is paused, so a window resize / move to a projector mid-mix never jumps the
    /// layout under the DJ's hands (continuous column flex still applies).</summary>
    public bool AnyDeckPlaying => DeckA.IsPlaying || DeckB.IsPlaying;

    // A beatmatch only counts while both decks are actually playing — two cued decks parked at the same
    // tempo aren't "locked" in the mix yet, and a stopped deck must drop the highlight.
    private void RefreshBpmMatch()
    {
        double bpmA = decimal.ToDouble(DeckA.Bpm);
        double bpmB = decimal.ToDouble(DeckB.Bpm);
        bool matched = DeckA.IsPlaying && DeckB.IsPlaying && BpmMatch.AreMatched(bpmA, bpmB);
        DeckA.SetBpmMatched(matched);
        DeckB.SetBpmMatched(matched);
        // When the lock is at an OCTAVE (half/double time), tag each deck with how its counter relates to the
        // other's, so a 140-vs-70 match reads as a deliberate half-time lock rather than "broken". Each deck's
        // tag is its own tempo's octave factor against the other's; unison (or no match) shows no tag.
        DeckA.SetBpmOctaveLabel(matched ? OctaveLabel(BpmMatch.OctaveFactor(bpmA, bpmB)) : "");
        DeckB.SetBpmOctaveLabel(matched ? OctaveLabel(BpmMatch.OctaveFactor(bpmB, bpmA)) : "");
    }

    // A power-of-two octave factor as a compact deck tag: 1 → none (unison), 0.5 → "½×", 0.25 → "¼×",
    // 2 → "2×", 4 → "4×". Thresholds (not float ==) keep it robust to the fold's rounding.
    private static string OctaveLabel(double factor)
    {
        if (factor >= 1.5)
            return $"{(int)Math.Round(factor)}×";
        if (factor <= 0.75)
        {
            int denom = (int)Math.Round(1.0 / factor);
            return denom == 2 ? "½×" : denom == 4 ? "¼×" : $"1/{denom}×";
        }
        return ""; // unison
    }

    /// <summary>Applies a new track-nudge step (seconds per ◄/► press) to both decks at runtime — called
    /// when the Settings value is saved, so the change takes effect without a restart.</summary>
    public void SetNudgeSeconds(double nudgeSeconds)
    {
        DeckA.SetNudgeSeconds(nudgeSeconds);
        DeckB.SetNudgeSeconds(nudgeSeconds);
    }

    /// <summary>
    /// Shared waveform ZOOM knob, 0..1: <c>0</c> = whole-track overview, increasing = zoom in (clockwise /
    /// drag up). Drives BOTH decks' waveform window in SECONDS, so A and B share one time-scale and the
    /// kick transients line up vertically — the deck ZOOM control in the waveform panel binds here.
    /// </summary>
    public double WaveformZoom
    {
        get => _waveformZoom;
        set
        {
            double clamped = double.IsNaN(value) ? _waveformZoom : Math.Clamp(value, 0.0, 1.0);
            this.RaiseAndSetIfChanged(ref _waveformZoom, clamped);
            double seconds = ZoomSecondsFromKnob(clamped);
            DeckA.SetWaveformZoomSeconds(seconds);
            DeckB.SetWaveformZoomSeconds(seconds);
        }
    }

    /// <summary>Applies a new waveform zoom level (seconds shown) to both decks at runtime — called when
    /// the Settings value is saved, so the change takes effect without a restart. Keeps the ZOOM knob in
    /// sync so the deck control reflects the saved value.</summary>
    public void SetWaveformZoom(double waveformZoomSeconds)
    {
        DeckA.SetWaveformZoomSeconds(waveformZoomSeconds);
        DeckB.SetWaveformZoomSeconds(waveformZoomSeconds);
        this.RaiseAndSetIfChanged(ref _waveformZoom, ZoomKnobFromSeconds(waveformZoomSeconds), nameof(WaveformZoom));
    }

    // Knob↔seconds mapping (geometric, so equal knob travel = equal zoom ratio). Knob 0 = overview
    // (0 s sentinel → whole track); just above 0 = the widest zoom (MaxZoomSeconds); knob 1 = the
    // tightest zoom (MinZoomSeconds).
    private static double ZoomSecondsFromKnob(double knob)
    {
        knob = Math.Clamp(knob, 0.0, 1.0);
        if (knob <= 0.0)
            return 0.0; // overview
        return VisualsSettings.MaxZoomSeconds
            * Math.Pow(VisualsSettings.MinZoomSeconds / VisualsSettings.MaxZoomSeconds, knob);
    }

    private static double ZoomKnobFromSeconds(double seconds)
    {
        if (seconds <= 0.0)
            return 0.0; // overview
        double s = Math.Clamp(seconds, VisualsSettings.MinZoomSeconds, VisualsSettings.MaxZoomSeconds);
        return Math.Log(s / VisualsSettings.MaxZoomSeconds)
            / Math.Log(VisualsSettings.MinZoomSeconds / VisualsSettings.MaxZoomSeconds);
    }

    public DeckViewModel DeckA { get; }
    public DeckViewModel DeckB { get; }
    public MixerViewModel Mixer { get; }

    /// <summary>Advances both decks' playheads from the engine's live position — driven by the Live
    /// render-loop timer so the zoomed waveform follows playback (a no-op for a stopped deck).</summary>
    public void UpdatePlayheads()
    {
        DeckA.UpdatePlayhead();
        DeckB.UpdatePlayhead();
        Mixer.UpdateLevels(DeckA.IsPlaying, DeckB.IsPlaying);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DeckA.PropertyChanged -= OnDeckPropertyChanged;
        DeckB.PropertyChanged -= OnDeckPropertyChanged;
        DeckA.Dispose();
        DeckB.Dispose();
        Mixer.Dispose();
    }

    // Catalog facts for a deck's loaded track — title + the BPM/key/duration a DJ mixes by, pre-formatted
    // the same way the Libraries table shows them. Null when there is no library or no matching entry.
    private DeckTrackInfo? ResolveTrackInfo(string trackPath)
    {
        if (FindTrack(trackPath) is not { } track)
            return null;

        string bpm = track.Bpm is { } b ? b.Bpm.ToString("0.0") : "—";
        string key = track.Key?.Camelot ?? "—";
        string duration = track.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : "—";
        return new DeckTrackInfo(track.Title, bpm, key, duration, track.Artist);
    }

    // The track's analyzed beat grid (BPM + first-beat) from the catalog, the source of truth. Lets a deck
    // self-heal a load that arrived without analysis (a restored session predating analysis, a queue load)
    // so the grid/BPM still appear. Null when there is no library/match or the track is unanalyzed.
    private BpmResult? ResolveAnalysis(string trackPath) => FindTrack(trackPath)?.Bpm;

    // The catalog entry for a loaded path. The loaded path can differ in form from the catalog's (a mapped
    // drive vs the UNC share it was scanned under, or a deck-queue path), so an exact match can miss a track
    // that IS in the library — fall back to a file-name match (deck B was showing no BPM because of this).
    private MusicTrack? FindTrack(string trackPath)
    {
        if (_library is null || string.IsNullOrEmpty(trackPath))
            return null;

        MusicTrack? track = _library.All
            .FirstOrDefault(t => string.Equals(t.File.Path, trackPath, StringComparison.OrdinalIgnoreCase));
        if (track is not null)
            return track;

        string fileName = PortablePath.GetFileName(trackPath);
        return string.IsNullOrEmpty(fileName)
            ? null
            : _library.All.FirstOrDefault(t =>
                string.Equals(PortablePath.GetFileName(t.File.Path), fileName, StringComparison.OrdinalIgnoreCase));
    }
}
