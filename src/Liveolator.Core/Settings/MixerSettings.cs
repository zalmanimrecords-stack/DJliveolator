using Liveolator.Core.Mixer;

namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted mixer preferences: MIX SEC, the AUTO crossfade time, so the DJ's chosen fade length survives a
/// restart. Pure data — persisted via <c>ISettingsStore</c>, clamped by <see cref="Normalized"/>.
/// </summary>
/// <param name="AutoCrossfadeSeconds">AUTO crossfade time in whole seconds, 0..20.</param>
public sealed record MixerSettings(double AutoCrossfadeSeconds = AutoCrossfadeRamp.DefaultSeconds)
{
    /// <summary>The default mixer preferences.</summary>
    public static MixerSettings Default { get; } = new();

    /// <summary>Returns a copy with MIX SEC clamped to whole seconds in its supported range.</summary>
    public MixerSettings Normalized()
        => this with { AutoCrossfadeSeconds = AutoCrossfadeRamp.ClampSeconds(AutoCrossfadeSeconds) };
}
