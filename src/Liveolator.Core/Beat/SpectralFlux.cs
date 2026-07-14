namespace Liveolator.Core.Beat;

/// <summary>
/// Pure spectral-flux onset measure: the sum of positive magnitude changes between two
/// consecutive spectra. Rising energy (a note/drum onset) contributes; decaying energy does not.
/// The incremental, per-frame counterpart of the offline <c>OnsetEnvelope</c> (doc 03).
/// </summary>
public static class SpectralFlux
{
    /// <summary>
    /// Sum of positive bin-wise differences (current − previous). Returns 0 when the spectra are
    /// empty or differ in length (e.g. across a format change), so a bad pair never poisons the
    /// onset envelope.
    /// </summary>
    public static double Positive(ReadOnlySpan<float> previous, ReadOnlySpan<float> current)
    {
        if (current.Length == 0 || previous.Length != current.Length)
            return 0.0;

        double sum = 0.0;
        for (int i = 0; i < current.Length; i++)
        {
            double diff = current[i] - previous[i];
            if (diff > 0.0)
                sum += diff;
        }
        return sum;
    }
}
