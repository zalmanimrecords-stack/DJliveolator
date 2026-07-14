using System.Collections.Generic;
using System.Text.Json;
using Liveolator.Core.Analysis.Stems;

namespace Liveolator.Media.Analysis;

/// <summary>
/// Parses the JSON manifest emitted by <c>separate_stems.py</c> (and persisted by
/// <see cref="StemStore"/>) into a <see cref="StemSet"/>. Pure and tolerant — blank, malformed, or
/// incomplete output (any of the four stems missing) yields <c>null</c>. Separated from the subprocess
/// invocation so it unit-tests without a Python runtime (mirrors <see cref="StructureOutputParser"/>).
/// </summary>
public static class StemManifestParser
{
    private static readonly IReadOnlyDictionary<string, StemKind> KindByName = new Dictionary<string, StemKind>
    {
        ["drums"] = StemKind.Drums,
        ["bass"] = StemKind.Bass,
        ["vocals"] = StemKind.Vocals,
        ["other"] = StemKind.Other,
    };

    /// <summary>
    /// Parse the manifest JSON, attributing it to <paramref name="sourcePath"/>. Returns <c>null</c> when
    /// the JSON is blank/invalid or does not contain all four stems.
    /// </summary>
    public static StemSet? Parse(string? json, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(sourcePath))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("stems", out JsonElement stems) ||
                stems.ValueKind != JsonValueKind.Object)
                return null;

            var paths = new Dictionary<StemKind, string>();
            foreach (KeyValuePair<string, StemKind> entry in KindByName)
            {
                if (stems.TryGetProperty(entry.Key, out JsonElement p) &&
                    p.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(p.GetString()))
                    paths[entry.Value] = p.GetString()!;
            }

            string model = root.TryGetProperty("model", out JsonElement m) && m.ValueKind == JsonValueKind.String
                ? (m.GetString() ?? "umxhq")
                : "umxhq";

            var set = new StemSet(sourcePath, model, paths);
            return set.IsComplete ? set : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Serialize a <see cref="StemSet"/> to the manifest JSON shape (for caching to disk).</summary>
    public static string Serialize(StemSet set)
    {
        var stems = new Dictionary<string, string>();
        foreach (KeyValuePair<string, StemKind> entry in KindByName)
            if (set.StemPaths.TryGetValue(entry.Value, out string? path))
                stems[entry.Key] = path;

        return JsonSerializer.Serialize(new { model = set.ModelId, stems });
    }
}
