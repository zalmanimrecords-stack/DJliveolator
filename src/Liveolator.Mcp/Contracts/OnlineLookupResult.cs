using Liveolator.Core.Enrichment;

namespace Liveolator.Mcp.Contracts;

/// <summary>
/// Agent-facing result of an online BPM/key lookup (doc 16/17). Carries an explicit
/// <see cref="Configured"/>/<see cref="Found"/> shape so agents can distinguish "enrichment is off"
/// from "no match", plus the mandatory GetSongBPM <see cref="Attribution"/> the caller must surface.
/// </summary>
public sealed record OnlineLookupResult(
    bool Configured,
    bool Found,
    double? Bpm,
    string? Key,
    string? Camelot,
    string? Genre,
    string? Source,
    string Attribution,
    string? Message)
{
    /// <summary>Mandatory attribution for GetSongBPM data (their terms require a visible backlink).</summary>
    public const string AttributionText = "BPM/key data via GetSongBPM.com — https://getsongbpm.com";

    public static OnlineLookupResult NotConfigured() => new(
        Configured: false, Found: false, Bpm: null, Key: null, Camelot: null, Genre: null, Source: null,
        Attribution: AttributionText,
        Message: "Online enrichment is not configured. Start the server with --getsongbpm-key (and optionally --acoustid-key/--fpcalc).");

    public static OnlineLookupResult NotFound(string message) => new(
        Configured: true, Found: false, Bpm: null, Key: null, Camelot: null, Genre: null, Source: null,
        Attribution: AttributionText, Message: message);

    public static OnlineLookupResult From(OnlineTrackMetadata metadata) => new(
        Configured: true, Found: true, metadata.Bpm, metadata.KeyName, metadata.Camelot, metadata.Genre,
        metadata.Source, AttributionText, Message: null);
}
