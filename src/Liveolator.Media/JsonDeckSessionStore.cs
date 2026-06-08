using System.Text.Json;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

public sealed record DeckSessionSnapshot(int Version, IReadOnlyList<DeckSessionState> Decks)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists loaded deck tracks under <c>live/deck-session.json</c>. Loads are tolerant and saves use
/// temp-then-move replacement, matching the other live-state stores.
/// </summary>
public sealed class JsonDeckSessionStore : IDeckSessionStore
{
    private const string FileName = "deck-session.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Action<string>? _onWarning;

    public JsonDeckSessionStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _path = System.IO.Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live", FileName);
        _onWarning = onWarning;
    }

    public string Path => _path;

    public async Task<IReadOnlyList<DeckSessionState>?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return null;

        DeckSessionSnapshot? snapshot;
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read);
            snapshot = await JsonSerializer.DeserializeAsync<DeckSessionSnapshot>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Deck session file at '{_path}' is unreadable ({ex.Message}); ignoring.");
            return null;
        }

        if (snapshot is null)
            return null;

        if (snapshot.Version != DeckSessionSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Deck session is version {snapshot.Version} " +
                $"(expected {DeckSessionSnapshot.CurrentVersion}); ignoring.");
            return null;
        }

        return snapshot.Decks is null
            ? Array.Empty<DeckSessionState>()
            : snapshot.Decks
                .Where(deck => deck.Slot >= 0 && !string.IsNullOrWhiteSpace(deck.TrackPath))
                .ToList();
    }

    public async Task SaveAsync(
        IReadOnlyList<DeckSessionState> decks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decks);

        string directory = System.IO.Path.GetDirectoryName(_path)!;
        System.IO.Directory.CreateDirectory(directory);
        string tempPath = _path + ".tmp";
        var snapshot = new DeckSessionSnapshot(DeckSessionSnapshot.CurrentVersion, decks.ToList());

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(
                stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _path, overwrite: true);
    }
}
