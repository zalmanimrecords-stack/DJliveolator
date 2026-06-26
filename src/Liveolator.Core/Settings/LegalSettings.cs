using Liveolator.Core.Legal;

namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted record of which Terms of Use version the user has accepted (doc 12 Settings tab). Pure data
/// — persisted via <c>ISettingsStore</c>. The first-launch acceptance gate reads
/// <see cref="HasAcceptedCurrentTerms"/>; the Settings view shows the accepted version.
/// </summary>
/// <param name="AcceptedTermsVersion">
/// The <see cref="TermsOfUse.CurrentVersion"/> the user last accepted, or <c>0</c> when none have been
/// accepted (a fresh install, or a settings file written before this field existed).
/// </param>
public sealed record LegalSettings(int AcceptedTermsVersion = 0)
{
    /// <summary>The default: no terms accepted yet, so the first-launch gate will prompt.</summary>
    public static LegalSettings Default { get; } = new();

    /// <summary>A copy with an acceptance recorded for the current terms version.</summary>
    public static LegalSettings AcceptedCurrent { get; } = new(TermsOfUse.CurrentVersion);

    /// <summary>
    /// True when the user has already accepted the current (or a newer) terms version, so no acceptance
    /// prompt is needed. A higher <see cref="TermsOfUse.CurrentVersion"/> than the accepted one re-prompts.
    /// </summary>
    public bool HasAcceptedCurrentTerms => AcceptedTermsVersion >= TermsOfUse.CurrentVersion;

    /// <summary>Returns a copy with a negative accepted-version (corrupt/hand-edited) folded back to 0.</summary>
    public LegalSettings Normalized()
        => AcceptedTermsVersion < 0 ? this with { AcceptedTermsVersion = 0 } : this;
}
