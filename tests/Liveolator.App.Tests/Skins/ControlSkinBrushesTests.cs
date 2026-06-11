using Avalonia.Media;
using Liveolator.App.Skins;
using Liveolator.Core.Skins;

namespace Liveolator.App.Tests.Skins;

public sealed class ControlSkinBrushesTests
{
    [Fact]
    public void From_maps_accent_and_leaves_omitted_colours_null()
    {
        var skin = new ControlSkinFile { Name = "Min", Kind = ControlSkinKind.Knob, Accent = "#2F80F6" };

        ControlSkinBrushes brushes = ControlSkinBrushes.From(skin);

        Assert.Equal(Color.Parse("#2F80F6"), ((ISolidColorBrush)brushes.Accent).Color);
        Assert.Null(brushes.Track);
        Assert.Null(brushes.Pointer);
        Assert.Null(brushes.Body);
    }

    [Fact]
    public void From_maps_all_declared_colours()
    {
        var skin = new ControlSkinFile
        {
            Name = "Full", Kind = ControlSkinKind.Knob,
            Accent = "#2F80F6", Track = "#26303F", Pointer = "#E7ECF3", Body = "#12171F",
        };

        ControlSkinBrushes brushes = ControlSkinBrushes.From(skin);

        Assert.Equal(Color.Parse("#26303F"), ((ISolidColorBrush)brushes.Track!).Color);
        Assert.Equal(Color.Parse("#E7ECF3"), ((ISolidColorBrush)brushes.Pointer!).Color);
        Assert.Equal(Color.Parse("#12171F"), ((ISolidColorBrush)brushes.Body!).Color);
    }
}
