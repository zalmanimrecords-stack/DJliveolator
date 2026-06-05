using System;
using Liveolator.Audio.Capture;
using Liveolator.Audio.Playback;
using Liveolator.Core.Audio;

namespace Liveolator.Audio.Tests.Capture;

/// <summary>Test double for the BASS capture interop: records calls and lets a test push samples.</summary>
internal sealed class FakeCaptureBackend : ICaptureBackend
{
    private Action<float[]>? _tap;

    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public bool Disposed { get; private set; }
    public AudioCaptureDevice? LastDevice { get; private set; }

    public BassChannelInfo Info { get; set; } = new(Channels: 2, SampleRate: 48_000);
    public Func<AudioCaptureDevice, BassChannelInfo>? StartOverride { get; set; }

    public BassChannelInfo Start(AudioCaptureDevice device, Action<float[]> onInterleavedSamples)
    {
        StartCalls++;
        LastDevice = device;
        _tap = onInterleavedSamples ?? throw new ArgumentNullException(nameof(onInterleavedSamples));
        return StartOverride?.Invoke(device) ?? Info;
    }

    public void Stop()
    {
        StopCalls++;
        _tap = null;
    }

    public void Dispose() => Disposed = true;

    /// <summary>Simulate BASS delivering captured samples to the armed tap.</summary>
    public void EmitSamples(float[] interleaved) => _tap?.Invoke(interleaved);

    /// <summary>True while a capture session is open (between Start and Stop).</summary>
    public bool IsCapturing => _tap is not null;
}
