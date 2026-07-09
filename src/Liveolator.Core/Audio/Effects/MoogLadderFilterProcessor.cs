namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// A four-pole Moog transistor-ladder low-pass (Stilson/Smith model, the classic CSound-lineage VCF) with
/// resonance, run as a realtime <see cref="IAudioEffectProcessor"/>. Two parameters: <c>cutoff</c> (0 = ~30 Hz,
/// 1 = fully open) and <c>resonance</c> (0 = none, up to just below self-oscillation). Fully open with no
/// resonance is treated as a bypass so EQ mode passes audio through untouched. Pure managed sample math —
/// unit-testable without BASS.
/// </summary>
public sealed class MoogLadderFilterProcessor : IAudioEffectProcessor
{
    private const double MinCutoffHz = 30.0;
    // Keep resonance below the ~1.0 self-oscillation point so a hard sweep can't run away into the limiter.
    private const double MaxResonance = 0.97;

    private readonly int _sampleRate;
    private readonly SmoothedParameter _cutoff;
    private readonly SmoothedParameter _resonance;

    // Per-audio-channel ladder state (4 stages + input/stage memories), sized on first Process.
    private int _channels;
    private double[] _y1 = Array.Empty<double>();
    private double[] _y2 = Array.Empty<double>();
    private double[] _y3 = Array.Empty<double>();
    private double[] _y4 = Array.Empty<double>();
    private double[] _oldX = Array.Empty<double>();
    private double[] _oldY1 = Array.Empty<double>();
    private double[] _oldY2 = Array.Empty<double>();
    private double[] _oldY3 = Array.Empty<double>();

    public MoogLadderFilterProcessor(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        _sampleRate = sampleRate;
        _cutoff = new SmoothedParameter(BuiltInAudioEffects.Neutral(BuiltInAudioEffects.Cutoff), sampleRate);
        _resonance = new SmoothedParameter(0.0, sampleRate);
    }

    public string PluginUid => BuiltInAudioEffects.MoogUid;

    public int LatencySamples => 0;

    public void SetParameter(string parameterId, double normalizedValue)
    {
        double v = Math.Clamp(normalizedValue, 0.0, 1.0);
        switch (parameterId)
        {
            case BuiltInAudioEffects.Cutoff:
                _cutoff.SetTarget(v);
                break;
            case BuiltInAudioEffects.Resonance:
                _resonance.SetTarget(v);
                break;
        }
    }

    public void LoadPreset(ReadOnlySpan<byte> state) { }

    public void Process(Span<float> interleaved, int channels)
    {
        EnsureChannels(channels);
        int frames = interleaved.Length / channels;

        for (int f = 0; f < frames; f++)
        {
            double cutoffKnob = _cutoff.Next();
            double resKnob = _resonance.Next();

            // Fully open + no resonance = transparent: leave the frame untouched (EQ-mode bypass).
            if (cutoffKnob >= 0.9995 && resKnob <= 0.0005)
                continue;

            double freq = MinCutoffHz * Math.Pow((_sampleRate * 0.45) / MinCutoffHz, cutoffKnob);
            double fc = Math.Clamp(2.0 * freq / _sampleRate, 0.0, 0.99); // normalized 0..1 (fraction of Nyquist)
            double k = 3.6 * fc - 1.6 * fc * fc - 1.0; // empirical tuning (Stilson/Smith)
            double p = (k + 1.0) * 0.5;
            double scale = Math.Exp((1.0 - p) * 1.386249);
            double r = Math.Min(resKnob, MaxResonance) * scale;

            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++)
            {
                double x = interleaved[baseIdx + c] - r * _y4[c];

                double y1 = x * p + _oldX[c] * p - k * _y1[c];
                double y2 = y1 * p + _oldY1[c] * p - k * _y2[c];
                double y3 = y2 * p + _oldY2[c] * p - k * _y3[c];
                double y4 = y3 * p + _oldY3[c] * p - k * _y4[c];
                y4 -= (y4 * y4 * y4) / 6.0; // soft clip — the ladder's saturation "character"

                _oldX[c] = x;
                _oldY1[c] = y1;
                _oldY2[c] = y2;
                _oldY3[c] = y3;
                _y1[c] = y1;
                _y2[c] = y2;
                _y3[c] = y3;
                _y4[c] = y4;

                interleaved[baseIdx + c] = (float)y4;
            }
        }
    }

    public void Dispose() { }

    private void EnsureChannels(int channels)
    {
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        if (channels == _channels)
            return;
        _channels = channels;
        _y1 = new double[channels];
        _y2 = new double[channels];
        _y3 = new double[channels];
        _y4 = new double[channels];
        _oldX = new double[channels];
        _oldY1 = new double[channels];
        _oldY2 = new double[channels];
        _oldY3 = new double[channels];
    }
}
