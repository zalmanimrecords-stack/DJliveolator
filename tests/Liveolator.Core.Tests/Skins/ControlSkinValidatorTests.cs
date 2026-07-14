using Liveolator.Core.Skins;

namespace Liveolator.Core.Tests.Skins;

public sealed class ControlSkinValidatorTests
{
    private static ControlSkinFile Valid() => new()
    {
        Name = "Cobalt Knob",
        Kind = ControlSkinKind.Knob,
        Accent = "#2F80F6",
        Track = "#26303F",
        Pointer = "#E7ECF3",
        Body = "#12171F",
    };

    [Fact]
    public void Accepts_a_well_formed_skin()
        => Assert.True(ControlSkinValidator.Validate(Valid()).IsValid);

    [Fact]
    public void Accent_only_is_enough()
    {
        var skin = new ControlSkinFile { Name = "Minimal", Kind = ControlSkinKind.Slider, Accent = "#FF0000" };
        Assert.True(ControlSkinValidator.Validate(skin).IsValid);
    }

    [Theory]
    [InlineData("knob")]
    [InlineData("SLIDER")]
    public void Kind_is_case_insensitive(string kind)
        => Assert.True(ControlSkinValidator.Validate(Valid() with { Kind = kind }).IsValid);

    [Fact]
    public void Null_is_rejected()
        => Assert.False(ControlSkinValidator.Validate(null).IsValid);

    [Fact]
    public void Missing_name_is_rejected()
        => Assert.False(ControlSkinValidator.Validate(Valid() with { Name = "  " }).IsValid);

    [Fact]
    public void Unknown_kind_is_rejected()
        => Assert.False(ControlSkinValidator.Validate(Valid() with { Kind = "Fader" }).IsValid);

    [Fact]
    public void Missing_accent_is_rejected()
        => Assert.False(ControlSkinValidator.Validate(Valid() with { Accent = "" }).IsValid);

    [Theory]
    [InlineData("2F80F6")]   // no leading #
    [InlineData("#12")]      // too short
    [InlineData("#GGGGGG")]  // not hex
    public void Malformed_colour_is_rejected(string accent)
        => Assert.False(ControlSkinValidator.Validate(Valid() with { Accent = accent }).IsValid);

    [Fact]
    public void Malformed_optional_colour_is_rejected()
        => Assert.False(ControlSkinValidator.Validate(Valid() with { Track = "#XYZ" }).IsValid);

    [Fact]
    public void Argb_colour_is_accepted()
        => Assert.True(ControlSkinValidator.Validate(Valid() with { Accent = "#802F80F6" }).IsValid);
}
