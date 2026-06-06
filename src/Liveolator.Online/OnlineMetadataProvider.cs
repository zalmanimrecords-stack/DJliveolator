using Liveolator.Core.Enrichment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Online;

/// <summary>
/// The composing <see cref="IMetadataProvider"/> (doc 16): identifies a track by acoustic fingerprint
/// via <see cref="IAcoustIdClient"/> (preferred — filename-independent), then looks up its tempo/key by
/// the resolved artist + title via <see cref="IGetSongBpmClient"/>. When no fingerprint is available it
/// falls back to the track's own tags. Pure composition over the two client seams, so it unit-tests
/// with fakes; both clients keep the HTTP behind their own seams.
/// </summary>
/// <remarks>
/// <b>Offline-first</b>: every failure path (no fingerprint match, no tags, client error) resolves to
/// <c>null</c> so a lookup never disturbs the local analysis the app already has.
/// </remarks>
public sealed class OnlineMetadataProvider : IMetadataProvider
{
    private readonly IGetSongBpmClient _bpm;
    private readonly IAcoustIdClient? _acoustId;
    private readonly ILogger _logger;

    /// <param name="bpm">Tempo/key source (required).</param>
    /// <param name="acoustId">Fingerprint identifier (optional — omit to match by tags only).</param>
    public OnlineMetadataProvider(
        IGetSongBpmClient bpm, IAcoustIdClient? acoustId = null, ILogger<OnlineMetadataProvider>? logger = null)
    {
        _bpm = bpm ?? throw new ArgumentNullException(nameof(bpm));
        _acoustId = acoustId;
        _logger = logger ?? NullLogger<OnlineMetadataProvider>.Instance;
    }

    public async Task<OnlineTrackMetadata?> LookupAsync(TrackQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            string? artist = query.Artist;
            string? title = query.Title;

            // Prefer fingerprint identification: more reliable than tags/filenames (doc 16).
            if (_acoustId is not null && query.HasFingerprint && query.Duration is { } duration)
            {
                RecordingMatch? match = await _acoustId
                    .LookupAsync(query.AcoustId!, (int)duration.TotalSeconds, cancellationToken)
                    .ConfigureAwait(false);
                if (match is not null)
                {
                    artist = match.Artist;
                    title = match.Title;
                }
            }

            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            {
                _logger.LogDebug("No artist/title to query online (no fingerprint match and no tags).");
                return null;
            }

            return await _bpm.SearchAsync(artist!, title!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Defence in depth: clients already swallow their own errors, but never let enrichment throw.
            _logger.LogWarning(ex, "Online metadata lookup failed; treating as not found.");
            return null;
        }
    }
}
