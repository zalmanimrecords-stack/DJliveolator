using Avalonia.Media;
using Liveolator.Core.Skins;
using Liveolator.App.Controls;

namespace Liveolator.App.Skins;

/// <summary>
/// Turns an agent-authored parametric <see cref="ControlSkinFile"/> (doc 30) into Avalonia brushes and
/// applies them to the existing vector <see cref="Knob"/> / <see cref="Fader"/> — the app-side half of the
/// "create a control look via MCP" loop. Only colours the skin actually declares are applied; omitted ones
/// leave the control's default brush untouched. Pure mapping, no business logic.
/// </summary>
public sealed record ControlSkinBrushes(IBrush Accent, IBrush? Track, IBrush? Pointer, IBrush? Body)
{
    public static ControlSkinBrushes From(ControlSkinFile skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        return new ControlSkinBrushes(
            Accent: Brush(skin.Accent)!,
            Track: Brush(skin.Track),
            Pointer: Brush(skin.Pointer),
            Body: Brush(skin.Body));
    }

    public void ApplyTo(Knob knob)
    {
        ArgumentNullException.ThrowIfNull(knob);
        knob.ArcBrush = Accent;
        if (Track is not null) knob.TrackBrush = Track;
        if (Pointer is not null) knob.PointerBrush = Pointer;
        if (Body is not null) knob.CapBrush = Body;
    }

    public void ApplyTo(Fader fader)
    {
        ArgumentNullException.ThrowIfNull(fader);
        fader.FillBrush = Accent;
        if (Track is not null) fader.TrackBrush = Track;
        // The slider thumb takes the body colour, falling back to the pointer colour when only that is set.
        IBrush? thumb = Body ?? Pointer;
        if (thumb is not null) fader.ThumbBrush = thumb;
    }

    private static IBrush? Brush(string? hex)
        => string.IsNullOrWhiteSpace(hex) ? null : new SolidColorBrush(Color.Parse(hex));
}
