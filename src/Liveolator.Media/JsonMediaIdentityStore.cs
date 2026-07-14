using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.Doctor;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

public sealed record MediaIdentitySnapshot(int Version, IReadOnlyList<MediaIdentity> Identities)
{
    public const int CurrentVersion = 1;
}

public sealed class JsonMediaIdentityStore : IMediaIdentityStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Action<string>? _onWarning;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public JsonMediaIdentityStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _path = System.IO.Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "library.identities.json");
        _onWarning = onWarning;
    }

    public string Path => _path;

    public async Task<IReadOnlyList<MediaIdentity>> LoadIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return Array.Empty<MediaIdentity>();

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read);
            MediaIdentitySnapshot? snapshot = await JsonSerializer.DeserializeAsync<MediaIdentitySnapshot>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
                return Array.Empty<MediaIdentity>();
            if (snapshot.Version != MediaIdentitySnapshot.CurrentVersion)
            {
                _onWarning?.Invoke(
                    $"Media identity index at '{_path}' is version {snapshot.Version} " +
                    $"(expected {MediaIdentitySnapshot.CurrentVersion}); rebuilding.");
                return Array.Empty<MediaIdentity>();
            }

            return snapshot.Identities ?? Array.Empty<MediaIdentity>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Media identity index at '{_path}' is unreadable ({ex.Message}); rebuilding.");
            return Array.Empty<MediaIdentity>();
        }
    }

    public async Task SaveIdentitiesAsync(
        IEnumerable<MediaIdentity> identities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identities);

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            string directory = System.IO.Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            var snapshot = new MediaIdentitySnapshot(MediaIdentitySnapshot.CurrentVersion, identities.ToList());

            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            File.Move(tempPath, _path, overwrite: true);
            tempPath = null;
        }
        finally
        {
            if (tempPath is not null && File.Exists(tempPath))
                File.Delete(tempPath);
            _saveGate.Release();
        }
    }
}
