using System.Runtime.InteropServices;
using Liveolator.Core.Dsp;
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

    // ── master-limiter region (Gap #5) ──────────────────────────────────────────────────────────
    // The post-crossfader brick-wall limiter so two summed decks never hard-clip the master (doc 11).
    // Pure Core DSP, applied in place inside OnMasterDsp on the BASS update thread.
    private readonly MasterLimiter _masterLimiter;

    // RT-thread allocation fix (doc 01: "no allocation on the audio thread"): the DSP callbacks used to
    // `new float[]` every buffer. These reusable scratch buffers are pre-sized from the BASS playback
    // buffer length and reused in place; they only ever grow (logged) if BASS hands a larger block than
    // the worst case we sized for, which should not happen in steady state.
    private float[] _channelScratch = Array.Empty<float>();
    private float[] _masterScratch = Array.Empty<float>();
    // ── end master-limiter region ────────────────────────────────────────────────────────────────

    /// <summary>A plugged deck's managed processor, the DSP delegate kept alive for BASS, and its
    /// original sample rate (so a pitch/rate change is expressed relative to the track's natural rate).</summary>
    private sealed record DeckDsp(BassMixerChannel Channel, DSPProcedure Procedure, float OriginalFrequency);

    public BassMixerBackend(
        int sampleRate = 48_000, int channels = 2, ILogger? logger = null, AudioSettings? audioSettings = null)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        _sampleRate = sampleRate;
        _channels = channels;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        // ── master-limiter region (Gap #5) ──
        _masterLimiter = new MasterLimiter(sampleRate, channels);

        var options = BassInitOptions.From(audioSettings);

        // Pre-size the RT scratch buffers to the worst-case buffer BASS will hand us: the playback
        // buffer length (ms) of interleaved float samples, with headroom, so the DSP callbacks never
        // allocate. Done before InitOutput sets the global buffer length.
        int worstCaseSamples = (int)Math.Ceiling(options.BufferMilliseconds * 0.001 * sampleRate) * channels;
        worstCaseSamples = Math.Max(worstCaseSamples, sampleRate * channels / 4); // floor: ~250 ms safety
        _channelScratch = new float[worstCaseSamples];
        _masterScratch = new float[worstCaseSamples];
        // ── end master-limiter region ──

        InitOutput(options);
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
    // RT-thread allocation fix (Gap #5, doc 01): reuse the pre-allocated _channelScratch instead of
    // `new float[]` every buffer. EnsureCapacity only grows on an unexpected oversized block (logged).
    private void ApplyChannelDsp(BassMixerChannel channel, IntPtr buffer, int length)
    {
        if (length <= 0 || buffer == IntPtr.Zero)
            return;
        int count = length / sizeof(float);
        EnsureChannelScratch(count);
        Marshal.Copy(buffer, _channelScratch, 0, count);
        channel.Process(_channelScratch.AsSpan(0, count), _channels);
        Marshal.Copy(_channelScratch, 0, buffer, count);
    }

    // BASS update thread: apply the master brick-wall limiter in place (Gap #5) so two summed decks
    // never hard-clip the master, then copy the limited master to the analysis tap. Allocation-free:
    // reuses _masterScratch instead of `new float[]` per buffer (doc 01 RT-thread rule).
    private void OnMasterDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0 || buffer == IntPtr.Zero)
            return;
        int count = length / sizeof(float);
        EnsureMasterScratch(count);

        // Limit the master bus in place: read the mix into the reused scratch (no per-buffer alloc),
        // run the brick-wall limiter, write it back to the device so the master never hard-clips.
        Marshal.Copy(buffer, _masterScratch, 0, count);
        _masterLimiter.Process(_masterScratch.AsSpan(0, count));
        Marshal.Copy(_masterScratch, 0, buffer, count);

        // Feed the (now limited) master to the analysis tap. This hands a buffer across the
        // Action<float[]> → ReadOnlyMemory<float> seam (frame pipeline), which may retain it
        // asynchronously, so the scratch cannot be reused here — allocate as the original code did.
        // The heavy DSP (channel filters + master limiter) is now allocation-free; only this ownership
        // hand-off allocates, and only when a tap is attached.
        if (_masterTap is { } tap)
        {
            var tapBuffer = new float[count];
            Array.Copy(_masterScratch, tapBuffer, count);
            tap(tapBuffer);
        }
    }

    // Grow-only capacity guards. Growth is off the steady-state path (worst-case sized up front) and is
    // logged because reallocating on the audio thread is exactly what doc 01 forbids — seeing this warn
    // means the up-front sizing was too small and must be raised.
    private void EnsureChannelScratch(int count)
    {
        if (_channelScratch.Length >= count)
            return;
        _logger.LogWarning(
            "Channel DSP buffer ({Count}) exceeded pre-allocated scratch ({Capacity}); growing on the audio thread.",
            count, _channelScratch.Length);
        _channelScratch = new float[count];
    }

    private void EnsureMasterScratch(int count)
    {
        if (_masterScratch.Length >= count)
            return;
        _logger.LogWarning(
            "Master DSP buffer ({Count}) exceeded pre-allocated scratch ({Capacity}); growing on the audio thread.",
            count, _masterScratch.Length);
        _masterScratch = new float[count];
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
