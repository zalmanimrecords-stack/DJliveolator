using Liveolator.Audio;

namespace Liveolator.Audio.Tests;

/// <summary>
/// Covers the report parser without launching FFmpeg, so the rule is testable on a machine (and in CI)
/// where FFmpeg is absent. The subprocess path itself is exercised by the integration tests.
/// </summary>
public sealed class FfmpegLoudnessMeterTests
{
    // Trimmed to the shape ebur128 actually emits: per-frame progress lines that also carry "I:", then the
    // summary block whose integrated figure is the one that counts.
    private const string Report = """
        [Parsed_ebur128_0 @ 0000021c] t: 1.19969 M: -21.4 S: -120.7 I: -19.8 LUFS LRA: 0.0 LU
        [Parsed_ebur128_0 @ 0000021c] t: 2.39969 M: -10.2 S: -13.4 I: -11.2 LUFS LRA: 2.1 LU
        [Parsed_ebur128_0 @ 0000021c] Summary:

          Integrated loudness:
            I:          -8.4 LUFS
            Threshold:  -18.6 LUFS

          Loudness range:
            LRA:         3.2 LU
        """;

    [Fact]
    public void ParseIntegratedLufs_TakesTheSummaryFigure_NotAProgressLine()
    {
        Assert.Equal(-8.4, FfmpegLoudnessMeter.ParseIntegratedLufs(Report));
    }

    [Fact]
    public void ParseIntegratedLufs_ReadsAPositiveValue()
    {
        Assert.Equal(2.5, FfmpegLoudnessMeter.ParseIntegratedLufs("    I:          2.5 LUFS"));
    }

    [Fact]
    public void ParseIntegratedLufs_TreatsDigitalSilence_AsUnmeasured()
    {
        // -inf is not a level a gain can be computed from, so it must read as "no measurement".
        Assert.Null(FfmpegLoudnessMeter.ParseIntegratedLufs("    I:        -inf LUFS"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ffmpeg version 6.0\nInvalid data found when processing input\n")]
    public void ParseIntegratedLufs_ReturnsNull_WhenTheReportHasNoIntegratedFigure(string report)
    {
        Assert.Null(FfmpegLoudnessMeter.ParseIntegratedLufs(report));
    }
}
