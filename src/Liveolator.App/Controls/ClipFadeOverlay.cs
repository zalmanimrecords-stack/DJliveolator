using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Liveolator.App.Controls;

/// <summary>
/// Draws a clip's gain envelope (the fade-in rise and fade-out fall) as a faint line over the clip, so the
/// fades the corner handles edit are visible. The envelope rises 0→full across the fade-in, holds, then
/// falls to 0 across the fade-out — mirroring <c>ClipGain</c>. Fades are timeline-domain, so their pixel
/// width is simply seconds × zoom. Non-interactive: the clip Border owns the corner-drag gesture.
/// </summary>
public sealed class ClipFadeOverlay : Control
{
    public static readonly StyledProperty<double> FadeInSecondsProperty =
        AvaloniaProperty.Register<ClipFadeOverlay, double>(nameof(FadeInSeconds));

    public static readonly StyledProperty<double> FadeOutSecondsProperty =
        AvaloniaProperty.Register<ClipFadeOverlay, double>(nameof(FadeOutSeconds));

    public static readonly StyledProperty<double> PixelsPerSecondProperty =
        AvaloniaProperty.Register<ClipFadeOverlay, double>(nameof(PixelsPerSecond), 8.0);

    public static readonly StyledProperty<IBrush> EnvelopeBrushProperty =
        AvaloniaProperty.Register<ClipFadeOverlay, IBrush>(
            nameof(EnvelopeBrush), new ImmutableSolidColorBrush(Color.FromArgb(0xBB, 0xE8, 0xEE, 0xF6)));

    static ClipFadeOverlay()
    {
        AffectsRender<ClipFadeOverlay>(
            FadeInSecondsProperty, FadeOutSecondsProperty, PixelsPerSecondProperty, EnvelopeBrushProperty);
    }

    public ClipFadeOverlay() => IsHitTestVisible = false;

    public double FadeInSeconds { get => GetValue(FadeInSecondsProperty); set => SetValue(FadeInSecondsProperty, value); }
    public double FadeOutSeconds { get => GetValue(FadeOutSecondsProperty); set => SetValue(FadeOutSecondsProperty, value); }
    public double PixelsPerSecond { get => GetValue(PixelsPerSecondProperty); set => SetValue(PixelsPerSecondProperty, value); }
    public IBrush EnvelopeBrush { get => GetValue(EnvelopeBrushProperty); set => SetValue(EnvelopeBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;

        double inPx = System.Math.Max(0, FadeInSeconds * PixelsPerSecond);
        double outPx = System.Math.Max(0, FadeOutSeconds * PixelsPerSecond);
        if (inPx <= 0 && outPx <= 0)
            return; // no fades to show

        // Keep the two ramps from overlapping past the clip width.
        if (inPx + outPx > b.Width)
        {
            double scale = b.Width / (inPx + outPx);
            inPx *= scale;
            outPx *= scale;
        }

        var pen = new Pen(EnvelopeBrush, 1.25) { LineJoin = PenLineJoin.Round };

        // Draw only the sloped part of each fade (top = full level, bottom = silence).
        if (inPx > 0)
            context.DrawLine(pen, new Point(0, b.Height), new Point(inPx, 0));
        if (outPx > 0)
            context.DrawLine(pen, new Point(b.Width - outPx, 0), new Point(b.Width, b.Height));
    }
}
