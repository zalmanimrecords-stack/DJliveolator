namespace Liveolator.Core.Library.Music;

/// <summary>
/// How a catalogued audio file is used: a full <see cref="Track"/> (a song you mix) or a
/// <see cref="Sample"/> (a short one-shot / loop you trigger). Mirrors the visual library's
/// <c>VisualMediaKind</c> split, so the Libraries UI can show the two groups separately.
/// </summary>
public enum MusicMediaKind
{
    /// <summary>A full track / song.</summary>
    Track,

    /// <summary>A short sample, one-shot, or loop.</summary>
    Sample
}
