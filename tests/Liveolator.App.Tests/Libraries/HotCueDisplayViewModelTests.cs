using Liveolator.App.Features.Libraries;
using Liveolator.Core.Analysis.Cues;

namespace Liveolator.App.Tests.Libraries;

public sealed class HotCueDisplayViewModelTests
{
    [Fact]
    public void Projects_number_time_label_and_color()
    {
        // 88_200 samples at 44.1 kHz = 2.0 s.
        var cue = new HotCue(Index: 0, PositionSamples: 88_200, Label: "Drop", Color: 0xFF0000);

        var vm = new HotCueDisplayViewModel(cue, sampleRate: 44_100);

        Assert.Equal("1", vm.Number);           // 1-based pad number
        Assert.Equal("0:02.00", vm.Time);
        Assert.Equal("Drop", vm.Label);
        Assert.Equal(0xFF0000, vm.Color);
        Assert.False(vm.IsAuto);
        Assert.Equal(string.Empty, vm.Tag);
    }

    [Fact]
    public void Formats_minutes_and_centiseconds()
    {
        // 1:30.55 = 90.55 s.
        long samples = (long)System.Math.Round(90.55 * 44_100);
        var cue = new HotCue(Index: 7, PositionSamples: samples);

        var vm = new HotCueDisplayViewModel(cue, sampleRate: 44_100);

        Assert.Equal("8", vm.Number);
        Assert.Equal("1:30.55", vm.Time);
    }

    [Fact]
    public void Unlabelled_auto_cue_shows_dash_and_auto_tag()
    {
        var cue = new HotCue(Index: 2, PositionSamples: 0, Label: null, Color: null, IsAuto: true);

        var vm = new HotCueDisplayViewModel(cue, sampleRate: 44_100);

        Assert.Equal("—", vm.Label);
        Assert.Null(vm.Color);
        Assert.True(vm.IsAuto);
        Assert.Equal("auto", vm.Tag);
    }

    [Fact]
    public void Non_positive_sample_rate_yields_dash_time_without_throwing()
    {
        var cue = new HotCue(Index: 0, PositionSamples: 44_100);

        var vm = new HotCueDisplayViewModel(cue, sampleRate: 0);

        Assert.Equal("—", vm.Time);
    }
}
