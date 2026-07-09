namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// A Schroeder-Moorer reverb (Freeverb topology: 8 parallel damped-feedback combs into 4 series all-pass
/// stages per channel) run as a realtime <see cref="IAudioEffectProcessor"/>. One parameter, <c>wet</c>
/// (0 = fully dry passthrough, 1 = strong tail); room size, damping and stereo spread are fixed to sensible
/// club defaults. The dry signal is always passed at unity, so <c>wet = 0</c> is an exact bypass (EQ mode).
/// Pure managed sample math — unit-testable without BASS.
/// </summary>
public sealed class FreeverbProcessor : IAudioEffectProcessor
{
    // Freeverb comb/all-pass buffer lengths (samples @ 44.1 kHz); scaled to the actual rate at build.
    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
    private const int StereoSpread = 23;

    private const double FixedGain = 0.015;
    private const double ScaleWet = 3.0;
    private const double RoomSize = 0.7;   // -> feedback
    private const double Damping = 0.5;
    private const double Feedback = RoomSize * 0.28 + 0.7;
    private const double Damp1 = Damping * 0.4;
    private const double Damp2 = 1.0 - Damp1;
    private const double AllpassFeedback = 0.5;

    private readonly int _sampleRate;
    private readonly SmoothedParameter _wet;

    private int _channels;
    private Comb[][] _combs = Array.Empty<Comb[]>();      // [channel][comb]
    private Allpass[][] _allpasses = Array.Empty<Allpass[]>();
    private bool _active;

    public FreeverbProcessor(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        _sampleRate = sampleRate;
        _wet = new SmoothedParameter(0.0, sampleRate);
    }

    public string PluginUid => BuiltInAudioEffects.ReverbUid;

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

            // Fully dry: leave the frame untouched. Clear the tanks once on the way down so a later
            // re-enable doesn't replay stale tail.
            if (wet <= 1e-5)
            {
                if (_active)
                {
                    ClearTanks();
                    _active = false;
                }
                continue;
            }
            _active = true;

            double wetGain = wet * ScaleWet;
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++)
            {
                double dry = interleaved[baseIdx + c];
                double input = dry * FixedGain;

                double reverb = 0.0;
                Comb[] combs = _combs[c];
                for (int i = 0; i < combs.Length; i++)
                    reverb += combs[i].Process(input);

                Allpass[] allpasses = _allpasses[c];
                for (int i = 0; i < allpasses.Length; i++)
                    reverb = allpasses[i].Process(reverb);

                interleaved[baseIdx + c] = (float)(dry + reverb * wetGain);
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
        _combs = new Comb[channels][];
        _allpasses = new Allpass[channels][];
        double rateScale = _sampleRate / 44100.0;
        for (int c = 0; c < channels; c++)
        {
            int spread = c * StereoSpread; // widen odd channels a touch (Freeverb's stereo spread)
            _combs[c] = new Comb[CombTuning.Length];
            for (int i = 0; i < CombTuning.Length; i++)
                _combs[c][i] = new Comb(Scale(CombTuning[i] + spread, rateScale));
            _allpasses[c] = new Allpass[AllpassTuning.Length];
            for (int i = 0; i < AllpassTuning.Length; i++)
                _allpasses[c][i] = new Allpass(Scale(AllpassTuning[i] + spread, rateScale));
        }
        _active = false;
    }

    private void ClearTanks()
    {
        foreach (Comb[] bank in _combs)
            foreach (Comb comb in bank)
                comb.Clear();
        foreach (Allpass[] bank in _allpasses)
            foreach (Allpass allpass in bank)
                allpass.Clear();
    }

    private static int Scale(int length, double rateScale) => Math.Max(1, (int)Math.Round(length * rateScale));

    // Lowpass-damped feedback comb (Freeverb).
    private sealed class Comb
    {
        private readonly double[] _buffer;
        private int _index;
        private double _filterStore;

        public Comb(int length) => _buffer = new double[length];

        public double Process(double input)
        {
            double output = _buffer[_index];
            _filterStore = output * Damp2 + _filterStore * Damp1;
            _buffer[_index] = input + _filterStore * Feedback;
            if (++_index >= _buffer.Length)
                _index = 0;
            return output;
        }

        public void Clear()
        {
            Array.Clear(_buffer);
            _filterStore = 0.0;
        }
    }

    // Schroeder all-pass (Freeverb).
    private sealed class Allpass
    {
        private readonly double[] _buffer;
        private int _index;

        public Allpass(int length) => _buffer = new double[length];

        public double Process(double input)
        {
            double buffered = _buffer[_index];
            double output = -input + buffered;
            _buffer[_index] = input + buffered * AllpassFeedback;
            if (++_index >= _buffer.Length)
                _index = 0;
            return output;
        }

        public void Clear() => Array.Clear(_buffer);
    }
}
