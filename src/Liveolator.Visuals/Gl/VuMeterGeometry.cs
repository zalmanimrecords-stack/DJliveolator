namespace Liveolator.Visuals.Gl;

/// <summary>
/// Shared geometry for the analog VU-meter add-on so the static face (rendered by
/// <see cref="VuMeterFace"/> with SkiaSharp) and the live needle (drawn by the generator shader in
/// <see cref="VuMeterAddon"/>) align exactly. Both work in <b>face pixel space</b> (origin top-left,
/// y down) over a fixed <see cref="FaceWidth"/>×<see cref="FaceHeight"/>; the compositor stretches both
/// to the window identically, so they stay registered at any aspect.
/// </summary>
/// <remarks>
/// Angles are measured in degrees from straight up, positive toward the right; the needle sweeps a tube
/// of fixed length around the brass hub at the pivot. The scale spans a slightly wider arc than the
/// needle travel, matching the reference meter (the "−"/"+" labels sit just past the needle's extremes).
/// </remarks>
internal static class VuMeterGeometry
{
    public const int FaceWidth = 1200;
    public const int FaceHeight = 800;

    public const float PivotXFrac = 0.5f;     // hub centred horizontally
    public const float PivotYFrac = 0.72f;    // hub near the lower third (from the top)
    public const float ArcRadiusFrac = 0.46f; // scale arc radius, as a fraction of the height

    public const float ScaleMinDeg = -58f;    // left end of the printed scale
    public const float ScaleMaxDeg = 58f;     // right end of the printed scale
    public const float NeedleMinDeg = -55f;   // uLevel 0 → resting far left
    public const float NeedleMaxDeg = 52f;    // uLevel 1 → far right
    public const float RedlineT = 0.68f;      // scale fraction where the red zone (0 VU) begins

    public static float PivotXPx => FaceWidth * PivotXFrac;
    public static float PivotYPx => FaceHeight * PivotYFrac;
    public static float ArcRadiusPx => FaceHeight * ArcRadiusFrac;

    /// <summary>Scale-parameter t∈[0,1] (left→right) → angle in degrees from vertical (+ = right).</summary>
    public static float AngleDegAt(float t) => ScaleMinDeg + (ScaleMaxDeg - ScaleMinDeg) * t;

    /// <summary>Top dB row, left→right: each label's scale fraction and text.</summary>
    public static readonly (float T, string Text)[] TopLabels =
    {
        (0.00f, "-"), // minus sign (drawn slightly longer in the face renderer)
        (0.10f, "20"),
        (0.21f, "10"),
        (0.30f, "7"),
        (0.38f, "5"),
        (0.47f, "3"),
        (0.57f, "1"),
        (0.68f, "0"),
        (0.785f, "1"),
        (0.87f, "2"),
        (0.95f, "3"),
        (1.00f, "+"),
    };

    /// <summary>Bottom percentage row, left→right.</summary>
    public static readonly (float T, string Text)[] BottomLabels =
    {
        (0.15f, "0"),
        (0.31f, "20"),
        (0.44f, "40"),
        (0.56f, "60"),
        (0.67f, "80"),
        (0.80f, "100"),
    };
}
