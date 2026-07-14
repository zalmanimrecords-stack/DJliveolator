namespace Liveolator.Core.Dsp;

/// <summary>
/// Stateful linear-interpolation resampler from a fixed source rate to a fixed target rate.
/// Pure C# and platform-agnostic so it unit-tests without hardware. Used by the frame pipeline
/// (doc 02) to normalise 44.1/48/96 kHz sources to one analysis rate before framing, so tempo
/// analysis is rate-consistent.
/// </summary>
/// <remarks>
/// The instance carries fractional read phase and the previous batch's boundary sample, so a signal
/// fed in arbitrarily-sized batches resamples identically to the same signal fed in one call. Not
/// thread-safe: drive it from a single analysis thread. When source and target rates match it is an
/// allocation-only passthrough (<see cref="IsResampling"/> is false).
/// </remarks>
public sealed class LinearResampler
{
    private readonly int _sourceRate;
    private readonly int _targetRate;
    private readonly double _step; // source samples advanced per output sample

    // Next output sample's position in source-sample units, relative to the start of the next batch.
    // A value in [-1, 0) interpolates between the previous batch's last sample and this batch's first.
    private double _pos;
    private float _prev;       // last sample of the previous batch (the anchor at index -1)
    private bool _hasPrev;

    public LinearResampler(int sourceRate, int targetRate)
    {
        if (sourceRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRate), "Sample rate must be positive.");
        if (targetRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetRate), "Sample rate must be positive.");

        _sourceRate = sourceRate;
        _targetRate = targetRate;
        _step = (double)sourceRate / targetRate;
    }

    /// <summary>The source rate in Hz this resampler was constructed for.</summary>
    public int SourceRate => _sourceRate;

    /// <summary>The target (analysis) rate in Hz this resampler converts to.</summary>
    public int TargetRate => _targetRate;

    /// <summary>False when source and target rates match (passthrough); true otherwise.</summary>
    public bool IsResampling => _sourceRate != _targetRate;

    /// <summary>
    /// Resample one batch of mono samples. Streaming-safe: phase and the boundary sample carry to
    /// the next call. Returns a fresh array (possibly empty); never throws on an empty input.
    /// </summary>
    public float[] Process(ReadOnlySpan<float> input)
    {
        if (!IsResampling)
            return input.ToArray();

        if (input.IsEmpty)
            return Array.Empty<float>();

        int n = input.Length;
        // Output samples are produced while their source position has both neighbours available,
        // i.e. floor(pos) and floor(pos)+1 are within [-1 .. n-1]. The last reachable position is
        // (n - 1): its right neighbour is index n-1 and left neighbour is n-2.
        var output = new System.Collections.Generic.List<float>(
            (int)(n / _step) + 1);

        double pos = _pos;
        while (pos <= n - 1)
        {
            int i0 = (int)Math.Floor(pos);
            double frac = pos - i0;
            float a = SampleAt(i0, input);
            // The right neighbour can be index n exactly when frac == 0 at pos == n-1; in that case
            // its weight is 0, so reuse a rather than read past the batch.
            float b = (i0 + 1 < n) ? SampleAt(i0 + 1, input) : a;
            output.Add((float)(a + (b - a) * frac));
            pos += _step;
        }

        // Advance the frame of reference to the start of the next batch.
        _pos = pos - n;
        _prev = input[n - 1];
        _hasPrev = true;

        return output.ToArray();
    }

    /// <summary>Reset carried phase/boundary state so the instance can resample a new stream.</summary>
    public void Reset()
    {
        _pos = 0.0;
        _prev = 0f;
        _hasPrev = false;
    }

    // Index -1 refers to the previous batch's last sample (0 before any audio has been seen).
    private float SampleAt(int index, ReadOnlySpan<float> input)
    {
        if (index >= 0)
            return input[index];
        return _hasPrev ? _prev : 0f;
    }
}
