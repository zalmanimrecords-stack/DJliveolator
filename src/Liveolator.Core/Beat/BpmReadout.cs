namespace Liveolator.Core.Beat;

/// <summary>
/// Stabilises a raw, jittery live tempo estimate into a steady value fit for a per-deck BPM
/// counter (doc 03). A pro readout never dances on its last digit: this gates samples on
/// confidence, applies hysteresis so sub-threshold wobble doesn't repaint, quantizes to a tenth,
/// and holds the last good value when detection goes uncertain. Pure logic — the caller owns
/// presentation (the "~estimate" marker, the "--.-" empty state) so layers stay separate.
/// </summary>
public sealed class BpmReadout
{
    private readonly double _confidenceFloor;
    private readonly double _changeThresholdBpm;
    private readonly double _quantumBpm;

    private double? _displayed;

    /// <param name="confidenceFloor">Minimum 0..1 confidence a sample needs before it can move the readout.</param>
    /// <param name="changeThresholdBpm">A confident sample must differ from the shown value by at least this (BPM) to repaint — kills last-digit flicker.</param>
    /// <param name="quantumBpm">Display resolution; the shown value is rounded to this step (a tenth by default).</param>
    public BpmReadout(
        double confidenceFloor = 0.15,
        double changeThresholdBpm = 0.3,
        double quantumBpm = 0.1)
    {
        if (confidenceFloor < 0.0 || confidenceFloor > 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidenceFloor), "Confidence floor must be in 0..1.");
        if (changeThresholdBpm < 0.0)
            throw new ArgumentOutOfRangeException(nameof(changeThresholdBpm), "Change threshold must be non-negative.");
        if (quantumBpm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(quantumBpm), "Quantum must be positive.");

        _confidenceFloor = confidenceFloor;
        _changeThresholdBpm = changeThresholdBpm;
        _quantumBpm = quantumBpm;
    }

    /// <summary>The steady value to display, or null when nothing confident has been seen yet.</summary>
    public double? DisplayedBpm => _displayed;

    /// <summary>True once a confident tempo has been adopted.</summary>
    public bool HasValue => _displayed.HasValue;

    /// <summary>
    /// Feeds one raw (bpm, confidence) estimate. Returns true only when the displayed value actually
    /// changed — so callers can repaint on change instead of every frame. Sub-floor confidence or a
    /// non-positive BPM is ignored and the held value is preserved (hold-last-good).
    /// </summary>
    public bool Update(double bpm, double confidence)
    {
        if (bpm <= 0.0 || confidence < _confidenceFloor)
            return false;

        double quantized = Quantize(bpm);

        if (_displayed is double current && Math.Abs(quantized - current) < _changeThresholdBpm)
            return false;

        if (_displayed is double shown && shown == quantized)
            return false;

        _displayed = quantized;
        return true;
    }

    /// <summary>Clears the readout — call when the deck unloads or stops being un-analyzed.</summary>
    public void Reset() => _displayed = null;

    private double Quantize(double bpm) => Math.Round(bpm / _quantumBpm) * _quantumBpm;
}
