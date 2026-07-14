using Liveolator.Audio;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Platform;
using Xunit;

namespace Liveolator.Integration.Tests;

/// <summary>End-to-end: real folder + real WAV decode + library scan + analysis.</summary>
public class MusicLibraryScanEndToEndTests
{
    private const int Sr = 44100;

    [Fact]
    public async Task Scan_RealWavFolder_AnalyzesTracks()
    {
        using var dir = new TempDir();
        dir.Write("track-120.wav", TestMedia.Pcm16Wav(TestMedia.ClickTrain(120, Sr, 8), Sr));
        dir.Write("crate/track-128.wav", TestMedia.Pcm16Wav(TestMedia.ClickTrain(128, Sr, 8), Sr));

        var library = new MusicLibrary(new FileSystemEnumerator(), new WavAudioDecoder());
        await library.ScanAsync(new[] { dir.Path });

        Assert.Equal(2, library.Count);
        Assert.All(library.All, t =>
        {
            Assert.NotEqual(MediaAnalysisStatus.Failed, t.Status);
            Assert.NotNull(t.Bpm);
            Assert.NotNull(t.Duration);
        });

        MusicTrack t120 = library.All.Single(t => t.File.Path.EndsWith("track-120.wav"));
        Assert.InRange(t120.Bpm!.Bpm, 117.0, 123.0);
    }

    [Fact]
    public async Task Scan_CorruptWav_MarkedFailed_OthersOk()
    {
        using var dir = new TempDir();
        dir.Write("good.wav", TestMedia.Pcm16Wav(TestMedia.ClickTrain(120, Sr, 6), Sr));
        dir.Write("broken.wav", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }); // not a real WAV

        var library = new MusicLibrary(new FileSystemEnumerator(), new WavAudioDecoder());
        await library.ScanAsync(new[] { dir.Path });

        Assert.Equal(2, library.Count);
        Assert.Equal(MediaAnalysisStatus.Failed, library.TryGet(Path.Combine(dir.Path, "broken.wav"))!.Status);
    }
}
