using Liveolator.Core.Beat;
using Liveolator.Core.Visuals;

namespace Liveolator.Core.Tests.Visuals;

/// <summary>
/// A pure stand-in for the GPU compositor: records every <see cref="IVisualPerformanceEngine"/> call
/// so the <c>VisualActionHandler</c> can be unit-tested off the GL engine (Core stays pure — no GL).
/// </summary>
internal sealed class FakeVisualPerformanceEngine : IVisualPerformanceEngine
{
    public FakeVisualPerformanceEngine(VisualBank bank) => ActiveBank = bank;

    public VisualBank ActiveBank { get; }

    public List<int> SelectedBanks { get; } = new();
    public List<(VisualScene Scene, Quantize When, int EveryN)> LoadedScenes { get; } = new();
    public List<(string Name, double Value)> Macros { get; } = new();
    public List<(int Layer, VisualSourceRef Source, Quantize When, int EveryN)> LayerSources { get; } = new();
    public List<int> ToggledLayers { get; } = new();
    public List<(int Layer, double Opacity)> Opacities { get; } = new();
    public List<(int Layer, string ClipId, Quantize When, int EveryN)> LaunchedClips { get; } = new();
    public List<bool> BlackoutCalls { get; } = new();
    public List<bool> StrobeCalls { get; } = new();
    public List<(TransitionStyle Style, Quantize When, int EveryN)> Transitions { get; } = new();

    public void SelectBank(int index) => SelectedBanks.Add(index);

    public void LoadScene(VisualScene scene, Quantize when, int everyN = 1)
        => LoadedScenes.Add((scene, when, everyN));

    public void SetMacro(string name, double value) => Macros.Add((name, value));

    public void SetLayerSource(int layer, VisualSourceRef source, Quantize when, int everyN = 1)
        => LayerSources.Add((layer, source, when, everyN));

    public void ToggleLayer(int layer) => ToggledLayers.Add(layer);

    public void SetLayerOpacity(int layer, double opacity) => Opacities.Add((layer, opacity));

    public void LaunchClip(int layer, string clipId, Quantize when, int everyN = 1)
        => LaunchedClips.Add((layer, clipId, when, everyN));

    public void Blackout(bool on) => BlackoutCalls.Add(on);

    public void Strobe(bool on) => StrobeCalls.Add(on);

    public void Transition(TransitionStyle style, Quantize when, int everyN = 1)
        => Transitions.Add((style, when, everyN));
}
