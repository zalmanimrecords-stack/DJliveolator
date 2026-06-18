using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Liveolator.Core.Platform;

namespace Liveolator.Platform.Audio;

/// <summary>
/// Windows <see cref="ISystemVolumeController"/> over the Core Audio (WASAPI) endpoint-volume API: it
/// resolves the default render device's <c>IAudioEndpointVolume</c> and gets/sets its master scalar level
/// (0..1) — the same value the Windows volume slider moves, affecting the whole machine. COM objects are
/// resolved lazily and reacquired if a call fails (e.g. the default device changed). All failures are
/// caught and reported as "unavailable" rather than thrown, so a volume gesture never crashes the app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemVolumeController : ISystemVolumeController, IDisposable
{
    // CLSCTX_INPROC_SERVER for IMMDevice.Activate.
    private const uint ClsCtxInprocServer = 0x1;

    // EDataFlow.eRender, ERole.eConsole — the device the user hears the mix on.
    private const int DataFlowRender = 0;
    private const int RoleConsole = 0;

    private readonly object _gate = new();
    private readonly Action<string>? _onWarning;
    private IAudioEndpointVolume? _endpointVolume;
    private bool _disposed;

    /// <param name="onWarning">Optional sink for diagnostic messages (wired to the app log at the root).</param>
    public WindowsSystemVolumeController(Action<string>? onWarning = null)
    {
        _onWarning = onWarning;
    }

    /// <summary>True once the endpoint volume interface can be resolved on this machine.</summary>
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
                return !_disposed && TryAcquire() is not null;
        }
    }

    public double GetVolume()
    {
        lock (_gate)
        {
            IAudioEndpointVolume? endpoint = TryAcquire();
            if (endpoint is null)
                return 0.0;
            try
            {
                int hr = endpoint.GetMasterVolumeLevelScalar(out float level);
                if (hr != 0)
                {
                    Warn($"GetMasterVolumeLevelScalar failed (HRESULT 0x{hr:X8}).");
                    return 0.0;
                }
                return Math.Clamp(level, 0f, 1f);
            }
            catch (Exception ex)
            {
                ReleaseAfterFailure(ex, nameof(GetVolume));
                return 0.0;
            }
        }
    }

    public void SetVolume(double level)
    {
        float scalar = (float)Math.Clamp(level, 0.0, 1.0);
        lock (_gate)
        {
            IAudioEndpointVolume? endpoint = TryAcquire();
            if (endpoint is null)
                return;
            try
            {
                Guid noEvent = Guid.Empty; // no event-context GUID: we are not a notification client
                int hr = endpoint.SetMasterVolumeLevelScalar(scalar, ref noEvent);
                if (hr != 0)
                    Warn($"SetMasterVolumeLevelScalar failed (HRESULT 0x{hr:X8}).");
            }
            catch (Exception ex)
            {
                ReleaseAfterFailure(ex, nameof(SetVolume));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ReleaseEndpoint();
        }
    }

    // Lazily resolve (and cache) the default render endpoint's volume interface. Returns null when Core
    // Audio is unavailable (e.g. a headless/RDP session with no audio device). Caller holds _gate.
    private IAudioEndpointVolume? TryAcquire()
    {
        if (_disposed)
            return null;
        if (_endpointVolume is not null)
            return _endpointVolume;

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            Type? enumType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            if (enumType is null)
                return null;
            enumerator = Activator.CreateInstance(enumType) as IMMDeviceEnumerator;
            if (enumerator is null)
                return null;

            int hr = enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleConsole, out device);
            if (hr != 0 || device is null)
            {
                Warn($"GetDefaultAudioEndpoint returned no device (HRESULT 0x{hr:X8}).");
                return null;
            }

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, ClsCtxInprocServer, IntPtr.Zero, out object volumeObject);
            if (hr != 0 || volumeObject is not IAudioEndpointVolume endpoint)
            {
                Warn($"Activating IAudioEndpointVolume failed (HRESULT 0x{hr:X8}).");
                return null;
            }

            _endpointVolume = endpoint;
            return _endpointVolume;
        }
        catch (Exception ex)
        {
            Warn($"Could not resolve the OS volume endpoint: {ex.Message}");
            return null;
        }
        finally
        {
            if (device is not null)
                Marshal.ReleaseComObject(device);
            if (enumerator is not null)
                Marshal.ReleaseComObject(enumerator);
        }
    }

    // A failed get/set usually means the cached endpoint is stale (default device changed/unplugged);
    // drop it so the next call reacquires against the current default device.
    private void ReleaseAfterFailure(Exception ex, string operation)
    {
        Warn($"{operation} failed; reacquiring endpoint. {ex.Message}");
        ReleaseEndpoint();
    }

    private void ReleaseEndpoint()
    {
        if (_endpointVolume is not null)
        {
            Marshal.ReleaseComObject(_endpointVolume);
            _endpointVolume = null;
        }
    }

    private void Warn(string message) => _onWarning?.Invoke($"WindowsSystemVolumeController: {message}");

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(
            int dataFlow, int role,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice? endpoint);

        // Remaining methods (GetDevice / Register / Unregister) are unused; the vtable stops here because
        // we only ever call the two declared above.
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        // OpenPropertyStore / GetId / GetState are unused.
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        // The vtable order is fixed by the COM contract; every method up to the ones we call must be
        // declared (with stack-correct signatures) so the runtime maps the slots correctly.
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        // SetChannel*/GetChannel*/Mute/Step/Range methods below this point are unused.
    }
}
