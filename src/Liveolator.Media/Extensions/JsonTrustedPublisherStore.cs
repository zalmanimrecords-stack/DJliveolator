using System.Text.Json;
using Liveolator.Core.Extensions;

namespace Liveolator.Media.Extensions;

/// <summary>
/// Read-only publisher trust store at <c>&lt;app-data&gt;/trusted-publishers.json</c>. Trust changes
/// are deliberately outside package installation, so a package can never add its own signing key.
/// </summary>
public sealed class JsonTrustedPublisherStore : ITrustedPublisherStore
{
    private readonly IReadOnlyDictionary<string, string> _keys;

    public JsonTrustedPublisherStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        FilePath = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "trusted-publishers.json");
        _keys = Load(FilePath, onWarning);
    }

    public string FilePath { get; }

    public bool TryGetPublicKey(string publisherKeyId, out string subjectPublicKeyInfoPem)
        => _keys.TryGetValue(publisherKeyId, out subjectPublicKeyInfoPem!);

    private static IReadOnlyDictionary<string, string> Load(string path, Action<string>? onWarning)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            string json = File.ReadAllText(path);
            Dictionary<string, string>? keys = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return keys is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(keys, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            onWarning?.Invoke($"Trusted publisher store is unreadable ({ex.Message}); no publishers are trusted.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
