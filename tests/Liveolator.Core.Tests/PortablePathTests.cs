using Liveolator.Core;
using Xunit;

namespace Liveolator.Core.Tests;

/// <summary>
/// The catalog stores whatever path a track was scanned under — a Windows drive path, a UNC share, or a
/// Unix path — and Liveolator runs on both Windows and macOS. These assertions use all three path shapes
/// and must hold identically on EVERY OS (that is the whole point: System.IO.Path only splits the host
/// separator, so on macOS it returned the whole "C:\a\b.mp3" as the file name, breaking catalog matching).
/// </summary>
public class PortablePathTests
{
    [Theory]
    [InlineData(@"C:\music\My Track.mp3", "My Track.mp3")]      // Windows drive path
    [InlineData(@"\\192.168.68.131\Storage\track.flac", "track.flac")] // UNC share
    [InlineData("/Users/dj/song.wav", "song.wav")]             // Unix path
    [InlineData(@"mixed/path\to\file.aac", "file.aac")]        // both separators
    [InlineData("bare.mp3", "bare.mp3")]                       // no separator
    [InlineData(@"C:\music\", "")]                             // trailing separator
    [InlineData("", "")]
    public void GetFileName_SplitsOnBothSeparators_OnAnyOs(string path, string expected)
        => Assert.Equal(expected, PortablePath.GetFileName(path));

    [Theory]
    [InlineData(@"C:\music\My Track.mp3", "My Track")]
    [InlineData(@"C:\music\My Track", "My Track")]             // no extension (the deck-title case)
    [InlineData(@"\\srv\share\a.b.flac", "a.b")]               // multi-dot keeps all but the last ext
    [InlineData("/u/x/song", "song")]
    [InlineData(@"C:\music\track.", "track")]                  // trailing dot
    public void GetFileNameWithoutExtension_StripsFinalExtension_OnAnyOs(string path, string expected)
        => Assert.Equal(expected, PortablePath.GetFileNameWithoutExtension(path));

    // The server catalogs POSIX mount paths; this machine reaches the same files over a UNC share.
    // Without the rebase every pulled row points at a path that does not exist here.
    [Fact]
    public void Rebase_maps_a_posix_mount_to_a_unc_share()
    {
        Assert.Equal(
            @"\192.168.68.131\Storage\Navidrome\music\a\b.mp3",
            PortablePath.Rebase(
                "/media/simon/external_4tb/Navidrome/music/a/b.mp3",
                "/media/simon/external_4tb",
                @"\192.168.68.131\Storage"));
    }

    [Fact]
    public void Rebase_tolerates_trailing_separators_on_either_prefix()
    {
        Assert.Equal(
            @"\host\Storage\x.mp3",
            PortablePath.Rebase("/mnt/music/x.mp3", "/mnt/music/", @"\host\Storage\"));
    }

    [Fact]
    public void Rebase_matches_the_prefix_case_insensitively()
    {
        Assert.Equal(
            @"\host\S\x.mp3",
            PortablePath.Rebase(@"D:\Music\x.mp3", @"d:\music", @"\host\S"));
    }

    [Fact]
    public void Rebase_returns_null_for_a_path_under_a_different_root()
    {
        Assert.Null(PortablePath.Rebase("/other/root/x.mp3", "/mnt/music", @"\host\S"));
    }

    // The trap this guards: a plain StartsWith would rebase /mnt/musicvideos under the /mnt/music prefix.
    [Fact]
    public void Rebase_does_not_match_a_sibling_sharing_a_name_prefix()
    {
        Assert.Null(PortablePath.Rebase("/mnt/musicvideos/x.mp4", "/mnt/music", @"\host\S"));
    }

    [Fact]
    public void Rebase_maps_the_root_itself()
    {
        Assert.Equal(@"\host\S", PortablePath.Rebase("/mnt/music", "/mnt/music", @"\host\S"));
    }

    [Fact]
    public void Rebase_can_map_back_to_a_posix_root()
    {
        Assert.Equal(
            "/mnt/music/a/b.mp3",
            PortablePath.Rebase(@"\host\Storage\a\b.mp3", @"\host\Storage", "/mnt/music"));
    }

    [Theory]
    [InlineData(null, "/a", "/b")]
    [InlineData("/a/x.mp3", null, "/b")]
    [InlineData("/a/x.mp3", "   ", "/b")]
    [InlineData("/a/x.mp3", "/a", null)]
    public void Rebase_returns_null_when_an_input_is_missing(string? path, string? from, string? to)
        => Assert.Null(PortablePath.Rebase(path, from, to));
}
