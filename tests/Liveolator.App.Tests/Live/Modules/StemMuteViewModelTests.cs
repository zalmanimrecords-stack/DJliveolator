using Liveolator.App.Features.Live.Modules;
using Liveolator.Core.Analysis.Stems;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class StemMuteViewModelTests
{
    [Theory]
    [InlineData(StemKind.Drums)]
    [InlineData(StemKind.Bass)]
    [InlineData(StemKind.Vocals)]
    [InlineData(StemKind.Other)]
    public void Icon_HasGeometry_ForEveryStem(StemKind kind)
    {
        var vm = new StemMuteViewModel(kind, onToggle: null);

        // Each stem's face is a vector glyph (the text was replaced by an icon); the name survives as
        // the tooltip so the button stays identifiable.
        Assert.False(string.IsNullOrWhiteSpace(vm.Icon));
        Assert.Equal(kind.ToString().ToUpperInvariant(), vm.Name);
    }
}
