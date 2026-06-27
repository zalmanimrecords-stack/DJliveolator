using Liveolator.App.Layout;
using Xunit;

namespace Liveolator.App.Tests.Layout;

public class LayoutSizeClassResolverTests
{
    [Theory]
    [InlineData(1000, LayoutSizeClass.Compact)]
    [InlineData(1179, LayoutSizeClass.Compact)]
    [InlineData(1180, LayoutSizeClass.Standard)]
    [InlineData(1440, LayoutSizeClass.Standard)]
    [InlineData(1699, LayoutSizeClass.Standard)]
    [InlineData(1700, LayoutSizeClass.Wide)]
    [InlineData(2000, LayoutSizeClass.Wide)]
    [InlineData(2399, LayoutSizeClass.Wide)]
    [InlineData(2400, LayoutSizeClass.Ultra)]
    [InlineData(3840, LayoutSizeClass.Ultra)]
    public void Classify_maps_width_to_tier(double width, LayoutSizeClass expected)
    {
        Assert.Equal(expected, LayoutSizeClassResolver.Classify(width));
    }

    [Fact]
    public void Resolve_keeps_current_tier_inside_the_dead_band()
    {
        // Width nudged just over the Compact->Standard boundary, but not past the dead-band:
        // a window dragged near the edge must not flip the layout.
        var result = LayoutSizeClassResolver.Resolve(1200, LayoutSizeClass.Compact);
        Assert.Equal(LayoutSizeClass.Compact, result);
    }

    [Fact]
    public void Resolve_steps_up_only_after_clearing_the_dead_band()
    {
        // CompactMax (1180) + Hysteresis (50) = 1230 is the commit point.
        Assert.Equal(LayoutSizeClass.Compact, LayoutSizeClassResolver.Resolve(1229, LayoutSizeClass.Compact));
        Assert.Equal(LayoutSizeClass.Standard, LayoutSizeClassResolver.Resolve(1230, LayoutSizeClass.Compact));
    }

    [Fact]
    public void Resolve_steps_down_only_after_clearing_the_dead_band()
    {
        // Coming down from Standard, the drop to Compact only commits below 1180 - 50 = 1130.
        Assert.Equal(LayoutSizeClass.Standard, LayoutSizeClassResolver.Resolve(1131, LayoutSizeClass.Standard));
        Assert.Equal(LayoutSizeClass.Compact, LayoutSizeClassResolver.Resolve(1130, LayoutSizeClass.Standard));
    }

    [Fact]
    public void Resolve_does_not_thrash_when_dithering_around_a_boundary()
    {
        // Drag back and forth across the raw boundary (1180) within the dead-band: tier holds.
        var cls = LayoutSizeClass.Compact;
        foreach (var w in new[] { 1175.0, 1185, 1170, 1190, 1160, 1200, 1150 })
            cls = LayoutSizeClassResolver.Resolve(w, cls);
        Assert.Equal(LayoutSizeClass.Compact, cls);
    }

    [Fact]
    public void Resolve_handles_a_multi_tier_jump_on_maximize()
    {
        // Maximizing a small window straight to 4K must land on Ultra, not crawl one tier.
        Assert.Equal(LayoutSizeClass.Ultra, LayoutSizeClassResolver.Resolve(3840, LayoutSizeClass.Compact));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public void Resolve_ignores_non_positive_or_unmeasured_width(double width)
    {
        // During first layout the width can arrive as 0/NaN; keep the current tier rather than flip to Compact.
        Assert.Equal(LayoutSizeClass.Wide, LayoutSizeClassResolver.Resolve(width, LayoutSizeClass.Wide));
    }

    [Theory]
    [InlineData(LayoutSizeClass.Compact, 1.0)]
    [InlineData(LayoutSizeClass.Standard, 1.0)]
    [InlineData(LayoutSizeClass.Wide, 1.2)]
    [InlineData(LayoutSizeClass.Ultra, 1.4)]
    public void Scale_steps_are_quantized_per_tier(LayoutSizeClass cls, double expected)
    {
        Assert.Equal(expected, LayoutScale.For(cls));
    }

    [Fact]
    public void Tokens_hold_the_baseline_on_the_laptop_tiers()
    {
        // Compact/Standard must not shrink below the live-performance floor — they stay at the baseline.
        foreach (var cls in new[] { LayoutSizeClass.Compact, LayoutSizeClass.Standard })
        {
            var tokens = LayoutSizeTokens.For(cls);
            foreach (var (key, baseValue) in LayoutSizeTokens.Baseline)
                Assert.Equal(baseValue, tokens[key]);
        }
    }

    [Theory]
    [InlineData(LayoutSizeClass.Wide, 1.2)]
    [InlineData(LayoutSizeClass.Ultra, 1.4)]
    public void Tokens_scale_by_the_tier_step_on_big_screens(LayoutSizeClass cls, double scale)
    {
        var tokens = LayoutSizeTokens.For(cls);

        Assert.Equal(LayoutSizeTokens.Baseline.Count, tokens.Count);
        foreach (var (key, baseValue) in LayoutSizeTokens.Baseline)
        {
            // The waveform + channel-fader heights follow their own steeper curves (the pro-DJ fillers),
            // not the uniform step.
            if (key is "SizeWaveformHeight" or "SizeFaderChannelHeight")
                continue;
            Assert.Equal(baseValue * scale, tokens[key]);
        }
    }

    [Theory]
    [InlineData(LayoutSizeClass.Compact, 62)]
    [InlineData(LayoutSizeClass.Standard, 62)]
    [InlineData(LayoutSizeClass.Wide, 120)]
    [InlineData(LayoutSizeClass.Ultra, 170)]
    public void Waveform_height_grows_steeply_on_big_screens(LayoutSizeClass cls, double expected)
    {
        Assert.Equal(expected, LayoutSizeTokens.For(cls)["SizeWaveformHeight"]);
    }

    [Theory]
    [InlineData(LayoutSizeClass.Compact, 108)]
    [InlineData(LayoutSizeClass.Standard, 108)]
    [InlineData(LayoutSizeClass.Wide, 200)]
    [InlineData(LayoutSizeClass.Ultra, 280)]
    public void Channel_fader_height_grows_steeply_on_big_screens(LayoutSizeClass cls, double expected)
    {
        Assert.Equal(expected, LayoutSizeTokens.For(cls)["SizeFaderChannelHeight"]);
    }

    [Theory]
    [InlineData("compact", LayoutSizeClass.Compact)]
    [InlineData("standard", LayoutSizeClass.Standard)]
    [InlineData("wide", LayoutSizeClass.Wide)]
    [InlineData("ultra", LayoutSizeClass.Ultra)]
    [InlineData(null, LayoutSizeClass.Standard)]
    [InlineData("bogus", LayoutSizeClass.Standard)]
    public void FromStyleClass_inverts_the_tier_class_name(string? styleClass, LayoutSizeClass expected)
    {
        Assert.Equal(expected, LayoutScale.FromStyleClass(styleClass));
    }

    [Theory]
    // Laptop tiers collapse the DJ browser (console keeps the height); wide/4K open a proportional band.
    [InlineData(LayoutSizeClass.Compact, 0.0)]
    [InlineData(LayoutSizeClass.Standard, 0.0)]
    public void Browser_row_collapses_on_laptop_tiers(LayoutSizeClass cls, double expected)
    {
        Assert.Equal(expected, DjBrowserLayout.RowShare(cls));
    }

    [Theory]
    [InlineData(LayoutSizeClass.Wide)]
    [InlineData(LayoutSizeClass.Ultra)]
    public void Browser_row_opens_a_proportional_band_on_big_screens(LayoutSizeClass cls)
    {
        Assert.True(DjBrowserLayout.RowShare(cls) > 0);
    }
}
