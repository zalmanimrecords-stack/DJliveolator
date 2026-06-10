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
///
/// This class is split across partial files by responsibility (the engine shares one <c>_gate</c> lock
/// and the per-slot state arrays declared here, so the split is by concern, not by object boundary):
/// <list type="bullet">
///   <item><c>TwoDeckBassEngine.cs</c> — fields, construction, lifecycle (reinit/dispose), pitch helpers.</item>
///   <item><c>TwoDeckBassEngine.Transport.cs</c> — load/play/stop/seek/jog/cue and end-of-track.</item>
///   <item><c>TwoDeckBassEngine.Tempo.cs</c> — pitch fader and base/effective BPM.</item>
///   <item><c>TwoDeckBassEngine.Sync.cs</c> — Sync/phase-lock correction loop and Quantize.</item>
///   <item><c>TwoDeckBassEngine.HotCues.cs</c> — hot-cue bank and persistence.</item>
///   <item><c>TwoDeckBassEngine.Loops.cs</c> — beat-length looping.</item>
/// </list>
/// </remarks>
public sealed partial class TwoDeckBassEngine : IMultiDeckPlaybackEngine, ISyncCorrectionDriver, IDisposable
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

    // Per-deck mutable transport + sync state (one object per slot, indexed A = 0, B = 1). Replaces the
    // former parallel per-slot arrays. All access is serialized by _gate; see DeckSlot.cs.
    private readonly DeckSlot[] _slots = new DeckSlot[Decks];

    // Persistent hot-cue store (doc 11/13, A3): null = cues stay RAM-only (the prior behaviour). When
    // present, a track's saved cue set is loaded on Load and re-saved on set/clear, keyed by file path.
    private readonly IHotCueStore? _hotCueStore;

    // The sample rate the persisted cue offsets are mapped against — the master mix rate. Cue positions
    // are stored as fractions here but persisted as samples, so the store record is self-describing.
    private readonly int _sampleRate;

    // Phase-lock loop tunables (gains/thresholds/output latency). Injected so the composition root can
    // pass the user's output latency; defaults to the professional preset.
    private readonly PhaseLockSettings _phaseLock;

    private bool _disposed;

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
        for (int slot = 0; slot < Decks; slot++)
            _slots[slot] = new DeckSlot(HotCuesPerDeck)
            {
                PitchPosition = PitchCenter, // fader centre = no tempo change
                PlaybackRate = 1.0,          // unity rate until a pitch/sync change
            };

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

    public int DeckCount => Decks;

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

    // Maps a normalized pitch position (0..1, 0.5 = centre) to a playback-rate multiplier within ±range.
    private static double RateFor(double normalizedPosition)
        => 1.0 + (Math.Clamp(normalizedPosition, 0.0, 1.0) - PitchCenter) * 2.0 * PitchRangePercent;

    private static double PitchPositionFor(double rate)
        => Math.Clamp(
            PitchCenter + ((rate - 1.0) / (2.0 * PitchRangePercent)),
            0.0,
            1.0);

    private static void ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= Decks)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range.");
    }
}
