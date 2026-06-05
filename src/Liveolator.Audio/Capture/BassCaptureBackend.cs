using ManagedBass;
using Liveolator.Audio.Playback;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Capture;

/// <summary>
/// Real BASS capture interop (<see cref="ICaptureBackend"/>): opens a BASS <em>record</em> device
/// and streams its float samples. The native bass library must be present at runtime; this type is
/// never exercised by unit tests (they use a fake) — mirrors <c>BassPlayback</c>.
/// </summary>
/// <remarks>
/// <para><b>Windows:</b> system-loopback is captured through the WASAPI loopback record endpoint,
/// which BASS surfaces as a normal record device (its name typically contains "loopback"); a
/// line-input is a regular record device. Both are opened the same way here — the device
/// <see cref="AudioCaptureDevice.Id"/> is the BASS record-device index.</para>
/// <para><b>macOS:</b> CoreAudio has no native output-loopback device, so <see cref="CaptureSourceKind.SystemLoopback"/>
/// requires a virtual loopback device (e.g. BlackHole) the user installs and selects as a line-input;
/// line-input capture itself works through the same BASS record path. If a future build needs the
/// dedicated BASSWASAPI add-on for tighter loopback, swap the implementation behind this seam — Core
/// and the source state machine do not change.</para>
/// </remarks>
internal sealed class BassCaptureBackend : ICaptureBackend
{
    private readonly ILogger _logger;
    private RecordProcedure? _recordProcedure; // kept alive: BASS holds an unmanaged pointer to it
    private Action<float[]>? _onSamples;
    private int _channels;
    private int _recordDevice = -1;
    private int _handle;
    private bool _disposed;

    public BassCaptureBackend(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public BassChannelInfo Start(AudioCaptureDevice device, Action<float[]> onInterleavedSamples)
    {
        ArgumentNullException.ThrowIfNull(device);
        _onSamples = onInterleavedSamples ?? throw new ArgumentNullException(nameof(onInterleavedSamples));

        int deviceIndex = ResolveDeviceIndex(device);

        if (!Bass.RecordInit(deviceIndex) && Bass.LastError != Errors.Already)
            throw new BassCaptureException($"RecordInit(device {deviceIndex}) failed: {Bass.LastError}");
        _recordDevice = deviceIndex;

        // freq=0/chans=0 => device default; Float for direct float samples (matches the deck path).
        _recordProcedure = OnRecord;
        _handle = Bass.RecordStart(0, 0, BassFlags.RecordPause | BassFlags.Float, _recordProcedure);
        if (_handle == 0)
            throw new BassCaptureException($"RecordStart('{device.Name}') failed: {Bass.LastError}");

        if (!Bass.ChannelGetInfo(_handle, out ChannelInfo info))
            throw new BassCaptureException($"ChannelGetInfo failed: {Bass.LastError}");
        _channels = info.Channels;

        if (!Bass.ChannelPlay(_handle)) // un-pause the paused recording
            throw new BassCaptureException($"ChannelPlay (start record) failed: {Bass.LastError}");

        return new BassChannelInfo(info.Channels, info.Frequency);
    }

    public void Stop()
    {
        if (_handle != 0)
        {
            Bass.ChannelStop(_handle);
            _handle = 0;
        }
    }

    private int ResolveDeviceIndex(AudioCaptureDevice device)
    {
        if (int.TryParse(device.Id, out int index))
            return index;
        _logger.LogWarning("Capture device id '{Id}' is not a record-device index; using default.", device.Id);
        return -1; // BASS default record device
    }

    // BASS record thread. Buffer holds 32-bit float samples (BassFlags.Float); length is in bytes.
    private bool OnRecord(int handle, IntPtr buffer, int length, IntPtr user)
    {
        if (length > 0 && buffer != IntPtr.Zero && _onSamples is not null)
        {
            int count = length / sizeof(float);
            var managed = new float[count];
            System.Runtime.InteropServices.Marshal.Copy(buffer, managed, 0, count);
            _onSamples(managed);
        }
        return true; // continue recording
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        if (_recordDevice >= 0)
        {
            Bass.CurrentRecordingDevice = _recordDevice;
            Bass.RecordFree();
            _recordDevice = -1;
        }
    }
}
