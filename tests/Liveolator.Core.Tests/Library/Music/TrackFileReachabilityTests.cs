using System;
using System.IO;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library.Music;

/// <summary>The reachability guard that keeps a scan/auto-cue pass from hanging on an offline or
/// un-downloaded cloud placeholder.</summary>
public sealed class TrackFileReachabilityTests
{
    [Fact]
    public void A_missing_file_is_not_decodable()
        => Assert.False(TrackFileReachability.IsLocallyDecodable(
            Path.Combine(Path.GetTempPath(), $"liveolator-missing-{Guid.NewGuid():N}.wav")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_path_is_not_decodable(string? path)
        => Assert.False(TrackFileReachability.IsLocallyDecodable(path!));

    [Fact]
    public void A_normal_local_file_is_decodable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-local-{Guid.NewGuid():N}.wav");
        File.WriteAllText(path, "x");
        try
        {
            Assert.True(TrackFileReachability.IsLocallyDecodable(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_offline_placeholder_is_not_decodable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-offline-{Guid.NewGuid():N}.wav");
        File.WriteAllText(path, "x");
        try
        {
            // Mark the file Offline (the classic dehydrated-placeholder attribute). Setting it can be a
            // no-op on some filesystems; only assert the skip when the attribute actually stuck.
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);
            if ((File.GetAttributes(path) & FileAttributes.Offline) != 0)
                Assert.False(TrackFileReachability.IsLocallyDecodable(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
