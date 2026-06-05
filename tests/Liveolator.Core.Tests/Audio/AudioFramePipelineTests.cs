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
}
