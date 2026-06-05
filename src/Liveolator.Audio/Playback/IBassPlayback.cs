namespace Liveolator.Audio.Playback;

/// <summary>
/// Thin seam over the BASS native calls a deck needs, so <see cref="DeckAudioSource"/> can be
/// unit-tested with a fake while the real P/Invoke (<see cref="BassPlayback"/>) is isolated.
/// Internal: it is a binding implementation detail, not a public contract.
/// </summary>
internal interface IBassPlayback : IDisposable
{
    /// <summary>Open a file as a float-format stream. Returns the channel handle; throws on failure.</summary>
    int CreateFileStream(string filePath);

    /// <summary>Channel format of an open handle.</summary>
    BassChannelInfo GetChannelInfo(int handle);

    /// <summary>Tap the played samples of a handle; the callback receives interleaved float samples.</summary>
    void SetSampleTap(int handle, Action<float[]> onInterleavedSamples);

    void Play(int handle);
    void Pause(int handle);
    void Free(int handle);
}

/// <summary>Channel format reported by BASS.</summary>
internal readonly record struct BassChannelInfo(int Channels, int SampleRate);
