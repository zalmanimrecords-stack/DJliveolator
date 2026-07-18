using Liveolator.Core.Dsp;

namespace Liveolator.Core.Analysis.Key;

/// <summary>
/// Extracts a 12-bin pitch-class profile (chroma / PCP) from mono PCM: each FFT bin's
/// magnitude is folded onto its pitch class and accumulated across frames, then normalized.
/// First stage of key detection (doc 03 / doc 16).
/// </summary>
public sealed class ChromaExtractor
{
    private const double MinFrequencyHz = 27.5;   // ~A0
    private const double MaxFrequencyHz = 5000.0;

    private readonly int _frameSize;
    private readonly int _hop;
    private readonly double[] _window;

    public ChromaExtractor(int frameSize = 4096, int hop = 2048)
    {
        Stft.ValidateFrameParams(frameSize, hop);

        _frameSize = frameSize;
        _hop = hop;
        _window = Window.Hann(frameSize);
    }

    /// <summary>Returns a normalized 12-element chroma vector (sums to 1, or all zeros).</summary>
    public double[] Compute(ReadOnlySpan<float> mono, int sampleRate)
    {
        var chroma = new double[12];
        if (sampleRate <= 0 || mono.Length < _frameSize)
            return chroma;

        int bins = _frameSize / 2 + 1;
        int[] pitchClassOfBin = BuildBinToPitchClassMap(bins, sampleRate);

        Stft.ForEachFrame(mono, _window, _hop, (_, mag) =>
        {
            for (int b = 0; b < bins; b++)
            {
                int pc = pitchClassOfBin[b];
                if (pc >= 0)
                    chroma[pc] += mag[b];
            }
        });

        double sum = 0;
        for (int i = 0; i < 12; i++) sum += chroma[i];
        if (sum > 0)
            for (int i = 0; i < 12; i++) chroma[i] /= sum;

        return chroma;
    }

    private int[] BuildBinToPitchClassMap(int bins, int sampleRate)
    {
        var map = new int[bins];
        for (int b = 0; b < bins; b++)
        {
            double freq = (double)b * sampleRate / _frameSize;
            if (freq < MinFrequencyHz || freq > MaxFrequencyHz)
            {
                map[b] = -1;
                continue;
            }
            // MIDI number (A4 = 440 Hz = 69), folded to a pitch class with C = 0.
            double midi = 69.0 + 12.0 * Math.Log2(freq / 440.0);
            int pc = ((int)Math.Round(midi)) % 12;
            if (pc < 0) pc += 12;
            map[b] = pc;
        }
        return map;
    }
}
