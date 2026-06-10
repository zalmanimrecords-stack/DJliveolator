using Liveolator.Core.Beat;

namespace Liveolator.Core.Visuals;

/// <summary>
/// High-level, beat-locked control of the visuals, sitting above the GPU compositor. It receives
/// only <c>PerformanceAction</c>s (doc 04), translates them into compositor calls (layer source
/// swaps, effect-parameter writes, blend/opacity), and defers quantized actions through the shared
/// beat clock (doc 03/08). The concrete implementation lives in the compositor binding
/// (<c>Liveolator.Visuals</c>); this seam keeps the action layer and tests off the GPU.
/// </summary>
public interface IVisualPerformanceEngine
{
    /// <summary>The bank currently addressable by pads / the Scene Grid.</summary>
    VisualBank ActiveBank { get; }

    /// <summary>
    /// The names of all banks addressable by <see cref="SelectBank"/>, in selection-index order, so a
    /// Scene Grid / Push surface can label its bank tabs from real data (doc 22 C3).
    /// </summary>
    IReadOnlyList<string> BankNames { get; }

    /// <summary>Selects the active bank by index; an out-of-range index is ignored.</summary>
    void SelectBank(int index);

    /// <summary>Loads a scene's full layer stack, applied atomically at the resolved quantum.</summary>
    void LoadScene(VisualScene scene, Quantize when, int everyN = 1);

    /// <summary>
    /// Loads a controllable generator preset (doc 28) onto a single layer: places the generator as the
    /// layer's source, installs the preset's ≤5 controllable macros, and seeds their values to the
    /// descriptor defaults. Other layers are left untouched. The installed macros are then driven by
    /// <see cref="SetMacro"/> (UI knobs / external controllers).
    /// </summary>
    void LoadPreset(GeneratorPresetBinding binding, int layer, Quantize when, int everyN = 1);

    /// <summary>Sets a macro from a normalized 0..1 value.</summary>
    void SetMacro(string name, double value);

    /// <summary>Swaps a layer's texture source at the resolved quantum.</summary>
    void SetLayerSource(int layer, VisualSourceRef source, Quantize when, int everyN = 1);

    /// <summary>Toggles a layer on/off.</summary>
    void ToggleLayer(int layer);

    /// <summary>Sets a layer's opacity (0..1).</summary>
    void SetLayerOpacity(int layer, double opacity);

    /// <summary>Launches a video clip on a layer at the resolved quantum.</summary>
    void LaunchClip(int layer, string clipId, Quantize when, int everyN = 1);

    /// <summary>Forces output to black (panic / blackout).</summary>
    void Blackout(bool on);

    /// <summary>Toggles a strobe overlay.</summary>
    void Strobe(bool on);

    /// <summary>Runs a transition at the resolved quantum.</summary>
    void Transition(TransitionStyle style, Quantize when, int everyN = 1);
}
