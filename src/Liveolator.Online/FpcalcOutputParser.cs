using System.Text.Json;
using Liveolator.Core.Enrichment;

namespace Liveolator.Online;

/// <summary>
/// Parses the JSON emitted by <c>fpcalc -json</c> into an <see cref="AudioFingerprint"/>. Pure and
/// tolerant — malformed output, or a response missing the fingerprint/duration, yields <c>null</c>
/// rather than throwing. Separated from the process invocation so the parsing is unit-tested without
/// the native binary.
/// </summary>
public static class FpcalcOutputParser
{
    /// <summary>Parse fpcalc JSON, or return <c>null</c> when it is blank/invalid/incomplete.</summary>
    public static AudioFingerprint? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string? fingerprint = root.TryGetProperty("fingerprint", out JsonElement fp) ? fp.GetString() : null;
            if (string.IsNullOrWhiteSpace(fingerprint))
                return null;

            if (!root.TryGetProperty("duration", out JsonElement dur) || dur.ValueKind != JsonValueKind.Number)
                return null;

            int seconds = (int)Math.Round(dur.GetDouble());
            return seconds > 0 ? new AudioFingerprint(fingerprint!, seconds) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
