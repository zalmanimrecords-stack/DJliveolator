using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of a saved STUDIO arrangement (the <c>live/studio-projects/</c> layout).</summary>
public sealed record StudioProjectSnapshot(
    int Version,
    string Name,
    double Bpm,
    IReadOnlyList<StudioClip> Clips,
    IReadOnlyList<AutomationLane> Automation,
    TempoCurve? Tempo = null)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists named STUDIO arrangements as one JSON file per project under
/// <c>&lt;root&gt;/live/studio-projects/&lt;sanitized-name&gt;.json</c>. Mirrors
/// <see cref="JsonPlaylistStore"/>: tolerant loads (missing / unreadable / incompatible-version →
/// <c>null</c> + warning, never a throw) and atomic temp-then-move saves so an interrupted write
/// never corrupts a saved project (global standards #16/#26, #20/#22).
/// </summary>
public sealed class JsonStudioProjectStore : IStudioProjectStore
{
    // Enums (AutomationTarget) are written as strings so the on-disk contract survives reordering.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;

    public JsonStudioProjectStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live", "studio-projects");
        _onWarning = onWarning;
    }

    /// <summary>The directory holding the per-project JSON files.</summary>
    public string Directory => _directory;

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(_directory))
            return Array.Empty<string>();

        var names = new List<string>();
        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StudioProjectSnapshot? snapshot = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && snapshot.Version == StudioProjectSnapshot.CurrentVersion)
                names.Add(snapshot.Name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public async Task<StudioProject?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        StudioProjectSnapshot? snapshot = await ReadAsync(PathFor(name), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        if (snapshot.Version != StudioProjectSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Studio project '{name}' is version {snapshot.Version} (expected {StudioProjectSnapshot.CurrentVersion}); ignoring.");
            return null;
        }

        return new StudioProject(snapshot.Name, snapshot.Bpm, snapshot.Clips, snapshot.Automation, snapshot.Tempo);
    }

    public async Task SaveAsync(StudioProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(project.Name);

        System.IO.Directory.CreateDirectory(_directory);
        string path = PathFor(project.Name);
        string tempPath = path + ".tmp";
        var snapshot = new StudioProjectSnapshot(
            StudioProjectSnapshot.CurrentVersion, project.Name, project.Bpm,
            project.Clips.ToList(), project.Automation.ToList(), project.EffectiveTempo);

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

    // Map a display name to a safe filename; the real display name is stored inside the JSON, so two
    // names that sanitize alike simply share a slot (last save wins).
    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "studio-project" : cleaned;
    }

    private async Task<StudioProjectSnapshot?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return await JsonSerializer.DeserializeAsync<StudioProjectSnapshot>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Studio project file at '{path}' is unreadable ({ex.Message}); skipping.");
            return null;
        }
    }
}
