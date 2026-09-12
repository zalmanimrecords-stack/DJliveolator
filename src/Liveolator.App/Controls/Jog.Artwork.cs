using Avalonia;
using Avalonia.Media;

namespace Liveolator.App.Controls;

public partial class Jog
{
    /// <summary>
    /// Draws the platter centrepiece inside a disc of the given radius: the user's <see cref="CenterImage"/>
    /// (e.g. a photoreal jellyfish) if set, otherwise a built-in translucent vector medusa. Either way the
    /// bass <paramref name="pulse"/> washes it toward <see cref="BassTintBrush"/> — a subtle red bloom on the
    /// kick — so the artwork pulses with the low end. Everything is clipped to the disc so it never spills
    /// onto the vinyl grooves or rim.
    /// </summary>
    private void DrawMedusa(DrawingContext context, Point centre, double radius, double pulse)
    {
        if (radius <= 0)
            return;

        double tint = BassTintStrength(pulse);
        var disc = new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        using (context.PushGeometryClip(new EllipseGeometry(disc)))
        {
            // While the deck plays the artwork turns about the disc centre like a record; the circular clip
            // and bass wash are rotation-invariant, so only the medusa itself spins.
            Matrix rotation = Matrix.CreateTranslation(-centre.X, -centre.Y)
                * Matrix.CreateRotation(_spinRadians)
                * Matrix.CreateTranslation(centre.X, centre.Y);
            using (context.PushTransform(rotation))
            {
                if (CenterImage is { } image)
                    context.DrawImage(image, disc);
                else
                    DrawVectorMedusa(context, centre, radius, tint);
            }

            // Red wash on the bass — drawn over the artwork, clamped to the disc by the clip.
            if (tint > 0.001 && BassTintBrush is SolidColorBrush red)
            {
                var wash = new SolidColorBrush(Color.FromArgb((byte)(tint * 255), red.Color.R, red.Color.G, red.Color.B));
                context.DrawEllipse(wash, null, centre, radius, radius);
            }
        }
    }

    /// <summary>The fallback hand-drawn jellyfish: a layered translucent bell over flowing oral arms. Cool
    /// aqua at rest, lerped toward the bass-tint colour as <paramref name="tint"/> rises so the whole medusa
    /// reddens on the kick (independent of the flat red wash, which also stacks on top).</summary>
    private void DrawVectorMedusa(DrawingContext context, Point centre, double radius, double tint)
    {
        // Cool base palette, warmed toward the tint colour. Using the tint colour keeps the vector and the
        // overlaid wash visually consistent.
        Color tintTarget = (BassTintBrush as SolidColorBrush)?.Color ?? Color.FromRgb(0xE5, 0x54, 0x4A);
        Color bell = Lerp(Color.FromArgb(0xB0, 0x8C, 0xDC, 0xF0), tintTarget, tint * 0.7);
        Color bellCore = Lerp(Color.FromArgb(0xE0, 0xCF, 0xF2, 0xFB), tintTarget, tint * 0.5);
        Color tentacle = Lerp(Color.FromArgb(0x96, 0x7F, 0xCF, 0xE6), tintTarget, tint * 0.7);

        var bellCentre = new Point(centre.X, centre.Y - radius * 0.22);
        double bellW = radius * 1.5;
        double bellH = radius * 1.18;

        // Oral arms first (behind the bell): a few curved, tapering translucent strands hanging below.
        double[] sway = { -0.62, -0.3, 0.0, 0.3, 0.62 };
        var tentaclePen = new Pen(new SolidColorBrush(tentacle), Math.Max(1.5, radius * 0.07))
        {
            LineCap = PenLineCap.Round,
        };
        double top = bellCentre.Y + bellH * 0.28;
        for (int i = 0; i < sway.Length; i++)
        {
            double x = bellCentre.X + sway[i] * (bellW * 0.42);
            double len = radius * (1.02 - Math.Abs(sway[i]) * 0.45);
            context.DrawGeometry(null, tentaclePen, Tentacle(x, top, len, sway[i] * radius * 0.5));
        }

        // Soft outer halo, then the main bell dome, then a brighter inner cap — three stacked ellipses give
        // the translucent, lit-from-within look of a real jellyfish bell.
        var halo = Color.FromArgb(0x40, bell.R, bell.G, bell.B);
        context.DrawEllipse(new SolidColorBrush(halo), null, bellCentre, bellW * 0.62, bellH * 0.62);
        context.DrawEllipse(new SolidColorBrush(bell), null, bellCentre, bellW * 0.5, bellH * 0.5);
        context.DrawEllipse(new SolidColorBrush(bellCore), null,
            new Point(bellCentre.X, bellCentre.Y - bellH * 0.08), bellW * 0.30, bellH * 0.32);
    }

    /// <summary>One tapering oral arm as a wavy vertical curve from (x, top) hanging down by <paramref name="length"/>,
    /// with a horizontal <paramref name="drift"/> so the strands fan out and curl.</summary>
    private static StreamGeometry Tentacle(double x, double top, double length, double drift)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x, top), isFilled: false);
            var c1 = new Point(x + drift * 0.6, top + length * 0.4);
            var c2 = new Point(x - drift * 0.4, top + length * 0.7);
            var end = new Point(x + drift, top + length);
            ctx.CubicBezierTo(c1, c2, end);
            ctx.EndFigure(false);
        }
        return geometry;
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

}
