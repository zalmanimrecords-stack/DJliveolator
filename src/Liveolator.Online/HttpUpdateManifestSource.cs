using System.Text.Json;
using Liveolator.Core.Update;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Online;

/// <summary>
/// Fetches the published <see cref="UpdateManifest"/> from the marketing website's static
/// <c>version.json</c> over HTTP. Mirrors <see cref="GetSongBpmClient"/>: the <see cref="HttpClient"/> is
/// injected so a fake handler parses real JSON in unit tests without any network, and every failure path
/// (transport error, non-success status, malformed body, missing fields) resolves to <c>null</c> so a
/// failed update check never blocks or crashes startup (offline-first; global standards #16/#26).
/// </summary>
/// <remarks>
/// The manifest is a small object served by the website (kept in step by
/// <c>scripts/publish-website-release.ps1</c>):
/// <code>{ "version": "0.1.5", "downloadUrl": "https://.../LiveolatorSetup-0.1.5.exe", "notes": ["..."] }</code>
/// A manifest missing <c>version</c> or <c>downloadUrl</c> is treated as no manifest.
/// </remarks>
public sealed class HttpUpdateManifestSource : IUpdateManifestSource
{
    private readonly HttpClient _http;
    private readonly string _manifestUrl;
    private readonly ILogger _logger;

    /// <param name="http">Client used for the request (its lifetime is owned by the composition root).</param>
    /// <param name="manifestUrl">Absolute URL of the website's <c>version.json</c>.</param>
    public HttpUpdateManifestSource(
        HttpClient http, string manifestUrl, ILogger<HttpUpdateManifestSource>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(manifestUrl))
            throw new ArgumentException("A manifest URL is required.", nameof(manifestUrl));
        _manifestUrl = manifestUrl;
        _logger = logger ?? NullLogger<HttpUpdateManifestSource>.Instance;
    }

    public async Task<UpdateManifest?> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync(_manifestUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Update manifest request returned {Status}.", (int)response.StatusCode);
                return null;
            }

            await using Stream stream =
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc =
                await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Parse(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Update manifest fetch failed; treating as no update.");
            return null;
        }
    }

    // Reads the manifest fields. version + downloadUrl are required; notes is an optional string array.
    private static UpdateManifest? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        string? version = ReadString(root, "version");
        string? downloadUrl = ReadString(root, "downloadUrl");
        if (version is null || downloadUrl is null)
            return null;

        return new UpdateManifest(version, downloadUrl, ReadNotes(root));
    }

    private static IReadOnlyList<string> ReadNotes(JsonElement root)
    {
        if (!root.TryGetProperty("notes", out JsonElement notes) || notes.ValueKind != JsonValueKind.Array)
            return UpdateManifest.NoNotes;

        var lines = new List<string>();
        foreach (JsonElement note in notes.EnumerateArray())
            if (note.ValueKind == JsonValueKind.String && NullIfBlank(note.GetString()) is { } line)
                lines.Add(line);
        return lines.Count > 0 ? lines : UpdateManifest.NoNotes;
    }

    private static string? ReadString(JsonElement root, string property)
        => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? NullIfBlank(value.GetString())
            : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
