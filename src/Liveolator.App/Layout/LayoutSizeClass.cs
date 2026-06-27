namespace Liveolator.App.Layout;

/// <summary>
/// Discrete responsive tiers for the whole shell, keyed on the host (window/content) width.
/// The layout reflows and the <see cref="LayoutScale"/> token steps at these boundaries instead of
/// continuously, so font sizes land on stable values (no shimmer on resize) and a skeuomorphic
/// knob/fader/waveform UI is scaled by size — never by a blurring render transform.
/// </summary>
public enum LayoutSizeClass
{
    /// <summary>Small laptops and small windows (e.g. 1366x768). Reflow + tighten, never down-scale.</summary>
    Compact,

    /// <summary>The design baseline (~1280-1700). The reference layout, essentially as-is.</summary>
    Standard,

    /// <summary>Large desktop displays (~1700-2400). Caps grow so the console uses the screen.</summary>
    Wide,

    /// <summary>4K and ultra-wide (&gt; 2400). Scale capped; layout wins (taller waveforms, side-by-side).</summary>
    Ultra,
}
