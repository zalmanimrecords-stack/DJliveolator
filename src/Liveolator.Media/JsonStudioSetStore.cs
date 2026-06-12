using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of a saved STUDIO set (the <c>live/studio-sets/</c> layout).</summary>
public sealed record StudioSetSnapshot(int Version, string Name, IReadOnlyList<StudioEntry> Entries)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists named STUDIO sets as one JSON file per set under
/// <c>&lt;root&gt;/live/studio-sets/&lt;sanitized-name&gt;.json</c>, separate from
/// <c>live/playlists/</c>. Mirrors <see cref="JsonPlaylistStore"/>: tolerant loads (missing /
/// unreadable / incompatible-version → <c>null</c> + warning, never a throw) and atomic
/// temp-then-move saves so an interrupted write never corrupts a saved set (global standards
/// #16/#26, #20/#22).
/// </summary>
public sealed class JsonStudioSetStore : IStudioSetStore
{
    // Enums are written as strings so the on-disk contract survives enum reordering — important
    // because StudioTransition embeds TransitionKind/TransitionAnchor/CrossfaderCurve (global #20/#22).
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;

    public JsonStudioSetStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live", "studio-sets");
        _onWarning = onWarning;
    }

    /// <summary>The directory holding the per-set JSON files.</summary>
    public string Directory => _directory;

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(_directory))
            return Array.Empty<string>();

        var names = new List<string>();
        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StudioSetSnapshot? snapshot = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && snapshot.Version == StudioSetSnapshot.CurrentVersion)
                names.Add(snapshot.Name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public async Task<StudioSet?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        StudioSetSnapshot? snapshot = await ReadAsync(PathFor(name), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        if (snapshot.Version != StudioSetSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Studio set '{name}' is version {snapshot.Version} (expected {StudioSetSnapshot.CurrentVersion}); ignoring.");
            return null;
        }

        return new StudioSet(snapshot.Name, snapshot.Entries);
    }

    public async Task SaveAsync(StudioSet set, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(set.Name);

        System.IO.Directory.CreateDirectory(_directory);
        string path = PathFor(set.Name);
        string tempPath = path + ".tmp";
        var snapshot = new StudioSetSnapshot(StudioSetSnapshot.CurrentVersion, set.Name, set.Entries.ToList());

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string name) => Path.Combine(_directory, Sanitize(name) + ".json");

    // Map a display name to a safe filename: strip characters illegal in a filename. The real display
    // name is stored inside the JSON, so two names that sanitize alike simply share a slot (last save wins).
    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "studio-set" : cleaned;
    }

    private async Task<StudioSetSnapshot?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return await JsonSerializer.DeserializeAsync<StudioSetSnapshot>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Studio set file at '{path}' is unreadable ({ex.Message}); skipping.");
            return null;
        }
    }
}
