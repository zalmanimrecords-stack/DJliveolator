using System;
using System.Linq;
using Liveolator.Core.Actions;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Waveform;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The shared performance modules — Deck A, Deck B and the crossfader/mixer (doc 11) — owned as a single
/// instance so both the DJ tab and the Live tab drive the <em>same</em> decks, not look-alike copies. The
/// instances are constructed once (in the composition root) and handed to every screen that hosts them, so
/// a track loaded or a knob moved on one tab is reflected on the other (one source of truth, doc 12).
/// All controls remain pure action sources through the one dispatcher (doc 04).
/// </summary>
public sealed class PerformanceDeckSet : IDisposable
{
    private readonly MusicLibrary? _library;
    private bool _disposed;

    /// <param name="dispatcher">The one action layer for every deck/mixer control; null disables them.</param>
    /// <param name="waveformProvider">Decodes the deck waveform overview; null leaves the placeholder strip.</param>
    /// <param name="library">Catalog used to surface a loaded track's Key · BPM · duration; null omits the meta.</param>
    public PerformanceDeckSet(
        IPerformanceActionDispatcher? dispatcher = null,
        IWaveformProvider? waveformProvider = null,
        MusicLibrary? library = null)
    {
        _library = library;
        DeckA = new DeckViewModel(slot: 0, dispatcher, waveformProvider, ResolveTrackInfo);
        DeckB = new DeckViewModel(slot: 1, dispatcher, waveformProvider, ResolveTrackInfo);
        Mixer = new MixerViewModel(dispatcher);
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
        if (track is null)
            return null;

        string bpm = track.Bpm is { } b ? b.Bpm.ToString("0.0") : "—";
        string key = track.Key?.Camelot ?? "—";
        string duration = track.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : "—";
        return new DeckTrackInfo(track.Title, bpm, key, duration);
    }
}
