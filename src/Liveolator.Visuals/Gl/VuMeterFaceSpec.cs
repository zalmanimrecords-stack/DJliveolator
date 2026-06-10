namespace Liveolator.Visuals.Gl;

/// <summary>
/// The authoring spec for a <b>custom VU-meter face (background) image</b> (doc 26): the recommended
/// pixel size plus the fixed pivot and arc the standard needle generator sweeps. A replacement face must
/// place its brass hub and printed scale at these positions so the unchanged needle (<see cref="VuMeterAddon"/>)
/// still registers with it. Sourced from <see cref="VuMeterGeometry"/> via <see cref="VuMeterAddon.FaceSpec"/>
/// so the numbers shown to the performer can never drift from what the shader/face renderer actually use.
/// </summary>
/// <param name="RecommendedWidth">Recommended face width in pixels.</param>
/// <param name="RecommendedHeight">Recommended face height in pixels.</param>
/// <param name="PivotXFraction">Needle pivot X as a fraction of width (0 = left, 1 = right).</param>
/// <param name="PivotYFraction">Needle pivot Y as a fraction of height (0 = top, 1 = bottom).</param>
/// <param name="PivotXPixels">Needle pivot X in pixels at the recommended size.</param>
/// <param name="PivotYPixels">Needle pivot Y in pixels at the recommended size.</param>
/// <param name="ArcRadiusFraction">Scale-arc radius as a fraction of height.</param>
/// <param name="ArcRadiusPixels">Scale-arc radius in pixels at the recommended size.</param>
/// <param name="NeedleMinDegrees">Needle angle (from straight up, + = right) at level 0 — far left.</param>
/// <param name="NeedleMaxDegrees">Needle angle at level 1 — far right.</param>
public sealed record VuMeterFaceSpec(
    int RecommendedWidth,
    int RecommendedHeight,
    double PivotXFraction,
    double PivotYFraction,
    int PivotXPixels,
    int PivotYPixels,
    double ArcRadiusFraction,
    int ArcRadiusPixels,
    double NeedleMinDegrees,
    double NeedleMaxDegrees)
{
    /// <summary>The recommended aspect ratio (width / height) — e.g. 1.5 for the 3:2 reference face.</summary>
    public double AspectRatio => (double)RecommendedWidth / RecommendedHeight;
}
