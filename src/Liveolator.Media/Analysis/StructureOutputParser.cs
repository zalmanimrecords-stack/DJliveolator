using System.Collections.Generic;
using System.Text.Json;
using Liveolator.Core.Analysis.Structure;

namespace Liveolator.Media.Analysis;

/// <summary>
/// Parses the JSON emitted by <c>analyze_structure.py</c> into a <see cref="SongStructure"/>. Pure and
/// tolerant — blank, malformed, or section-less output yields <c>null</c>; individual malformed sections
/// are skipped. Separated from the subprocess invocation so it unit-tests without a Python runtime
/// (mirrors <c>FpcalcOutputParser</c>).
/// </summary>
public static class StructureOutputParser
{
    /// <summary>Parse the analyzer JSON, or return <c>null</c> when it is blank/invalid/section-less.</summary>
    public static SongStructure? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("sections", out JsonElement sections) ||
                sections.ValueKind != JsonValueKind.Array)
                return null;

            var parsed = new List<SongSection>();
            foreach (JsonElement section in sections.EnumerateArray())
            {
                if (section.ValueKind != JsonValueKind.Object ||
                    !section.TryGetProperty("startSeconds", out JsonElement start) ||
                    start.ValueKind != JsonValueKind.Number)
                    continue; // skip a malformed entry rather than fail the whole parse

                string label = section.TryGetProperty("label", out JsonElement l) && l.ValueKind == JsonValueKind.String
                    ? (l.GetString() ?? SongSectionLabel.Section)
                    : SongSectionLabel.Section;

                parsed.Add(new SongSection(start.GetDouble(), label));
            }

            if (parsed.Count == 0)
                return null;

            string analyzedWith = root.TryGetProperty("analyzedWith", out JsonElement aw) && aw.ValueKind == JsonValueKind.String
                ? (aw.GetString() ?? "librosa")
                : "librosa";

            return new SongStructure(parsed, analyzedWith);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
