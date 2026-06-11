# 28 — Controllable Preset Generator Add-on (frame-feedback, externally controllable)

> The built-in reference preset is named **FRKTL** (not "Milkdrop").
> User-authored presets live as self-contained `.frktl` files in a folder — see **[doc 29](29-frktl-preset-authoring.md)**
> for the file format and an AI prompt that generates them.

> Status: **PLAN / TODO** (2026-06-10). Research-backed work plan. Not yet implemented.
> Builds on: doc 26 (visual add-on standard), doc 21 (extension system), doc 08 (compositor),
> doc 05 (controller mapping), doc 04 (PerformanceAction dispatcher).

## Goal

A new add-on type that works on a frame-feedback principle (full-frame procedural GLSL
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
3. **No frame-feedback in `GeneratorPass`** — it allocates a single FBO; frame-feedback trails/warp
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
- [x] Extend the package descriptor loader to also read presets:
      [`ExtensionContentLoader`](../src/Liveolator.Media/Extensions/ExtensionContentLoader.cs) now takes
      an optional `IGeneratorPresetRegistry`, loads `presets.json` after `visual-effects.json`, and
      validates each preset against its registered generator descriptor (generator exists, is
      Generator-role, every controllable id is declared; ≤5 enforced by the `GeneratorPreset` ctor at
      deserialization). An invalid preset throws `InvalidDataException`, so that pack's presets are
      skipped + logged via `onWarning` while other packs still load (doc 21 tolerance). Tests:
      [GeneratorPresetRoundTripTests.cs](../tests/Liveolator.Media.Tests/GeneratorPresetRoundTripTests.cs)
      (registers a valid preset; rejects undeclared-parameter and unknown-generator presets). Media suite 106/106.
- [ ] **Built-in reference preset(s)** — at least one in-process `FrktlPresetAddon`
      (mirrors `PsyFractalVisualizerAddon.TryRegister`) shipping a generator shader with feedback +
      a preset exposing 5 params (e.g. `GLOW`, `WARP`, `SPEED`, `ZOOM`, `DECAY`). Registered in
      `ServiceConfig.WireVisuals` after extension reload, same as the other built-ins.

## Phase 3 — Compositor: frame-feedback in `GeneratorPass` (doc 08) ✅ DONE (GL verified manually)

> `tests/Liveolator.Visuals.Tests` for the pure parts; GL itself is verified manually (no headless GL).

- [x] Added a second FBO/texture slot to [`GeneratorPass`](../src/Liveolator.Visuals/Gl/GeneratorPass.cs),
      swapped each frame (`_front`). Ping-pong is **only engaged when the shader declares `uPreviousFrame`**;
      otherwise slot 0 is used exactly like the original single-buffer path — VU meter / psy-fractal are
      byte-for-byte unaffected (backward compatible).
- [x] Binds the previous frame on texture unit 0 and sets the `uPreviousFrame` sampler when present.
- [x] Re-allocates on viewport change and clears every slot to transparent on allocation, so a feedback
      shader's first previous-frame sample is black and a resize shows no stale trails.
- [x] Premultiplied-alpha output contract kept; `uTime`/beat/`uBass..uHigh`/`uLevel` still feed the shader.
- [x] **Built-in `FrktlPresetAddon`** ([file](../src/Liveolator.Visuals/Gl/FrktlPresetAddon.cs)):
      a feedback generator (trails + swirl warp + audio/beat energy) exposing 5 controllable params
      (GLOW/WARP/SPEED/ZOOM/DECAY) as the **FRKTL** preset; ASCII-only shader. Registered into both
      registries in `ServiceConfig`. Tests: [FrktlPresetAddonTests.cs](../tests/Liveolator.Visuals.Tests/Gl/FrktlPresetAddonTests.cs)
      (generator role, 5 params, clean expansion, declares `uPreviousFrame`, ASCII-only). Visuals suite green.
- [ ] **Manual GL verification (owner):** load `liveolator.builtin.frktl/preset` onto a layer, confirm
      trails/warp react to audio + beat, and the GLOW knob changes the look live. (Tracked in Phase 7.)

## Phase 4 — Engine: load a preset (`GlVisualPerformanceEngine` / `IVisualPerformanceEngine`) ✅ DONE

- [x] Added `IVisualPerformanceEngine.LoadPreset(binding, layer, when, everyN)`. The decision (2026-06-10):
      **a preset occupies one dedicated layer** (the action's Slot); other layers are untouched.
- [x] `GlVisualPerformanceEngine.LoadPreset` installs the binding's macros into a now-mutable macro set
      (`Volatile.Read`/`Write` snapshot under a gate, mirroring the registries), seeds `_macroValues`
      with the descriptor defaults, and places the generator on the target layer via the existing
      `MutateLayer` (which marks the composition dirty, so the renderer rebuild picks up the new macros).
- [x] The new macros are visible to `EffectParameterResolver`: they target the generator by its **effect
      id** (`GeneratorPresetExpansion.Expand(preset, descriptor, layer)` overload), matching the instance
      id the renderer assigns to a generator layer ([LayeredQuadRenderer.cs:299](../src/Liveolator.Visuals/Gl/LayeredQuadRenderer.cs)) —
      **so no renderer or scene-model change was needed.**
- **Validated (off-GPU):** `GlVisualPerformanceEnginePresetTests` — macros installed, generator placed,
      brightness kept, negative layer ignored. Visuals suite 100/100. (GL render verified manually — Phase 7.)

## Phase 5 — Action + controller wiring (doc 04 / doc 05) ✅ DONE (MIDI-learn UI pending)

- [x] **`VisualLoadPreset` `PerformanceActionKind`** appended (serialized order preserved); handled by
      `VisualActionHandler`, which resolves the preset via `IGeneratorPresetRegistry` + descriptor via
      `IVisualEffectRegistry`, expands, and calls `engine.LoadPreset`. Missing/unknown/unwired inputs log
      + no-op (never throw). Wired in DI (`ServiceConfig`): the preset registry is created, fed to
      `ExtensionContentLoader`, registered as `IGeneratorPresetRegistry`, and passed to the handler.
  - [x] Tests: `VisualLoadPresetActionTests` (Core) — expands + drives engine with the controllable
        macros; unknown/blank/unwired → no engine call. Handler now owns 13 kinds.
- [x] Controllable params reuse the **existing** `VisualSetMacro` action (preset macro names as `Argument`).
- [x] **MIDI learn** — the MAPPINGS tab now lists one learn target per controllable preset parameter
      (`Visuals: <preset> - <LABEL>`), carrying the namespaced macro name as `Argument`. `MappingsViewModel`
      takes `IGeneratorPresetRegistry` (auto-injected by DI) and passes the target's `Argument` to
      `IMidiControlSession.BeginLearn` (which already threaded it through to the `ControllerBinding`).
      Tests: [MappingsViewModelPresetTargetTests.cs](../tests/Liveolator.App.Tests/Mappings/MappingsViewModelPresetTargetTests.cs).
      (The global "click a control while in learn mode" path already covered the live preset knobs too.)

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

## Phase 6 — UI: dynamic per-preset knobs (Avalonia, `Liveolator.App`) ✅ DONE

- [x] [`PresetControlsViewModel`](../src/Liveolator.App/Features/Live/Modules/PresetControlsViewModel.cs) +
      [`PresetOptionViewModel`](../src/Liveolator.App/Features/Live/Modules/PresetOptionViewModel.cs): for the
      active preset it builds ≤5 `ContinuousControlViewModel`s labelled from `ControllableParameter.Label`,
      each emitting `VisualSetMacro` with the namespaced macro name — data-driven (count + labels from the
      preset, modelled on `MacroEncodersViewModel`). Knobs seed to the descriptor defaults via
      `GeneratorPresetExpansion`.
- [x] Subscribes to `VisualSetMacro` feedback and calls `SetFromFeedback` (no re-emit loop).
- [x] Preset picker on the LIVE tab's Visual Control surface (no separate VJ tab exists yet; visuals live
      in `VisualControlViewModel`/`VisualControlView`). Selecting a preset dispatches `VisualLoadPreset`
      onto the base layer (slot 0). Wired through `VisualControlViewModel` → `LiveViewModel` → `ServiceConfig` DI.
- [x] Empty/disabled states: surface hidden when unwired (`IsEnabled`); 0 controllable ⇒ no knobs; never >5.
- [x] AXAML: [VisualControlView.axaml](../src/Liveolator.App/Features/Live/Modules/VisualControlView.axaml)
      PRESET section (ComboBox + horizontal knob row, mirroring the OPACITY knob; tokenised styles).
- **Validated:** [PresetControlsViewModelTests.cs](../tests/Liveolator.App.Tests/Live/Modules/PresetControlsViewModelTests.cs)
      6/6 green (list, load→dispatch+knobs, knob→VisualSetMacro, select→load, unwired no-op, feedback sync).
      (Pre-existing unrelated `Libraries*` test failures on this branch are not touched by this work.)
- [x] MIDI-learn affordance to bind a hardware knob to a preset macro name — done via MAPPINGS-tab targets (see Phase 5).

## Phase 7 — Validate & document

- [ ] `dotnet build` + `dotnet test` green (Core + Visuals test projects).
- [ ] Manual GL verification steps (append to `Liveolator.Visuals/CLAUDE.md` checklist): load the
      built-in FRKTL preset, confirm trails/warp react to audio + beat, turn the GLOW knob
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
