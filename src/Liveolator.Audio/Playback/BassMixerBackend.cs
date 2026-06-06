using System.Runtime.InteropServices;
using Liveolator.Core.Dsp;
using Liveolator.Core.Settings;
using Liveolator.Core.Audio.Effects;
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
/// <remarks>
/// Headphone cue (PFL): when the user has chosen a separate cue output device, the backend opens a
/// second BASS device for it (the CMD STUDIO 2A's channels 3/4) and exposes <see cref="ICueOutput"/>
/// so <see cref="BassMixer"/> can push the Core-computed cue/master output gains. The summed cued-deck
/// (pre-fade) signal feeding the headphones comes from per-deck cue sends, which need the deck
/// plumbing (<see cref="PlugDeck"/>) the deck-loops branch owns — see the integration note. Until
/// those land the cue output carries the master leg only; the gains for both legs are stored here.
/// The master fed to both the analysis tap and the cue leg is the post-limiter signal (see
/// <see cref="OnMasterDsp"/>).
/// </remarks>
internal sealed class BassMixerBackend : IBassMixerBackend, ICueOutput
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly ILogger _logger;
    private readonly IAudioEffectRackProvider? _effectRacks;
    private readonly Dictionary<int, DeckDsp> _decks = new();
    private readonly int _cueDeviceIndex;

    private int _mixer;
    private int _cueMixer;                  // 0 until a cue output device is opened
    private int _cueMasterPush;             // push stream feeding the master leg into the cue mixer
    private DSPProcedure? _masterDsp;       // kept alive: BASS holds an unmanaged pointer to it
    private Action<float[]>? _masterTap;
    private volatile float _masterLegGain;  // master-into-headphones gain (equal-power, level-scaled)
    private bool _disposed;

    // ── master-limiter region (Gap #5) ──────────────────────────────────────────────────────────
    // The post-crossfader brick-wall limiter so two summed decks never hard-clip the master (doc 11).
    // Pure Core DSP, applied in place inside OnMasterDsp on the BASS update thread.
    private readonly MasterLimiter _masterLimiter;

    // RT-thread allocation fix (doc 01: "no allocation on the audio thread"): the DSP callbacks used to
    // `new float[]` every buffer. These reusable scratch buffers are pre-sized from the BASS playback
    // buffer length and reused in place; they only ever grow (logged) if BASS hands a larger block than
    // the worst case we sized for, which should not happen in steady state. _cueLegScratch holds the
    // scaled master leg pushed to the cue mixer so that path is allocation-free too.
    private float[] _channelScratch = Array.Empty<float>();
    private float[] _masterScratch = Array.Empty<float>();
    private float[] _cueLegScratch = Array.Empty<float>();
    // ── end master-limiter region ────────────────────────────────────────────────────────────────

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
        int sampleRate = 48_000, int channels = 2, ILogger? logger = null,
        AudioSettings? audioSettings = null, IAudioEffectRackProvider? effectRacks = null)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        _sampleRate = sampleRate;
        _channels = channels;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _effectRacks = effectRacks;

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
        _cueLegScratch = new float[worstCaseSamples];
        // ── end master-limiter region ──

        InitOutput(options);
        _cueDeviceIndex = TryInitCueDevice(options);
    }

    // Open the user-chosen headphone-cue output device (doc 11) so a second BASS device backs the cue
    // mixer. A missing/stale cue device must not disable all audio — log and run without cue.
    private int TryInitCueDevice(BassInitOptions options)
    {
        if (!options.HasCueDevice || options.CueDeviceIndex == options.DeviceIndex)
            return BassInitOptions.NoCueDevice; // no separate cue output, or it is the master device

        if (TryInitDevice(options.CueDeviceIndex))
            return options.CueDeviceIndex;

        _logger.LogWarning(
            "Headphone-cue output device {Device} unavailable ({Error}); cue output disabled.",
            options.CueDeviceIndex, Bass.LastError);
        return BassInitOptions.NoCueDevice;
    }

    /// <summary>True when a separate headphone-cue output device was successfully opened.</summary>
    public bool HasCueOutput => _cueDeviceIndex >= 1;

    // Applies the user's persisted output choice (doc 12) before opening BASS: the playback buffer is a
    // global config BASS reads at device-open time, so set it first, then open the chosen device. A
    // stale saved device (since unplugged) must not disable all audio — fall back to the system default.
    private void InitOutput(BassInitOptions options)
    {
        // The playback buffer must exceed BASS's automatic update period or it starves between refills
        // and the whole device drags (a 40 ms buffer left with the default 100 ms period plays at ~0.4×).
        // Apply a matching low update period so a short DJ-latency buffer stays continuously filled.
        Bass.Configure(Configuration.UpdatePeriod, options.UpdatePeriodMilliseconds);
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

    public bool ReinitOutput(BassInitOptions options)
    {
        if (_disposed)
            return false;

        // Buffer length is a global BASS config; set it before opening the device so the new device
        // picks it up. (An already-running device keeps its buffer until re-opened — acceptable here.)
        // The playback buffer must exceed BASS's automatic update period or it starves between refills
        // and the whole device drags (a 40 ms buffer left with the default 100 ms period plays at ~0.4×).
        // Apply a matching low update period so a short DJ-latency buffer stays continuously filled.
        Bass.Configure(Configuration.UpdatePeriod, options.UpdatePeriodMilliseconds);
        Bass.PlaybackBufferLength = options.BufferMilliseconds;

        if (!TryInitDevice(options.DeviceIndex))
        {
            _logger.LogWarning("BASS re-init of device {Device} failed: {Error}", options.DeviceIndex, Bass.LastError);
            return false;
        }

        // Re-route the live mixer (and thus every plugged deck) to the freshly opened device, then make
        // it the current device so subsequent calls target it. Channels keep playing across the move.
        if (_mixer != 0 && !Bass.ChannelSetDevice(_mixer, options.DeviceIndex))
        {
            _logger.LogWarning("Routing the master mix to device {Device} failed: {Error}",
                options.DeviceIndex, Bass.LastError);
            return false;
        }

        Bass.CurrentDevice = options.DeviceIndex;
        return true;
    }

    public MasterMixInfo CreateMaster()
    {
        // A mixer stream that does not auto-stop when all sources pause/end (decks come and go).
        _mixer = BassMix.CreateMixerStream(_sampleRate, _channels, BassFlags.Float | BassFlags.MixerNonStop);
        if (_mixer == 0)
            throw new BassPlaybackException($"CreateMixerStream failed: {Bass.LastError}");

        CreateCueMixerIfConfigured();
        return new MasterMixInfo(_channels, _sampleRate);
    }

    // The headphone-cue output is its own mixer stream on the cue device. The cued-deck (PFL) sends
    // that should feed it depend on per-deck plumbing the deck-loops branch owns (see integration
    // note); for now it carries the master leg, tapped from the master mixer and scaled by the
    // master-into-headphones gain so the cue/master blend knob already works for monitoring.
    private void CreateCueMixerIfConfigured()
    {
        if (!HasCueOutput)
            return;

        // CreateMixerStream binds to the current BASS device, so select the cue device before creating
        // the cue mixer, then restore the master device for the rest of the master setup.
        int masterDevice = Bass.CurrentDevice;
        Bass.CurrentDevice = _cueDeviceIndex;
        _cueMixer = BassMix.CreateMixerStream(_sampleRate, _channels, BassFlags.Float | BassFlags.MixerNonStop);
        if (_cueMixer == 0)
        {
            _logger.LogWarning("Cue mixer creation failed: {Error}; cue output disabled.", Bass.LastError);
            Bass.CurrentDevice = masterDevice;
            return;
        }

        // A push (STREAMPROC_PUSH) source plugged into the cue mixer carries the master leg: the
        // master DSP pushes scaled master samples into it each buffer. Decode flag — the cue mixer
        // pulls it, it never plays to the device itself.
        _cueMasterPush = Bass.CreateStream(
            _sampleRate, _channels, BassFlags.Float | BassFlags.Decode, StreamProcedureType.Push);
        if (_cueMasterPush == 0 || !BassMix.MixerAddChannel(_cueMixer, _cueMasterPush, BassFlags.Default))
        {
            _logger.LogWarning("Cue master-leg push stream setup failed: {Error}; cue output disabled.", Bass.LastError);
            Bass.StreamFree(_cueMixer);
            _cueMixer = 0;
            _cueMasterPush = 0;
            Bass.CurrentDevice = masterDevice;
            return;
        }

        if (!Bass.ChannelPlay(_cueMixer))
            _logger.LogWarning("Bass.ChannelPlay (cue) failed: {Error}", Bass.LastError);

        Bass.CurrentDevice = masterDevice;
    }

    public void SetCueOutputGains(double cueGain, double masterGain)
    {
        // Only the master leg is fed for now (see the class remarks / integration note): the cued-deck
        // leg gain (cueGain) is applied where the per-deck cue sends sum, which the deck plumbing owns.
        _masterLegGain = (float)Math.Clamp(masterGain, 0.0, 1.0);
        if (cueGain > 0f && _cueMixer != 0)
        {
            _logger.LogDebug(
                "Cue-deck leg gain {Cue} requested but per-deck cue sends are not yet wired; master leg only.",
                cueGain);
        }
    }

    public int OpenDeckStream(string filePath)
    {
        // Decoding stream: the mixer pulls and sums it; it never plays to the device on its own.
        int handle = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (handle == 0)
            throw new BassPlaybackException($"CreateStream('{filePath}') failed: {Bass.LastError}");
        return handle;
    }

    public IBassMixerChannel PlugDeck(int deckHandle, int slot)
    {
        var channel = new BassMixerChannel(_channels, _effectRacks?.GetRack(slot));
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
    // never hard-clip the master, write the limited master back to the device, then hand the limited
    // master to (a) the analysis tap and (b) the headphone-cue master leg. Allocation-free on the heavy
    // path: reuses _masterScratch instead of `new float[]` per buffer (doc 01 RT-thread rule).
    private void OnMasterDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0 || buffer == IntPtr.Zero)
            return;
        int count = length / sizeof(float);
        EnsureMasterScratch(count);

        // Read the mix into the reused scratch (no per-buffer alloc), run the optional master effect
        // rack (VST3) first, then the brick-wall limiter LAST so the limiter guarantees the final output
        // ceiling regardless of what the effects do (doc 11 + Gap #5), then write it back to the device.
        Marshal.Copy(buffer, _masterScratch, 0, count);
        _effectRacks?.GetRack(AudioEffectRackSlot.Master).Process(_masterScratch.AsSpan(0, count), _channels);
        _masterLimiter.Process(_masterScratch.AsSpan(0, count));
        Marshal.Copy(_masterScratch, 0, buffer, count);

        // Feed the (now limited) master to the analysis tap. This hands a buffer across the
        // Action<float[]> → ReadOnlyMemory<float> seam (frame pipeline), which may retain it
        // asynchronously, so the scratch cannot be reused here — allocate as the original code did.
        // The heavy DSP (channel filters + master limiter) is allocation-free; only this ownership
        // hand-off allocates, and only when a tap is attached.
        if (_masterTap is { } tap)
        {
            var tapBuffer = new float[count];
            Array.Copy(_masterScratch, tapBuffer, count);
            tap(tapBuffer);
        }

        // Push the (limited) master leg into the headphone-cue mixer so the DJ monitors the same signal
        // the house hears, blended by the cue/master knob.
        FeedCueMasterLeg(count, length);
    }

    // Push the (already limited) master samples, scaled by the master-leg gain, into the cue mixer's
    // push source. A zero gain still pushes silence so the cue mixer's clock keeps advancing.
    // Allocation-free: reuses _cueLegScratch (doc 01 RT-thread rule), consistent with the limiter fix.
    private void FeedCueMasterLeg(int count, int byteLength)
    {
        if (_cueMasterPush == 0)
            return;

        EnsureCueLegScratch(count);
        float gain = _masterLegGain;
        if (gain == 0f)
            Array.Clear(_cueLegScratch, 0, count);
        else
            for (int i = 0; i < count; i++)
                _cueLegScratch[i] = _masterScratch[i] * gain;

        if (Bass.StreamPutData(_cueMasterPush, _cueLegScratch, byteLength) == -1)
            _logger.LogDebug("Cue master-leg push failed: {Error}", Bass.LastError);
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

    private void EnsureCueLegScratch(int count)
    {
        if (_cueLegScratch.Length >= count)
            return;
        _logger.LogWarning(
            "Cue-leg buffer ({Count}) exceeded pre-allocated scratch ({Capacity}); growing on the audio thread.",
            count, _cueLegScratch.Length);
        _cueLegScratch = new float[count];
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
        if (_cueMixer != 0)
            Bass.StreamFree(_cueMixer); // frees the plugged push source with it
        if (_mixer != 0)
            Bass.StreamFree(_mixer);
        Bass.Free();
    }
}
