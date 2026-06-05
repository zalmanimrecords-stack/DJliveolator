using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// A decorative, deterministic waveform graphic used as the deck's "no track loaded" placeholder
/// (a real waveform from decoded audio is a later increment, doc 18). It draws mirrored vertical bars
/// in a faint accent tone; purely visual, no data or interaction.
/// </summary>
public sealed class WaveformStrip : Control
{
    public static readonly StyledProperty<IBrush> BarBrushProperty =
        AvaloniaProperty.Register<WaveformStrip, IBrush>(
            nameof(BarBrush), new SolidColorBrush(Color.FromArgb(0x80, 0x2F, 0x80, 0xF6)));

    static WaveformStrip()
    {
        AffectsRender<WaveformStrip>(BarBrushProperty);
    }

    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        Rect b = Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;

        double cy = b.Height / 2;
        double maxAmp = (b.Height / 2) - 3;
        const double step = 3.0;
        var pen = new Pen(BarBrush, 2) { LineCap = PenLineCap.Round };

        for (double x = 2; x < b.Width - 2; x += step)
        {
            // deterministic pseudo-waveform: a couple of detuned sines + a slow envelope
            double env = 0.45 + (0.55 * Math.Abs(Math.Sin(x * 0.018)));
            double shape = (Math.Sin(x * 0.13) * 0.6) + (Math.Sin((x * 0.37) + 1.0) * 0.4);
            double amp = maxAmp * env * (0.25 + (0.75 * Math.Abs(shape)));
            context.DrawLine(pen, new Point(x, cy - amp), new Point(x, cy + amp));
        }
    }
}
