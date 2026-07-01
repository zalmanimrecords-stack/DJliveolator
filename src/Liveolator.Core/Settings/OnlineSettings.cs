namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted online-enrichment preferences (doc 12/16): the GetSongBPM API key used to fill in missing
/// genre/BPM/key during a library scan. Pure data — persisted via <c>ISettingsStore</c>. The key is a
/// per-user courtesy credential, not a shipped secret, so it lives in the user's settings file (never
/// hardcoded — global #17). Blank means "not configured" (enrichment is skipped).
/// </summary>
public sealed record OnlineSettings(string? GetSongBpmApiKey = null)
{
    public static OnlineSettings Default { get; } = new();

    public OnlineSettings Normalized()
        => this with { GetSongBpmApiKey = string.IsNullOrWhiteSpace(GetSongBpmApiKey) ? null : GetSongBpmApiKey.Trim() };
}
