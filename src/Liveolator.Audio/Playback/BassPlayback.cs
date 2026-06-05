using System.Runtime.InteropServices;
using ManagedBass;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Real BASS interop (<see cref="IBassPlayback"/>): initialises the default output device once and
/// drives float-format file streams. Native bass library must be present at runtime; this type is
/// never exercised by unit tests (they use a fake) — the realtime smoke check lives separately.
/// </summary>
internal sealed class BassPlayback : IBassPlayback
{
    private readonly ILogger _logger;
    private DSPProcedure? _dspProcedure; // kept alive: BASS holds an unmanaged pointer to it
    private Action<float[]>? _onSamples;
    private bool _disposed;

    public BassPlayback(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!Bass.Init() && Bass.LastError != Errors.Already)
            throw new BassPlaybackException($"Bass.Init failed: {Bass.LastError}");
    }

    public int CreateFileStream(string filePath)
    {
        int handle = Bass.CreateStream(filePath, 0, 0, BassFlags.Float);
        if (handle == 0)
            throw new BassPlaybackException($"CreateStream('{filePath}') failed: {Bass.LastError}");
        return handle;
    }

    public BassChannelInfo GetChannelInfo(int handle)
    {
        if (!Bass.ChannelGetInfo(handle, out ChannelInfo info))
            throw new BassPlaybackException($"ChannelGetInfo failed: {Bass.LastError}");
        return new BassChannelInfo(info.Channels, info.Frequency);
    }

    public void SetSampleTap(int handle, Action<float[]> onInterleavedSamples)
    {
        _onSamples = onInterleavedSamples ?? throw new ArgumentNullException(nameof(onInterleavedSamples));
        _dspProcedure = OnDsp;
        if (Bass.ChannelSetDSP(handle, _dspProcedure) == 0)
            throw new BassPlaybackException($"ChannelSetDSP failed: {Bass.LastError}");
    }

    // BASS update thread. Buffer holds 32-bit float samples (BassFlags.Float); Length is in bytes.
    private void OnDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0 || buffer == IntPtr.Zero || _onSamples is null)
            return;

        int count = length / sizeof(float);
        var managed = new float[count];
        Marshal.Copy(buffer, managed, 0, count);
        _onSamples(managed);
    }

    public void Play(int handle)
    {
        if (!Bass.ChannelPlay(handle))
            _logger.LogWarning("Bass.ChannelPlay failed: {Error}", Bass.LastError);
    }

    public void Pause(int handle)
    {
        if (!Bass.ChannelPause(handle) && Bass.LastError != Errors.NotPlaying)
            _logger.LogWarning("Bass.ChannelPause failed: {Error}", Bass.LastError);
    }

    public void Free(int handle)
    {
        if (handle != 0)
            Bass.StreamFree(handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Bass.Free();
    }
}
