using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.SmartCollections;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

public sealed record SmartCollectionSnapshot(int Version, SmartCollectionDefinition Definition)
{
    public const int CurrentVersion = 1;
}

public sealed class JsonSmartCollectionStore : ISmartCollectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;

    public JsonSmartCollectionStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "collections");
        _onWarning = onWarning;
    }

    public string Directory => _directory;

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(_directory))
            return Array.Empty<string>();

        var names = new List<string>();
        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SmartCollectionSnapshot? snapshot = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (snapshot?.Version == SmartCollectionSnapshot.CurrentVersion)
                names.Add(snapshot.Definition.Name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public async Task<SmartCollectionDefinition?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string? path = await ResolvePathAsync(name, cancellationToken).ConfigureAwait(false);
        if (path is null)
            return null;

        SmartCollectionSnapshot? snapshot = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        if (snapshot.Version != SmartCollectionSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Smart collection '{name}' is version {snapshot.Version} " +
                $"(expected {SmartCollectionSnapshot.CurrentVersion}); ignoring.");
            return null;
        }

        return snapshot.Definition;
    }

    public async Task SaveAsync(SmartCollectionDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);

        System.IO.Directory.CreateDirectory(_directory);
        string path = await ResolvePathAsync(definition.Name, cancellationToken).ConfigureAwait(false)
            ?? PathFor(definition.Name);
        string tempPath = path + ".tmp";
        var snapshot = new SmartCollectionSnapshot(SmartCollectionSnapshot.CurrentVersion, definition);

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

    private string PathFor(string name) => Path.Combine(_directory, $"{Sanitize(name)}.{ShortHash(name)}.json");

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
            SmartCollectionSnapshot? snapshot = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && string.Equals(snapshot.Definition.Name, name, StringComparison.Ordinal))
                return file;
        }

        return null;
    }

    private async Task<SmartCollectionSnapshot?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return await JsonSerializer.DeserializeAsync<SmartCollectionSnapshot>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Smart collection at '{path}' is unreadable ({ex.Message}); skipping.");
            return null;
        }
    }

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "collection" : cleaned;
    }

    private static string ShortHash(string name)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}

