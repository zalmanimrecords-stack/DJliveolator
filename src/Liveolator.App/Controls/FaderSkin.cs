using Avalonia.Media.Imaging;

namespace Liveolator.App.Controls;

/// <summary>
/// A loaded fader/slider skin (doc 30): two PNGs — a <see cref="Track"/> drawn down the control and a
/// <see cref="Thumb"/> cap blitted at the value position. This is the DJ-software track+cap model (a fader's
/// travel is continuous, so a per-frame filmstrip like the knob's would be wasteful). Pure presentation;
/// the only logic is where the thumb sits for a value (see <see cref="SkinnableFader"/>). Vertical in this
/// POC — a horizontal skin (or rotating the same assets) is a follow-up.
/// </summary>
public sealed class FaderSkin
{
    public FaderSkin(Bitmap track, Bitmap thumb)
    {
        Track = track ?? throw new ArgumentNullException(nameof(track));
        Thumb = thumb ?? throw new ArgumentNullException(nameof(thumb));
    }

    public Bitmap Track { get; }
    public Bitmap Thumb { get; }
}
