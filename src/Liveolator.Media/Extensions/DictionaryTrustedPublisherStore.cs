using Liveolator.Core.Extensions;

namespace Liveolator.Media.Extensions;

public sealed class DictionaryTrustedPublisherStore : ITrustedPublisherStore
{
    private readonly IReadOnlyDictionary<string, string> _keys;

    public DictionaryTrustedPublisherStore(IReadOnlyDictionary<string, string>? keys = null)
        => _keys = keys ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public bool TryGetPublicKey(string publisherKeyId, out string subjectPublicKeyInfoPem)
        => _keys.TryGetValue(publisherKeyId, out subjectPublicKeyInfoPem!);
}
