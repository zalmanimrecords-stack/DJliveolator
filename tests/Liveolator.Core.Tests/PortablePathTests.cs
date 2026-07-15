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
}
