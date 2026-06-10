# 28 — Controllable Preset Generator Add-on (MilkDrop-style, externally controllable)

> Status: **PLAN / TODO** (2026-06-10). Research-backed work plan. Not yet implemented.
> Builds on: doc 26 (visual add-on standard), doc 21 (extension system), doc 08 (compositor),
> doc 05 (controller mapping), doc 04 (PerformanceAction dispatcher).

## Goal

A new add-on type that works on a MilkDrop-like principle (full-frame procedural GLSL
generators with frame-feedback for trails/warp), **with one key difference**: each **preset**
declares **up to 5 controllable parameters** that are exposed as labelled knobs in the UI and
can be driven by an external MIDI controller (e.g. a knob mapped to `GLOW`). Which parameters
are controllable is defined **per preset**.

This stays inside the settled architecture: it is a `VisualEffectRole.Generator` add-on
(doc 26), driven only through `PerformanceAction` (`VisualSetMacro`), never by direct calls.
We do **not** re-introduce projectM/MilkDrop binaries — this is our own GLSL compositor
(`Liveolator.Visuals/CLAUDE.md`, iron rule 1).

## What already exists (reuse — do NOT rebuild)

- **Generator add-on pattern** — `VisualEffectRole.Generator`, `GeneratorPass` (viewport FBO,
  re-rendered each frame), reference impls `VuMeterAddon` + `PsyFractalVisualizerAddon`
  (`src/Liveolator.Visuals/Gl/`).
- **Up to 64 shader parameters per effect** — `VisualEffectParameter(Id, Uniform, Min, Max, Default)`
  on `VisualEffectDescriptor` (`src/Liveolator.Core/Visuals/VisualEffectDescriptor.cs`).
- **Controllable-parameter path is ALREADY wired for generators.** `LayeredQuadRenderer.ResolveGeneratorParameters`
  ([LayeredQuadRenderer.cs:330](../src/Liveolator.Visuals/Gl/LayeredQuadRenderer.cs)) routes the
  generator's params through `EffectParameterResolver.Resolve` using the generator's `EffectRef.InstanceId`.
  So a `VisualMacro` whose `MacroTarget = { Layer, EffectInstanceId = <generator instance>, Parameter = "glow" }`
  already maps a normalized 0..1 macro value onto the generator's `uGlow`. **No new resolver plumbing needed.**
- **Macro → uniform** — `VisualMacro.Resolve(0..1) → [Min,Max]`, `MacroTarget(Layer, EffectInstanceId, Parameter)`
  (`src/Liveolator.Core/Visuals/`).
- **Control path** — external MIDI: `ControllerBinding{ Action = VisualSetMacro, Argument = <macroName> }`
  → `ControllerMapper` → `PerformanceActionDispatcher` → `VisualActionHandler.SetMacro` →
  `GlVisualPerformanceEngine.SetMacro(name, value)` → `_macroValues[name]` (thread-safe) → read each frame.
- **UI knob** — `Knob` control + `ContinuousControlViewModel` (emits `VisualSetMacro`, has
  `SetFromFeedback` to avoid feedback loops). `MacroEncodersViewModel` is the current (hardcoded, 8-slot) host.
- **Persistence** — `VisualScene.MacroValues` (saved per scene), `VisualMacrosSnapshot` →
  `live/macros.json`, `ILiveProfileStore`.

## Gaps to build

1. **No "preset" concept** that bundles a generator + a declaration of which ≤5 of its parameters
   are the *controllable* ones (with display labels). Macros today are global and the UI encoders
   are hardcoded — nothing ties "this preset exposes GLOW/SPEED/WARP" together.
2. **No dynamic UI** that renders the *active preset's* ≤5 labelled knobs (the 8 encoders are fixed).
3. **No frame-feedback in `GeneratorPass`** — it allocates a single FBO; MilkDrop-style trails/warp
   need a previous-frame texture (double-buffer / ping-pong + a `uPreviousFrame` sampler).
4. **No ≤5 validation** for "controllable" params (descriptor allows ≤64 total).
5. **MIDI-learn convenience** to bind a hardware knob to one of the active preset's 5 params.

---

## Phase 0 — Decide & spike (no production code)

- [x] **Decision: preset = data, not a second add-on type.** A *preset* is a
      `VisualEffectRole.Generator` descriptor (the shader) **plus** a list of `ControllableParameter`
      (≤5) selected from the descriptor's `Parameters`. The add-on package ships the generator
      shader(s) + one or more presets.
- [x] **Decision: where the ≤5 controllable set lives.** Chosen (b): a separate `presets.json` in the
      package, so one generator can back several presets with different exposed knobs.
- [x] **Decision: macro-name scheme** = `<presetId>.<paramId>` (collision-safe across presets).
- [x] **Decision: `VisualLoadPreset` is a distinct action** (Phase 5), not folded into scene loading.
- [ ] **Spike: frame-feedback** — throwaway branch proving a `uPreviousFrame` sampler + ping-pong in
      `GeneratorPass` produces trails on Intel + the dev GPU (watch the ASCII-only / "premature EOF"
      shader trap — `ShaderText.Sanitize` + ASCII test guard, per memory `visuals-fail-silently`).
      *(Deferred into Phase 3.)*

## Phase 1 — Core model (pure C#, TDD in `tests/Liveolator.Core.Tests`) ✅ DONE

- [x] **`ControllableParameter`** record — `Id`, `Label` (UI caption, e.g. "GLOW"), references an
      existing `VisualEffectParameter.Id` on the generator descriptor. File:
      [`ControllableParameter.cs`](../src/Liveolator.Core/Visuals/ControllableParameter.cs).
- [x] **`GeneratorPreset`** record — `PresetId`, `Name`, `GeneratorEffectId`, `GeneratorVersion`,
      `IReadOnlyList<ControllableParameter> Controllable` (validates Count ≤ 5 via
      `MaxControllableParameters`, unique ids, non-blank fields). File:
      [`GeneratorPreset.cs`](../src/Liveolator.Core/Visuals/GeneratorPreset.cs).
  - [x] Tests: rejects >5; rejects duplicate ids; rejects blank fields; accepts 0..5; version defaults.
        ([`GeneratorPresetTests.cs`](../tests/Liveolator.Core.Tests/Visuals/GeneratorPresetTests.cs))
- [x] **`GeneratorPresetExpansion`** (pure) + `GeneratorPresetBinding` — given a `GeneratorPreset` +
      generator descriptor + layer index + deterministic `InstanceId`, produces the generator
      `EffectRef` (defaults for all params) and the ≤5 `VisualMacro`s targeting the generator instance,
      plus normalized initial values. Cross-validates ids against the descriptor and the Generator
      role. Files: [`GeneratorPresetExpansion.cs`](../src/Liveolator.Core/Visuals/GeneratorPresetExpansion.cs),
      [`GeneratorPresetBinding.cs`](../src/Liveolator.Core/Visuals/GeneratorPresetBinding.cs).
  - [x] Tests: macro count == controllable count; targets point at the generator instance; ranges match
        the descriptor; namespaced macro names; normalized defaults; throws on unknown id / id mismatch /
        non-generator role. ([`GeneratorPresetExpansionTests.cs`](../tests/Liveolator.Core.Tests/Visuals/GeneratorPresetExpansionTests.cs))
  - **Validated:** `dotnet test` → 805/805 Core tests green (17 new), no regressions.
- [x] **Persistence DTO + version + JSON contract** — added `GeneratorPresetsSnapshot(Version, Presets)`
      ([LiveProfileSnapshots.cs](../src/Liveolator.Media/LiveProfileSnapshots.cs)); the `presets.json`
      shape is pinned by round-trip + hand-authored-JSON + >5-rejection tests
      ([GeneratorPresetSerializationTests.cs](../tests/Liveolator.Core.Tests/Visuals/GeneratorPresetSerializationTests.cs)),
      using the same `JsonSerializerOptions` as `ExtensionContentLoader`. The `ILiveProfileStore`
      load/save methods are deferred to Phase 4 (when scene/engine integration needs the active-preset
      selection persisted — current macro values already ride on `VisualScene.MacroValues`).

## Phase 2 — Add-on packaging & registration (doc 26 / doc 21)

- [x] **Preset registry seam** — `IGeneratorPresetRegistry` + thread-safe `GeneratorPresetRegistry`
      (Core), mirroring `IVisualEffectRegistry`: `Presets`, `TryGet(presetId, out preset)`, atomic
      `ReplacePackage` (pre-validates the combined set so a duplicate-id replace leaves state intact),
      `RemovePackage`; preset ids unique across packages. Files:
      [IGeneratorPresetRegistry.cs](../src/Liveolator.Core/Visuals/IGeneratorPresetRegistry.cs),
      [GeneratorPresetRegistry.cs](../src/Liveolator.Core/Visuals/GeneratorPresetRegistry.cs). Tests:
      [GeneratorPresetRegistryTests.cs](../tests/Liveolator.Core.Tests/Visuals/GeneratorPresetRegistryTests.cs).
      Still **TODO**: register in DI in `ServiceConfig` alongside `IVisualEffectRegistry` (with Phase 4).
- [ ] Extend the package descriptor loader to also read presets: `ExtensionContentLoader`
      (`src/Liveolator.Media/Extensions/ExtensionContentLoader.cs`) loads `presets.json` next to
      `visual-effects.json`, validates each preset against its generator descriptor (≤5, ids exist),
      and registers them via `IGeneratorPresetRegistry`. Reject (don't silently skip) invalid presets
      — surface via `onWarning`.
- [ ] **Built-in reference preset(s)** — at least one in-process `MilkdropStarterPresetAddon`
      (mirrors `PsyFractalVisualizerAddon.TryRegister`) shipping a generator shader with feedback +
      a preset exposing 5 params (e.g. `GLOW`, `WARP`, `SPEED`, `ZOOM`, `DECAY`). Registered in
      `ServiceConfig.WireVisuals` after extension reload, same as the other built-ins.

## Phase 3 — Compositor: frame-feedback in `GeneratorPass` (doc 08)

> `tests/Liveolator.Visuals.Tests` for the pure parts; GL itself is verified manually (no headless GL).

- [ ] Add an optional second FBO/texture pair to `GeneratorPass`
      (`src/Liveolator.Visuals/Gl/GeneratorPass.cs`), swapped each frame (ping-pong like
      `EffectChainRenderer`'s `_textures[2]`).
- [ ] Bind previous frame to texture unit 1; cache + set `uPreviousFrame` sampler when the shader
      declares it. Generators that don't declare it are unaffected (backward compatible).
- [ ] Re-allocate/clear both targets on viewport change; clear previous to transparent on first frame
      and on scene switch (no stale trails from a different preset).
- [ ] Keep the premultiplied-alpha output contract (doc 26).
- [ ] Confirm `uTime`/beat/`uBass..uHigh`/`uLevel` uniforms still feed the feedback shader (they
      already flow through `GeneratorPass`).

## Phase 4 — Engine: load a preset (`GlVisualPerformanceEngine` / `IVisualPerformanceEngine`)

- [ ] Add a way to make the active scene's generator layer use a preset: a `VisualLoadScene`-style
      path (or a new `VisualLoadPreset` action — see Phase 5) that calls `GeneratorPresetExpansion`,
      installs the ≤5 macros into the engine's macro set, seeds `_macroValues` with the defaults, and
      sets the generator layer's `EffectRef`/`GeneratorRef`. Reuse the existing
      `LoadScene → MacroValues` seeding pattern.
- [ ] Ensure the new macros are visible to `EffectParameterResolver` (they must be in the engine's
      `_macros` list, which `ResolveGeneratorParameters` already consults).

## Phase 5 — Action + controller wiring (doc 04 / doc 05)

- [ ] **`VisualLoadPreset` `PerformanceActionKind`** (append to the enum end — the comment in
      `PerformanceActionKind.cs` warns that order is serialized, so **append only**), `Argument` =
      `presetId`. Handle in `VisualActionHandler`.
  - [ ] Tests in `tests/Liveolator.Core.Tests`: dispatch routes to handler; missing/unknown preset
        id logs + no-ops (no throw).
- [ ] Controllable params reuse the **existing** `VisualSetMacro` action — the preset's derived macro
      names are the `Argument`. No new per-param action kind.
- [ ] **MIDI learn** — extend the mapping UI/`ControllerBinding` so a learned knob can target one of
      the active preset's 5 macro names (don't hardcode CC numbers — capture via learn, per project
      decision). The plumbing already accepts `Argument = macroName`.

## Phase 6 — UI: dynamic per-preset knobs (Avalonia, `Liveolator.App`)

- [ ] New `PresetControlsViewModel` (`src/Liveolator.App/Features/Live/Modules/`) that, for the active
      preset, builds ≤5 `ContinuousControlViewModel`s labelled from `ControllableParameter.Label`,
      each emitting `VisualSetMacro` with the derived macro name. Model on `MacroEncodersViewModel` but
      **data-driven** (count + labels from the preset, not the hardcoded `Specs` array).
- [ ] Subscribe to action feedback (`ActionFeedbackState`) and call `SetFromFeedback` so MIDI/preset
      loads move the on-screen knobs without re-emitting (loop guard already exists).
- [ ] A preset picker on the VJ/LIVE tab (the VJ tab currently has the weakest UI — see memory
      `open-questions-and-known-gaps`); selecting a preset dispatches `VisualLoadPreset`.
- [ ] Empty/extra states: 0 controllable params ⇒ no knobs; never render more than 5.
- [ ] Accessibility (global standard 25): each knob labelled, keyboard-focusable.

## Phase 7 — Validate & document

- [ ] `dotnet build` + `dotnet test` green (Core + Visuals test projects).
- [ ] Manual GL verification steps (append to `Liveolator.Visuals/CLAUDE.md` checklist): load the
      built-in MilkDrop starter preset, confirm trails/warp react to audio + beat, turn the GLOW knob
      (UI) and a learned MIDI knob → `uGlow` changes live; switch preset → knob set + labels change.
- [ ] Update docs: this file → mark sections done; add a short "controllable preset" section to
      doc 26; note the new action kind in doc 04's catalogue.
- [ ] `graphify update .` after code lands (project rule).

## Open decisions (resolve in Phase 0)

- Macro-name scheme for derived controllable macros (`<presetId>.<paramId>` recommended for
  collision-safety vs. the current short global names like `glow`/`speed`).
- Presets in their own `presets.json` (recommended) vs. inline on the descriptor.
- Whether `VisualLoadPreset` is a distinct action or folded into scene loading.

## Risks / watch-list

- **Silent shader failures** — non-ASCII in GLSL ⇒ "premature EOF" on Intel; keep `ShaderText.Sanitize`
  + ASCII test guard. App logs to `%APPDATA%\Liveolator\logs\liveolator.log` — read first when debugging
  (memory `visuals-fail-silently`).
- **No headless GL** — feedback buffer can only be verified on a display; keep all *logic* (expansion,
  validation, resolver) pure and unit-tested off the GPU.
- **Serialized enum order** — append new `PerformanceActionKind` at the end only.
- **Backward compatibility** — generators without `uPreviousFrame` and profiles without presets must
  keep working unchanged.
