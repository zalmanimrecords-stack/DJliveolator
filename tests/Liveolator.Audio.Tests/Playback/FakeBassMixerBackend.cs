using System;
using System.Collections.Generic;
using Liveolator.Audio.Playback;
using Liveolator.Core.Mixer;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Test double for the native two-deck BASS backend: records the deck lifecycle and lets a test push
/// master-tap samples, so <see cref="TwoDeckBassEngine"/>'s state machine is exercised without BASS.
/// </summary>
internal sealed class FakeBassMixerBackend : IBassMixerBackend
{
    private Action<float[]>? _masterTap;
    private int _nextHandle = 100;

    public MasterMixInfo MasterInfo { get; set; } = new(Channels: 2, SampleRate: 48_000);
    public List<string> Opened { get; } = new();
    public Dictionary<int, FakeBassMixerChannel> Channels { get; } = new();
    public Dictionary<int, bool> Playing { get; } = new();
    public List<int> Unplugged { get; } = new();
    public int MasterStarts { get; private set; }
    public bool Disposed { get; private set; }
    public Func<string, int>? OpenOverride { get; set; }

    public MasterMixInfo CreateMaster() => MasterInfo;

    public int OpenDeckStream(string filePath)
    {
        Opened.Add(filePath);
        return OpenOverride?.Invoke(filePath) ?? _nextHandle++;
    }

    public IBassMixerChannel PlugDeck(int deckHandle, int slot)
    {
        var channel = new FakeBassMixerChannel(deckHandle);
        Channels[deckHandle] = channel;
        Playing[deckHandle] = false;
        return channel;
    }

    public void SetDeckPlaying(int deckHandle, bool playing) => Playing[deckHandle] = playing;

    public void UnplugDeck(int deckHandle)
    {
        Unplugged.Add(deckHandle);
        Playing.Remove(deckHandle);
    }

    public Dictionary<int, double> PositionFraction { get; } = new();
    public Dictionary<int, double> Rate { get; } = new();

    /// <summary>Per-deck total length in seconds; defaults to 100 s so fraction↔seconds math is testable.</summary>
    public Dictionary<int, double> LengthSeconds { get; } = new();

    /// <summary>Loops armed via <see cref="SetDeckLoop"/>, by deck handle (cleared by <see cref="ClearDeckLoop"/>).</summary>
    public Dictionary<int, (double Start, double End)> Loops { get; } = new();
    public List<int> LoopsCleared { get; } = new();

    public double GetDeckPositionFraction(int deckHandle)
        => PositionFraction.TryGetValue(deckHandle, out double f) ? f : 0.0;

    public void SetDeckPositionFraction(int deckHandle, double fraction) => PositionFraction[deckHandle] = fraction;

    public void SetDeckRate(int deckHandle, double rateMultiplier) => Rate[deckHandle] = rateMultiplier;

    private double DeckLength(int deckHandle)
        => LengthSeconds.TryGetValue(deckHandle, out double len) && len > 0 ? len : 100.0;

    public double GetDeckPositionSeconds(int deckHandle) => GetDeckPositionFraction(deckHandle) * DeckLength(deckHandle);

    public double GetDeckLengthSeconds(int deckHandle) => DeckLength(deckHandle);

    public void SetDeckLoop(int deckHandle, double startSeconds, double endSeconds)
        => Loops[deckHandle] = (startSeconds, endSeconds);

    public void ClearDeckLoop(int deckHandle)
    {
        Loops.Remove(deckHandle);
        LoopsCleared.Add(deckHandle);
    }

    /// <summary>End-of-stream callbacks armed via <see cref="SetDeckEndCallback"/>, by deck handle.</summary>
    public Dictionary<int, Action> EndCallbacks { get; } = new();

    public void SetDeckEndCallback(int deckHandle, Action onEnded) => EndCallbacks[deckHandle] = onEnded;

    /// <summary>Simulate the deck stream reaching its end so the engine's end-of-track path runs.</summary>
    public void EmitDeckEnd(int deckHandle)
    {
        if (EndCallbacks.TryGetValue(deckHandle, out Action? cb))
            cb();
    }

    public void StartMaster(Action<float[]> onMasterSamples)
    {
        _masterTap = onMasterSamples;
        MasterStarts++;
    }

    public List<BassInitOptions> Reinits { get; } = new();
    public bool ReinitResult { get; set; } = true;

    public bool ReinitOutput(BassInitOptions options)
    {
        Reinits.Add(options);
        return ReinitResult;
    }

    public void Dispose() => Disposed = true;

    /// <summary>Simulate BASSmix delivering mixed samples to the armed master tap.</summary>
    public void EmitMaster(float[] interleaved) => _masterTap?.Invoke(interleaved);
}

/// <summary>Test double for a per-deck BASS_FX control; records the last applied values.</summary>
internal sealed class FakeBassMixerChannel : IBassMixerChannel
{
    public DeckLevel Level { get; set; } = DeckLevel.Silent;
    public FakeBassMixerChannel(int deckHandle) => DeckHandle = deckHandle;

    public int DeckHandle { get; }
    public double? Volume { get; private set; }
    public Dictionary<EqBand, BiquadCoefficients> Eq { get; } = new();
    public BiquadCoefficients? Filter { get; private set; }
    public bool? Cue { get; private set; }

    public void SetVolume(double linearGain) => Volume = linearGain;
    public void SetEqBand(EqBand band, BiquadCoefficients coefficients) => Eq[band] = coefficients;
    public void SetFilter(BiquadCoefficients coefficients) => Filter = coefficients;
    public void SetCue(bool enabled) => Cue = enabled;
}
