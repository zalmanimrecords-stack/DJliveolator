using Liveolator.Core.Audio;
using Liveolator.Core.Mixer;
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

    private readonly IBassMixerBackend _backend;
    private readonly BassMixer _mixer;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly MasterAudioSource _master;
    private readonly LoadedDeck?[] _decks = new LoadedDeck?[Decks];
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
    public TwoDeckBassEngine(
        BassMixer mixer, int sampleRate = 48_000, int channels = 2, ILoggerFactory? loggerFactory = null)
        : this(
            new BassMixerBackend(
                sampleRate, channels,
                (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<BassMixerBackend>()),
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

        MasterMixInfo info = _backend.CreateMaster();
        _master = new MasterAudioSource(info.Channels, info.SampleRate);
        // Arm the master tap immediately: the mix runs continuously and the beat clock is fed whenever
        // a deck is playing, with no extra "start the mix" step for the host to coordinate.
        _backend.StartMaster(_master.Emit);
    }

    /// <summary>The post-crossfader master mix; feed this to a <see cref="MasterMixPlaybackEngine"/>.</summary>
    public IAudioSource MasterSource => _master;

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

    // Caller holds _gate. Unplugs and forgets any deck in the slot, clearing its mixer channel.
    private void UnloadSlot(int slot)
    {
        if (_decks[slot] is not { } deck)
            return;
        _backend.UnplugDeck(deck.Handle);
        _mixer.SetChannel(slot, null);
        _decks[slot] = null;
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
