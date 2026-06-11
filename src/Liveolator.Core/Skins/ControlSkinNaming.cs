using System.Text;

namespace Liveolator.Core.Skins;

/// <summary>
/// Shared naming for control skins (doc 30) so any writer and the app's loader agree on the id derived
/// from a skin name: a filesystem-safe, stable slug (lowercase, non-alphanumerics → '-', collapsed).
/// Mirrors <c>FrktlPresetNaming</c> so authored files land back on a predictable id.
/// </summary>
public static class ControlSkinNaming
{
    /// <summary>Package id all authored control skins share (used to namespace their ids).</summary>
    public const string PackageId = "liveolator.control-skins";

    public static string Slug(string? name)
    {
        var builder = new StringBuilder((name ?? string.Empty).Length);
        bool lastDash = false;
        foreach (char ch in (name ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && builder.Length > 0)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "skin" : slug;
    }
}
