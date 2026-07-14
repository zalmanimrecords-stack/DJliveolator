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

    // Serializes concurrent saves: two writes to the same path (e.g. a user save while another is in
    // flight) would otherwise race on the temp file and corrupt the live file or throw. Mirrors
    // JsonCatalogStore's gate so all persisted Live data behaves identically (doc 27 medium fix).
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    /// <summary>Serializes <paramref name="snapshot"/> to <paramref name="path"/> atomically.</summary>
    public async Task SaveAsync<T>(string path, T snapshot, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write to a UNIQUE temp file then move, so an interrupted or concurrent write never corrupts
            // the live file and an abandoned temp can't collide with the next save.
            tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
        }
        finally
        {
            if (tempPath is not null)
                File.Delete(tempPath);
            _saveGate.Release();
        }
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
