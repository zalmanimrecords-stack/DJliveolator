using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// A compact horizontal gain-reduction (GR) meter for the master limiter: a recessed segmented bar that
/// lights amber→red in proportion to <see cref="GainReductionDb"/> / <see cref="FullScaleDb"/>, with an
/// activity LED at the left that glows whenever the limiter is working. The brick-wall limiter is
/// inaudible by design, so this is how the DJ SEES when — and how hard — it engages (and thus the effect
/// of the CHARACTER / SMART / CEILING controls). Pure presentation: the value flows in from the
/// view-model, which polls <see cref="Liveolator.Core.Mixer.ILimiterMeter"/> and applies the peak-hold.
/// </summary>
public class GainReductionMeter : Control
{
    private const int Segments = 12;
    private const double ActiveThresholdDb = 0.1; // below this the LED reads idle (no meaningful reduction)

    // House meter palette (mirrors Fader's level meter): amber for light reduction, red as it slams.
    private static readonly Color Amber = Color.FromRgb(0xFF, 0xA2, 0x2B);
    private static readonly Color Red = Color.FromRgb(0xE5, 0x54, 0x4A);
    private static readonly IBrush UnlitSegment = new SolidColorBrush(Color.FromArgb(0x70, 0x18, 0x20, 0x29));
    private static readonly IBrush Track = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0D));
    private static readonly IBrush LedIdle = new SolidColorBrush(Color.FromArgb(0x60, 0x3A, 0x2A, 0x12));

    public static readonly StyledProperty<double> GainReductionDbProperty =
        AvaloniaProperty.Register<GainReductionMeter, double>(nameof(GainReductionDb));

    public static readonly StyledProperty<double> FullScaleDbProperty =
        AvaloniaProperty.Register<GainReductionMeter, double>(nameof(FullScaleDb), defaultValue: 12.0);

    static GainReductionMeter()
    {
        AffectsRender<GainReductionMeter>(GainReductionDbProperty, FullScaleDbProperty);
    }

    /// <summary>Current gain reduction in dB (0 = not limiting). Already peak-held by the view-model.</summary>
    public double GainReductionDb
    {
        get => GetValue(GainReductionDbProperty);
        set => SetValue(GainReductionDbProperty, value);
    }

    /// <summary>The dB of reduction that fills the meter completely (its 100% deflection).</summary>
    public double FullScaleDb
    {
        get => GetValue(FullScaleDbProperty);
        set => SetValue(FullScaleDbProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width < 6 || b.Height < 4)
            return;

        double fullScale = FullScaleDb > 0 ? FullScaleDb : 12.0;
        double db = Math.Max(0.0, GainReductionDb);
        double fraction = Math.Clamp(db / fullScale, 0.0, 1.0);
        bool active = db >= ActiveThresholdDb;

        double ledR = Math.Min(b.Height / 2 - 1, 5);
        var ledCentre = new Point(ledR + 1, b.Height / 2);
        double barLeft = ledCentre.X + ledR + 4;
        double barRight = b.Width - 1;
        double barWidth = barRight - barLeft;

        // Recessed track behind the bar.
        var track = new Rect(barLeft - 2, b.Height / 2 - ledR - 1, Math.Max(0, barWidth + 4), (ledR + 1) * 2);
        context.DrawRectangle(Track, null, track, 2, 2);

        // Activity LED: amber glow scaled by reduction, with a halo when engaged.
        if (active)
        {
            IBrush led = new SolidColorBrush(LerpColor(Amber, Red, fraction));
            context.DrawEllipse(ControlBrush.Halo(led, 0.30), null, ledCentre, ledR + 3, ledR + 3);
            context.DrawEllipse(led, null, ledCentre, ledR, ledR);
        }
        else
        {
            context.DrawEllipse(LedIdle, null, ledCentre, ledR, ledR);
        }

        // Segmented bar fills left→right with the held reduction.
        if (barWidth <= 0)
            return;
        const double gap = 2;
        double segWidth = Math.Max(1, (barWidth - (Segments - 1) * gap) / Segments);
        double segTop = b.Height / 2 - ledR;
        double segHeight = ledR * 2;
        int lit = (int)Math.Ceiling(fraction * Segments);
        for (int i = 0; i < Segments; i++)
        {
            double x = barLeft + i * (segWidth + gap);
            var rect = new Rect(x, segTop, segWidth, segHeight);
            IBrush brush = i < lit
                ? new SolidColorBrush(LerpColor(Amber, Red, (double)(i + 1) / Segments))
                : UnlitSegment;
            context.DrawRectangle(brush, null, rect, 1, 1);
        }
    }

    private static Color LerpColor(Color a, Color c, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromRgb(
            (byte)(a.R + (c.R - a.R) * t),
            (byte)(a.G + (c.G - a.G) * t),
            (byte)(a.B + (c.B - a.B) * t));
    }
}
