namespace Liveolator.Core.Analysis.Stems;

/// <summary>
/// The four source-separated stems Liveolator produces (doc 32 §2.3). Model-agnostic: every
/// supported separator (Open-Unmix default, Demucs opt-in) outputs exactly these four parts.
/// </summary>
public enum StemKind
{
    Drums,
    Bass,
    Vocals,
    Other,
}
