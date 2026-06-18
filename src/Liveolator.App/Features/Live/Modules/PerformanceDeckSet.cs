using System;
using System.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
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
    public PerformanceDeckSet(
        IPerformanceActionDispatcher? dispatcher = null,
        IWaveformProvider? waveformProvider = null,
        MusicLibrary? library = null,
        IDeckLevelMeter? levelMeter = null,
        double waveformZoomSeconds = VisualsSettings.DefaultZoomSeconds,
        double nudgeSeconds = VisualsSettings.DefaultNudgeSeconds)
    {
        _library = library;
        DeckA = new DeckViewModel(slot: 0, dispatcher, waveformProvider, ResolveTrackInfo, waveformZoomSeconds, nudgeSeconds);
        DeckB = new DeckViewModel(slot: 1, dispatcher, waveformProvider, ResolveTrackInfo, waveformZoomSeconds, nudgeSeconds);
        // The mixer hosts the per-channel EQ/filter knobs (the DJ mixer renders them as channel strips), so
        // it is given both decks — the knobs already emit per-slot Mixer* actions, this just relocates them.
        Mixer = new MixerViewModel(dispatcher, levelMeter, DeckA, DeckB);
        _waveformZoom = ZoomKnobFromSeconds(waveformZoomSeconds); // reflect the initial zoom on the knob
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
        DeckA.Dispose();
        DeckB.Dispose();
        Mixer.Dispose();
    }

    // Catalog facts for a deck's loaded track — title + the BPM/key/duration a DJ mixes by, pre-formatted
    // the same way the Libraries table shows them. Null when there is no library or no matching entry.
    private DeckTrackInfo? ResolveTrackInfo(string trackPath)
    {
        if (_library is null || string.IsNullOrEmpty(trackPath))
            return null;

        MusicTrack? track = _library.All
            .FirstOrDefault(t => string.Equals(t.File.Path, trackPath, StringComparison.OrdinalIgnoreCase));

        // The loaded path can differ in form from the catalog's (e.g. a mapped drive vs the UNC share the
        // track was scanned under, or a deck-queue path), so an exact match can miss a track that IS in the
        // library. Fall back to a file-name match so the deck still surfaces its Key·BPM·duration (deck B
        // was showing no BPM because of this).
        if (track is null)
        {
            string fileName = System.IO.Path.GetFileName(trackPath);
            if (!string.IsNullOrEmpty(fileName))
                track = _library.All.FirstOrDefault(t =>
                    string.Equals(System.IO.Path.GetFileName(t.File.Path), fileName, StringComparison.OrdinalIgnoreCase));
        }

        if (track is null)
            return null;

        string bpm = track.Bpm is { } b ? b.Bpm.ToString("0.0") : "—";
        string key = track.Key?.Camelot ?? "—";
        string duration = track.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : "—";
        return new DeckTrackInfo(track.Title, bpm, key, duration);
    }
}
