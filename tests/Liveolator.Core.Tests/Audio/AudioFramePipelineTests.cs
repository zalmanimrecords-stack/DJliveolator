using System;
using System.Collections.Generic;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class AudioFramePipelineTests
{
    private const int FrameSize = 1024;
    private const int Hop = 256;
    private const int Rate = 48_000;

    private static (FakeAudioSource source, AudioFramePipeline pipeline, List<AudioFrameData> frames) Build()
    {
        var source = new FakeAudioSource();
        var pipeline = new AudioFramePipeline(source, new SpectrumAnalyzer(FrameSize), Hop);
        var frames = new List<AudioFrameData>();
        pipeline.FrameAvailable += (_, f) => frames.Add(f);
        return (source, pipeline, frames);
    }

    private static float[] MonoToStereoInterleaved(float[] mono)
    {
        var interleaved = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++)
        {
            interleaved[2 * i] = mono[i];
            interleaved[2 * i + 1] = mono[i];
        }
        return interleaved;
    }

    [Fact]
    public void Ctor_RejectsOutOfRangeHop()
    {
        var source = new FakeAudioSource();
        var analyzer = new SpectrumAnalyzer(FrameSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFramePipeline(source, analyzer, hop: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFramePipeline(source, analyzer, hop: FrameSize + 1));
    }

    [Fact]
    public void NoFramesEmitted_BelowOneFrameWorthOfSamples()
    {
        var (source, _, frames) = Build();

        source.Emit(new float[(FrameSize - 1) * 2], channels: 2, sampleRate: Rate);

        Assert.Empty(frames);
    }

    [Fact]
    public void EmitsOneFrame_ForExactlyOneFrameOfMono()
    {
        var (source, _, frames) = Build();

        source.Emit(new float[FrameSize * 2], channels: 2, sampleRate: Rate);

        Assert.Single(frames);
        Assert.Equal(0, frames[0].FrameIndex);
        Assert.Equal(Rate, frames[0].SampleRate);
        Assert.Equal(FrameSize, frames[0].MonoPcm.Length);
    }

    [Fact]
    public void EmitsOverlappingFrames_AccordingToHop()
    {
        var (source, _, frames) = Build();

        // frameSize + 3*hop mono samples → frames at starts 0, hop, 2*hop, 3*hop = 4 frames.
        int monoCount = FrameSize + 3 * Hop;
        source.Emit(new float[monoCount * 2], channels: 2, sampleRate: Rate);

        Assert.Equal(4, frames.Count);
        for (int i = 0; i < frames.Count; i++)
            Assert.Equal(i, frames[i].FrameIndex);
    }

    [Fact]
    public void FrameIndexAndTimestamp_AreContinuousAcrossBatches()
    {
        var (source, _, frames) = Build();

        // Two separate batches; framing must carry over the buffer boundary.
        source.Emit(new float[FrameSize * 2], channels: 2, sampleRate: Rate); // 1 frame (index 0)
        source.Emit(new float[Hop * 2], channels: 2, sampleRate: Rate);       // 1 more (index 1)

        Assert.Equal(2, frames.Count);
        Assert.Equal(0, frames[0].FrameIndex);
        Assert.Equal(1, frames[1].FrameIndex);
        Assert.Equal(0.0, frames[0].TimestampSeconds, precision: 9);
        Assert.Equal((double)Hop / Rate, frames[1].TimestampSeconds, precision: 9);
    }

    [Fact]
    public void StereoIsDownmixedToMono_ByAveraging()
    {
        var (source, _, frames) = Build();

        // Left = 1.0, Right = 0.0 → mono should be 0.5 for every sample.
        var interleaved = new float[FrameSize * 2];
        for (int i = 0; i < FrameSize; i++)
        {
            interleaved[2 * i] = 1.0f;
            interleaved[2 * i + 1] = 0.0f;
        }
        source.Emit(interleaved, channels: 2, sampleRate: Rate);

        Assert.Single(frames);
        Assert.All(frames[0].MonoPcm, s => Assert.Equal(0.5f, s, precision: 6));
    }

    [Fact]
    public void MonoSourceIsPassedThrough()
    {
        var (source, _, frames) = Build();

        var mono = new float[FrameSize];
        Array.Fill(mono, 0.25f);
        source.Emit(mono, channels: 1, sampleRate: Rate);

        Assert.Single(frames);
        Assert.All(frames[0].MonoPcm, s => Assert.Equal(0.25f, s, precision: 6));
    }

    [Fact]
    public void GetLatestFrame_IsEmptyBeforeAnyAudio_ThenTracksLast()
    {
        var (source, pipeline, _) = Build();

        Assert.Equal(-1, pipeline.GetLatestFrame().FrameIndex);

        source.Emit(new float[(FrameSize + Hop) * 2], channels: 2, sampleRate: Rate);

        Assert.Equal(1, pipeline.GetLatestFrame().FrameIndex);
    }

    [Theory]
    [InlineData(0, 2)]   // empty buffer
    [InlineData(8, 0)]   // zero channels
    public void MalformedBatch_DoesNotThrowAndEmitsNothing(int sampleCount, int channels)
    {
        var (source, _, frames) = Build();

        source.Emit(new float[sampleCount], channels, sampleRate: Rate);

        Assert.Empty(frames);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSource()
    {
        var (source, pipeline, frames) = Build();

        pipeline.Dispose();
        source.Emit(new float[FrameSize * 2], channels: 2, sampleRate: Rate);

        Assert.Empty(frames);
    }

    // ---- Fixed analysis-rate (resampling) path ----

    private const int AnalysisRate = 44_100;

    private static (FakeAudioSource source, AudioFramePipeline pipeline, List<AudioFrameData> frames)
        BuildResampling(int analysisFrameSize)
    {
        var source = new FakeAudioSource();
        var pipeline = new AudioFramePipeline(
            source, new SpectrumAnalyzer(analysisFrameSize), hop: analysisFrameSize / 4,
            analysisSampleRate: AnalysisRate);
        var frames = new List<AudioFrameData>();
        pipeline.FrameAvailable += (_, f) => frames.Add(f);
        return (source, pipeline, frames);
    }

    private static float[] SineMono(int count, double frequencyHz, int sampleRate)
    {
        var mono = new float[count];
        for (int i = 0; i < count; i++)
            mono[i] = (float)Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate);
        return mono;
    }

    [Fact]
    public void AnalysisRate_IsEmittedOnEveryFrame_NotTheSourceRate()
    {
        var (source, _, frames) = BuildResampling(analysisFrameSize: 1024);

        // Feed plenty of 96 kHz audio so the resampled buffer yields at least one frame.
        source.Emit(SineMono(96_000, frequencyHz: 1_000, sampleRate: 96_000), channels: 1, sampleRate: 96_000);

        Assert.NotEmpty(frames);
        Assert.All(frames, f => Assert.Equal(AnalysisRate, f.SampleRate));
    }

    [Fact]
    public void Timestamps_AreInResampledTime_AndContinuous()
    {
        var (source, _, frames) = BuildResampling(analysisFrameSize: 1024);
        int hop = 1024 / 4;

        source.Emit(SineMono(96_000, frequencyHz: 1_000, sampleRate: 96_000), channels: 1, sampleRate: 96_000);

        Assert.True(frames.Count >= 2);
        Assert.Equal(0.0, frames[0].TimestampSeconds, precision: 9);
        // Frame N starts at N*hop analysis samples → N*hop/AnalysisRate seconds.
        for (int i = 0; i < frames.Count; i++)
        {
            Assert.Equal(i, frames[i].FrameIndex);
            Assert.Equal((double)(i * hop) / AnalysisRate, frames[i].TimestampSeconds, precision: 9);
        }
    }

    [Theory]
    [InlineData(48_000)]
    [InlineData(96_000)]
    public void SpectrumPeak_MatchesTheReferenceRate_AcrossSourceRates(int sourceRate)
    {
        const int analysisFrameSize = 2048;
        const double toneHz = 1_000.0;
        const int seconds = 1;

        int ReferencePeak()
        {
            var (src, _, frames) = BuildResampling(analysisFrameSize);
            src.Emit(SineMono(AnalysisRate * seconds, toneHz, AnalysisRate), channels: 1, sampleRate: AnalysisRate);
            return DominantBin(frames[0].Spectrum);
        }

        var (source, _, capturedFrames) = BuildResampling(analysisFrameSize);
        source.Emit(SineMono(sourceRate * seconds, toneHz, sourceRate), channels: 1, sampleRate: sourceRate);

        int referenceBin = ReferencePeak();
        int sourceBin = DominantBin(capturedFrames[0].Spectrum);

        // After resampling to the common analysis rate the dominant bin must line up (±1 bin).
        Assert.InRange(sourceBin, referenceBin - 1, referenceBin + 1);

        // Sanity: the bin maps back to ~1 kHz at the analysis rate.
        double binHz = (double)sourceBin * AnalysisRate / analysisFrameSize;
        Assert.InRange(binHz, toneHz - 30, toneHz + 30);
    }

    private static int DominantBin(float[] spectrum)
    {
        int peak = 1; // skip DC
        for (int i = 2; i < spectrum.Length; i++)
            if (spectrum[i] > spectrum[peak]) peak = i;
        return peak;
    }
}
