namespace Liveolator.Core.Analysis.Structure;

/// <summary>
/// The known section labels the structure analyzer emits (doc 32). The Python side writes these
/// exact strings; unknown labels are tolerated and mapped to a generic boundary downstream.
/// </summary>
public static class SongSectionLabel
{
    public const string Intro = "intro";
    public const string BuildUp = "buildup";
    public const string Drop = "drop";
    public const string Breakdown = "breakdown";
    public const string Outro = "outro";

    /// <summary>Generic boundary — a phrase/section with no more specific role.</summary>
    public const string Section = "section";
}
