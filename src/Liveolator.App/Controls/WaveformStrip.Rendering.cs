using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Liveolator.App.Controls;

public sealed partial class WaveformStrip
{
    // Closed spectral silhouettes replace the dense barcode texture. Peak-hold sampling is unchanged;
    // a path is built once per band and shared by both sides of the playhead, with no per-column objects.
    private void RenderEnvelope(
        DrawingContext context, Rect bounds, IReadOnlyList<float> values, IBrush brush,
        WaveGeometry geometry, double start, double span, double fillOpacity, double minimum = 0)
    {
        double first = Math.Max(1, Math.Ceiling(-start / span * bounds.Width));
        double last = Math.Min(Math.Ceiling(bounds.Width - 1) - 1,
            Math.Ceiling((1 - start) / span * bounds.Width) - 1);
        if (last < first)
            return;

        double baseline = geometry.Folded ? geometry.Baseline : geometry.Center;
        var silhouette = new StreamGeometry();
        using (StreamGeometryContext path = silhouette.Open())
        {
            path.BeginFigure(new Point(first, baseline), true);
            for (double x = first; x <= last; x++)
            {
                double amp = Math.Max(minimum,
                    geometry.MaxAmp * Math.Clamp(ColumnPeak(values, x, 1, bounds.Width, start, span), 0f, 1f));
                var (top, bottom) = geometry.Bar(x, amp);
                path.LineTo(geometry.Folded ? bottom : top);
            }

            if (!geometry.Folded)
                for (double x = last; x >= first; x--)
                {
                    double amp = Math.Max(minimum,
                        geometry.MaxAmp * Math.Clamp(ColumnPeak(values, x, 1, bounds.Width, start, span), 0f, 1f));
                    path.LineTo(geometry.Bar(x, amp).Bottom);
                }
            else
                path.LineTo(new Point(last, baseline));
            path.EndFigure(true);
        }

        double playheadX = Math.Clamp((Progress - start) / span * bounds.Width, 0, bounds.Width);
        var edge = new Pen(brush, 1.05) { LineJoin = PenLineJoin.Round };
        DrawEnvelopeRegion(context, new Rect(0, 0, playheadX, bounds.Height),
            silhouette, brush, edge, fillOpacity * 0.65, 0.58);
        DrawEnvelopeRegion(context, new Rect(playheadX, 0, bounds.Width - playheadX, bounds.Height),
            silhouette, brush, edge, fillOpacity, 0.95);
    }

    private static void DrawEnvelopeRegion(
        DrawingContext context, Rect region, Geometry silhouette, IBrush brush,
        Pen edge, double fillOpacity, double edgeOpacity)
    {
        if (region.Width <= 0)
            return;
        using (context.PushClip(region))
        {
            using (context.PushOpacity(fillOpacity))
                context.DrawGeometry(brush, null, silhouette);
            using (context.PushOpacity(edgeOpacity))
                context.DrawGeometry(null, edge, silhouette);
        }
    }

    // Quiet amplitude guides and a separate timing rail let the coloured bands carry the information.
    private void RenderWaveGuides(
        DrawingContext context, Rect bounds, double waveTop, double waveHeight,
        double combTop, double combHeight, WaveGeometry geometry)
    {
        var guide = new Pen(GridBrush, 1);
        using (context.PushOpacity(0.14))
        {
            for (int i = 1; i <= 3; i++)
            {
                double y = Math.Round(waveTop + waveHeight * i / 4) + 0.5;
                context.DrawLine(guide, new Point(0, y), new Point(bounds.Width, y));
            }
        }

        using (context.PushOpacity(0.04))
            context.DrawRectangle(BeatBrush, null, new Rect(0, combTop, bounds.Width, combHeight));
        double edgeY = CombAtTop ? combTop + combHeight : combTop;
        using (context.PushOpacity(0.45))
            context.DrawLine(guide, new Point(0, edgeY + 0.5), new Point(bounds.Width, edgeY + 0.5));
        if (!geometry.Folded)
        {
            double baseline = waveTop + geometry.Center + 0.5;
            using (context.PushOpacity(0.30))
                context.DrawLine(guide, new Point(0, baseline), new Point(bounds.Width, baseline));
        }
    }

    // Bar and phrase guides stay full-height for beat matching, while emphasis lives in the timing rail.
    // Labels appear only when their bar spacing leaves enough room; the original index math owns placement.
    private void RenderBeatComb(
        DrawingContext context, Rect bounds, double combTop, double combHeight,
        bool combAtTop, double start, double span)
    {
        if (BeatGrid is not { Count: >= 2 } grid || span <= 0 || combHeight <= 0)
            return;
        double beatPx = (grid[1] - grid[0]) / span * bounds.Width;
        if (beatPx <= 0 || beatPx * BeatsPerBar < 7)
            return;

        bool drawBeats = beatPx >= 7;
        bool drawLabels = beatPx * BeatsPerBar >= 46 && combHeight >= 12;
        double combBottom = combTop + combHeight;
        double beatNear = combAtTop ? combTop : combTop + combHeight * 0.64;
        double beatFar = combAtTop ? combTop + combHeight * 0.36 : combBottom;
        var beatPen = new Pen(BeatBrush, 1);
        var barPen = new Pen(BarLineBrush, 1);
        var phrasePen = new Pen(DownbeatBrush, 1.5);
        int offset = DownbeatOffset;
        int foldedOffset = ((offset % BeatsPerBar) + BeatsPerBar) % BeatsPerBar;
        double end = start + span;
        for (int i = 0; i < grid.Count; i++)
        {
            double fraction = grid[i];
            if (fraction < start || fraction > end)
                continue;
            bool isBar = IsBarDownbeat(i, offset, BeatsPerBar);
            if (!isBar && !drawBeats)
                continue;

            double x = (fraction - start) / span * bounds.Width;
            if (!isBar)
            {
                using (context.PushOpacity(0.45))
                    context.DrawLine(beatPen, new Point(x, beatNear), new Point(x, beatFar));
                continue;
            }

            bool isPhrase = IsPhraseDownbeat(i, offset, BeatsPerBar, BarsPerPhrase);
            Pen pen = isPhrase ? phrasePen : barPen;
            IBrush marker = isPhrase ? DownbeatBrush : BarLineBrush;
            using (context.PushOpacity(isPhrase ? 0.55 : 0.26))
                context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
            using (context.PushOpacity(0.90))
            {
                context.DrawLine(pen, new Point(x, combTop + 2), new Point(x, combBottom - 2));
                double capY = combAtTop ? combTop + 1 : combBottom - 3;
                context.DrawRectangle(marker, null, new Rect(x - (isPhrase ? 3 : 2), capY, isPhrase ? 6 : 4, 2));
            }

            if (drawLabels && x >= 0 && x < bounds.Width - 25)
            {
                int barNumber = (i - foldedOffset) / BeatsPerBar + 1;
                if (barNumber < 1)
                    continue;
                var label = new FormattedText(barNumber.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 9, marker);
                using (context.PushOpacity(isPhrase ? 0.9 : 0.65))
                    context.DrawText(label, new Point(x + 5, combTop + (combHeight - label.Height) / 2));
            }
        }
    }

    private void RenderPlayhead(DrawingContext context, Rect bounds, double start, double span)
    {
        double x = (Math.Clamp(Progress, 0, 1) - start) / span * bounds.Width;
        if (x <= 0 || x >= bounds.Width)
            return;

        using (context.PushOpacity(0.07))
            context.DrawLine(new Pen(PlayheadBrush, 7), new Point(x, 0), new Point(x, bounds.Height));
        using (context.PushOpacity(0.18))
            context.DrawLine(new Pen(PlayheadBrush, 3), new Point(x, 0), new Point(x, bounds.Height));
        context.DrawLine(new Pen(PlayheadBrush, 1.4), new Point(x, 0), new Point(x, bounds.Height));

        if (bounds.Height < 28)
            return;
        var cap = new StreamGeometry();
        using (StreamGeometryContext path = cap.Open())
        {
            double edge = CombAtTop ? bounds.Height : 0;
            double direction = CombAtTop ? -1 : 1;
            path.BeginFigure(new Point(x - 4, edge), true);
            path.LineTo(new Point(x + 4, edge));
            path.LineTo(new Point(x + 4, edge + direction * 3));
            path.LineTo(new Point(x, edge + direction * 7));
            path.LineTo(new Point(x - 4, edge + direction * 3));
            path.EndFigure(true);
        }
        context.DrawGeometry(PlayheadBrush, null, cap);
    }
}