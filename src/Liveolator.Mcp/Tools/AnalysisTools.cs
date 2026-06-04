using System.ComponentModel;
using Liveolator.Core.Analysis;
using Liveolator.Mcp.Contracts;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>MCP tool for one-off analysis of a single file without adding it to the catalog.</summary>
[McpServerToolType]
public sealed class AnalysisTools
{
    [McpServerTool(Name = "analyze_track")]
    [Description("Analyze a single audio file on demand and return its BPM, musical key + Camelot " +
                 "code, intro/outro cues and duration — without adding it to the catalog. Useful for " +
                 "inspecting one file. WAV works with no setup; other formats need FFmpeg.")]
    public static async Task<TrackInfo> AnalyzeTrack(
        IAudioDecoder decoder,
        TrackAnalyzer analyzer,
        [Description("Absolute path of the audio file to analyze.")] string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Audio file not found.", path);
        if (!decoder.CanDecode(path))
            throw new NotSupportedException($"Unsupported audio format for '{path}'.");

        TrackAnalysisResult result = await analyzer.AnalyzeAsync(decoder, path, cancellationToken).ConfigureAwait(false);
        return TrackInfo.FromAnalysis(path, result);
    }
}
