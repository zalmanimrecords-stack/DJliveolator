using System;
using System.Collections.Generic;
using Liveolator.Audio.Playback;
using Liveolator.Audio.Recording;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Audio.Tests.Recording;

public sealed class BassMasterRecorderTests
{
    private static BassMasterRecorder NewRecorder(FakeMasterSource source, out List<RecordingSinkSpy> sinks)
    {
        var created = new List<RecordingSinkSpy>();
        sinks = created;
        return new BassMasterRecorder(
            source,
            source.Channels,
            source.SampleRate,
            (path, channels, rate) =>
            {
                var sink = new RecordingSinkSpy(path, channels, rate);
                created.Add(sink);
                return sink;
            },
            NullLoggerFactory.Instance);
    }

    [Fact]
    public void IsAvailable_True_WhenMasterSourcePresent()
    {
        var recorder = NewRecorder(new FakeMasterSource(2, 48_000), out _);
        Assert.True(recorder.IsAvailable);
    }

    [Fact]
    public void Start_OpensSink_WithMasterChannelFormat_AndMarksRecording()
    {
        var source = new FakeMasterSource(2, 44_100);
        var recorder = NewRecorder(source, out List<RecordingSinkSpy> sinks);

        bool started = recorder.Start("C:/out/set.wav");

        Assert.True(started);
        Assert.True(recorder.IsRecording);
        RecordingSinkSpy sink = Assert.Single(sinks);
        Assert.Equal("C:/out/set.wav", sink.Path);
        Assert.Equal(2, sink.Channels);
        Assert.Equal(44_100, sink.SampleRate);
    }

    [Fact]
    public void MasterSamples_AreWrittenToTheSink_WhileRecording()
    {
        var source = new FakeMasterSource(2, 48_000);
        var recorder = NewRecorder(source, out List<RecordingSinkSpy> sinks);

        recorder.Start("p.wav");
        source.Emit(new float[] { 0.1f, -0.1f, 0.2f, -0.2f });

        Assert.Equal(new float[] { 0.1f, -0.1f, 0.2f, -0.2f }, sinks[0].Written);
    }

    [Fact]
    public void Stop_DisposesSink_AndStopsRecording()
    {
        var source = new FakeMasterSource(2, 48_000);
        var recorder = NewRecorder(source, out List<RecordingSinkSpy> sinks);

        recorder.Start("p.wav");
        recorder.Stop();

        Assert.False(recorder.IsRecording);
        Assert.True(sinks[0].Disposed);
    }

    [Fact]
    public void AfterStop_NoMoreSamplesAreWritten()
    {
        var source = new FakeMasterSource(2, 48_000);
        var recorder = NewRecorder(source, out List<RecordingSinkSpy> sinks);

        recorder.Start("p.wav");
        recorder.Stop();
        source.Emit(new float[] { 0.5f, 0.5f });

        Assert.Empty(sinks[0].Written);
    }

    [Fact]
    public void Start_WhenAlreadyRecording_ReturnsFalse_AndDoesNotOpenSecondSink()
    {
        var source = new FakeMasterSource(2, 48_000);
        var recorder = NewRecorder(source, out List<RecordingSinkSpy> sinks);

        recorder.Start("a.wav");
        bool second = recorder.Start("b.wav");

        Assert.False(second);
        Assert.Single(sinks);
    }

    [Fact]
    public void Stop_WhenNotRecording_IsNoOp()
    {
        var recorder = NewRecorder(new FakeMasterSource(2, 48_000), out _);
        recorder.Stop(); // must not throw
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void SinkWriteThrows_StopsRecording_AndDoesNotRethrow_ToPlayback()
    {
        var source = new FakeMasterSource(2, 48_000);
        var created = new List<RecordingSinkSpy>();
        var recorder = new BassMasterRecorder(
            source,
            source.Channels,
            source.SampleRate,
            (path, ch, rate) =>
            {
                var sink = new RecordingSinkSpy(path, ch, rate) { ThrowOnWrite = true };
                created.Add(sink);
                return sink;
            },
            NullLoggerFactory.Instance);

        recorder.Start("p.wav");
        // The master tap fires this on the audio thread; a disk failure must not bubble out.
        source.Emit(new float[] { 0.1f, 0.1f });

        Assert.False(recorder.IsRecording);
        Assert.True(created[0].Disposed);
    }

    [Fact]
    public void StartFailsToOpenSink_ReturnsFalse_StaysStopped()
    {
        var source = new FakeMasterSource(2, 48_000);
        var recorder = new BassMasterRecorder(
            source,
            source.Channels,
            source.SampleRate,
            (_, _, _) => throw new IOException("disk full"),
            NullLoggerFactory.Instance);

        bool started = recorder.Start("p.wav");

        Assert.False(started);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void Dispose_StopsAnActiveRecording()
    {
        var source = new FakeMasterSource(2, 48_000);
        var recorder = NewRecorder(source, out List<RecordingSinkSpy> sinks);

        recorder.Start("p.wav");
        recorder.Dispose();

        Assert.True(sinks[0].Disposed);
        // After dispose the tap must be detached: emitting writes nothing.
        source.Emit(new float[] { 0.9f, 0.9f });
        Assert.Empty(sinks[0].Written);
    }

    /// <summary>A minimal master <see cref="IAudioSource"/> stand-in that emits interleaved samples on demand.</summary>
    private sealed class FakeMasterSource : IAudioSource
    {
        public FakeMasterSource(int channels, int rate)
        {
            Channels = channels;
            SampleRate = rate;
        }

        public int Channels { get; }
        public int SampleRate { get; }
        public string Name => "Master Mix (fake)";
        public bool IsRunning { get; private set; }
        public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;

        public void Emit(float[] interleaved)
            => SamplesAvailable?.Invoke(this, new AudioSamplesAvailable(interleaved, Channels, SampleRate));

        public void Dispose() { }
    }

    private sealed class RecordingSinkSpy : IMasterRecordingSink
    {
        public RecordingSinkSpy(string path, int channels, int sampleRate)
        {
            Path = path;
            Channels = channels;
            SampleRate = sampleRate;
        }

        public string Path { get; }
        public int Channels { get; }
        public int SampleRate { get; }
        public bool Disposed { get; private set; }
        public bool ThrowOnWrite { get; set; }
        public List<float> Written { get; } = new();

        public void Write(ReadOnlySpan<float> interleaved)
        {
            if (ThrowOnWrite)
                throw new IOException("disk write failed");
            foreach (float s in interleaved)
                Written.Add(s);
        }

        public void Dispose() => Disposed = true;
    }
}
