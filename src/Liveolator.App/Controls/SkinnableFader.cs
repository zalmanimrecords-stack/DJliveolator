using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// A linear fader that renders from a PNG <see cref="Skin"/> (doc 30) — a track image plus a thumb cap —
/// instead of the vector look. Inherits every behaviour from <see cref="Fader"/> (the same drag / arrow-key
/// interaction and two-way <c>Value</c> binding), so a skinned and a vector fader emit identically through
/// the action seam (doc 04). With no skin it falls back to the inherited vector render. This POC handles the
/// vertical orientation (mixer channel faders); the cap rides the track at the value position.
/// </summary>
public sealed class SkinnableFader : Fader
{
    /// <summary>Vertical padding (px) at each end so the cap never clips the track edge. Matches <see cref="Fader"/>.</summary>
    private const double Pad = 10.0;

    public static readonly StyledProperty<FaderSkin?> SkinProperty =
        AvaloniaProperty.Register<SkinnableFader, FaderSkin?>(nameof(Skin));

    static SkinnableFader()
    {
        AffectsRender<SkinnableFader>(SkinProperty);
    }

    /// <summary>The loaded track+thumb skin, or null to render the inherited vector look.</summary>
    public FaderSkin? Skin
    {
        get => GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        FaderSkin? skin = Skin;
        // The vector Fader supports both orientations; the image POC is vertical only — fall back otherwise.
        if (skin is null || Orientation != Orientation.Vertical)
        {
            base.Render(context);
            return;
        }

        Rect bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        double trackW = skin.Track.PixelSize.Width;
        var trackDest = new Rect((bounds.Width - trackW) / 2, 0, trackW, bounds.Height);
        context.DrawImage(skin.Track, PixelRect(skin.Track), trackDest);

        double thumbW = skin.Thumb.PixelSize.Width;
        double thumbH = skin.Thumb.PixelSize.Height;
        double centreY = VerticalThumbCentreY(bounds.Height, Pad, Value);
        var thumbDest = new Rect((bounds.Width - thumbW) / 2, centreY - (thumbH / 2), thumbW, thumbH);
        context.DrawImage(skin.Thumb, PixelRect(skin.Thumb), thumbDest);
    }

    /// <summary>Y of the thumb centre for a 0..1 value: value 0 sits at the bottom, value 1 at the top.</summary>
    internal static double VerticalThumbCentreY(double height, double pad, double value)
    {
        double top = pad;
        double bottom = height - pad;
        double length = Math.Max(1, bottom - top);
        double v = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
        return bottom - (v * length);
    }

    private static Rect PixelRect(Avalonia.Media.Imaging.Bitmap bitmap)
        => new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
}
