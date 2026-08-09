namespace Liveolator.Core.Analysis.Key;

/// <summary>
/// Classifies a chroma vector into a musical key using the Krumhansl–Schmuckler template
/// method: correlate the 12-bin chroma against major and minor key profiles at all 12
/// rotations and pick the strongest match. Second stage of key detection (doc 03 / doc 16).
/// </summary>
public sealed class KeyClassifier
{
    // Temperley's Kostka–Payne profiles, measured from how often each scale degree is actually USED in
    // written music. They replace Krumhansl–Kessler, whose probe-tone ratings were how listeners judged
    // a tone's fit after a cadence: those rate the tonic and dominant so far above everything else that
    // the major and minor templates barely differ outside the third, and on the flat, riff-driven chroma
    // of electronic music the major template won nearly every time. Measured over a ten-record melodic
    // house/techno set, against the corrected chroma (see ChromaExtractor, the larger half of issue #5),
    // these read 8/10 keys where Krumhansl–Kessler reads 5/10.
    private static readonly double[] MajorProfile =
        { 0.748, 0.060, 0.488, 0.082, 0.670, 0.460, 0.096, 0.715, 0.104, 0.366, 0.057, 0.400 };
    private static readonly double[] MinorProfile =
        { 0.712, 0.084, 0.474, 0.618, 0.049, 0.460, 0.105, 0.747, 0.404, 0.067, 0.133, 0.330 };

    public MusicalKey Classify(double[] chroma)
    {
        ArgumentNullException.ThrowIfNull(chroma);
        if (chroma.Length != 12)
            throw new ArgumentException("Chroma vector must have 12 elements.", nameof(chroma));

        double bestCorr = double.NegativeInfinity;
        int bestTonic = 0;
        KeyMode bestMode = KeyMode.Minor;

        // Minor is tested first so a tie is not silently awarded to the major already seated by loop
        // order — a bias pointing the same way as the mode failures this classifier was built to fix.
        for (int tonic = 0; tonic < 12; tonic++)
        {
            double minor = Correlation(chroma, MinorProfile, tonic);
            if (minor > bestCorr)
            {
                bestCorr = minor;
                bestTonic = tonic;
                bestMode = KeyMode.Minor;
            }

            double major = Correlation(chroma, MajorProfile, tonic);
            if (major > bestCorr)
            {
                bestCorr = major;
                bestTonic = tonic;
                bestMode = KeyMode.Major;
            }
        }

        // How well the chroma matches the winning template — NOT the odds the key is right. Measured over
        // a ten-record set neither this nor the margin over the runner-up separated the correct reads from
        // the wrong ones, so it ranks candidates within a track and must not be used as a downstream gate.
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
