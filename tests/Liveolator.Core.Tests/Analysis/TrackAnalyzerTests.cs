using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Key;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class TrackAnalyzerTests
{
    [Fact]
    public void AnalyzePcm_CMajorTriad_DetectsCMajor()
    {
        // Root + fifth emphasized over the third, mirroring the key-profile weighting.
        var triad = TestSignals.Chord(
            new[] { (261.63, 1.0), (329.63, 0.6), (392.00, 0.8) }, // C4, E4, G4
            44100, seconds: 2.0);

        TrackAnalysisResult result = new TrackAnalyzer().AnalyzePcm(triad, 44100);

        Assert.Equal(0, result.Key.Tonic);
        Assert.Equal(KeyMode.Major, result.Key.Mode);
        Assert.Equal("8B", result.Key.Camelot);
    }

    [Fact]
    public void AnalyzePcm_AMinorTriad_DetectsAMinor()
    {
        var triad = TestSignals.Chord(
            new[] { (440.00, 1.0), (523.25, 0.6), (659.25, 0.8) }, // A4, C5, E5
            44100, seconds: 2.0);

        TrackAnalysisResult result = new TrackAnalyzer().AnalyzePcm(triad, 44100);

        Assert.Equal(9, result.Key.Tonic);
        Assert.Equal(KeyMode.Minor, result.Key.Mode);
        Assert.Equal("8A", result.Key.Camelot);
    }

    [Fact]
    public void AnalyzePcm_ReportsDuration()
    {
        var tone = TestSignals.Sine(440, 44100, seconds: 2.0);
        TrackAnalysisResult result = new TrackAnalyzer().AnalyzePcm(tone, 44100);
        Assert.InRange(result.Duration.TotalSeconds, 1.99, 2.01);
    }

    [Fact]
    public async Task AnalyzeAsync_DecodesThenMeasuresBpm()
    {
        var decoder = new FakeAudioDecoder(TestSignals.ClickTrain(120, 44100, seconds: 10));

        TrackAnalysisResult result = await new TrackAnalyzer().AnalyzeAsync(decoder, "song.wav");

        Assert.InRange(result.Bpm.Bpm, 117.0, 123.0);
    }

    [Fact]
    public async Task AnalyzeAsync_UnsupportedFile_Throws()
    {
        var decoder = new FakeAudioDecoder(Array.Empty<float>()) { CanDecodeResult = false };
        await Assert.ThrowsAsync<NotSupportedException>(
            () => new TrackAnalyzer().AnalyzeAsync(decoder, "song.xyz"));
    }

    /// <summary>In-memory decoder that streams a preset PCM buffer in fixed-size blocks.</summary>
    private sealed class FakeAudioDecoder : IAudioDecoder
    {
        private readonly float[] _pcm;
        public bool CanDecodeResult { get; init; } = true;

        public FakeAudioDecoder(float[] pcm) => _pcm = pcm;

        public bool CanDecode(string filePath) => CanDecodeResult;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const int block = 4096;
            for (int offset = 0; offset < _pcm.Length; offset += block)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int len = Math.Min(block, _pcm.Length - offset);
                yield return new ReadOnlyMemory<float>(_pcm, offset, len);
                await Task.Yield();
            }
        }
    }
}
