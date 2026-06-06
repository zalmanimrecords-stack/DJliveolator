using Liveolator.Core.Beat;
using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class LiveClockSelectorTests
{
    [Fact]
    public void Select_prefers_the_audio_clock_when_present()
    {
        var audio = new FakeBeatClock();
        var manual = new FakeBeatClock();

        Assert.Same(audio, LiveClockSelector.Select(audio, manual));
    }

    [Fact]
    public void Select_falls_back_to_the_manual_clock_when_audio_is_absent()
    {
        var manual = new FakeBeatClock();

        Assert.Same(manual, LiveClockSelector.Select(null, manual));
    }

    [Fact]
    public void Select_requires_a_manual_fallback_clock()
    {
        Assert.Throws<ArgumentNullException>(() => LiveClockSelector.Select(new FakeBeatClock(), null!));
    }
}
