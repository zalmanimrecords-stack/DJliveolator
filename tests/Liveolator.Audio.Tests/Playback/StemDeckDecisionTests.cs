using System.Collections.Generic;
using Liveolator.Audio.Playback;
using Liveolator.Core.Analysis.Stems;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// The pure stem-vs-single-file load decision (doc 32 §2b). This is the managed logic that IS testable
/// without BASS — the native submix behaviour is owner-verified on hardware.
/// </summary>
public sealed class StemDeckDecisionTests
{
    private static StemSet Complete(string root = @"C:\cache")
        => new("S:\\music\\track.flac", "umxhq", new Dictionary<StemKind, string>
        {
            [StemKind.Drums] = root + @"\drums.flac",
            [StemKind.Bass] = root + @"\bass.flac",
            [StemKind.Vocals] = root + @"\vocals.flac",
            [StemKind.Other] = root + @"\other.flac",
        });

    [Fact]
    public void GateOff_NeverUsesStems()
    {
        Assert.False(StemDeckDecision.ShouldUseStems(gateEnabled: false, Complete(), out string reason));
        Assert.Equal("stems gate off", reason);
    }

    [Fact]
    public void GateOn_NoCachedSet_DoesNotUseStems()
    {
        Assert.False(StemDeckDecision.ShouldUseStems(gateEnabled: true, set: null, out string reason));
        Assert.Equal("no cached stems", reason);
    }

    [Fact]
    public void GateOn_IncompleteSet_DoesNotUseStems()
    {
        var incomplete = new StemSet("t.flac", "umxhq", new Dictionary<StemKind, string>
        {
            [StemKind.Drums] = @"C:\cache\drums.flac", // missing the other three
        });
        Assert.False(StemDeckDecision.ShouldUseStems(gateEnabled: true, incomplete, out string reason));
        Assert.Equal("incomplete stem set", reason);
    }

    [Fact]
    public void GateOn_NetworkStemPath_DoesNotUseStems()
    {
        StemSet onNetwork = Complete(root: @"\\192.168.68.131\Storage\stems");
        Assert.False(StemDeckDecision.ShouldUseStems(gateEnabled: true, onNetwork, out string reason));
        Assert.Equal("stem path is not local", reason);
    }

    [Fact]
    public void GateOn_CompleteLocalSet_UsesStems()
    {
        Assert.True(StemDeckDecision.ShouldUseStems(gateEnabled: true, Complete(), out string reason));
        Assert.Equal("complete local stem set", reason);
    }

    [Theory]
    [InlineData(@"C:\cache\drums.flac", true)]
    [InlineData(@"\\server\share\drums.flac", false)] // UNC
    [InlineData("//server/share/drums.flac", false)]   // forward-slash UNC
    [InlineData("relative/path.flac", false)]          // not fully qualified
    [InlineData("", false)]
    public void IsLocalPath_AcceptsOnlyLocalFullyQualifiedPaths(string path, bool expected)
        => Assert.Equal(expected, StemDeckDecision.IsLocalPath(path));
}
