using System.ComponentModel;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio.Set;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>
/// MCP tools for building a beat-matched DJ set from the catalog, inspecting it, and auditioning its
/// transitions. The set is saved as a STUDIO arrangement, so it opens in the app's STUDIO tab.
/// </summary>
[McpServerToolType]
public sealed class DjSetTools
{
    private const int MinSetLength = 2;
    private const int MaxSetLength = 60;
    private const int DefaultSetLength = 8;
    private const double MinWarpPercent = 0.5;
    private const double MaxWarpPercent = 15.0;
    private const int MinSampleRate = 8_000;
    private const int MaxSampleRate = 192_000;

    [McpServerTool(Name = "build_dj_set")]
    [Description("Build a beat-matched DJ set from the catalog and save it as a STUDIO arrangement. " +
                 "Orders tracks harmonically, warps them all to one set tempo, and places each so its " +
                 "phrases line up with the others — with the mix points taken from each track's analyzed " +
                 "structure (leaving on an outro after the last drop, entering where the drums start) and " +
                 "a bass swap plus equal-power crossfade at every join. Returns every transition it made " +
                 "and every candidate it rejected, so the set can be judged and rebuilt without listening.")]
    public static async Task<DjSetResult> BuildDjSet(
        DjSetSession session,
        [Description("Exact file path of the track to start from. Omit to start from the first usable track.")] string? seedPath = null,
        [Description("Build from exactly these catalogued tracks — the arranger still decides their order, " +
                     "but nothing else can enter the set. Pass the records you actually picked; without it " +
                     "the pool is the whole catalog, and any other library in it competes on tempo and key.")] string[]? trackPaths = null,
        [Description("How many tracks the set should contain, including the seed. Defaults to every track " +
                     "in trackPaths, or 8 when building from the whole catalog.")] int? length = null,
        [Description("Max tempo change per step while ordering, in BPM. Default 6.")] double bpmTolerance = 6.0,
        [Description("Tempo direction across the set: Any, Steady, Rising (build up), or Falling (wind down).")] string trend = "Any",
        [Description("Crossfade length in bars: 8, 16, 24 or 32. Default 16 (about 30 seconds at 128 BPM).")] int overlapBars = 16,
        [Description("How far a track may be time-stretched to reach the set tempo, as a percentage. " +
                     "Default 6, which suits 4/4 electronic music; use 3 for vocal or live-drummed material, " +
                     "where stretching shows up much sooner. Tracks needing more are rejected and reported.")] double maxWarpPercent = 6.0,
        [Description("Leave out tracks whose beat grid is not trustworthy, instead of mixing them short " +
                     "and unstretched. Default false.")] bool excludeLowGridConfidence = false,
        [Description("Name to save the set under. Reusing a name replaces that set.")] string name = "DJ Set",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        // Naming the candidates is itself the length request in the common case ("mix these ten records"),
        // so the default follows them rather than silently truncating the pick to 8.
        int resolvedLength = length ?? trackPaths?.Length ?? DefaultSetLength;
        if (resolvedLength is < MinSetLength or > MaxSetLength)
            throw new ArgumentException($"Set length must be between {MinSetLength} and {MaxSetLength} tracks.", nameof(length));
        if (bpmTolerance < 0)
            throw new ArgumentException("BPM tolerance cannot be negative.", nameof(bpmTolerance));
        if (!Enum.TryParse(trend, ignoreCase: true, out BpmTrend parsedTrend))
            throw new ArgumentException($"Unknown trend '{trend}'. Use Any, Steady, Rising, or Falling.", nameof(trend));
        if (overlapBars is < SetBuildOptions.MinOverlapBars or > SetBuildOptions.MaxOverlapBars)
            throw new ArgumentException(
                $"Overlap must be between {SetBuildOptions.MinOverlapBars} and {SetBuildOptions.MaxOverlapBars} bars — " +
                "shorter reads as a mistake rather than a mix, longer lets two arrangements fight each other.",
                nameof(overlapBars));
        if (maxWarpPercent is < MinWarpPercent or > MaxWarpPercent)
            throw new ArgumentException($"The warp limit must be between {MinWarpPercent} and {MaxWarpPercent} percent.", nameof(maxWarpPercent));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Set name cannot be empty.", nameof(name));

        var options = new SetBuildOptions(
            ProjectName: name.Trim(),
            OverlapBars: overlapBars,
            MaxWarpPercent: maxWarpPercent,
            ExcludeLowGridConfidence: excludeLowGridConfidence);

        return await session
            .BuildAsync(
                seedPath, new HarmonicSetOptions(resolvedLength, bpmTolerance, parsedTrend), options,
                cancellationToken, trackPaths)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_dj_sets")]
    [Description("List the names of every saved DJ set (STUDIO arrangement).")]
    public static async Task<IReadOnlyList<string>> ListDjSets(
        DjSetSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return await session.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_dj_set")]
    [Description("Read back a saved DJ set: its tempo, the tracks in play order with the stretch applied " +
                 "to each, and where consecutive tracks overlap. The mix-point provenance and build " +
                 "warnings are not stored, so rebuild the set to see those again.")]
    public static async Task<SavedSetInfo> GetDjSet(
        DjSetSession session,
        [Description("Name the set was saved under.")] string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Set name cannot be empty.", nameof(name));

        return await session.GetAsync(name.Trim(), cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"No saved set named '{name}'. Use list_dj_sets to see what is stored.", nameof(name));
    }

    [McpServerTool(Name = "render_set_preview")]
    [Description("Render each of a saved set's transitions to its own WAV, with a phrase of lead-in and " +
                 "lead-out — the audio worth listening to when judging whether the mixes work. Only the " +
                 "joins are rendered: a full set decodes every track at once and an hour-long mix will not " +
                 "fit in memory.")]
    public static async Task<SetPreviewResult> RenderSetPreview(
        DjSetSession session,
        [Description("Name of the saved set to audition.")] string name,
        [Description("Absolute path of the folder to write the WAV files into. Created if missing.")] string outputDirectory,
        [Description("Render sample rate in Hz. Default 44100.")] int sampleRate = 44_100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Set name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Path.IsPathRooted(outputDirectory))
            throw new ArgumentException("Provide an absolute output folder path.", nameof(outputDirectory));
        if (sampleRate is < MinSampleRate or > MaxSampleRate)
            throw new ArgumentException($"Sample rate must be between {MinSampleRate} and {MaxSampleRate} Hz.", nameof(sampleRate));

        return await session
            .RenderPreviewAsync(name.Trim(), outputDirectory, sampleRate, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "export_set_mix")]
    [Description("Render a saved set to ONE continuous mix WAV, ready to upload, alongside a " +
                 "machine-readable tracklist and a YouTube chapter/description text file. Unlike " +
                 "render_set_preview (which renders only the joins, for judging), this produces the whole " +
                 "mix; it streams to disk, so length is not bounded by memory. " +
                 "REFUSES BY DEFAULT when the mix is not fit to publish — a clip running at its native " +
                 "tempo against the set tempo, a clip left at unity gain (run measure_catalog_loudness " +
                 "first), or a blend clamped under the 8-bar floor — and returns each problem with its " +
                 "remedy instead of a file. Pass force to render anyway once you have listened and " +
                 "decided. Unreachable source files always fail, force or not, so a mix never ships with " +
                 "silent stretches. Reports the measured integrated LUFS of the file it produced.")]
    public static async Task<SetMixExport> ExportSetMix(
        DjSetSession session,
        [Description("Name of the saved set to export.")] string name,
        [Description("Absolute path of the folder to write the mix and tracklist into. Created if missing.")]
        string outputDirectory,
        [Description("Render anyway when the publish gate finds problems. Default false.")] bool force = false,
        [Description("Render sample rate in Hz. Default 44100 — the source rate of essentially all " +
                     "released dance music, so leave it alone unless you have a reason.")]
        int sampleRate = 44_100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Supply the name of a saved set to export.", nameof(name));
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Supply an output directory for the mix.", nameof(outputDirectory));
        if (sampleRate is < MinSampleRate or > MaxSampleRate)
            throw new ArgumentException($"Sample rate must be between {MinSampleRate} and {MaxSampleRate} Hz.", nameof(sampleRate));

        return await session
            .ExportMixAsync(name.Trim(), outputDirectory, sampleRate, force, cancellationToken)
            .ConfigureAwait(false);
    }
}
