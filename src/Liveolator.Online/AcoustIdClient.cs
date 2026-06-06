using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Online;

/// <summary>
/// AcoustID v2 <c>/lookup</c> implementation of <see cref="IAcoustIdClient"/>. The <see cref="HttpClient"/>
/// is injected (its <see cref="HttpClient.BaseAddress"/> points at the AcoustID host), so this parses
/// real responses in unit tests via a fake handler — no live network. Any non-success response, empty
/// result, or parse/transport error resolves to <c>null</c> (offline-first, doc 16).
/// </summary>
public sealed class AcoustIdClient : IAcoustIdClient
{
    private readonly HttpClient _http;
    private readonly string _clientKey;
    private readonly ILogger _logger;

    public AcoustIdClient(HttpClient http, string clientKey, ILogger<AcoustIdClient>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(clientKey))
            throw new ArgumentException("AcoustID client key is required.", nameof(clientKey));
        _clientKey = clientKey;
        _logger = logger ?? NullLogger<AcoustIdClient>.Instance;
    }

    public async Task<RecordingMatch?> LookupAsync(
        string fingerprint, int durationSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || durationSeconds <= 0)
            return null;

        try
        {
            string url = "v2/lookup"
                + $"?client={Uri.EscapeDataString(_clientKey)}"
                + "&meta=recordings"
                + $"&duration={durationSeconds.ToString(CultureInfo.InvariantCulture)}"
                + $"&fingerprint={Uri.EscapeDataString(fingerprint)}";

            using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AcoustID lookup returned {Status}.", (int)response.StatusCode);
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseBestMatch(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "AcoustID lookup failed; treating as no match.");
            return null;
        }
    }

    // Picks the highest-scoring result that carries a recording with a title + artist.
    private static RecordingMatch? ParseBestMatch(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array)
            return null;

        RecordingMatch? best = null;
        foreach (JsonElement result in results.EnumerateArray())
        {
            double score = result.TryGetProperty("score", out JsonElement s) && s.ValueKind == JsonValueKind.Number
                ? s.GetDouble() : 0.0;
            if (!result.TryGetProperty("recordings", out JsonElement recordings) || recordings.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement rec in recordings.EnumerateArray())
            {
                string? title = rec.TryGetProperty("title", out JsonElement t) ? t.GetString() : null;
                string? artist = JoinArtists(rec);
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
                    continue;
                if (best is null || score > best.Score)
                    best = new RecordingMatch(artist!, title!, score);
                break; // first usable recording of this result is enough
            }
        }
        return best;
    }

    private static string? JoinArtists(JsonElement recording)
    {
        if (!recording.TryGetProperty("artists", out JsonElement artists) || artists.ValueKind != JsonValueKind.Array)
            return null;
        var names = new List<string>();
        foreach (JsonElement a in artists.EnumerateArray())
            if (a.TryGetProperty("name", out JsonElement n) && n.GetString() is { Length: > 0 } name)
                names.Add(name);
        return names.Count == 0 ? null : string.Join(", ", names);
    }
}
