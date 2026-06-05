using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class AudioBeatClockTests
{
    /// <summary>A frame provider a test pumps synthetic frames through.</summary>
    private sealed class FakeFrameProvider : IAudioFrameProvider
    {
        private AudioFrameData _latest = AudioFrameData.Empty;
        public event EventHandler<AudioFrameData>? FrameAvailable;
        public AudioFrameData GetLatestFrame() => _latest;
        public void Emit(AudioFrameData frame)
        {
            _latest = frame;
            FrameAvailable?.Invoke(this, frame);
        }
    }

    private const double EnvelopeRateHz = 100.0;   // frames spaced 10 ms apart
    private const int SpectrumBins = 16;
    private const long HostTicksPerSecond = 1_000_000;

    // Feed `frames` analysis frames; every `beatPeriodFrames` a "loud" frame creates an onset.
    private static void PumpPeriodicOnsets(
        FakeFrameProvider provider, FakeHostClock host, int frames, int beatPeriodFrames)
    {
        var loud = Enumerable.Repeat(1f, SpectrumBins).ToArray();
        var quiet = new float[SpectrumBins];

        for (int k = 0; k < frames; k++)
        {
            double t = k / EnvelopeRateHz;
            host.NowTicks = (long)(t * HostTicksPerSecond);
            float[] spectrum = (k % beatPeriodFrames == 0) ? loud : quiet;
            provider.Emit(new AudioFrameData(
                MonoPcm: Array.Empty<float>(),
                Spectrum: spectrum,
                Waveform: Array.Empty<float>(),
                SampleRate: 48_000,
                FrameIndex: k,
                TimestampSeconds: t));
        }
    }

    [Fact]
    public void DetectsTempoFromPeriodicOnsets()
    {
        var provider = new FakeFrameProvider();
        var host = new FakeHostClock(HostTicksPerSecond);
        using var clock = new AudioBeatClock(
            provider, host, confidenceThreshold: 0.0, analysisWindowSeconds: 8.0,
            estimateIntervalSeconds: 0.3, source: BeatClockSource.System);

        // Onset every 50 frames → 60 * 100 / 50 = 120 BPM.
        PumpPeriodicOnsets(provider, host, frames: 800, beatPeriodFrames: 50);

        Assert.InRange(clock.Current.Bpm, 116.0, 124.0);
        Assert.True(clock.Current.Confidence > 0.0);
        Assert.Equal(BeatClockSource.System, clock.Current.Source);
        Assert.NotEmpty(clock.Current.Candidates);
    }

    [Fact]
    public void DoesNotLockOnSilence()
    {
        var provider = new FakeFrameProvider();
        var host = new FakeHostClock(HostTicksPerSecond);
        using var clock = new AudioBeatClock(provider, host); // default confidence threshold

        // All-silent spectra → flux stays ~0 → no confident tempo.
        for (int k = 0; k < 800; k++)
        {
            double t = k / EnvelopeRateHz;
            host.NowTicks = (long)(t * HostTicksPerSecond);
            provider.Emit(new AudioFrameData(
                Array.Empty<float>(), new float[SpectrumBins], Array.Empty<float>(),
                48_000, k, t));
        }

        Assert.Equal(0.0, clock.Current.Bpm);
        Assert.False(clock.Current.IsLocked);
        Assert.Same(BeatClockState.Idle, clock.Current);
    }

    [Fact]
    public void PublishesBeatCrossingsOnceLocked()
    {
        var provider = new FakeFrameProvider();
        var host = new FakeHostClock(HostTicksPerSecond);
        using var clock = new AudioBeatClock(
            provider, host, confidenceThreshold: 0.0, analysisWindowSeconds: 8.0,
            estimateIntervalSeconds: 0.3);

        var states = new List<BeatClockState>();
        clock.StateChanged += (_, s) => states.Add(s);

        PumpPeriodicOnsets(provider, host, frames: 800, beatPeriodFrames: 50);

        Assert.Contains(states, s => s.IsBeat);
        Assert.True(clock.Current.BeatCount > 0);
        Assert.InRange(clock.Current.BeatPhase, 0.0, 1.0);
    }

    [Fact]
    public void IgnoresEmptyFrames()
    {
        var provider = new FakeFrameProvider();
        var host = new FakeHostClock(HostTicksPerSecond);
        using var clock = new AudioBeatClock(provider, host);

        provider.Emit(AudioFrameData.Empty);

        Assert.Same(BeatClockState.Idle, clock.Current);
    }

    [Fact]
    public void Dispose_UnsubscribesFromFrames()
    {
        var provider = new FakeFrameProvider();
        var host = new FakeHostClock(HostTicksPerSecond);
        var clock = new AudioBeatClock(
            provider, host, confidenceThreshold: 0.0, estimateIntervalSeconds: 0.3,
            analysisWindowSeconds: 8.0);

        clock.Dispose();
        PumpPeriodicOnsets(provider, host, frames: 800, beatPeriodFrames: 50);

        Assert.Equal(0.0, clock.Current.Bpm);
    }
}
