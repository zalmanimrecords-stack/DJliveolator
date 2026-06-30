using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Stems;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Stems;

public class StemSetTests
{
    private static Dictionary<StemKind, string> AllFour() => new()
    {
        [StemKind.Drums] = "/c/drums.flac",
        [StemKind.Bass] = "/c/bass.flac",
        [StemKind.Vocals] = "/c/vocals.flac",
        [StemKind.Other] = "/c/other.flac",
    };

    [Fact]
    public void StemSet_HoldsSourceModelAndPaths()
    {
        var set = new StemSet("/c/song.mp3", "umxhq", AllFour());

        Assert.Equal("/c/song.mp3", set.SourcePath);
        Assert.Equal("umxhq", set.ModelId);
        Assert.Equal("/c/drums.flac", set.StemPaths[StemKind.Drums]);
    }

    [Fact]
    public void IsComplete_TrueWhenAllFourPresent()
        => Assert.True(new StemSet("/c/song.mp3", "umxhq", AllFour()).IsComplete);

    [Fact]
    public void IsComplete_FalseWhenAStemMissing()
    {
        var partial = AllFour();
        partial.Remove(StemKind.Vocals);
        Assert.False(new StemSet("/c/song.mp3", "umxhq", partial).IsComplete);
    }

    [Fact]
    public async Task FakeSeparator_HonorsTheSeamContract()
    {
        IStemSeparator separator = new FakeStemSeparator(
            new StemSet("/c/song.mp3", "umxhq", AllFour()));

        StemSet? result = await separator.SeparateAsync(decoder: null!, "/c/song.mp3");

        Assert.NotNull(result);
        Assert.True(result!.IsComplete);
    }

    [Fact]
    public async Task FakeSeparator_ReturnsNullWhenUnavailable()
    {
        IStemSeparator separator = new FakeStemSeparator(result: null);
        Assert.Null(await separator.SeparateAsync(decoder: null!, "/c/song.mp3"));
    }

    /// <summary>Minimal in-memory separator proving the seam is implementable without Python.</summary>
    private sealed class FakeStemSeparator(StemSet? result) : IStemSeparator
    {
        public Task<StemSet?> SeparateAsync(IAudioDecoder decoder, string filePath, CancellationToken ct = default)
            => Task.FromResult(result);
    }
}
