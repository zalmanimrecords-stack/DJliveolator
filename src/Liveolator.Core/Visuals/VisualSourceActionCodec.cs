using System.Text.Json;

namespace Liveolator.Core.Visuals;

/// <summary>Serializes a layer source through the primitive PerformanceAction.Argument field.</summary>
public static class VisualSourceActionCodec
{
    public static string Encode(VisualSourceRef source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return JsonSerializer.Serialize(source);
    }

    public static bool TryDecode(string? value, out VisualSourceRef? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            source = JsonSerializer.Deserialize<VisualSourceRef>(value);
            return source is not null && !string.IsNullOrWhiteSpace(source.Reference);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
