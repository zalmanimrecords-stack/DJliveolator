using Avalonia;
using Avalonia.Media;

namespace Liveolator.App.Controls;

public partial class Jog
{
    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;

        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double progress = Math.Clamp(_dragging && !_bendMode ? _scrubFraction : Progress, 0, 1);
        double pulse = IsEnabled && IsKickActive ? KickEnergyAt(Progress, KickPeaks) : 0;
        double faceRadius = size * 0.365;

        DrawBezel(context, centre, size);
        DrawPlatter(context, centre, faceRadius, size);
        DrawArtworkWell(context, centre, faceRadius * 0.70, size, pulse);
        DrawTransportRing(context, centre, size, progress);
        DrawPositionNeedle(context, centre, faceRadius, size, progress);
        DrawHub(context, centre, size);

        if (pulse > 0.001)
            DrawKickGlow(context, centre, size, pulse);
    }

    private void DrawBezel(DrawingContext context, Point centre, double size)
    {
        double radius = size * 0.455;
        using (context.PushOpacity(0.40))
            context.DrawEllipse(PlatterBrush, null,
                new Point(centre.X, centre.Y + size * 0.014), size * 0.473, size * 0.473);

        // Theme-coloured machined metal: a light upper shoulder and a dark recessed lower edge.
        context.DrawEllipse(SurfaceGradient(TrackBrush, 0.20),
            new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.19 : 0.08), 0.8),
            centre, radius, radius);
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(PlatterBrush, 0.90), size * 0.008),
            centre, radius - size * 0.006, radius - size * 0.006);
        context.DrawEllipse(PlatterBrush, null, centre, size * 0.414, size * 0.414);
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.12 : 0.05), 0.7),
            centre, size * 0.414, size * 0.414);
        DrawRimTicks(context, centre, size);
    }

    private void DrawRimTicks(DrawingContext context, Point centre, double size)
    {
        int count = size < 140 ? 72 : 96;
        int majorInterval = count / 12;
        var minor = new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.23 : 0.09), 0.7);
        var major = new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.51 : 0.18), 1.0);

        for (int index = 0; index < count; index++)
        {
            bool isMajor = index % majorInterval == 0;
            double angle = StartAngle + index * 360.0 / count;
            context.DrawLine(isMajor ? major : minor,
                PointOnCircle(centre, size * (isMajor ? 0.421 : 0.432), angle),
                PointOnCircle(centre, size * 0.448, angle));
        }
    }

    private void DrawPlatter(DrawingContext context, Point centre, double radius, double size)
    {
        context.DrawEllipse(SurfaceGradient(PlatterBrush, IsEnabled ? 0.09 : 0.04),
            new Pen(ControlBrush.Halo(TrackBrush, 0.9), Math.Max(1, size * 0.006)),
            centre, radius, radius);

        var groove = new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.065 : 0.025), 0.6);
        for (int index = 0; index < 7; index++)
        {
            double grooveRadius = radius * (0.76 + index * 0.031);
            context.DrawEllipse(null, groove, centre, grooveRadius, grooveRadius);
        }

        // Two restrained reflections describe the lip without obscuring the vinyl grooves.
        context.DrawGeometry(null, new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.18 : 0.07), 0.8),
            Arc(centre, radius * 0.98, -150, -48));
        context.DrawGeometry(null, new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.08 : 0.03), 0.6),
            Arc(centre, radius * 0.98, 25, 98));
    }

    private void DrawArtworkWell(DrawingContext context, Point centre, double radius, double size, double pulse)
    {
        context.DrawEllipse(PlatterBrush,
            new Pen(ControlBrush.Halo(TrackBrush, 0.95), Math.Max(2, size * 0.013)),
            centre, radius + size * 0.006, radius + size * 0.006);
        using (context.PushOpacity(IsEnabled ? 1 : 0.32))
            DrawMedusa(context, centre, radius, pulse);
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.22 : 0.07), 0.8),
            centre, radius, radius);
    }

    private void DrawTransportRing(DrawingContext context, Point centre, double size, double progress)
    {
        double radius = size * 0.391;
        double width = Math.Max(1.7, size * 0.014);
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(TrackBrush, 0.9), width + 1),
            centre, radius, radius);
        if (!IsEnabled)
            return;

        if (progress > 0.0008)
        {
            var halo = new Pen(ControlBrush.Halo(ArcBrush, 0.12), width * 3.2) { LineCap = PenLineCap.Round };
            var arc = new Pen(ArcBrush, width) { LineCap = PenLineCap.Round };
            // A closed ellipse keeps the ring visible at exactly 100%, where an arc has identical endpoints.
            if (progress >= 0.99999)
            {
                context.DrawEllipse(null, halo, centre, radius, radius);
                context.DrawEllipse(null, arc, centre, radius, radius);
            }
            else
            {
                var geometry = Arc(centre, radius, StartAngle, StartAngle + 360 * progress);
                context.DrawGeometry(null, halo, geometry);
                context.DrawGeometry(null, arc, geometry);
            }
        }

        double angle = StartAngle + 360 * progress;
        Point marker = PointOnCircle(centre, radius, angle);
        context.DrawEllipse(ControlBrush.Halo(ArcBrush, 0.18), null, marker, width * 2.1, width * 2.1);
        context.DrawEllipse(ArcBrush, null, marker, width * 1.03, width * 1.03);
        context.DrawEllipse(MarkerBrush, null, marker, width * 0.49, width * 0.49);
    }

    private void DrawPositionNeedle(DrawingContext context, Point centre, double faceRadius, double size, double progress)
    {
        if (!IsEnabled)
            return;

        double angle = StartAngle + 360 * progress;
        // Keep the artwork readable: the fine spindle line stops at its outer edge, and the bright
        // position index occupies only the vinyl band between the artwork and transport ring.
        context.DrawLine(new Pen(ControlBrush.Halo(MarkerBrush, 0.32), Math.Max(0.7, size * 0.004)),
            PointOnCircle(centre, size * 0.038, angle),
            PointOnCircle(centre, faceRadius * 0.65, angle));
        context.DrawLine(new Pen(MarkerBrush, Math.Max(1.5, size * 0.010)) { LineCap = PenLineCap.Round },
            PointOnCircle(centre, faceRadius * 0.77, angle),
            PointOnCircle(centre, faceRadius * 0.97, angle));
    }

    private void DrawHub(DrawingContext context, Point centre, double size)
    {
        double radius = size * 0.035;
        context.DrawEllipse(SurfaceGradient(TrackBrush, IsEnabled ? 0.27 : 0.10),
            new Pen(ControlBrush.Halo(PlatterBrush, 0.95), Math.Max(1, size * 0.006)),
            centre, radius, radius);
        context.DrawEllipse(ControlBrush.Halo(MarkerBrush, IsEnabled ? 0.50 : 0.15), null,
            centre, Math.Max(0.8, size * 0.006), Math.Max(0.8, size * 0.006));
    }

    private void DrawKickGlow(DrawingContext context, Point centre, double size, double pulse)
    {
        // The light sits outside the metal shoulder and remains within bounds, even on a full kick.
        double radius = size * 0.467;
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(GlowBrush, 0.10 * pulse), size * 0.037),
            centre, radius, radius);
        context.DrawEllipse(null, new Pen(ControlBrush.Halo(GlowBrush, 0.65 * pulse), Math.Max(1, size * 0.009)),
            centre, radius, radius);
    }

    private IBrush SurfaceGradient(IBrush surface, double highlight)
    {
        if (surface is not ISolidColorBrush body || MarkerBrush is not ISolidColorBrush light)
            return surface;

        Color recessed = (PlatterBrush as ISolidColorBrush)?.Color ?? body.Color;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.2, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.8, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Lerp(body.Color, light.Color, highlight), 0),
                new GradientStop(body.Color, 0.45),
                new GradientStop(Lerp(body.Color, recessed, 0.65), 0.78),
                new GradientStop(Lerp(body.Color, light.Color, highlight * 0.22), 1),
            },
        };
    }
}
