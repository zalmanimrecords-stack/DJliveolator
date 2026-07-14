namespace Liveolator.Core.Settings;

/// <summary>
/// Where the VU-meter needle pivots from — the performer's choice (doc 26 add-on settings). The two
/// options are vertical mirror images of each other; the dial face and the needle are rendered to match.
/// </summary>
public enum VuMeterNeedleOrigin
{
    /// <summary>Classic VU meter: hub near the bottom, needle points UP, scale arc above the hub.</summary>
    Bottom = 0,

    /// <summary>Hub near the top, needle hangs DOWN, scale arc below the hub.</summary>
    Top = 1,
}
