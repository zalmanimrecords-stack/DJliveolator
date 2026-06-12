using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class StudioSetTests
{
    [Fact]
    public void Empty_HasNameAndNoEntries()
    {
        StudioSet set = StudioSet.Empty("Warmup");

        Assert.Equal("Warmup", set.Name);
        Assert.Empty(set.Entries);
    }

    [Fact]
    public void WithEntries_ReplacesEntries_KeepsName()
    {
        StudioSet set = StudioSet.Empty("Set");
        var entries = new[] { new StudioEntry("/m/a.wav"), new StudioEntry("/m/b.wav") };

        StudioSet updated = set.WithEntries(entries);

        Assert.Equal("Set", updated.Name);
        Assert.Equal(2, updated.Entries.Count);
    }

    [Fact]
    public void WithEntries_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => StudioSet.Empty("x").WithEntries(null!));

    [Fact]
    public void TrackPaths_ProjectsEntryPathsInOrder()
    {
        var set = new StudioSet("Set", new[]
        {
            new StudioEntry("/m/a.wav"),
            new StudioEntry("/m/b.wav"),
            new StudioEntry("/m/c.wav"),
        });

        Assert.Equal(new[] { "/m/a.wav", "/m/b.wav", "/m/c.wav" }, set.TrackPaths);
    }

    [Fact]
    public void FirstEntry_HasNoIncomingTransition_ByConvention()
    {
        // The seed entry is led into by nothing; planners must leave TransitionIn null on entry 0.
        var seed = new StudioEntry("/m/a.wav");

        Assert.Null(seed.TransitionIn);
    }

    [Fact]
    public void Cut_DefaultTransition_IsZeroLengthSharpTailOverlap()
    {
        StudioTransition cut = StudioTransition.Cut;

        Assert.Equal(TransitionKind.Cut, cut.Kind);
        Assert.Equal(0, cut.LengthBeats);
        Assert.Equal(CrossfaderCurve.Sharp, cut.Curve);
        Assert.Equal(TransitionAnchor.TailOverlap, cut.Anchor);
    }
}
