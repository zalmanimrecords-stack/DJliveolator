namespace Liveolator.Core.Audio;

/// <summary>Maps FFT magnitudes into four visual bands with attack/release smoothing.</summary>
public sealed class FrequencyBandEnvelope
{
    private readonly double _attackSeconds;
    private readonly double _releaseSeconds;
    private VisualAudioBands _current = VisualAudioBands.Silent;

    public FrequencyBandEnvelope(double attackSeconds = 0.035, double releaseSeconds = 0.22)
    {
        if (attackSeconds <= 0 || double.IsNaN(attackSeconds))
            throw new ArgumentOutOfRangeException(nameof(attackSeconds));
        if (releaseSeconds <= 0 || double.IsNaN(releaseSeconds))
            throw new ArgumentOutOfRangeException(nameof(releaseSeconds));
        _attackSeconds = attackSeconds;
        _releaseSeconds = releaseSeconds;
    }

    public VisualAudioBands Process(ReadOnlySpan<float> spectrum, int sampleRate, double dtSeconds)
    {
        if (spectrum.Length < 2 || sampleRate <= 0)
            return _current;

        int fftSize = (spectrum.Length - 1) * 2;
        double binHz = (double)sampleRate / fftSize;
        var target = new VisualAudioBands(
            Band(spectrum, binHz, fftSize, 20, 140),
            Band(spectrum, binHz, fftSize, 140, 500),
            Band(spectrum, binHz, fftSize, 500, 2_500),
            Band(spectrum, binHz, fftSize, 2_500, 12_000));

        if (dtSeconds <= 0 || double.IsNaN(dtSeconds))
            return _current;

        _current = new VisualAudioBands(
            Smooth(_current.Bass, target.Bass, dtSeconds),
            Smooth(_current.LowMid, target.LowMid, dtSeconds),
            Smooth(_current.Mid, target.Mid, dtSeconds),
            Smooth(_current.High, target.High, dtSeconds));
        return _current;
    }

    private double Smooth(double current, double target, double dt)
    {
        double tau = target > current ? _attackSeconds : _releaseSeconds;
        return Math.Clamp(current + (target - current) * (1.0 - Math.Exp(-dt / tau)), 0.0, 1.0);
    }

    private static double Band(
        ReadOnlySpan<float> spectrum,
        double binHz,
        int fftSize,
        double minHz,
        double maxHz)
    {
        int first = Math.Clamp((int)Math.Ceiling(minHz / binHz), 0, spectrum.Length - 1);
        int last = Math.Clamp((int)Math.Floor(maxHz / binHz), first, spectrum.Length - 1);
        double sumSquares = 0;
        int count = 0;
        for (int i = first; i <= last; i++)
        {
            double magnitude = spectrum[i];
            if (double.IsNaN(magnitude) || magnitude < 0)
                continue;
            double normalized = 2.0 * magnitude / fftSize;
            sumSquares += normalized * normalized;
            count++;
        }

        if (count == 0)
            return 0;

        double rms = Math.Sqrt(sumSquares / count);
        return Math.Clamp(1.0 - Math.Exp(-6.0 * rms), 0.0, 1.0);
    }
}
