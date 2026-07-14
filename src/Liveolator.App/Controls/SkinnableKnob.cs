using Avalonia;
using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// A rotary knob that renders from a PNG filmstrip <see cref="Skin"/> (doc 30) instead of the vector
/// look. It inherits every behaviour from <see cref="Knob"/> — the same vertical-drag / arrow-key /
/// double-click-home interaction and the same two-way <c>Value</c> binding — so a skinned and a vector
/// knob emit identically through the action seam (doc 04). Only the drawing differs: with a skin it
/// blits the frame for the current value; with no skin it falls back to the inherited vector render, so
/// a theme that ships no images simply keeps today's look.
/// </summary>
public sealed class SkinnableKnob : Knob
{
    public static readonly StyledProperty<KnobSkin?> SkinProperty =
        AvaloniaProperty.Register<SkinnableKnob, KnobSkin?>(nameof(Skin));

    static SkinnableKnob()
    {
        AffectsRender<SkinnableKnob>(SkinProperty);
    }

    /// <summary>The loaded filmstrip skin, or null to render the inherited vector look.</summary>
    public KnobSkin? Skin
    {
        get => GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        KnobSkin? skin = Skin;
        if (skin is null)
        {
            base.Render(context);
            return;
        }

        Rect bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // Draw the frame square and centred so a non-square control letterboxes rather than distorts.
        double side = Math.Min(bounds.Width, bounds.Height);
        var dest = new Rect((bounds.Width - side) / 2, (bounds.Height - side) / 2, side, side);
        context.DrawImage(skin.Strip, skin.FrameRect(Value), dest);
    }
}
