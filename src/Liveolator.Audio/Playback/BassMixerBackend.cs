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
/// <remarks>
/// Headphone cue (PFL): when the user has chosen a separate cue output device, the backend opens a
/// second BASS device for it (the CMD STUDIO 2A's channels 3/4) and exposes <see cref="ICueOutput"/>
/// so <see cref="BassMixer"/> can push the Core-computed cue/master output gains. The summed cued-deck
/// (pre-fade) signal feeding the headphones comes from per-deck cue sends, which need the deck
/// plumbing (<see cref="PlugDeck"/>) the deck-loops branch owns — see the integration note. Until
/// those land the cue output carries the master leg only; the gains for both legs are stored here.
/// </remarks>
internal sealed class BassMixerBackend : IBassMixerBackend, ICueOutput
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly ILogger _logger;
    private readonly Dictionary<int, DeckDsp> _decks = new();
    private readonly int _cueDeviceIndex;

    private int _mixer;
    private int _cueMixer;                  // 0 until a cue output device is opened
    private int _cueMasterPush;             // push stream feeding the master leg into the cue mixer
    private DSPProcedure? _masterDsp;       // kept alive: BASS holds an unmanaged pointer to it
    private Action<float[]>? _masterTap;
    private volatile float _masterLegGain;  // master-into-headphones gain (equal-power, level-scaled)
    private bool _disposed;

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

        BassInitOptions options = BassInitOptions.From(audioSettings);
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

    // BASS update thread: copy the mixed master to the analysis tap (read-only, never written back)
    // and, when a cue output is open, push the master leg (scaled by the cue/master blend) into the
    // headphone-cue mixer so the DJ can monitor the master through the cue/master knob.
    private void OnMasterDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0 || buffer == IntPtr.Zero)
            return;
        int count = length / sizeof(float);
        var managed = new float[count];
        Marshal.Copy(buffer, managed, 0, count);

        _masterTap?.Invoke(managed);
        FeedCueMasterLeg(managed, length);
    }

    // Push the master samples, scaled by the master-leg gain, into the cue mixer's push source. A zero
    // gain still pushes silence so the cue mixer's clock keeps advancing.
    private void FeedCueMasterLeg(float[] master, int byteLength)
    {
        if (_cueMasterPush == 0)
            return;

        float gain = _masterLegGain;
        var leg = new float[master.Length];
        if (gain != 0f)
        {
            for (int i = 0; i < master.Length; i++)
                leg[i] = master[i] * gain;
        }

        if (Bass.StreamPutData(_cueMasterPush, leg, byteLength) == -1)
            _logger.LogDebug("Cue master-leg push failed: {Error}", Bass.LastError);
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
