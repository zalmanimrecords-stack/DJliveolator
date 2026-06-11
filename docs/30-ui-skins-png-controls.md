# 30 — UI Skins: PNG-based knobs & faders

> Status: **design + POC**. The POC (a filmstrip-driven `SkinnableKnob` + a baked sample
> skin, rendered through the existing UI-shot harness) lands first; the Core theme-manifest
> integration (Phase 2) and faders (Phase 3) follow. Builds on the existing token theming
> (`UiThemeDefinition` / `UiThemeManager` / `UiThemeApplier`, see `docs/19`).

## Why

Today `Knob` ([Controls/Knob.cs](../src/Liveolator.App/Controls/Knob.cs)) and `Fader`
([Controls/Fader.cs](../src/Liveolator.App/Controls/Fader.cs)) are **drawn vectorially** in
`Render(DrawingContext)`. That is light and theme-tinted (the `Accent`/`S*` tokens), but the
look is hard-coded in C#: making it *photorealistic* or letting a user pick a different control
look means editing code. The owner wants (a) **fully realistic** knobs/sliders and (b) **more
flexible theming** — drop-in looks, not just recoloured vectors.

## The accepted model — filmstrips (the DJ-software standard)

Serato / Traktor / VirtualDJ skins all use the same trick, and it is what we adopt:

- **Knob = a filmstrip PNG**: one tall image holding `N` square frames stacked top→bottom,
  each frame the knob rendered at one rotation step from min→max. At render time we pick
  `frame = round(Value * (N - 1))` and blit that frame into the control bounds. Lighting and
  shadow are *baked per frame*, so the knob looks like the real rendered/photographed object at
  every angle — something a single rotated sprite cannot achieve for a knurled cap.
- **Fader = track + cap** (Phase 3): a track image (optionally 9-slice for stretch) plus a cap
  image translated along the track by `Value`.

This is pure 2D image compositing — **no GL, no native** — so it stays inside `Liveolator.App`
(module rule #4) and renders through Avalonia's `DrawingContext.DrawImage(src, destRect)`.

### Alternative considered — single rotated sprite

One cap PNG + `RotateTransform` by value. Fewer/smaller assets, but the highlight rotates with
the cap (wrong for knurled / brushed-metal caps). **Rejected as the default**; may be offered as
a cheap skin variant later.

## Architecture (fits the existing seams)

```
Liveolator.Core/Settings (PURE — no images)
  UiSkinManifest        new record: asset entries (name → relative path, kind, frameCount, orientation)
  UiThemeDefinition     gains optional `Skin` (a UiSkinManifest); null = vector look (back-compat)
  UiThemeManager        validates skin entries (path shape, frameCount ≥ 1) alongside tokens

Liveolator.App/Controls (App — owns bitmaps)
  KnobSkin              loaded skin: Bitmap strip + FrameCount + Vertical; FrameRect(value) math
  SkinnableKnob         Control; same Value/DefaultValue + drag/keys as Knob; draws a frame.
                        Skin == null  ->  falls back to the vector Knob look (nothing breaks)
  FaderSkin / SkinnableFader   (Phase 3)

Liveolator.App/Theme
  UiSkinResolver        resolves a UiSkinManifest's relative paths to loaded KnobSkin/FaderSkin
                        (avares:// for built-ins, file:// for user theme packages)
```

**Invariant preserved:** skins are *presentation only*. `SkinnableKnob.Value` still flows out
through its two-way binding, so the `PerformanceAction` seam (doc 04) is untouched — a skinned
knob and a vector knob emit identically.

### Theming flexibility

The skin rides on the same `UiThemeDefinition` a user already edits. A theme package therefore
becomes: tokens (colours/fonts/sizes) **+** an optional skin (image set). Three usage levels:

1. **Recolour only** — tokens, no skin (today's behaviour, e.g. *Brasswork*).
2. **Reskin** — supply a knob/fader filmstrip set; tokens still tint any vector fallback and the UI chrome.
3. **Full custom pack** — ship a folder (`theme.json` + PNGs) loaded as a package via
   `UiThemeManager.ReplacePackage` (same mechanism the built-ins use).

## Asset contract (filmstrip knob)

| Field        | Meaning                                                        |
|--------------|---------------------------------------------------------------|
| `path`       | relative to the manifest (`knob.png`)                         |
| `kind`       | `knobFilmstrip`                                               |
| `frameCount` | number of frames in the strip (e.g. 65)                       |
| `orientation`| `vertical` (frames stacked top→bottom) — only mode in Phase 1 |

Frame size is derived: `frameW = image.Width`, `frameH = image.Height / frameCount`. Frames must
be square-ish; the resolver logs and rejects a strip whose height isn't divisible by `frameCount`.

## POC scope (this change)

1. `KnobSkin` record + `FrameRect(value)` math — **unit-tested first** (pure, no render).
2. `SkinnableKnob` control — filmstrip draw + the existing drag/double-click-home/arrow-key
   interaction; vector fallback when `Skin` is null.
3. A baked sample strip `Assets/Skins/aurora/knob.png` — generated from the current vector knob
   so the first shipping skin is pixel-faithful to today's look (proves the pipeline; swapping in
   a photographic strip is then the only step to full realism).
4. `Assets/**` wired as `AvaloniaResource` in the App csproj.
5. UI-shot parity test renders a `SkinnableKnob` row → `artifacts/ui-shots/skinnable-knob.png`.

## Parametric skins via MCP (shipped)

Beyond pre-rendered PNG filmstrips, an external AI agent can author a **parametric** control
look — a colour palette, no image — through the MCP server, mirroring the `.frktl` preset flow
(`docs/29`). The app renders its built-in vector knob/slider with the palette, so no asset is
produced and the MCP/Core layers stay image-free.

```
Liveolator.Core/Skins (PURE)
  ControlSkinFile        record: name, kind (Knob|Slider), accent (required) + track/pointer/body (optional hex)
  ControlSkinValidator   name + known kind + #RRGGBB/#AARRGGBB colours; accent-only is valid
  ControlSkinNaming      stable slug + package id "liveolator.control-skins"

Liveolator.Media/Skins
  ControlSkinWriter      validates + writes <slug>.ctrlskin to the shared control-skins folder; lists; never clobbers w/o overwrite

Liveolator.Mcp
  ControlSkinSession     owns the folder (under the server data dir); Create/List/Spec over the writer
  ControlSkinTools       get_control_skin_spec · create_control_skin · list_control_skins  (auto-discovered)

Liveolator.App/Skins
  ControlSkinBrushes     maps a ControlSkinFile -> Avalonia brushes; ApplyTo(Knob) / ApplyTo(Fader)
```

Agent flow: `get_control_skin_spec` (format + worked example) → `create_control_skin` (validated,
written, or `error`) → `list_control_skins`. The folder is shared with the app (same pattern as
`frktl-presets`). Proven end-to-end by `artifacts/ui-shots/control-skins.png` (two authored knob
looks + a slider rendered from `ControlSkinFile` data).

## App integration — load + pick + apply (shipped, Phase 2)

What an agent authors now appears in the app without a restart:

```
Liveolator.Media/Skins/ControlSkinFolderLoader   Load() -> LoadedControlSkin[] (id + full file), tolerant
Liveolator.App/Skins/ControlSkinCatalog          IControlSkinCatalog: the skins found at startup, by id
Liveolator.App/Skins/ControlSkinApplier          writes the 7 control-brush resources from the active skins
Liveolator.App/Skins/IControlSkinApplier         seam so the Avalonia-free SettingsViewModel re-skins on Save
```

- **Resources + styles:** App.axaml defines 7 brush keys (`KnobArc/KnobTrack/KnobCap/KnobPointer`,
  `FaderFill/FaderTrack/FaderThumb`) that default to the theme tokens; `Spartan.axaml`'s Knob/Fader styles
  bind to THESE via `DynamicResource`. So overriding a key updates every control **live**. A colour the
  skin omits falls back to the themed token, so a skin is reversible (apply `null` ⇒ themed look).
- **Startup:** `App.OnFrameworkInitializationCompleted` applies the persisted skins *after* the UI theme.
- **Settings:** `ExtensionSettings.ActiveKnobSkinId` / `ActiveSliderSkinId` persist the choice; the Settings
  tab has a Knob-skin + Slider-skin picker (filtered by kind), applied live on Save via `IControlSkinApplier`.
- Proven by `artifacts/ui-shots/control-skins-applied.png` (PLAIN knobs/faders re-skinned through styles).

## Follow-ups (not in POC)

- **Phase 2:** `UiSkinManifest` in Core + validation + `UiSkinResolver`; let `UiThemeApplier`
  publish the active skin so views pick `SkinnableKnob` vs `Knob` from the theme.
- **Phase 3:** `SkinnableFader` (track + cap), crossfader, vertical/horizontal.
- **Authoring:** a `tools/` filmstrip baker (render any knob design → strip) + docs, mirroring
  the `.frktl` preset authoring flow (`docs/29`).
- Wire the active skin into `DeckView`/`MixerView`/`MacroEncodersView` once Phase 2 lands.
