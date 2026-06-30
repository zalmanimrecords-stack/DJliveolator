using System.Runtime.CompilerServices;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class TrackAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_NoStructureAnalyzer_StructureIsNull()
    {
        var decoder = new FakeAudioDecoder(TestSignals.ClickTrain(120, 44100, seconds: 10));
        TrackAnalysisResult result = await new TrackAnalyzer().AnalyzeAsync(decoder, "song.wav");
        Assert.Null(result.Structure);
    }

    [Fact]
    public async Task AnalyzeAsync_WithStructureAnalyzer_AttachesStructure()
    {
        var decoder = new FakeAudioDecoder(TestSignals.ClickTrain(120, 44100, seconds: 10));
        var structure = new SongStructure(new[] { new SongSection(0.0, SongSectionLabel.Intro) }, "fake 1.0");
        var analyzer = new TrackAnalyzer(structureAnalyzer: new FakeStructureAnalyzer(structure));

        TrackAnalysisResult result = await analyzer.AnalyzeAsync(decoder, "song.wav");

        Assert.Same(structure, result.Structure);
    }

    [Fact]
    public async Task AnalyzeAsync_StructureAnalyzerReturnsNull_StructureIsNull()
    {
        var decoder = new FakeAudioDecoder(TestSignals.ClickTrain(120, 44100, seconds: 10));
        var analyzer = new TrackAnalyzer(structureAnalyzer: new FakeStructureAnalyzer(null));

        TrackAnalysisResult result = await analyzer.AnalyzeAsync(decoder, "song.wav");

        Assert.Null(result.Structure);
    }

    [Fact]
    public async Task AnalyzeAsync_StructureAnalyzerThrows_CoreAnalysisStillSucceeds()
    {
        var decoder = new FakeAudioDecoder(TestSignals.ClickTrain(120, 44100, seconds: 10));
        var analyzer = new TrackAnalyzer(structureAnalyzer: new ThrowingStructureAnalyzer());

        TrackAnalysisResult result = await analyzer.AnalyzeAsync(decoder, "song.wav");

        Assert.Null(result.Structure);
        Assert.InRange(result.Bpm.Bpm, 117.0, 123.0);
    }

    private sealed class FakeStructureAnalyzer : ISongStructureAnalyzer
    {
        private readonly SongStructure? _result;
        public FakeStructureAnalyzer(SongStructure? result) => _result = result;
        public Task<SongStructure?> AnalyzeAsync(IAudioDecoder decoder, string filePath, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class ThrowingStructureAnalyzer : ISongStructureAnalyzer
    {
        public Task<SongStructure?> AnalyzeAsync(IAudioDecoder decoder, string filePath, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

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
