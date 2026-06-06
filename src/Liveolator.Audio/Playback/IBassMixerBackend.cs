namespace Liveolator.Audio.Playback;

/// <summary>
/// Thin seam over the native BASS calls a two-deck mix needs (a BASSmix master channel, decoding deck
/// streams plugged into it, per-deck BASS_FX control, and a master output tap), so
/// <see cref="TwoDeckBassEngine"/>'s load/play/stop state machine unit-tests with a fake while the real
/// BASSmix/BASS_FX P/Invoke (<see cref="BassMixerBackend"/>) is isolated. Internal: a binding
/// implementation detail, mirroring the <see cref="IBassPlayback"/> pattern.
/// </summary>
/// <remarks>
/// The backend owns the single master channel created by <see cref="CreateMaster"/>; deck handles it
/// hands back from <see cref="OpenDeckStream"/> address individual decks for play/pause/unplug. All
/// methods run on the caller's thread except the <see cref="StartMaster"/> tap, which fires on the
/// BASS update thread.
/// </remarks>
internal interface IBassMixerBackend : IDisposable
{
    /// <summary>Create the master mix channel and report its output format. Throws on failure.</summary>
    MasterMixInfo CreateMaster();

    /// <summary>Open a decoding deck stream for a file (not yet plugged into the mixer). Throws on failure.</summary>
    int OpenDeckStream(string filePath);

    /// <summary>Plug an opened deck stream into the master mix, returning its per-deck FX control.</summary>
    IBassMixerChannel PlugDeck(int deckHandle);

    /// <summary>Pause or resume a plugged deck's contribution to the mix.</summary>
    void SetDeckPlaying(int deckHandle, bool playing);

    /// <summary>Unplug a deck from the mix and free its stream.</summary>
    void UnplugDeck(int deckHandle);

    /// <summary>The deck's current playback position as a 0..1 fraction of its length (0 if unknown).</summary>
    double GetDeckPositionFraction(int deckHandle);

    /// <summary>Seek the deck to a 0..1 fraction of its length (the caller clamps to range).</summary>
    void SetDeckPositionFraction(int deckHandle, double fraction);

    /// <summary>Set the deck's playback rate as a multiplier of its original sample rate (1.0 = original).</summary>
    void SetDeckRate(int deckHandle, double rateMultiplier);

    /// <summary>The deck's current playback position in seconds from the track start (0 if unknown).</summary>
    double GetDeckPositionSeconds(int deckHandle);

    /// <summary>The deck's total length in seconds (0 if unknown), used to scale loop/position math.</summary>
    double GetDeckLengthSeconds(int deckHandle);

    /// <summary>
    /// Arm a loop on the deck over the half-open time region [<paramref name="startSeconds"/>,
    /// <paramref name="endSeconds"/>) (BASS_SYNC_POS at the end-point seeking back to the start). Replaces
    /// any prior loop on the deck.
    /// </summary>
    void SetDeckLoop(int deckHandle, double startSeconds, double endSeconds);

    /// <summary>Remove the deck's active loop so playback continues past the former region.</summary>
    void ClearDeckLoop(int deckHandle);

    /// <summary>Start the master output and arm the tap that delivers mixed samples to the frame pipeline.</summary>
    void StartMaster(Action<float[]> onMasterSamples);

    /// <summary>
    /// Re-open the output on a new device / buffer at runtime (doc 12 Settings re-init). Returns true if
    /// the requested (or a safe fallback) device is now open; false if the re-open failed. Must not throw
    /// for an expected device error — return false so the coordinator can roll back (global #16/#26).
    /// </summary>
    bool ReinitOutput(BassInitOptions options);
}

/// <summary>Output format of the master mix channel reported by BASS.</summary>
internal readonly record struct MasterMixInfo(int Channels, int SampleRate);
