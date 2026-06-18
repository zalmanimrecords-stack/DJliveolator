using System.Security.Cryptography;
using System.Text;
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
/// <c>&lt;root&gt;/live/studio-projects/&lt;sanitized-name&gt;.&lt;hash&gt;.json</c>. The filename is a
/// sanitized prefix plus a short disambiguator derived from the exact display name, so two distinct
/// names that sanitize alike (e.g. "My Set: 1" and "My Set_ 1") get distinct files and never silently
/// overwrite each other. The exact display name lives inside the JSON, so listing/loading resolve by
/// display name; legacy single-file <c>&lt;sanitized-name&gt;.json</c> projects still load via a
/// stored-name scan. Mirrors <see cref="JsonPlaylistStore"/>: tolerant loads (missing / unreadable /
/// incompatible-version -&gt; <c>null</c> + warning, never a throw) and atomic temp-then-move saves so an
/// interrupted write never corrupts a saved project (global standards #16/#26, #20/#22).
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

        string? path = await ResolvePathAsync(name, cancellationToken).ConfigureAwait(false);
        if (path is null)
            return null;

        StudioProjectSnapshot? snapshot = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
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
        // Save in place when the project already lives in a legacy single-file slot, so we never orphan
        // or duplicate an existing file; otherwise use the collision-proof name-keyed path.
        string path = await ResolvePathAsync(project.Name, cancellationToken).ConfigureAwait(false)
            ?? PathFor(project.Name);
        string tempPath = path + ".tmp";
        var snapshot = new StudioProjectSnapshot(
            StudioProjectSnapshot.CurrentVersion, project.Name, project.Bpm,
            project.Clips.ToList(), project.Automation.ToList(), project.EffectiveTempo);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string? path = await ResolvePathAsync(name, cancellationToken).ConfigureAwait(false);
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }

    // The deterministic, collision-proof on-disk path for a display name.
    private string PathFor(string name) => Path.Combine(_directory, FileNameFor(name));

    // <sanitized-prefix>.<8 hex of SHA-256(exact name)>.json. The hash disambiguates distinct names
    // that sanitize to the same prefix; the sanitized prefix keeps filenames human-recognizable.
    private static string FileNameFor(string name) => $"{Sanitize(name)}.{ShortHash(name)}.json";

    // Resolve the file backing a display name: first the deterministic name-keyed path, then a tolerant
    // scan that matches the exact display name stored inside the JSON. The scan recovers projects saved
    // under the legacy <sanitized-name>.json layout without renaming or orphaning them.
    private async Task<string?> ResolvePathAsync(string name, CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(_directory))
            return null;

        string preferred = PathFor(name);
        if (File.Exists(preferred))
            return preferred;

        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(file, preferred, StringComparison.OrdinalIgnoreCase))
                continue;
            StudioProjectSnapshot? snapshot = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && string.Equals(snapshot.Name, name, StringComparison.Ordinal))
                return file;
        }

        return null;
    }

    // Map a display name to a safe filename prefix. Two names that sanitize alike are still kept apart
    // by the hash suffix in <see cref="FileNameFor"/>, so this only needs to produce a legal prefix.
    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "studio-project" : cleaned;
    }

    // First 8 hex chars of SHA-256 over the exact display name: a short, stable, case-sensitive
    // disambiguator so distinct names never share a file.
    private static string ShortHash(string name)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var builder = new StringBuilder(8);
        for (int i = 0; i < 4; i++)
            builder.Append(hash[i].ToString("x2"));
        return builder.ToString();
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
