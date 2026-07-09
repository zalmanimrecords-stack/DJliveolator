namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// A classic four-stage all-pass phaser with an LFO-swept notch and light feedback, run as a realtime
/// <see cref="IAudioEffectProcessor"/>. One parameter, <c>wet</c> (0 = dry passthrough, 1 = full depth);
/// the LFO rate, sweep range and feedback are fixed to a musical default (a slow ~0.3 Hz sweep). The dry
/// signal is always kept at unity, so <c>wet = 0</c> is an exact bypass (EQ mode). Pure managed sample
/// math — unit-testable without BASS.
/// </summary>
public sealed class PhaserProcessor : IAudioEffectProcessor
{
    private const int Stages = 4;
    private const double MinHz = 440.0;
    private const double MaxHz = 1600.0;
    private const double LfoRateHz = 0.3;
    private const double FeedbackAmount = 0.5;

    private readonly int _sampleRate;
    private readonly double _dMin;
    private readonly double _dMax;
    private readonly double _lfoIncrement;
    private readonly SmoothedParameter _wet;

    private double _lfoPhase;
    private int _channels;
    private double[][] _apState = Array.Empty<double[]>(); // [channel][stage] all-pass memory
    private double[] _feedbackMemory = Array.Empty<double>();

    public PhaserProcessor(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        _sampleRate = sampleRate;
        _dMin = MinHz / (sampleRate * 0.5);
        _dMax = MaxHz / (sampleRate * 0.5);
        _lfoIncrement = 2.0 * Math.PI * LfoRateHz / sampleRate;
        _wet = new SmoothedParameter(0.0, sampleRate);
    }

    public string PluginUid => BuiltInAudioEffects.PhaserUid;

    public int LatencySamples => 0;

    public void SetParameter(string parameterId, double normalizedValue)
    {
        if (parameterId == BuiltInAudioEffects.Wet)
            _wet.SetTarget(Math.Clamp(normalizedValue, 0.0, 1.0));
    }

    public void LoadPreset(ReadOnlySpan<byte> state) { }

    public void Process(Span<float> interleaved, int channels)
    {
        EnsureChannels(channels);
        int frames = interleaved.Length / channels;

        for (int f = 0; f < frames; f++)
        {
            double wet = _wet.Next();

            // Fully dry: leave the frame untouched, but keep advancing the LFO so re-enabling picks up
            // in phase rather than jumping.
            _lfoPhase += _lfoIncrement;
            if (_lfoPhase >= 2.0 * Math.PI)
                _lfoPhase -= 2.0 * Math.PI;
            if (wet <= 1e-5)
                continue;

            double d = _dMin + (_dMax - _dMin) * ((Math.Sin(_lfoPhase) + 1.0) * 0.5);
            double a1 = (1.0 - d) / (1.0 + d);

            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++)
            {
                double dry = interleaved[baseIdx + c];
                double y = dry + _feedbackMemory[c] * FeedbackAmount;

                double[] state = _apState[c];
                for (int s = 0; s < Stages; s++)
                {
                    double outp = -a1 * y + state[s];
                    state[s] = y * a1 + outp;
                    y = outp;
                }
                _feedbackMemory[c] = y;

                interleaved[baseIdx + c] = (float)(dry + y * wet);
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
        _apState = new double[channels][];
        for (int c = 0; c < channels; c++)
            _apState[c] = new double[Stages];
        _feedbackMemory = new double[channels];
    }
}
