using Liveolator.Core.Audio;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Mixer;
using Liveolator.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Playback;

/// <summary>
/// The two-deck BASS playback engine (doc 11): each deck is a decoding stream plugged into one BASSmix
/// master channel, and the master mix is exposed as an <see cref="IAudioSource"/> so the beat engine
/// sees the audible post-crossfader signal (doc 02). Implements the slot-addressed
/// <see cref="IMultiDeckPlaybackEngine"/> that <see cref="DeckActionHandler"/> drives, and registers a
/// per-deck <see cref="IBassMixerChannel"/> into <see cref="BassMixer"/> as decks load, so the Core
/// <c>MixerActionHandler</c>'s gain/EQ/filter actually route to BASS_FX.
/// </summary>
/// <remarks>
/// All native BASS calls live behind <see cref="IBassMixerBackend"/>, so this load/play/stop state
/// machine unit-tests with a fake — native BASSmix/BASS_FX is not present in CI (mirrors the
/// <see cref="IBassPlayback"/> pattern). Engine ↔ mixer registration is the seam that was missing:
/// before this, mixer actions reached <see cref="BassMixer"/> but were dropped because no channel was
/// ever registered.
/// </remarks>
public sealed class TwoDeckBassEngine : IMultiDeckPlaybackEngine, IDisposable
{
    /// <summary>Number of addressable deck slots (A = 0, B = 1).</summary>
    public const int Decks = MixerState.DeckCount;

    /// <summary>Pitch fader half-range: normalized position 0/1 maps to ∓8% of the original tempo.</summary>
    private const double PitchRangePercent = 0.08;

    /// <summary>Normalized pitch position with no tempo change (the fader centre).</summary>
    private const double PitchCenter = 0.5;

    /// <summary>Hot-cue slots per deck (a row of pads).</summary>
    private const int HotCuesPerDeck = 8;

    private readonly IBassMixerBackend _backend;
    private readonly BassMixer _mixer;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly MasterAudioSource _master;
    private readonly LoadedDeck?[] _decks = new LoadedDeck?[Decks];

    // Per-slot transport state that persists across track loads (a DJ keeps the pitch fader and the
    // sync/quantize toggles where they were set when swapping tracks). Position is read live from the
    // backend, so it is not stored here.
    private readonly double[] _pitchPosition = new double[Decks];
    private readonly bool[] _syncLocked = new bool[Decks];
    private readonly bool[] _quantize = new bool[Decks];

    // Per-slot analyzed natural tempo (BPM) used as the Sync reference; 0 = unknown. Set when a track
    // with a known BPM loads (doc 11). Cleared when the slot unloads.
    private readonly double[] _baseBpm = new double[Decks];

    // Hot-cue memory per deck: a position fraction per pad, null = unset. Belongs to the loaded track,
    // so it is cleared when the slot unloads.
    private readonly double?[][] _hotCues = new double?[Decks][];
    private bool _disposed;

    /// <summary>A deck currently plugged into the mix: its BASS handle, FX control, and play state.</summary>
    private sealed record LoadedDeck(int Handle, IBassMixerChannel Channel, bool Playing);

    /// <summary>
    /// Public entry point: builds a real BASSmix backend and registers its per-deck channels into
    /// <paramref name="mixer"/>. The App composes one engine and disposes it on shutdown.
    /// </summary>
    /// <param name="mixer">The realtime mixer to register deck channels into (must address both decks).</param>
    /// <param name="sampleRate">Master mix output rate (Hz).</param>
    /// <param name="channels">Master mix channel count (2 = stereo).</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="audioSettings">
    /// The user's persisted output choice (device + buffer, doc 12); null = the system default device
    /// and default buffer. Applied when the backend opens BASS.
    /// </param>
    public TwoDeckBassEngine(
        BassMixer mixer, int sampleRate = 48_000, int channels = 2,
        ILoggerFactory? loggerFactory = null, AudioSettings? audioSettings = null)
        : this(
            new BassMixerBackend(
                sampleRate, channels,
                (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<BassMixerBackend>(),
                audioSettings),
            mixer, loggerFactory)
    {
    }

    /// <summary>
    /// Constructs the engine over a backend seam. Internal so tests inject a fake; the public ctor
    /// above wires the real BASSmix <see cref="BassMixerBackend"/>.
    /// </summary>
    internal TwoDeckBassEngine(IBassMixerBackend backend, BassMixer mixer, ILoggerFactory? loggerFactory = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        if (mixer.DeckCount < Decks)
            throw new ArgumentException(
                $"Mixer addresses {mixer.DeckCount} deck(s); the two-deck engine needs {Decks}.", nameof(mixer));

        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TwoDeckBassEngine>();
        Array.Fill(_pitchPosition, PitchCenter);
        for (int slot = 0; slot < Decks; slot++)
            _hotCues[slot] = new double?[HotCuesPerDeck];

        MasterMixInfo info = _backend.CreateMaster();
        _master = new MasterAudioSource(info.Channels, info.SampleRate);
        // Arm the master tap immediately: the mix runs continuously and the beat clock is fed whenever
        // a deck is playing, with no extra "start the mix" step for the host to coordinate.
        _backend.StartMaster(_master.Emit);
    }

    /// <summary>The post-crossfader master mix; feed this to a <see cref="MasterMixPlaybackEngine"/>.</summary>
    public IAudioSource MasterSource => _master;

    /// <summary>
    /// Re-open the output device / buffer at runtime from the user's settings (doc 12). Returns true if
    /// audio is now running on the requested (or fallback) device; false on failure so the caller (the
    /// <c>AudioReinitCoordinator</c>) can roll back. Decks stay loaded across the re-route.
    /// </summary>
    public bool ReinitializeOutput(AudioSettings settings)
    {
        lock (_gate)
        {
            if (_disposed)
                return false;
            return _backend.ReinitOutput(BassInitOptions.From(settings));
        }
    }

    public int DeckCount => Decks;

    public bool IsPlaying(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _decks[slot]?.Playing ?? false;
    }

    public void Load(int slot, string trackPath)
    {
        ValidateSlot(slot);
        if (string.IsNullOrWhiteSpace(trackPath))
            throw new ArgumentException("trackPath must be a non-empty path.", nameof(trackPath));

        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TwoDeckBassEngine));

            try
            {
                UnloadSlot(slot);
                int handle = _backend.OpenDeckStream(trackPath);
                IBassMixerChannel channel = _backend.PlugDeck(handle);
                _mixer.SetChannel(slot, channel); // route the Core mixer's gain/EQ/filter to this deck
                _decks[slot] = new LoadedDeck(handle, channel, Playing: false);
                // Re-apply the slot's tempo to the new track so swapping decks keeps the setting: the
                // manual pitch fader normally, or the synced rate when Sync is engaged (set once the
                // load action supplies the new track's base BPM via SetDeckBaseBpm).
                if (!_syncLocked[slot])
                    _backend.SetDeckRate(handle, RateFor(_pitchPosition[slot]));
                _logger.LogInformation("Loaded deck slot {Slot} <- {Track}", slot, trackPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load deck slot {Slot} <- {Track}", slot, trackPath);
                throw;
            }
        }
    }

    public void PlayPause(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is not { } deck)
            {
                _logger.LogWarning("PlayPause deck slot {Slot} requested with no track loaded; ignoring.", slot);
                return;
            }
            bool next = !deck.Playing;
            _backend.SetDeckPlaying(deck.Handle, next);
            _decks[slot] = deck with { Playing = next };
        }
    }

    public void Stop(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is { Playing: true } deck)
            {
                _backend.SetDeckPlaying(deck.Handle, false);
                _decks[slot] = deck with { Playing = false };
            }
        }
    }

    public double Position(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
            return _decks[slot] is { } deck ? _backend.GetDeckPositionFraction(deck.Handle) : 0.0;
    }

    public void Seek(int slot, double position, bool relative)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is not { } deck)
                return; // nothing loaded — no playhead to move
            double target = relative
                ? Math.Clamp(_backend.GetDeckPositionFraction(deck.Handle) + position, 0.0, 1.0)
                : Math.Clamp(position, 0.0, 1.0);
            _backend.SetDeckPositionFraction(deck.Handle, target);
        }
    }

    public double PitchPosition(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _pitchPosition[slot];
    }

    public void SetPitch(int slot, double value, bool relative)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            double next = Math.Clamp(relative ? _pitchPosition[slot] + value : value, 0.0, 1.0);
            _pitchPosition[slot] = next;
            // While Sync is engaged the synced rate owns the deck (doc 11: Sync is an assist; manual
            // nudging of a synced deck is a later increment). The position is still stored so it takes
            // effect the moment Sync is released.
            if (_decks[slot] is { } deck && !_syncLocked[slot])
                _backend.SetDeckRate(deck.Handle, RateFor(next));
            // This deck may be the sync leader — pull any synced follower to the new tempo.
            ReapplySyncedFollowers();
        }
    }

    public void Cue(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is not { } deck)
                return;
            // Jump to the cue point (the track start in this increment — settable cue points are a later
            // increment) and pause there, the standard "back to cue" behaviour.
            _backend.SetDeckPositionFraction(deck.Handle, 0.0);
            _backend.SetDeckPlaying(deck.Handle, false);
            _decks[slot] = deck with { Playing = false };
        }
    }

    public double DeckBaseBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _baseBpm[slot];
    }

    public void SetDeckBaseBpm(int slot, double bpm)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            _baseBpm[slot] = bpm > 0.0 ? bpm : 0.0;
            // A new reference tempo re-beatmatches: this deck may be a leader (pull its followers) or a
            // synced follower whose own tempo just changed.
            ReapplySyncedFollowers();
        }
    }

    public bool IsSyncLocked(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _syncLocked[slot];
    }

    public void SetSyncLock(int slot, bool enabled)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            _syncLocked[slot] = enabled;
            if (enabled)
            {
                ReapplyRate(slot); // beatmatch this deck's tempo to the leader now
            }
            else if (_decks[slot] is { } deck)
            {
                // Released: hand the deck back to its manual pitch fader.
                _backend.SetDeckRate(deck.Handle, RateFor(_pitchPosition[slot]));
            }
        }
    }

    // Caller holds _gate. Beatmatch one synced deck to the sync leader: leader = the other deck if it is
    // loaded, not itself sync-locked, and has a known base BPM. With no valid leader (or this deck's own
    // base BPM unknown) the rate is left unchanged — Sync stays armed but silent, never a wrong tempo.
    private void ReapplyRate(int slot)
    {
        if (_decks[slot] is not { } deck)
            return;
        if (_baseBpm[slot] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} sync: own BPM unknown; rate unchanged.", slot);
            return;
        }

        int leader = slot == 0 ? 1 : 0;
        if (_decks[leader] is null || _syncLocked[leader] || _baseBpm[leader] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} sync: no valid leader; rate unchanged.", slot);
            return;
        }

        // The leader's audible tempo is its natural BPM scaled by its own pitch fader.
        double leaderEffectiveBpm = _baseBpm[leader] * RateFor(_pitchPosition[leader]);
        double rate = TempoSyncCalculator.RateFor(leaderEffectiveBpm, _baseBpm[slot]);
        _backend.SetDeckRate(deck.Handle, rate);
    }

    // Caller holds _gate. A leader-tempo change (load / base BPM / pitch) must pull every synced deck.
    private void ReapplySyncedFollowers()
    {
        for (int slot = 0; slot < Decks; slot++)
            if (_syncLocked[slot])
                ReapplyRate(slot);
    }

    public bool IsQuantizeEnabled(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _quantize[slot];
    }

    public void SetQuantize(int slot, bool enabled)
    {
        ValidateSlot(slot);
        lock (_gate) _quantize[slot] = enabled;
        // Quantize-on-beat-grid for cue/loop actions is a later increment; the flag is held + fed back.
        if (enabled)
            _logger.LogInformation("Deck slot {Slot} quantize armed; beat-grid quantize is not yet implemented.", slot);
    }

    public int HotCueCount => HotCuesPerDeck;

    public bool IsHotCueSet(int slot, int cueIndex)
    {
        ValidateSlot(slot);
        if (cueIndex < 0 || cueIndex >= HotCuesPerDeck)
            return false;
        lock (_gate) return _hotCues[slot][cueIndex].HasValue;
    }

    public void HotCue(int slot, int cueIndex)
    {
        ValidateSlot(slot);
        if (cueIndex < 0 || cueIndex >= HotCuesPerDeck)
            throw new ArgumentOutOfRangeException(nameof(cueIndex), cueIndex, "Hot-cue index is out of range.");
        lock (_gate)
        {
            if (_decks[slot] is not { } deck)
                return; // nothing loaded — no position to store or jump to
            if (_hotCues[slot][cueIndex] is { } position)
                _backend.SetDeckPositionFraction(deck.Handle, position); // jump to the stored cue
            else
                _hotCues[slot][cueIndex] = _backend.GetDeckPositionFraction(deck.Handle); // set at current position
        }
    }

    // Maps a normalized pitch position (0..1, 0.5 = centre) to a playback-rate multiplier within ±range.
    private static double RateFor(double normalizedPosition)
        => 1.0 + (Math.Clamp(normalizedPosition, 0.0, 1.0) - PitchCenter) * 2.0 * PitchRangePercent;

    // Caller holds _gate. Unplugs and forgets any deck in the slot, clearing its mixer channel and the
    // track-specific hot-cues (a new track gets fresh cues).
    private void UnloadSlot(int slot)
    {
        if (_decks[slot] is not { } deck)
            return;
        _backend.UnplugDeck(deck.Handle);
        _mixer.SetChannel(slot, null);
        _decks[slot] = null;
        Array.Clear(_hotCues[slot]);
        _baseBpm[slot] = 0.0; // base BPM belongs to the track — the new track supplies its own on load
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            for (int slot = 0; slot < Decks; slot++)
                UnloadSlot(slot);
            _backend.Dispose();
        }
    }

    private static void ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= Decks)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range.");
    }
}
