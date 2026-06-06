using System;
using System.Collections.Generic;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

/// <summary>
/// Verifies the capture-source controller (doc 01 / doc 12): selecting a device opens a source via the
/// factory and routes it into the live switch; selecting null detaches; a failed open keeps the prior
/// source running; and a previous source is stopped/disposed when a new one is selected.
/// </summary>
public sealed class CaptureSourceControllerTests
{
    private sealed class FakeSource : IAudioSource
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public string Name => "fake";
        public bool IsRunning => Started && !Stopped;
#pragma warning disable CS0067 // the live switch subscribes in production; the fake never raises it
        public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;
#pragma warning restore CS0067
        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory : IAudioCaptureSourceFactory
    {
        public List<AudioCaptureDevice> Requested { get; } = new();
        public FakeSource? Next { get; set; } = new();
        public bool Throw { get; set; }

        public IAudioSource CreateCaptureSource(AudioCaptureDevice device)
        {
            Requested.Add(device);
            if (Throw)
                throw new InvalidOperationException("cannot open device");
            return Next ??= new FakeSource();
        }
    }

    private static AudioCaptureDevice Device(string id, CaptureSourceKind kind = CaptureSourceKind.LineInput)
        => new(id, $"Device {id}", kind, IsDefault: false);

    [Fact]
    public void Select_OpensStartsAndRoutesSource()
    {
        var factory = new FakeFactory();
        var liveInput = new SwitchableAudioSource();
        var controller = new CaptureSourceController(factory, liveInput);

        bool ok = controller.SelectCaptureSource(Device("2"));

        Assert.True(ok);
        Assert.True(factory.Next!.Started);
        Assert.Equal("fake", liveInput.Name); // the switch now forwards the capture source
    }

    [Fact]
    public void Select_Null_DetachesCurrentSource()
    {
        var first = new FakeSource();
        var factory = new FakeFactory { Next = first };
        var liveInput = new SwitchableAudioSource();
        var controller = new CaptureSourceController(factory, liveInput);
        controller.SelectCaptureSource(Device("1"));

        bool ok = controller.SelectCaptureSource(null);

        Assert.True(ok);
        Assert.True(first.Stopped);
        Assert.True(first.Disposed);
        Assert.Equal("(none)", liveInput.Name); // switch detached
    }

    [Fact]
    public void Select_NewDevice_DisposesPreviousSource()
    {
        var first = new FakeSource();
        var second = new FakeSource();
        var factory = new FakeFactory { Next = first };
        var liveInput = new SwitchableAudioSource();
        var controller = new CaptureSourceController(factory, liveInput);
        controller.SelectCaptureSource(Device("1"));

        factory.Next = second;
        controller.SelectCaptureSource(Device("2"));

        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
    }

    [Fact]
    public void Select_OpenFails_KeepsPreviousSourceAndReturnsFalse()
    {
        var first = new FakeSource();
        var factory = new FakeFactory { Next = first };
        var liveInput = new SwitchableAudioSource();
        var controller = new CaptureSourceController(factory, liveInput);
        controller.SelectCaptureSource(Device("1"));

        factory.Throw = true;
        bool ok = controller.SelectCaptureSource(Device("2"));

        Assert.False(ok);
        Assert.False(first.Disposed);       // prior source untouched
        Assert.Equal("fake", liveInput.Name); // still routed to the original
    }
}
