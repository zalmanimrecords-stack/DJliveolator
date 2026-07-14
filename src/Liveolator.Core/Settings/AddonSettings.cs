namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted per-add-on preferences (doc 26 — the visual add-on standard). Currently the only
/// configurable add-on is the built-in VU meter, whose static dial-face image the performer may
/// replace from the Add-ons tab while the needle stays standard. Pure data — persisted via
/// <c>ISettingsStore</c>; <see cref="Normalized"/> folds a blank/whitespace path to null so a stale
/// or hand-edited config can never push an empty path into the visual engine.
/// </summary>
/// <param name="VuMeterBackgroundImagePath">Absolute path to a custom VU-meter face (background) image,
/// or null to use the built-in face. The needle generator is unaffected.</param>
/// <param name="VuMeterNeedleOrigin">Whether the VU-meter needle pivots from the bottom (classic) or the top.</param>
public sealed record AddonSettings(
    string? VuMeterBackgroundImagePath = null,
    VuMeterNeedleOrigin VuMeterNeedleOrigin = VuMeterNeedleOrigin.Bottom)
{
    /// <summary>The default add-on preferences: every add-on at its built-in defaults.</summary>
    public static AddonSettings Default { get; } = new();

    /// <summary>Returns a copy with the custom face path trimmed, folding blank/whitespace to null.</summary>
    public AddonSettings Normalized()
        => this with
        {
            VuMeterBackgroundImagePath = string.IsNullOrWhiteSpace(VuMeterBackgroundImagePath)
                ? null
                : VuMeterBackgroundImagePath.Trim(),
        };
}
