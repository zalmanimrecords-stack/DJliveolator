using System.Text;

namespace Liveolator.Core.Visuals;

/// <summary>
/// Shared naming for <c>.frktl</c> presets (doc 29) so the folder loader and any writer agree on the id
/// derived from a file/preset name: a filesystem-safe, stable slug (lowercase, non-alphanumerics → '-',
/// collapsed). Keeping it in one place guarantees a written file lands back on the same preset id.
/// </summary>
public static class FrktlPresetNaming
{
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
        return slug.Length == 0 ? "preset" : slug;
    }
}
