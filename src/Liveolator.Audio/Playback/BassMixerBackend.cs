using System.Runtime.InteropServices;
using Liveolator.Core.Settings;
using ManagedBass;
using ManagedBass.Mix;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Real BASSmix interop (<see cref="IBassMixerBackend"/>): one mixer master channel fed by decoding
/// deck streams, each deck filtered in place by a managed <see cref="BassMixerChannel"/> DSP (gain +
/// EQ + filter from the Core coefficients), and the master output tapped for the frame pipeline. Native
/// bass + bassmix libraries must be present at runtime; this type is never exercised by unit tests —
/// the state machine and the channel DSP are tested with fakes, and real audio is verified manually
/// (doc 11 checklist), mirroring <see cref="BassPlayback"/>.
/// </summary>
internal sealed class BassMixerBackend : IBassMixerBackend
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly ILogger _logger;
    private readonly Dictionary<int, DeckDsp> _decks = new();

    private int _mixer;
    private DSPProcedure? _masterDsp;       // kept alive: BASS holds an unmanaged pointer to it
    private Action<float[]>? _masterTap;
    private bool _disposed;

    /// <summary>A plugged deck's managed processor and the DSP delegate kept alive for BASS.</summary>
    private sealed record DeckDsp(BassMixerChannel Channel, DSPProcedure Procedure);

    public BassMixerBackend(
        int sampleRate = 48_000, int channels = 2, ILogger? logger = null, AudioSettings? audioSettings = null)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        _sampleRate = sampleRate;
        _channels = channels;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        InitOutput(BassInitOptions.From(audioSettings));
    }

    // Applies the user's persisted output choice (doc 12) before opening BASS: the playback buffer is a
    // global config BASS reads at device-open time, so set it first, then open the chosen device. A
    // stale saved device (since unplugged) must not disable all audio — fall back to the system default.
    private void InitOutput(BassInitOptions options)
    {
        Bass.PlaybackBufferLength = options.BufferMilliseconds;

        if (TryInitDevice(options.DeviceIndex))
            return;

        Errors firstError = Bass.LastError;
        if (options.DeviceIndex == BassInitOptions.DefaultDevice || !TryInitDevice(BassInitOptions.DefaultDevice))
            throw new BassPlaybackException($"Bass.Init failed: {firstError}");

        _logger.LogWarning(
            "BASS output device {Device} unavailable ({Error}); using the system default instead.",
            options.DeviceIndex, firstError);
    }

    // BASS treats a re-init of an already-open device as success for our purposes (Errors.Already).
    private static bool TryInitDevice(int device) => Bass.Init(device) || Bass.LastError == Errors.Already;

    public MasterMixInfo CreateMaster()
    {
        // A mixer stream that does not auto-stop when all sources pause/end (decks come and go).
        _mixer = BassMix.CreateMixerStream(_sampleRate, _channels, BassFlags.Float | BassFlags.MixerNonStop);
        if (_mixer == 0)
            throw new BassPlaybackException($"CreateMixerStream failed: {Bass.LastError}");
        return new MasterMixInfo(_channels, _sampleRate);
    }

    public int OpenDeckStream(string filePath)
    {
        // Decoding stream: the mixer pulls and sums it; it never plays to the device on its own.
        int handle = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (handle == 0)
            throw new BassPlaybackException($"CreateStream('{filePath}') failed: {Bass.LastError}");
        return handle;
    }

    public IBassMixerChannel PlugDeck(int deckHandle)
    {
        var channel = new BassMixerChannel(_channels);
        // Add paused: the engine flips play state explicitly so a freshly loaded deck is silent.
        if (!BassMix.MixerAddChannel(_mixer, deckHandle, BassFlags.MixerChanPause))
            throw new BassPlaybackException($"MixerAddChannel failed: {Bass.LastError}");

        // Per-deck DSP applies gain/EQ/filter to the deck's samples before the mixer sums them.
        DSPProcedure procedure = (handle, chan, buffer, length, user) => ApplyChannelDsp(channel, buffer, length);
        if (Bass.ChannelSetDSP(deckHandle, procedure) == 0)
        {
            BassMix.MixerRemoveChannel(deckHandle);
            throw new BassPlaybackException($"ChannelSetDSP (deck) failed: {Bass.LastError}");
        }

        _decks[deckHandle] = new DeckDsp(channel, procedure);
        return channel;
    }

    public void SetDeckPlaying(int deckHandle, bool playing)
    {
        BassFlags value = playing ? BassFlags.Default : BassFlags.MixerChanPause;
        BassMix.ChannelFlags(deckHandle, value, BassFlags.MixerChanPause);
    }

    public void UnplugDeck(int deckHandle)
    {
        _decks.Remove(deckHandle);
        BassMix.MixerRemoveChannel(deckHandle);
        Bass.StreamFree(deckHandle);
    }

    public void StartMaster(Action<float[]> onMasterSamples)
    {
        _masterTap = onMasterSamples ?? throw new ArgumentNullException(nameof(onMasterSamples));
        _masterDsp = OnMasterDsp;
        if (Bass.ChannelSetDSP(_mixer, _masterDsp) == 0)
            throw new BassPlaybackException($"ChannelSetDSP (master) failed: {Bass.LastError}");
        if (!Bass.ChannelPlay(_mixer))
            _logger.LogWarning("Bass.ChannelPlay (master) failed: {Error}", Bass.LastError);
    }

    // BASS update thread: filter the deck buffer in place via the managed channel processor.
    private void ApplyChannelDsp(BassMixerChannel channel, IntPtr buffer, int length)
    {
        if (length <= 0 || buffer == IntPtr.Zero)
            return;
        int count = length / sizeof(float);
        var managed = new float[count];
        Marshal.Copy(buffer, managed, 0, count);
        channel.Process(managed, _channels);
        Marshal.Copy(managed, 0, buffer, count);
    }

    // BASS update thread: copy the mixed master to the analysis tap (read-only — never written back).
    private void OnMasterDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0 || buffer == IntPtr.Zero || _masterTap is null)
            return;
        int count = length / sizeof(float);
        var managed = new float[count];
        Marshal.Copy(buffer, managed, 0, count);
        _masterTap(managed);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (int deckHandle in new List<int>(_decks.Keys))
        {
            BassMix.MixerRemoveChannel(deckHandle);
            Bass.StreamFree(deckHandle);
        }
        _decks.Clear();
        if (_mixer != 0)
            Bass.StreamFree(_mixer);
        Bass.Free();
    }
}
