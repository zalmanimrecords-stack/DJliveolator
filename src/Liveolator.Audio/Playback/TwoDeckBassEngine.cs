using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Audio;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Audio.Effects;
using Liveolator.Core.Mixer;
using Liveolator.Core.Persistence;
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
public sealed class TwoDeckBassEngine : IMultiDeckPlaybackEngine, ISyncCorrectionDriver, IDisposable
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

    // Persistent hot-cue store (doc 11/13, A3): null = cues stay RAM-only (the prior behaviour). When
    // present, a track's saved cue set is loaded on Load and re-saved on set/clear, keyed by file path.
    private readonly IHotCueStore? _hotCueStore;

    // The sample rate the persisted cue offsets are mapped against — the master mix rate. Cue positions
    // are stored as fractions here but persisted as samples, so the store record is self-describing.
    private readonly int _sampleRate;

    // Per-slot file path of the loaded track; the cue-store key. null = nothing loaded.
    private readonly string?[] _loadedPath = new string?[Decks];

    // Per-slot temporary (primary) cue position as a 0..1 fraction; null = unset, so the Cue button
    // returns to the track start (the prior behaviour). Belongs to the track — cleared on unload (A5).
    private readonly double?[] _tempCue = new double?[Decks];

    // Per-slot transport state that persists across track loads (a DJ keeps the pitch fader and the
    // sync/quantize toggles where they were set when swapping tracks). Position is read live from the
    // backend, so it is not stored here.
    private readonly double[] _pitchPosition = new double[Decks];
    private readonly double[] _playbackRate = new double[Decks];
    private readonly bool[] _syncLocked = new bool[Decks];
    private readonly bool[] _quantize = new bool[Decks];

    // Per-slot beat-lock state for the SYNC indicator (Off/Active/Locked/Drifting), driven by the
    // continuous correction loop and reset to Off when sync is released.
    private readonly SyncLockState[] _syncState = new SyncLockState[Decks];

    // Phase-lock loop tunables (gains/thresholds/output latency). Injected so the composition root can
    // pass the user's output latency; defaults to the professional preset.
    private readonly PhaseLockSettings _phaseLock;

    // Per-slot analyzed natural tempo (BPM) used as the Sync reference; 0 = unknown. Set when a track
    // with a known BPM loads (doc 11). Cleared when the slot unloads.
    private readonly double[] _baseBpm = new double[Decks];

    // Per-slot first-beat (downbeat) anchor in seconds; 0 = unknown. Fed from the track's analyzed
    // BpmResult on load (like base BPM) and used by Quantize phase-match. Cleared when the slot unloads.
    private readonly double[] _firstBeat = new double[Decks];

    // Per-slot active loop length in beats; 0 = no loop. The loop region (seconds) is derived from this
    // and the base BPM so it stays a musical length. Belongs to the track, cleared when the slot unloads.
    private readonly double[] _loopBeats = new double[Decks];

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
        ILoggerFactory? loggerFactory = null, AudioSettings? audioSettings = null,
        IAudioEffectRackProvider? effectRacks = null,
        IHotCueStore? hotCueStore = null,
        PhaseLockSettings? phaseLock = null)
        : this(
            new BassMixerBackend(
                sampleRate, channels,
                (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<BassMixerBackend>(),
                audioSettings,
                effectRacks),
            mixer, loggerFactory, hotCueStore, phaseLock)
    {
    }

    /// <summary>
    /// Constructs the engine over a backend seam. Internal so tests inject a fake; the public ctor
    /// above wires the real BASSmix <see cref="BassMixerBackend"/>.
    /// </summary>
    internal TwoDeckBassEngine(
        IBassMixerBackend backend, BassMixer mixer, ILoggerFactory? loggerFactory = null,
        IHotCueStore? hotCueStore = null, PhaseLockSettings? phaseLock = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        _hotCueStore = hotCueStore;
        _phaseLock = phaseLock ?? PhaseLockSettings.Default;
        if (mixer.DeckCount < Decks)
            throw new ArgumentException(
                $"Mixer addresses {mixer.DeckCount} deck(s); the two-deck engine needs {Decks}.", nameof(mixer));

        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TwoDeckBassEngine>();
        Array.Fill(_pitchPosition, PitchCenter);
        Array.Fill(_playbackRate, 1.0);
        for (int slot = 0; slot < Decks; slot++)
            _hotCues[slot] = new double?[HotCuesPerDeck];

        // The real BASSmix backend is also the headphone-cue output; route the Core mixer's cue/master
        // output gains to it so the cue-mix knob reaches the second output. A fake backend (tests) or a
        // future backend without cue simply skips this — the mixer then logs and drops cue-gain pushes.
        if (_backend is ICueOutput cueOutput)
            _mixer.SetCueOutput(cueOutput);

        MasterMixInfo info = _backend.CreateMaster();
        _sampleRate = info.SampleRate;
        _master = new MasterAudioSource(info.Channels, info.SampleRate);
        // Arm the master tap immediately: the mix runs continuously and the beat clock is fed whenever
        // a deck is playing, with no extra "start the mix" step for the host to coordinate.
        _backend.StartMaster(_master.Emit);
    }

    /// <inheritdoc />
    public event EventHandler<int>? DeckEnded;

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
                // Open the new stream BEFORE unloading the current track, so a failed open (missing /
                // corrupt / unreadable file — e.g. a stale live-queue or restored entry) leaves the deck's
                // existing track loaded and playable rather than wiping it. A bad track must never empty a
                // good deck (global standards #16/#26).
                int handle = _backend.OpenDeckStream(trackPath);
                UnloadSlot(slot);
                IBassMixerChannel channel = _backend.PlugDeck(handle, slot);
                _mixer.SetChannel(slot, channel); // route the Core mixer's gain/EQ/filter to this deck
                _decks[slot] = new LoadedDeck(handle, channel, Playing: false);
                _loadedPath[slot] = trackPath; // the cue-store key for this slot
                // Re-apply the slot's tempo to the new track so swapping decks keeps the setting: the
                // manual pitch fader normally, or the synced rate when Sync is engaged (set once the
                // load action supplies the new track's base BPM via SetDeckBaseBpm).
                if (!_syncLocked[slot])
                    _backend.SetDeckRate(handle, _playbackRate[slot]);
                // Restore the track's persisted hot cues (A3). Tolerant: a missing/unreadable store
                // leaves the slot with the fresh (empty) cue bank UnloadSlot cleared — never a throw.
                LoadPersistedHotCues(slot, handle, trackPath);
                // Arm end-of-track handling (A4): when this stream runs out, mark the slot stopped and
                // raise DeckEnded so the live queue can auto-advance (or stop when dry).
                _backend.SetDeckEndCallback(handle, () => OnDeckEnded(slot, handle));
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

    public void Jog(int slot, double deltaSeconds)
    {
        ValidateSlot(slot);
        if (!double.IsFinite(deltaSeconds))
            return;

        lock (_gate)
        {
            if (_decks[slot] is not { } deck)
                return;

            double lengthSeconds = _backend.GetDeckLengthSeconds(deck.Handle);
            if (lengthSeconds <= 0.0)
                return;

            double targetSeconds = Math.Clamp(
                _backend.GetDeckPositionSeconds(deck.Handle) + deltaSeconds,
                0.0,
                lengthSeconds);
            _backend.SetDeckPositionFraction(deck.Handle, targetSeconds / lengthSeconds);
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
            _playbackRate[slot] = RateFor(next);
            // While Sync is engaged the synced rate owns the deck (doc 11: Sync is an assist; manual
            // nudging of a synced deck is a later increment). The position is still stored so it takes
            // effect the moment Sync is released.
            if (_decks[slot] is { } deck && !_syncLocked[slot])
                _backend.SetDeckRate(deck.Handle, _playbackRate[slot]);
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

            // CDJ back-to-cue (A5): the pure resolver decides set-vs-return from the deck's transport
            // state, live position, and stored temp cue. Set drops a fresh cue here; return jumps to the
            // stored cue (or track start when none is set) and pauses.
            double current = _backend.GetDeckPositionFraction(deck.Handle);
            CueButtonAction action = CueButtonResolver.Resolve(deck.Playing, current, _tempCue[slot]);
            if (action == CueButtonAction.SetCueHere)
            {
                _tempCue[slot] = current;
                _backend.SetDeckPlaying(deck.Handle, false);
                _decks[slot] = deck with { Playing = false };
                _logger.LogInformation("Deck slot {Slot} cue: set temp cue at {Pos:F4}.", slot, current);
                return;
            }

            double target = _tempCue[slot] ?? 0.0; // return to the stored cue, else the track start
            _backend.SetDeckPositionFraction(deck.Handle, target);
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

    public double DeckFirstBeat(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _firstBeat[slot];
    }

    public void SetDeckFirstBeat(int slot, double firstBeatSeconds)
    {
        ValidateSlot(slot);
        lock (_gate) _firstBeat[slot] = firstBeatSeconds > 0.0 ? firstBeatSeconds : 0.0;
    }

    public void SyncOnce(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is not { } deck || _baseBpm[slot] <= 0.0)
                return;

            int leader = slot == 0 ? 1 : 0;
            if (_decks[leader] is null || _baseBpm[leader] <= 0.0)
                return;

            double leaderRate = _playbackRate[leader];
            double targetRate = TempoSyncCalculator.RateFor(
                _baseBpm[leader] * leaderRate,
                _baseBpm[slot]);
            _playbackRate[slot] = targetRate;
            _pitchPosition[slot] = PitchPositionFor(targetRate);
            _backend.SetDeckRate(deck.Handle, targetRate);
            PhaseAlignToLeader(slot);
            _logger.LogInformation(
                "Deck slot {Slot} one-shot synced to deck {Leader} at rate {Rate:F5}.",
                slot,
                leader,
                targetRate);
        }
    }

    public double DeckBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return EffectiveBpm(slot);
    }

    public double MinimumDeckBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _baseBpm[slot] > 0.0 ? _baseBpm[slot] * (1.0 - PitchRangePercent) : 0.0;
    }

    public double MaximumDeckBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _baseBpm[slot] > 0.0 ? _baseBpm[slot] * (1.0 + PitchRangePercent) : 0.0;
    }

    public void SetDeckBpm(int slot, double bpm)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_baseBpm[slot] <= 0.0 || bpm <= 0.0)
                return;

            _pitchPosition[slot] = PitchPositionFor(bpm / _baseBpm[slot]);
            _playbackRate[slot] = RateFor(_pitchPosition[slot]);
            if (_decks[slot] is { } deck && !_syncLocked[slot])
                _backend.SetDeckRate(deck.Handle, _playbackRate[slot]);
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
                // Professional SYNC engage (doc 11): (1) beatmatch tempo to the master, then (2) snap the
                // beat phase onto the master grid once so it lands inside the lock zone immediately (no
                // long audible pitch-ride). The continuous loop (UpdateSync) then holds it there.
                ReapplyRate(slot);
                if (ValidLeaderSlot(slot) >= 0)
                    PhaseAlignToLeader(slot);
                SetSyncStateLocked(slot, SyncLockState.Active); // the loop refines to Locked on the next tick
            }
            else
            {
                if (_decks[slot] is { } deck)
                    _backend.SetDeckRate(deck.Handle, _playbackRate[slot]);
                SetSyncStateLocked(slot, SyncLockState.Off);
            }
        }
    }

    public int? SyncMaster
    {
        get { lock (_gate) return ComputeSyncMasterLocked(); }
    }

    public SyncLockState SyncState(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _syncState[slot];
    }

    /// <inheritdoc />
    public void UpdateSync(long hostTimeTicks)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            // Hold every engaged (slave) deck phase-locked to its master. Normally just one deck is the
            // slave; if a deck has Sync armed but has no valid master yet (the other deck unloaded), it
            // stays Active but uncorrected — never a wrong tempo.
            for (int slot = 0; slot < Decks; slot++)
            {
                if (!_syncLocked[slot])
                    continue;
                int leader = ValidLeaderSlot(slot);
                if (leader < 0)
                {
                    SetSyncStateLocked(slot, SyncLockState.Active);
                    continue;
                }
                CorrectSlaveLocked(slot, leader);
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetSyncMasterBeat(out double effectiveBpm, out double continuousBeat)
    {
        effectiveBpm = 0.0;
        continuousBeat = 0.0;
        lock (_gate)
        {
            if (_disposed || ComputeSyncMasterLocked() is not int master || _decks[master] is not { } deck)
                return false;

            double bpm = EffectiveBpm(master);
            if (bpm <= 0.0)
                return false;

            // Continuous beat position from the master deck's true playhead — the deterministic grid the
            // shared clock (and the visuals) lock to. Latency-compensated so the published grid matches
            // what the listener hears.
            double posSeconds = _backend.GetDeckPositionSeconds(deck.Handle) - _phaseLock.OutputLatencySeconds;
            effectiveBpm = bpm;
            continuousBeat = (posSeconds - _firstBeat[master]) / (60.0 / _baseBpm[master]);
            return true;
        }
    }

    // Caller holds _gate. The sync master is the valid leader of whichever deck currently has Sync
    // engaged (the slave). Computed rather than stored so it stays correct across loads/unloads. Null
    // when no deck is synced or the would-be master is not a valid reference.
    private int? ComputeSyncMasterLocked()
    {
        for (int slot = 0; slot < Decks; slot++)
        {
            if (!_syncLocked[slot])
                continue;
            int leader = ValidLeaderSlot(slot);
            if (leader >= 0)
                return leader;
        }
        return null;
    }

    // Caller holds _gate. The other deck if it is a valid sync reference for this slot (loaded, not itself
    // synced, known base BPM) and this slot can be matched (loaded, known base BPM); otherwise -1.
    private int ValidLeaderSlot(int slot)
    {
        if (_decks[slot] is null || _baseBpm[slot] <= 0.0)
            return -1;
        int leader = slot == 0 ? 1 : 0;
        if (_decks[leader] is null || _syncLocked[leader] || _baseBpm[leader] <= 0.0)
            return -1;
        return leader;
    }

    // Caller holds _gate. One correction tick for a synced slave: measure the residual beat-phase error
    // against the master, apply the clamped micro pitch-bend, and re-snap once if it has slipped too far.
    private void CorrectSlaveLocked(int slot, int leader)
    {
        if (_decks[slot] is not { } deck || _decks[leader] is not { } leaderDeck)
            return;

        if (_baseBpm[slot] <= 0.0 || _baseBpm[leader] <= 0.0)
        {
            SetSyncStateLocked(slot, SyncLockState.Active);
            return;
        }

        // Latency-compensated positions. The same output latency is subtracted from both decks, so for
        // deck-to-deck phase it cancels (they share one output path) — kept explicit for correctness and
        // for any future split routing; it primarily aligns the shared clock / visuals to audible output.
        double lat = _phaseLock.OutputLatencySeconds;
        // Position and first-beat are source-media coordinates. Their grid spacing is therefore the
        // analyzed base BPM; playback rate changes how quickly the playhead crosses that grid, not the
        // distance between kick markers in the source.
        var slavePhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(deck.Handle) - lat, _firstBeat[slot], _baseBpm[slot]);
        var masterPhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(leaderDeck.Handle) - lat, _firstBeat[leader], _baseBpm[leader]);

        double beatmatchedRate = SyncedRateFor(slot); // the tempo-matched base rate, before phase correction
        PhaseLockCorrection correction =
            PhaseLockController.Correct(slavePhase, masterPhase, beatmatchedRate, _phaseLock);

        _backend.SetDeckRate(deck.Handle, correction.EffectiveRate);

        if (correction.RequiresReSnap)
        {
            double length = _backend.GetDeckLengthSeconds(deck.Handle);
            if (length > 0.0)
            {
                double target = Math.Clamp((slavePhase.PositionSeconds + correction.ReSnapSeconds) / length, 0.0, 1.0);
                _backend.SetDeckPositionFraction(deck.Handle, target);
            }
        }

        SetSyncStateLocked(slot, correction.State);
    }

    // Caller holds _gate. Store the slot's sync state, logging only on a transition (never per frame) so
    // set diagnostics capture lock/drift changes without flooding the log (doc 03 invariant).
    private void SetSyncStateLocked(int slot, SyncLockState state)
    {
        if (_syncState[slot] == state)
            return;
        _logger.LogInformation("Deck slot {Slot} sync state {Old} -> {New}.", slot, _syncState[slot], state);
        _syncState[slot] = state;
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

        // A prior one-shot sync can put the leader beyond the manual pitch fader's display range.
        double leaderEffectiveBpm = _baseBpm[leader] * _playbackRate[leader];
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
        lock (_gate)
        {
            _quantize[slot] = enabled;
            // Phase match (doc 11): enabling Quantize snaps the deck's beat phase onto the leader's grid
            // once, now. The latch stays so a UI/LED reflects the armed state; the alignment is the action.
            if (enabled)
                PhaseAlignToLeader(slot);
        }
    }

    // Caller holds _gate. Snap one deck's playhead so its beat phase lines up with the sync leader's grid.
    // Leader = the other deck if it is loaded with a known anchor + BPM. With no valid leader (or this
    // deck's own anchor/BPM unknown) the playhead is left where it is — Quantize arms but does not guess.
    private void PhaseAlignToLeader(int slot)
    {
        if (_decks[slot] is not { } deck)
            return;

        if (_baseBpm[slot] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} quantize: own tempo unknown; phase unchanged.", slot);
            return;
        }

        int leader = slot == 0 ? 1 : 0;
        if (_decks[leader] is null || _baseBpm[leader] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} quantize: no valid leader; phase unchanged.", slot);
            return;
        }

        // Deck positions and anchors are measured in source-media seconds, so phase must use each
        // track's analyzed base BPM. Effective BPM describes wall-clock playback speed and would skew
        // the kick grid whenever Sync changes the deck rate.
        var followerPhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(deck.Handle), _firstBeat[slot], _baseBpm[slot]);
        var leaderPhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(_decks[leader]!.Handle), _firstBeat[leader], _baseBpm[leader]);

        double nudgeSeconds = PhaseAlignmentCalculator.PhaseNudgeSeconds(followerPhase, leaderPhase);
        double length = _backend.GetDeckLengthSeconds(deck.Handle);
        if (length <= 0.0)
            return;

        double targetFraction = Math.Clamp((followerPhase.PositionSeconds + nudgeSeconds) / length, 0.0, 1.0);
        _backend.SetDeckPositionFraction(deck.Handle, targetFraction);
        _logger.LogInformation(
            "Deck slot {Slot} quantize: phase-aligned by {Nudge:F4}s to the leader grid.", slot, nudgeSeconds);
    }

    // Caller holds _gate. A one-shot sync may exceed the manual pitch fader's display range, so audible
    // tempo follows the separately retained playback rate. 0 when base BPM is unknown.
    private double EffectiveBpm(int slot)
    {
        if (_baseBpm[slot] <= 0.0)
            return 0.0;
        double rate = _syncLocked[slot] ? SyncedRateFor(slot) : _playbackRate[slot];
        return _baseBpm[slot] * rate;
    }

    // Caller holds _gate. The rate Sync would apply to a follower deck (its leader's audible tempo folded
    // to the nearest octave), or its manual pitch rate when no valid leader exists — mirrors ReapplyRate.
    private double SyncedRateFor(int slot)
    {
        int leader = slot == 0 ? 1 : 0;
        if (_baseBpm[slot] <= 0.0 || _decks[leader] is null || _syncLocked[leader] || _baseBpm[leader] <= 0.0)
            return _playbackRate[slot];
        double leaderEffectiveBpm = _baseBpm[leader] * _playbackRate[leader];
        return TempoSyncCalculator.RateFor(leaderEffectiveBpm, _baseBpm[slot]);
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
            {
                _backend.SetDeckPositionFraction(deck.Handle, position); // jump to the stored cue
            }
            else
            {
                _hotCues[slot][cueIndex] = _backend.GetDeckPositionFraction(deck.Handle); // set at current position
                SavePersistedHotCues(slot, deck.Handle); // a newly set cue survives the next load/restart
            }
        }
    }

    // Caller holds _gate. Load a track's persisted cue set (A3) and project the sample-based cues onto
    // this deck's 0..1 fraction bank using the deck length. No store, no length, or an unreadable file
    // all leave the (already-cleared) bank empty — a persistence hiccup must never crash a load.
    private void LoadPersistedHotCues(int slot, int handle, string trackPath)
    {
        if (_hotCueStore is null)
            return;

        try
        {
            TrackCueRecord? record = _hotCueStore.LoadAsync(trackPath).GetAwaiter().GetResult();
            if (record is null)
                return;

            double lengthSeconds = _backend.GetDeckLengthSeconds(handle);
            int sampleRate = record.SampleRate > 0 ? record.SampleRate : _sampleRate;
            foreach (HotCue cue in record.HotCues)
            {
                if (cue.Index < 0 || cue.Index >= HotCuesPerDeck)
                    continue; // tolerate a hand-edited / wider-bank file
                _hotCues[slot][cue.Index] =
                    HotCuePositionMapper.SamplesToFraction(cue.PositionSamples, lengthSeconds, sampleRate);
            }
            _logger.LogInformation(
                "Deck slot {Slot}: restored {Count} persisted hot cue(s) for {Track}.",
                slot, record.HotCues.Count, trackPath);
        }
        catch (Exception ex)
        {
            // Degrade to no-cues rather than failing the load (global standards #16/#26).
            _logger.LogWarning(ex, "Could not load persisted hot cues for deck slot {Slot} <- {Track}.", slot, trackPath);
        }
    }

    // Caller holds _gate. Persist the slot's current cue bank (A3), keyed by the loaded path, as a
    // sample-based record. Fire-and-forget so a pad press stays instant; a failed save is logged, never
    // thrown, and never blocks the show. Reads the bank snapshot now (under the gate) so the async write
    // does not race a later cue edit.
    private void SavePersistedHotCues(int slot, int handle)
    {
        if (_hotCueStore is null || _loadedPath[slot] is not { } trackPath)
            return;

        double lengthSeconds = _backend.GetDeckLengthSeconds(handle);
        var set = new TrackCueSet(_sampleRate > 0 ? _sampleRate : 1, HotCuesPerDeck);
        for (int i = 0; i < HotCuesPerDeck; i++)
        {
            if (_hotCues[slot][i] is { } fraction)
                set = set.SetHotCue(i, HotCuePositionMapper.FractionToSamples(fraction, lengthSeconds, _sampleRate));
        }

        TrackCueRecord record = TrackCueRecord.FromCueSet(trackPath, set);
        try
        {
            // Fire-and-forget: a pad press stays instant. Both a synchronous throw (a misbehaving store)
            // and an async fault are logged and dropped, never crashing the show (global #16/#26).
            _ = _hotCueStore.SaveAsync(record).ContinueWith(
                task => _logger.LogWarning(
                    task.Exception?.GetBaseException(),
                    "Could not persist hot cues for deck slot {Slot} <- {Track}.", slot, trackPath),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist hot cues for deck slot {Slot} <- {Track}.", slot, trackPath);
        }
    }

    public double LoopBeats(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _loopBeats[slot];
    }

    public bool IsLooping(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _loopBeats[slot] > 0.0;
    }

    public void SetLoop(int slot, double beats)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is not { } deck)
            {
                _logger.LogWarning("SetLoop deck slot {Slot} requested with no track loaded; ignoring.", slot);
                return;
            }
            if (_baseBpm[slot] <= 0.0)
            {
                // A beat-length loop needs the deck's tempo to size the region; without it, do nothing
                // rather than guess a wrong span (doc 11 loops are beat-synced to the deck grid).
                _logger.LogWarning("SetLoop deck slot {Slot} ignored: base BPM unknown.", slot);
                return;
            }
            if (beats < BeatLoopCalculator.MinBeats)
            {
                ClearLoopLocked(slot, deck);
                return;
            }

            // Convert the musical beat length to a concrete time region starting at the current playhead,
            // using the deck's natural BPM so the loop is musically <beats> beats regardless of pitch.
            double startSeconds = _backend.GetDeckPositionSeconds(deck.Handle);
            LoopRegion region = BeatLoopCalculator.Region(startSeconds, beats, _baseBpm[slot]);
            _backend.SetDeckLoop(deck.Handle, region.StartSeconds, region.EndSeconds);
            _loopBeats[slot] = beats;
            _logger.LogInformation(
                "Deck slot {Slot} loop: {Beats} beats -> [{Start:F3}s, {End:F3}s).",
                slot, beats, region.StartSeconds, region.EndSeconds);
        }
    }

    public void ClearLoop(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is { } deck)
                ClearLoopLocked(slot, deck);
        }
    }

    // Caller holds _gate. Drop any active loop on the slot (backend + tracked beat length).
    private void ClearLoopLocked(int slot, LoadedDeck deck)
    {
        if (_loopBeats[slot] <= 0.0)
            return;
        _backend.ClearDeckLoop(deck.Handle);
        _loopBeats[slot] = 0.0;
        _logger.LogInformation("Deck slot {Slot} loop cleared.", slot);
    }

    // End-of-track (A4): fired from the backend's end-of-stream sync (the BASS sync thread). Marks the
    // slot stopped under the gate, then raises DeckEnded OUTSIDE the lock so a subscriber that drives the
    // engine back (e.g. the live-queue binding loading the next track) does not run nested under _gate.
    // Guarded by handle so a stale callback from an already-replaced deck is ignored.
    private void OnDeckEnded(int slot, int handle)
    {
        lock (_gate)
        {
            if (_disposed || _decks[slot] is not { } deck || deck.Handle != handle)
                return; // the slot was replaced/unloaded before the end fired — ignore the stale callback
            _decks[slot] = deck with { Playing = false };
        }

        try
        {
            DeckEnded?.Invoke(this, slot);
        }
        catch (Exception ex)
        {
            // A misbehaving subscriber must not bubble onto the BASS sync thread (global #16/#26).
            _logger.LogError(ex, "A DeckEnded handler threw for deck slot {Slot}.", slot);
        }
    }

    // Maps a normalized pitch position (0..1, 0.5 = centre) to a playback-rate multiplier within ±range.
    private static double RateFor(double normalizedPosition)
        => 1.0 + (Math.Clamp(normalizedPosition, 0.0, 1.0) - PitchCenter) * 2.0 * PitchRangePercent;

    private static double PitchPositionFor(double rate)
        => Math.Clamp(
            PitchCenter + ((rate - 1.0) / (2.0 * PitchRangePercent)),
            0.0,
            1.0);

    // Caller holds _gate. Unplugs and forgets any deck in the slot, clearing its mixer channel and the
    // track-specific hot-cues (a new track gets fresh cues).
    private void UnloadSlot(int slot)
    {
        if (_decks[slot] is not { } deck)
            return;
        _backend.ClearDeckLoop(deck.Handle); // drop any loop sync before the stream is freed
        _backend.UnplugDeck(deck.Handle);
        _mixer.SetChannel(slot, null);
        _decks[slot] = null;
        _loadedPath[slot] = null;
        _tempCue[slot] = null; // the temp cue belongs to the track — the new track starts with none
        Array.Clear(_hotCues[slot]);
        _baseBpm[slot] = 0.0;   // base BPM belongs to the track — the new track supplies its own on load
        _firstBeat[slot] = 0.0; // first-beat anchor likewise belongs to the track
        _loopBeats[slot] = 0.0; // a new track has no active loop
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
