using Liveolator.Core.Actions;

namespace Liveolator.App.Composition;

/// <summary>
/// Stems are shelved until the rest of the app is stable (owner decision, 2026-09-24). Unless
/// <c>LIVEOLATOR_STEMS=1</c> is set, every stem surface is hidden from the UI and no deck loads as a
/// stem deck, whatever the persisted <c>AppSettings.Audio.StemsEnabled</c> says. The stem code and that
/// setting are kept, so bringing the feature back is a flag flip rather than a rebuild.
/// </summary>
public static class StemsFeature
{
    public const string EnvironmentVariable = "LIVEOLATOR_STEMS";

    /// <summary>Read once at startup; XAML binds to it through <c>x:Static</c>.</summary>
    public static bool IsEnabled { get; } = IsOn(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static bool IsOn(string? environmentValue) => environmentValue == "1";

    public static bool IsStemAction(PerformanceActionKind kind) =>
        kind is PerformanceActionKind.DeckStemMute or PerformanceActionKind.DeckStemGain;
}
