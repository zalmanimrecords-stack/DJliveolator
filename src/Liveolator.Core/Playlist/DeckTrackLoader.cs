using Liveolator.Core.Actions;

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
    public DeckLoadResult Load(int slot, string trackPath, double bpm, double firstBeatSeconds = 0)
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

        if (_dispatcher.GetFeedback(PerformanceActionKind.DeckPlayPause, slot).IsActive)
        {
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.PlaylistAppendTrack, Slot: slot, Argument: trackPath));
            return new DeckLoadResult(
                DeckLoadOutcome.Queued,
                $"Deck {deck} is playing — added \"{title}\" to its queue.");
        }

        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, Slot: slot, Value: bpm, Argument: trackPath));
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat, Slot: slot, Value: firstBeatSeconds));
        return new DeckLoadResult(DeckLoadOutcome.Loaded, $"Loaded \"{title}\" → Deck {deck}");
    }
}
