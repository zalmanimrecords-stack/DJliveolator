using System.Globalization;
using System.Text.Json;
using Liveolator.Core.Enrichment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Online;

/// <summary>
/// GetSongBPM <c>/search</c> implementation of <see cref="IGetSongBpmClient"/>. The <see cref="HttpClient"/>
/// is injected (its <see cref="HttpClient.BaseAddress"/> points at the GetSongBPM host), so this parses
/// real responses in unit tests via a fake handler — no live network. Any non-success response, empty
/// result, or parse/transport error resolves to <c>null</c> (offline-first, doc 16).
/// </summary>
public sealed class GetSongBpmClient : IGetSongBpmClient
{
    /// <summary>Provider name stamped on results (also the attribution target — getsongbpm.com).</summary>
    public const string SourceName = "GetSongBPM";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger _logger;

    public GetSongBpmClient(HttpClient http, string apiKey, ILogger<GetSongBpmClient>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("GetSongBPM API key is required.", nameof(apiKey));
        _apiKey = apiKey;
        _logger = logger ?? NullLogger<GetSongBpmClient>.Instance;
    }

    public async Task<OnlineTrackMetadata?> SearchAsync(
        string artist, string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return null;

        try
        {
            // lookup=song:<title> artist:<artist> (the whole value is URL-encoded as one parameter).
            string lookup = Uri.EscapeDataString($"song:{title} artist:{artist}");
            string url = $"search/?api_key={Uri.EscapeDataString(_apiKey)}&type=song&lookup={lookup}";

            using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetSongBPM search returned {Status}.", (int)response.StatusCode);
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseFirst(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "GetSongBPM search failed; treating as no match.");
            return null;
        }
    }

    // Reads the first search hit. tempo/key arrive as strings ("140"); genre comes off the artist.
    private static OnlineTrackMetadata? ParseFirst(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("search", out JsonElement search)
            || search.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement hit in search.EnumerateArray())
        {
            double? bpm = ParseTempo(hit);
            string? keyName = hit.TryGetProperty("key_of", out JsonElement k) ? NullIfBlank(k.GetString()) : null;
            string? genre = ParseGenre(hit);
            if (bpm is null && keyName is null && genre is null)
                continue;
            return new OnlineTrackMetadata(bpm, Camelot: null, keyName, genre, SourceName);
        }
        return null;
    }

    private static double? ParseTempo(JsonElement hit)
    {
        if (!hit.TryGetProperty("tempo", out JsonElement tempo))
            return null;
        string? raw = tempo.ValueKind == JsonValueKind.Number ? tempo.GetRawText() : tempo.GetString();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
            ? value : null;
    }

    private static string? ParseGenre(JsonElement hit)
    {
        if (!hit.TryGetProperty("artist", out JsonElement artist)
            || !artist.TryGetProperty("genres", out JsonElement genres)
            || genres.ValueKind != JsonValueKind.Array)
            return null;
        foreach (JsonElement g in genres.EnumerateArray())
            if (NullIfBlank(g.GetString()) is { } genre)
                return genre;
        return null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
