namespace Liveolator.Core.Library;

/// <summary>Progress report during a library scan (for UI progress bars / cancellation feedback).</summary>
public readonly record struct ScanProgress(int Done, int Total, string CurrentFile);
