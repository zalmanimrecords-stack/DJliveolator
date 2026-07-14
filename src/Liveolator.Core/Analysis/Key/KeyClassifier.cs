namespace Liveolator.Core.Analysis.Key;

/// <summary>
/// Classifies a chroma vector into a musical key using the Krumhansl–Schmuckler template
/// method: correlate the 12-bin chroma against major and minor key profiles at all 12
/// rotations and pick the strongest match. Second stage of key detection (doc 03 / doc 16).
/// </summary>
public sealed class KeyClassifier
{
    // Krumhansl–Kessler probe-tone key profiles.
    private static readonly double[] MajorProfile =
        { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] MinorProfile =
        { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    public MusicalKey Classify(double[] chroma)
    {
        ArgumentNullException.ThrowIfNull(chroma);
        if (chroma.Length != 12)
            throw new ArgumentException("Chroma vector must have 12 elements.", nameof(chroma));

        double bestCorr = double.NegativeInfinity;
        int bestTonic = 0;
        KeyMode bestMode = KeyMode.Major;

        for (int tonic = 0; tonic < 12; tonic++)
        {
            double major = Correlation(chroma, MajorProfile, tonic);
            if (major > bestCorr)
            {
                bestCorr = major;
                bestTonic = tonic;
                bestMode = KeyMode.Major;
            }

            double minor = Correlation(chroma, MinorProfile, tonic);
            if (minor > bestCorr)
            {
                bestCorr = minor;
                bestTonic = tonic;
                bestMode = KeyMode.Minor;
            }
        }

        double confidence = Math.Clamp(bestCorr, 0.0, 1.0);
        return new MusicalKey(bestTonic, bestMode, Camelot.Code(bestTonic, bestMode), confidence);
    }

    /// <summary>Pearson correlation between the chroma and a profile rotated to the given tonic.</summary>
    private static double Correlation(double[] chroma, double[] profile, int tonic)
    {
        Span<double> rotated = stackalloc double[12];
        double meanChroma = 0, meanProfile = 0;
        for (int i = 0; i < 12; i++)
        {
            rotated[i] = profile[(((i - tonic) % 12) + 12) % 12];
            meanChroma += chroma[i];
            meanProfile += rotated[i];
        }
        meanChroma /= 12;
        meanProfile /= 12;

        double num = 0, varC = 0, varP = 0;
        for (int i = 0; i < 12; i++)
        {
            double a = chroma[i] - meanChroma;
            double b = rotated[i] - meanProfile;
            num += a * b;
            varC += a * a;
            varP += b * b;
        }

        if (varC <= 0 || varP <= 0)
            return 0;
        return num / Math.Sqrt(varC * varP);
    }
}
