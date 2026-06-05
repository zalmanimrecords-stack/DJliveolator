using System;
using System.Collections.Generic;
using Liveolator.Audio.Capture;
using Liveolator.Audio.Playback;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Audio.Tests.Capture;

public class CaptureAudioSourceTests
{
    private static readonly AudioCaptureDevice Loopback =
        new("0", "Speakers (loopback)", CaptureSourceKind.SystemLoopback, IsDefault: true);

    private static readonly AudioCaptureDevice LineIn =
        new("3", "Line In (USB Codec)", CaptureSourceKind.LineInput, IsDefault: false);

    private static CaptureAudioSource NewSource(FakeCaptureBackend backend, AudioCaptureDevice? device = null)
        => new(backend, device ?? Loopback, NullLogger<CaptureAudioSource>.Instance);

    [Fact]
    public void Name_IsDeviceName()
    {
        var source = NewSource(new FakeCaptureBackend(), LineIn);
        Assert.Equal("Line In (USB Codec)", source.Name);
    }

    [Fact]
    public void Start_OpensCaptureForTheSelectedDeviceAndRuns()
    {
        var backend = new FakeCaptureBackend();
        var source = NewSource(backend, LineIn);

        source.Start();

        Assert.True(source.IsRunning);
        Assert.Equal(1, backend.StartCalls);
        Assert.Same(LineIn, backend.LastDevice);
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        var backend = new FakeCaptureBackend();
        var source = NewSource(backend);

        source.Start();
        source.Start();

        Assert.Equal(1, backend.StartCalls);
    }

    [Fact]
    public void Stop_StopsCaptureAndIsIdempotent()
    {
        var backend = new FakeCaptureBackend();
        var source = NewSource(backend);

        source.Start();
        source.Stop();
        source.Stop();

        Assert.False(source.IsRunning);
        Assert.Equal(1, backend.StopCalls);
    }

    [Fact]
    public void Restart_OpensAFreshCaptureSession()
    {
        // Unlike a deck (reusable stream), restarting capture re-opens — so a hot-plugged or
        // re-selected device is picked up. Two Starts => two backend opens.
        var backend = new FakeCaptureBackend();
        var source = NewSource(backend);

        source.Start();
        source.Stop();
        source.Start();

        Assert.True(source.IsRunning);
        Assert.Equal(2, backend.StartCalls);
        Assert.Equal(1, backend.StopCalls);
    }

    [Fact]
    public void CapturedSamples_AreForwardedWithDeviceChannelFormat()
    {
        var backend = new FakeCaptureBackend { Info = new BassChannelInfo(2, 44_100) };
        var source = NewSource(backend);
        var received = new List<AudioSamplesAvailable>();
        source.SamplesAvailable += (_, e) => received.Add(e);

        source.Start();
        var buffer = new float[] { 0.3f, -0.3f, 0.4f, -0.4f };
        backend.EmitSamples(buffer);

        Assert.Single(received);
        Assert.Equal(2, received[0].Channels);
        Assert.Equal(44_100, received[0].SampleRate);
        Assert.True(buffer.AsSpan().SequenceEqual(received[0].Interleaved.Span));
    }

    [Fact]
    public void EmptyCapture_RaisesNothing()
    {
        var backend = new FakeCaptureBackend();
        var source = NewSource(backend);
        var received = new List<AudioSamplesAvailable>();
        source.SamplesAvailable += (_, e) => received.Add(e);

        source.Start();
        backend.EmitSamples(Array.Empty<float>());

        Assert.Empty(received);
    }

    [Fact]
    public void Dispose_DisposesBackendAndStopsRunning()
    {
        var backend = new FakeCaptureBackend();
        var source = NewSource(backend);

        source.Start();
        source.Dispose();

        Assert.True(backend.Disposed);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void Start_AfterDispose_Throws()
    {
        var source = NewSource(new FakeCaptureBackend());
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.Start());
    }

    [Fact]
    public void Start_WhenBackendFails_LogsAndRethrows_StaysStopped()
    {
        var backend = new FakeCaptureBackend
        {
            StartOverride = _ => throw new BassCaptureException("device busy")
        };
        var source = NewSource(backend);

        Assert.Throws<BassCaptureException>(() => source.Start());
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void Stop_WhenBackendThrows_Swallows_AndMarksStopped()
    {
        var backend = new ThrowingStopBackend();
        var source = new CaptureAudioSource(backend, Loopback, NullLogger<CaptureAudioSource>.Instance);

        source.Start();
        source.Stop(); // must not throw — tearing down a source should be safe

        Assert.False(source.IsRunning);
    }

    private sealed class ThrowingStopBackend : ICaptureBackend
    {
        public BassChannelInfo Start(AudioCaptureDevice device, Action<float[]> onInterleavedSamples)
            => new(2, 48_000);
        public void Stop() => throw new BassCaptureException("stop failed");
        public void Dispose() { }
    }
}
