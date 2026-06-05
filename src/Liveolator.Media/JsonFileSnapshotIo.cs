using System.Text.Json;
using System.Text.Json.Serialization;

namespace Liveolator.Media;

/// <summary>
/// Shared JSON snapshot IO: atomic temp-then-move saves and tolerant loads that never throw on a
/// readable-but-old or corrupt file (global standards #16/#26). Mirrors the discipline in
/// <see cref="JsonCatalogStore"/> so all persisted Live data behaves identically.
/// </summary>
internal sealed class JsonFileSnapshotIo
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Action<string>? _onWarning;

    public JsonFileSnapshotIo(Action<string>? onWarning) => _onWarning = onWarning;

    /// <summary>Serializes <paramref name="snapshot"/> to <paramref name="path"/> atomically.</summary>
    public async Task SaveAsync<T>(string path, T snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write to a temp file then move, so an interrupted write never corrupts the live file.
        string tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Deserializes a snapshot from <paramref name="path"/>, or <c>null</c> when the file is missing
    /// or unreadable (a warning is reported for the unreadable case).
    /// </summary>
    public async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Live profile at '{path}' is unreadable ({ex.Message}); ignoring it.");
            return null;
        }
    }

    /// <summary>Reports an incompatible-version warning for <paramref name="path"/>.</summary>
    public void WarnVersionMismatch(string path, int found, int expected)
        => _onWarning?.Invoke(
            $"Live profile at '{path}' is version {found} (expected {expected}); ignoring it.");
}
