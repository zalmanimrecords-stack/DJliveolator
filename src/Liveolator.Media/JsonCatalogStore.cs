using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.Music;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the persisted music catalog (the doc 13 / doc 16 cache).</summary>
public sealed record MusicCatalogSnapshot(int Version, IReadOnlyList<MusicTrack> Tracks)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists the analyzed music catalog as JSON under the per-user app-data root, so a later run
/// re-loads it and only re-analyzes new/changed files. A missing or corrupt cache yields an
/// empty catalog (triggering a fresh scan) and reports a warning — it never crashes the app
/// (global standards #16, #26).
/// </summary>
public sealed class JsonCatalogStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;

    public JsonCatalogStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = rootDirectory ?? DefaultRoot();
        _onWarning = onWarning;
    }

    /// <summary>Full path of the music-catalog JSON file.</summary>
    public string MusicCatalogPath => Path.Combine(_directory, "catalog.music.json");

    /// <summary>Default persistence root: <c>%APPDATA%/Liveolator</c> (or the XDG/Mac equivalent).</summary>
    public static string DefaultRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator");

    public async Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        Directory.CreateDirectory(_directory);

        var snapshot = new MusicCatalogSnapshot(MusicCatalogSnapshot.CurrentVersion, tracks.ToList());

        // Write to a temp file then move, so an interrupted write never corrupts the live cache.
        string tempPath = MusicCatalogPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, MusicCatalogPath, overwrite: true);
    }

    /// <summary>Loads the persisted catalog, or an empty list when none exists or it is unreadable.</summary>
    public async Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(MusicCatalogPath))
            return Array.Empty<MusicTrack>();

        try
        {
            await using var stream = new FileStream(MusicCatalogPath, FileMode.Open, FileAccess.Read);
            MusicCatalogSnapshot? snapshot = await JsonSerializer
                .DeserializeAsync<MusicCatalogSnapshot>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return snapshot?.Tracks ?? Array.Empty<MusicTrack>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Catalog cache at '{MusicCatalogPath}' is unreadable ({ex.Message}); re-analyzing from scratch.");
            return Array.Empty<MusicTrack>();
        }
    }
}
