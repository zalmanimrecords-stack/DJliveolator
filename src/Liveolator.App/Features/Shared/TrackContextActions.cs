using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using ReactiveUI;

namespace Liveolator.App.Features.Shared;

/// <summary>
/// The shared back-end for the per-track right-click menu (Add to Deck A/B, Add to playlist).
/// One DI singleton drives every track row's <see cref="TrackMenuViewModel"/>. Deck loads go through
/// the <see cref="IPerformanceActionDispatcher"/> (doc 04 seam — never a direct engine call);
/// playlist edits go through <see cref="IPlaylistStore"/>. Deck availability is read from the
/// dispatcher feedback seam, so deck items disable when a deck isn't backed and the whole deck section
/// disables in catalog-browser mode (no dispatcher) — never a silent failure (global #16/#26).
/// </summary>
public sealed class TrackContextActions
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IPlaylistStore _store;
    private readonly Action<string>? _onStatus;

    public TrackContextActions(
        IPerformanceActionDispatcher? dispatcher,
        IPlaylistStore store,
        Action<string>? onStatus = null)
    {
        _dispatcher = dispatcher;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _onStatus = onStatus;

        CanLoadToDeckA = DeckSlotAvailable(0);
        CanLoadToDeckB = DeckSlotAvailable(1);
    }

    /// <summary>Saved set names for the "Add to playlist" submenu; refreshed from the store.</summary>
    public ObservableCollection<string> Playlists { get; } = new();

    /// <summary>True when deck slot A / B is backed by the engine (drives the deck menu items).</summary>
    public bool CanLoadToDeckA { get; }
    public bool CanLoadToDeckB { get; }

    /// <summary>
    /// Stages a track on a deck slot (A = 0, B = 1) without auto-playing it. <paramref name="bpm"/> is the
    /// track's analyzed tempo (0 = unknown), fed to the deck as its Sync reference (doc 11);
    /// <paramref name="firstBeatSeconds"/> is the analyzed downbeat anchor (0 = unknown), fed to phase-match
    /// (doc 22 A1) right after the load.
    /// </summary>
    public void LoadToDeck(int slot, string trackPath, double bpm, double firstBeatSeconds = 0)
    {
        if (_dispatcher is null || string.IsNullOrWhiteSpace(trackPath))
            return;
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, Slot: slot, Value: bpm, Argument: trackPath));
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat, Slot: slot, Value: firstBeatSeconds));
        _onStatus?.Invoke($"Loaded \"{TitleOf(trackPath)}\" → Deck {(slot == 0 ? "A" : "B")}");
    }

    /// <summary>Reloads the saved-set names (call at startup and after a set is created/changed).</summary>
    public async Task RefreshPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<string> names = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                Playlists.Clear();
                foreach (string name in names)
                    Playlists.Add(name);
            });
        }
        catch (Exception ex)
        {
            _onStatus?.Invoke($"Could not list playlists: {ex.Message}");
        }
    }

    /// <summary>Appends a track to a saved set (no duplicates), creating nothing — the set must exist.</summary>
    public async Task AddToPlaylistAsync(string trackPath, string playlistName, CancellationToken cancellationToken = default)
    {
        try
        {
            Playlist? playlist = await _store.LoadAsync(playlistName, cancellationToken).ConfigureAwait(false);
            playlist ??= Playlist.Empty(playlistName);

            if (playlist.TrackPaths.Any(p => string.Equals(p, trackPath, StringComparison.OrdinalIgnoreCase)))
            {
                _onStatus?.Invoke($"\"{TitleOf(trackPath)}\" is already in \"{playlistName}\".");
                return;
            }

            var updated = playlist.WithTracks(playlist.TrackPaths.Append(trackPath));
            await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            _onStatus?.Invoke($"Added \"{TitleOf(trackPath)}\" to \"{playlistName}\".");
        }
        catch (Exception ex)
        {
            _onStatus?.Invoke($"Could not add to \"{playlistName}\": {ex.Message}");
        }
    }

    /// <summary>Creates a new, uniquely-named set containing just this track, then saves it.</summary>
    public async Task AddToNewPlaylistAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        try
        {
            string name = await NextNewSetNameAsync(cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(new Playlist(name, new[] { trackPath }), cancellationToken).ConfigureAwait(false);
            await RefreshPlaylistsAsync(cancellationToken).ConfigureAwait(false);
            _onStatus?.Invoke($"Created \"{name}\" with \"{TitleOf(trackPath)}\".");
        }
        catch (Exception ex)
        {
            _onStatus?.Invoke($"Could not create a new set: {ex.Message}");
        }
    }

    private async Task<string> NextNewSetNameAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> existing = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        const string baseName = "New set";
        if (!taken.Contains(baseName))
            return baseName;
        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    private bool DeckSlotAvailable(int slot)
        => _dispatcher?.GetFeedback(PerformanceActionKind.DeckPlayPause, slot).IsAvailable ?? false;

    private static string TitleOf(string path) => System.IO.Path.GetFileNameWithoutExtension(path);
}
