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

    /// <summary>A plugged deck's managed processor, the DSP delegate kept alive for BASS, its original
    /// sample rate (so a pitch/rate change is expressed relative to the track's natural rate), and any
    /// active loop sync handle + start byte (kept so the loop callback can seek back and so a re-arm or
    /// clear can remove the prior sync).</summary>
    private sealed class DeckDsp
    {
        public DeckDsp(BassMixerChannel channel, DSPProcedure procedure, float originalFrequency)
        {
            Channel = channel;
            Procedure = procedure;
            OriginalFrequency = originalFrequency;
        }

        public BassMixerChannel Channel { get; }
        public DSPProcedure Procedure { get; }
        public float OriginalFrequency { get; }

        // Loop state: the registered BASS_SYNC_POS handle, the callback (kept alive for BASS), and the
        // loop in-point in bytes. 0 sync handle = no active loop.
        public int LoopSync { get; set; }
        public SyncProcedure? LoopProcedure { get; set; }
        public long LoopStartBytes { get; set; }
    }

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

        // Remember the deck's natural sample rate so SetDeckRate can express pitch as a multiple of it.
        Bass.ChannelGetAttribute(deckHandle, ChannelAttribute.Frequency, out float originalFrequency);
        _decks[deckHandle] = new DeckDsp(channel, procedure, originalFrequency);
        return channel;
    }

    public double GetDeckPositionFraction(int deckHandle)
    {
        // Mixer source channels report position through BassMix, in the same byte unit as the length.
        long position = BassMix.ChannelGetPosition(deckHandle);
        long length = Bass.ChannelGetLength(deckHandle);
        return position > 0 && length > 0 ? (double)position / length : 0.0;
    }

    public void SetDeckPositionFraction(int deckHandle, double fraction)
    {
        long length = Bass.ChannelGetLength(deckHandle);
        if (length <= 0)
            return;
        long target = (long)(Math.Clamp(fraction, 0.0, 1.0) * length);
        if (!BassMix.ChannelSetPosition(deckHandle, target))
            _logger.LogWarning("Seek deck {Handle} failed: {Error}", deckHandle, Bass.LastError);
    }

    public void SetDeckRate(int deckHandle, double rateMultiplier)
    {
        if (!_decks.TryGetValue(deckHandle, out DeckDsp? deck) || deck.OriginalFrequency <= 0)
            return;
        // Vinyl-style pitch: scale the playback frequency (tempo and pitch move together, like a DJ
        // pitch fader). Tempo-without-pitch would need BASS_FX; this increment keeps it BASS_FX-free.
        double frequency = deck.OriginalFrequency * rateMultiplier;
        if (!Bass.ChannelSetAttribute(deckHandle, ChannelAttribute.Frequency, (float)frequency))
            _logger.LogWarning("Set rate on deck {Handle} failed: {Error}", deckHandle, Bass.LastError);
    }

    public double GetDeckPositionSeconds(int deckHandle)
    {
        long position = BassMix.ChannelGetPosition(deckHandle);
        if (position < 0)
            return 0.0;
        double seconds = Bass.ChannelBytes2Seconds(deckHandle, position);
        return seconds > 0 ? seconds : 0.0;
    }

    public double GetDeckLengthSeconds(int deckHandle)
    {
        long length = Bass.ChannelGetLength(deckHandle);
        if (length <= 0)
            return 0.0;
        double seconds = Bass.ChannelBytes2Seconds(deckHandle, length);
        return seconds > 0 ? seconds : 0.0;
    }

    public void SetDeckLoop(int deckHandle, double startSeconds, double endSeconds)
    {
        if (!_decks.TryGetValue(deckHandle, out DeckDsp? deck))
            return;
        if (endSeconds <= startSeconds)
        {
            _logger.LogWarning(
                "Ignoring loop on deck {Handle}: end {End}s not after start {Start}s.",
                deckHandle, endSeconds, startSeconds);
            return;
        }

        ClearDeckLoopSync(deckHandle, deck); // replace any prior loop on this deck

        long startBytes = Bass.ChannelSeconds2Bytes(deckHandle, Math.Max(0.0, startSeconds));
        long endBytes = Bass.ChannelSeconds2Bytes(deckHandle, endSeconds);
        if (startBytes < 0 || endBytes <= startBytes)
        {
            _logger.LogWarning("Loop region for deck {Handle} resolved to an empty byte span; ignoring.", deckHandle);
            return;
        }

        deck.LoopStartBytes = startBytes;
        // BASS_SYNC_POS at the loop out-point seeks the deck back to the in-point, so the region repeats.
        // SyncFlags.Mixtime fires on the mixer's pull thread for sample-accurate, click-free looping.
        deck.LoopProcedure = (handle, channel, data, user) =>
        {
            if (!BassMix.ChannelSetPosition(deckHandle, deck.LoopStartBytes))
                _logger.LogWarning("Loop wrap seek on deck {Handle} failed: {Error}", deckHandle, Bass.LastError);
        };
        deck.LoopSync = BassMix.ChannelSetSync(
            deckHandle, SyncFlags.Position | SyncFlags.Mixtime, endBytes, deck.LoopProcedure);
        if (deck.LoopSync == 0)
        {
            deck.LoopProcedure = null;
            _logger.LogWarning("Arming loop sync on deck {Handle} failed: {Error}", deckHandle, Bass.LastError);
        }
    }

    public void ClearDeckLoop(int deckHandle)
    {
        if (_decks.TryGetValue(deckHandle, out DeckDsp? deck))
            ClearDeckLoopSync(deckHandle, deck);
    }

    private static void ClearDeckLoopSync(int deckHandle, DeckDsp deck)
    {
        if (deck.LoopSync != 0)
        {
            BassMix.ChannelRemoveSync(deckHandle, deck.LoopSync);
            deck.LoopSync = 0;
            deck.LoopProcedure = null;
            deck.LoopStartBytes = 0;
        }
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
