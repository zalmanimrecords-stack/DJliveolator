using Liveolator.Core.Actions;
using Liveolator.Core.Audio;

namespace Liveolator.Core.Playlist;

/// <summary>How a deck-load request was resolved (drives the status line — never a silent outcome).</summary>
public enum DeckLoadOutcome
{
    /// <summary>The track was staged on the deck (load + downbeat anchor dispatched).</summary>
    Loaded,

    /// <summary>The deck is playing, so the track was appended to that deck's live queue instead.</summary>
    Queued,

    /// <summary>The file could not be found (missing, or its drive/share is offline); nothing dispatched.</summary>
    FileMissing,

    /// <summary>The file was present but the audio engine could not open it (corrupt/unsupported, or a
    /// missing native effects library), so the deck reported the load as failed.</summary>
    LoadFailed,
}

/// <summary>The resolved outcome plus the human-readable status message for the UI.</summary>
public sealed record DeckLoadResult(DeckLoadOutcome Outcome, string Message);

/// <summary>
/// The one load-a-track-onto-a-deck policy every UI surface shares (doc 09/11): verify the file is
/// reachable BEFORE dispatching (a BASS FileOpen failure deep in the engine must not masquerade as a
/// successful load — global #26), and never cut off a playing deck — when the target deck is playing
/// the track is appended to that deck's live queue (<see cref="PerformanceActionKind.PlaylistAppendTrack"/>)
/// and plays when the current one ends. Emits only <see cref="PerformanceAction"/>s (doc 04 seam).
/// </summary>
public sealed class DeckTrackLoader
{
    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly Func<string, bool> _fileExists;

    /// <param name="fileExists">File-reachability probe (the composition root passes <c>File.Exists</c>;
    /// kept injected so this policy stays pure and unit-testable).</param>
    public DeckTrackLoader(IPerformanceActionDispatcher dispatcher, Func<string, bool> fileExists)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    /// <summary>
    /// Stages <paramref name="trackPath"/> on deck <paramref name="slot"/> (A = 0, B = 1) without
    /// auto-playing it — or queues it on that deck when the deck is playing. <paramref name="bpm"/> is
    /// the analyzed tempo (0 = unknown) fed as the deck's Sync reference; <paramref name="firstBeatSeconds"/>
    /// is the analyzed downbeat anchor fed to phase-match (doc 22 A1).
    /// </summary>
    /// <param name="replacePlaying">
    /// When true, load onto the deck even if it is playing (replacing the current track) instead of
    /// queueing behind it — for an <b>audition</b> where the user explicitly asked to hear THIS track now
    /// (the library "Play" button). The default (false) keeps the never-cut-off-a-playing-deck policy for
    /// the staging surfaces ("Load → Deck", "Add to Deck").
    /// </param>
    public DeckLoadResult Load(
        int slot,
        string trackPath,
        double bpm,
        double firstBeatSeconds = 0,
        bool replacePlaying = false,
        IReadOnlyList<double>? kickOnsetsSeconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackPath);
        string deck = slot == 0 ? "A" : "B";
        string title = System.IO.Path.GetFileNameWithoutExtension(trackPath);

        if (!_fileExists(trackPath))
        {
            return new DeckLoadResult(
                DeckLoadOutcome.FileMissing,
                $"Cannot load \"{title}\" — the file is missing or its drive is offline ({trackPath}).");
        }

        if (!replacePlaying && _dispatcher.GetFeedback(PerformanceActionKind.DeckPlayPause, slot).IsActive)
        {
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.PlaylistAppendTrack, Slot: slot, Argument: trackPath));
            return new DeckLoadResult(
                DeckLoadOutcome.Queued,
                $"Deck {deck} is playing — added \"{title}\" to its queue.");
        }

        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, Slot: slot, Value: bpm, Argument: trackPath));

        // The handler raises DeckLoadTrack feedback synchronously during Dispatch, marking the load
        // unavailable when the engine could not open the file (a deep BASS/decoder failure that the
        // dispatcher swallows — it never surfaces as an exception here). Honour that instead of reporting a
        // success that contradicts the deck's own "couldn't load" state (global #26).
        if (!_dispatcher.GetFeedback(PerformanceActionKind.DeckLoadTrack, slot).IsAvailable)
        {
            return new DeckLoadResult(
                DeckLoadOutcome.LoadFailed,
                $"Couldn't load \"{title}\" — the audio engine could not open it ({trackPath}).");
        }

        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat,
            Slot: slot,
            Value: firstBeatSeconds,
            Argument: DeckKickOnsetCodec.Encode(kickOnsetsSeconds)));
        return new DeckLoadResult(DeckLoadOutcome.Loaded, $"Loaded \"{title}\" → Deck {deck}");
    }
}
