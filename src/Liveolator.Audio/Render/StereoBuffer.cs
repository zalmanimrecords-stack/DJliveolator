namespace Liveolator.Audio.Render;

/// <summary>
/// A decoded clip as two equal-length channel buffers (left/right) at the render rate - the unit the
/// offline stereo renderer mixes. A genuinely mono source duplicates its single channel into both
/// (<see cref="FromMono"/>) so the rest of the pipeline is uniformly stereo, matching the realtime
/// mixer's per-channel processing.
/// </summary>
internal sealed class StereoBuffer
{
    public StereoBuffer(float[] left, float[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Length != right.Length)
            throw new ArgumentException(
                $"Left ({left.Length}) and right ({right.Length}) channels must be the same length.", nameof(right));

        Left = left;
        Right = right;
    }

    public float[] Left { get; }

    public float[] Right { get; }

    /// <summary>The per-channel sample count (frames). Both channels share this length.</summary>
    public int Length => Left.Length;

    /// <summary>Duplicate a single mono buffer into both channels (shared reference; never mutated in place).</summary>
    public static StereoBuffer FromMono(float[] mono)
    {
        ArgumentNullException.ThrowIfNull(mono);
        return new StereoBuffer(mono, mono);
    }
}
