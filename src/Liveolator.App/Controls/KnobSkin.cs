using Avalonia;
using Avalonia.Media.Imaging;

namespace Liveolator.App.Controls;

/// <summary>
/// A loaded knob filmstrip skin (doc 30): one vertical strip bitmap holding <see cref="FrameCount"/>
/// frames stacked top→bottom, the knob rendered from min (top) to max (bottom). Pure presentation —
/// the only logic is picking the frame for a normalized 0..1 value. Owned by the App layer because it
/// holds an Avalonia <see cref="Bitmap"/>; the Core theme manifest only references it by path (doc 30).
/// </summary>
public sealed class KnobSkin
{
    public KnobSkin(Bitmap strip, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(strip);
        if (frameCount < 1)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "A filmstrip needs at least one frame.");
        Strip = strip;
        FrameCount = frameCount;
    }

    public Bitmap Strip { get; }
    public int FrameCount { get; }

    public double FrameWidth => Strip.PixelSize.Width;
    public double FrameHeight => (double)Strip.PixelSize.Height / FrameCount;

    /// <summary>Source rectangle (in the strip's own pixels) of the frame shown for a 0..1 value.</summary>
    public Rect FrameRect(double value)
    {
        int index = FrameIndexFor(value, FrameCount);
        return new Rect(0, index * FrameHeight, FrameWidth, FrameHeight);
    }

    /// <summary>Maps a normalized value to a frame index. Out-of-range and NaN clamp into 0..count-1.</summary>
    internal static int FrameIndexFor(double value, int frameCount)
    {
        double clamped = double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);
        int index = (int)Math.Round(clamped * (frameCount - 1), MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, frameCount - 1);
    }
}
