namespace Liveolator.Core.Persistence;

/// <summary>Persists the tracks currently loaded into the performance decks.</summary>
public interface IDeckSessionStore
{
    Task<IReadOnlyList<DeckSessionState>?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyList<DeckSessionState> decks,
        CancellationToken cancellationToken = default);
}
