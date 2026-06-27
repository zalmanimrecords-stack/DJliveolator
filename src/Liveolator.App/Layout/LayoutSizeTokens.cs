using System.Collections.Generic;

namespace Liveolator.App.Layout;

/// <summary>
/// The size design tokens (knob/fader dimensions and key readout font sizes) and their per-tier scaled
/// values. Controls reference these by key as DynamicResources; the shell Window (MainWindow.ApplySizeClass)
/// rewrites the values when the <see cref="LayoutSizeClass"/> changes, so a 4K screen gets larger controls
/// and readouts (size-driven, crisp) instead of a blurring render transform.
///
/// Compact and Standard stay at the baseline (scale 1.0) — the laptop tier reflows rather than shrinking,
/// so these floors are also the live-performance minimum hit-target / legibility sizes. Only Wide/Ultra grow.
/// </summary>
public static class LayoutSizeTokens
{
    /// <summary>Baseline (scale 1.0) token values, in logical px. Keys match the DynamicResource keys in XAML.</summary>
    public static readonly IReadOnlyDictionary<string, double> Baseline = new Dictionary<string, double>
    {
        // Knobs (per-channel EQ/filter are ridden live, so they sit at the usable floor and grow on big screens).
        ["SizeKnobEq"] = 24,
        ["SizeKnobCue"] = 32,
        ["SizeKnobCut"] = 34,
        ["SizeKnobZoom"] = 40,

        // Faders.
        ["SizeFaderChannelWidth"] = 44,
        ["SizeFaderChannelHeight"] = 108,

        // Waveform strip height — more vertical waveform is genuinely useful on a big screen (the kick band
        // and beat marks the DJ aligns by eye), unlike a stretched bitmap; the strip re-rasterizes to bounds.
        ["SizeWaveformHeight"] = 62,

        // Readouts the DJ reads across a dark booth — these are the first thing that should grow.
        ["FontDeckTitle"] = 14,
        ["FontBpmReadout"] = 12,
        ["FontTimeReadout"] = 12,
        ["FontTrackKey"] = 14,
    };

    /// <summary>
    /// Waveform strip height per tier. This uses a STEEPER curve than the uniform control scale: tall
    /// full-width waveforms are the signature "pro DJ" filler (Rekordbox/Serato), so on big screens they
    /// grow much more than a knob does, which is what makes a 2-deck console read as full rather than sparse.
    /// </summary>
    private static double WaveformHeight(LayoutSizeClass cls) => cls switch
    {
        LayoutSizeClass.Compact => 62,
        LayoutSizeClass.Standard => 62,
        LayoutSizeClass.Wide => 120,
        LayoutSizeClass.Ultra => 170,
        _ => 62,
    };

    /// <summary>
    /// Channel fader height per tier — also a steeper curve than the uniform control scale: tall channel
    /// faders are the pro-mixer filler, so the mixer strip grows to match the filled decks/waveforms on a
    /// big screen instead of floating as a short centered island.
    /// </summary>
    private static double ChannelFaderHeight(LayoutSizeClass cls) => cls switch
    {
        LayoutSizeClass.Compact => 108,
        LayoutSizeClass.Standard => 108,
        LayoutSizeClass.Wide => 200,
        LayoutSizeClass.Ultra => 280,
        _ => 108,
    };

    /// <summary>The token values for a tier (baseline × the quantized <see cref="LayoutScale"/> step,
    /// except the waveform height which follows its own steeper curve).</summary>
    public static IReadOnlyDictionary<string, double> For(LayoutSizeClass cls)
    {
        double scale = LayoutScale.For(cls);
        var result = new Dictionary<string, double>(Baseline.Count);
        foreach (var (key, baseValue) in Baseline)
            result[key] = baseValue * scale;
        result["SizeWaveformHeight"] = WaveformHeight(cls);
        result["SizeFaderChannelHeight"] = ChannelFaderHeight(cls);
        return result;
    }
}
