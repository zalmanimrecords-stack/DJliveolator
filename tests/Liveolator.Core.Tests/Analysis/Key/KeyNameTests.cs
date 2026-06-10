using Liveolator.Core.Analysis.Key;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Key;

public class KeyNameTests
{
    [Theory]
    [InlineData("Am", 9, KeyMode.Minor, "8A")]       // GetSongBPM short form
    [InlineData("A minor", 9, KeyMode.Minor, "8A")]  // full word
    [InlineData("C", 0, KeyMode.Major, "8B")]        // bare note → major
    [InlineData("C major", 0, KeyMode.Major, "8B")]
    [InlineData("F#m", 6, KeyMode.Minor, "11A")]     // sharp
    [InlineData("Dbm", 1, KeyMode.Minor, "12A")]     // flat
    [InlineData("Ab", 8, KeyMode.Major, "4B")]       // flat, major
    [InlineData("g#min", 8, KeyMode.Minor, "1A")]    // lowercase + "min"
    public void TryParse_ParsesCommonNotations(string name, int tonic, KeyMode mode, string camelot)
    {
        Assert.True(KeyName.TryParse(name, out MusicalKey? key));
        Assert.Equal(tonic, key!.Tonic);
        Assert.Equal(mode, key.Mode);
        Assert.Equal(camelot, key.Camelot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("H")]        // not a note letter
    [InlineData("xyz")]
    [InlineData("A flat")]   // word accidentals are not parsed (avoid guessing)
    public void TryParse_RejectsUnrecognized(string? name)
        => Assert.False(KeyName.TryParse(name, out _));
}
