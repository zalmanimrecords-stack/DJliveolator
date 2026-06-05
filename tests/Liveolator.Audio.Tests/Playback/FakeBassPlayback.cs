using Liveolator.Audio.Playback;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>Test double for the BASS interop: records calls and lets a test push tapped samples.</summary>
internal sealed class FakeBassPlayback : IBassPlayback
{
    private Action<float[]>? _tap;

    public int CreateStreamCalls { get; private set; }
    public int PlayCalls { get; private set; }
    public int PauseCalls { get; private set; }
    public int FreeCalls { get; private set; }
    public int LastFreedHandle { get; private set; }
    public bool Disposed { get; private set; }

    public int HandleToReturn { get; set; } = 42;
    public BassChannelInfo Info { get; set; } = new(Channels: 2, SampleRate: 48_000);
    public Func<string, int>? CreateStreamOverride { get; set; }

    public int CreateFileStream(string filePath)
    {
        CreateStreamCalls++;
        return CreateStreamOverride?.Invoke(filePath) ?? HandleToReturn;
    }

    public BassChannelInfo GetChannelInfo(int handle) => Info;

    public void SetSampleTap(int handle, Action<float[]> onInterleavedSamples) => _tap = onInterleavedSamples;

    public void Play(int handle) => PlayCalls++;
    public void Pause(int handle) => PauseCalls++;

    public void Free(int handle)
    {
        FreeCalls++;
        LastFreedHandle = handle;
    }

    public void Dispose() => Disposed = true;

    /// <summary>Simulate BASS delivering played samples to the armed tap.</summary>
    public void EmitSamples(float[] interleaved) => _tap?.Invoke(interleaved);
}
