# 26 — Visual Add-on Standard (third-party developer guide)

> **Status:** the contract is implemented and shipped. The built-in **VU meter** (a generator add-on)
> renders out of the box and is the canonical reference example below. This document is the public
> standard a third-party developer follows to build their own visual add-on.
>
> Builds on the package machinery in **`docs/21-extension-system.md`** and the compositor in
> **`docs/08-visual-performance-engine.md`** — read this for the *visual add-on* contract specifically.

## 1. What a visual add-on is

A visual add-on is a signed `.liveolator-pack` that contributes one or more **GLSL fragment shaders**
to the compositor, each described by a `VisualEffectDescriptor`. A shader plays one of two **roles**:

| Role | What it does | Reaches the screen as |
|------|--------------|-----------------------|
| **Effect** (default) | Post-processes a layer's existing texture (samples `uTexture`) — blur, hue, feedback, etc. | An `EffectRef` in a layer's effect chain |
| **Generator** | Draws a layer's pixels from uniforms alone — no input texture (a VU meter, an oscilloscope, a procedural background) | A layer whose source is `VisualSourceKind.Generator`, `Reference` = the generator's effect id |

Both roles receive the same **automatic uniforms** (beat + live audio level + resolution), so any add-on
can be **beat-synced and audio-reactive** off the one shared clock — the product's core idea (doc 00/03).

Add-ons contain **only declarative content** — shaders, JSON, themes, media. They never contain .NET
assemblies or native binaries; the host compiles and runs the shaders in its own GL context.

## 2. Package format

A `.liveolator-pack` is a ZIP archive (see doc 21 for the full validation/signing rules):

```text
my-addon.liveolator-pack
├── manifest.json          (required) — id, version, API level, publisher, content kinds, file hashes
├── signature.json         (optional) — ECDSA P-256/SHA-256 over the exact manifest bytes
├── visual-effects.json    — array of VisualEffectDescriptor (effects and/or generators)
└── shaders/
    └── *.frag             — GLSL fragment shaders referenced by the descriptors (≤ 512 KiB each)
```

### manifest.json

```json
{
  "PackageId": "com.example.meters",
  "Version": "1.0.0",
  "RequiredApiVersion": "1.0.0",
  "Publisher": "Example Audio",
  "Content": "VisualEffects",
  "Dependencies": [],
  "Files": [
    { "Path": "visual-effects.json", "Sha256": "<hex>", "Size": 412 },
    { "Path": "shaders/vu-meter.frag", "Sha256": "<hex>", "Size": 2310 }
  ]
}
```

- `PackageId` — reverse-DNS, globally unique; every effect id must be prefixed with it.
- `RequiredApiVersion` — the visual add-on API the pack targets. The host accepts a pack whose **major**
  matches the host's API major (current API: **1.0.0**); a newer major is rejected, not silently run.
- `Content` — a flags value; a visuals pack uses `VisualEffects`. (`UiTheme`/`VisualShow` are separate.)
- `Files` — every payload file with its exact byte length and SHA-256. Hash/size mismatch, undeclared
  files, traversal paths, symlinks, and shaders over 512 KiB are all rejected at install.

### Signing & trust

`signature.json` carries a publisher key id and an ECDSA P-256/SHA-256 signature over the exact manifest
bytes. Trusted public keys live in `<app-data>/Liveolator/trusted-publishers.json` (a pack cannot modify
it). **Unsigned packs install only when Developer Mode is enabled** — the path you use while developing.

## 3. Content contract — `visual-effects.json`

An array of `VisualEffectDescriptor`:

```json
[
  {
    "EffectId": "com.example.meters/vu-meter",
    "Version": "1.0.0",
    "PackageId": "com.example.meters",
    "ShaderPath": "shaders/vu-meter.frag",
    "Role": "Generator",
    "Parameters": [
      { "Id": "redline", "Uniform": "uRedline", "Min": 0.0, "Max": 1.0, "Default": 0.85 }
    ],
    "MinimumOpenGlMajor": 3,
    "MinimumOpenGlMinor": 3
  }
]
```

Rules (enforced by the loader + the isolated shader probe):

- `EffectId` is **package-qualified** (`<PackageId>/<name>`); `PackageId` must equal the manifest's.
- `Role` is `"Effect"` (default if omitted — old packs keep working) or `"Generator"`.
- `ShaderPath` stays inside the package (no traversal).
- `Parameters`: at most **64**. Each declares a logical `Id`, the GLSL `Uniform` name it drives, and a
  `[Min, Max]` range with a `Default`. **Every declared `Uniform` must exist in the compiled shader** —
  the host validates this in an isolated GL probe process before the effect is registered; a shader that
  fails to compile or is missing a declared uniform is rejected (the rest of the pack still loads).

## 4. The GLSL contract

Every shader is a `#version 330 core` fragment shader with this boilerplate:

```glsl
#version 330 core
in  vec2 vTexCoord;   // 0..1 across the quad; y runs top→bottom
out vec4 fragColor;   // PREMULTIPLIED alpha (see below)
```

### Automatic uniforms (provided by the host every frame)

Declare only the ones you use; an unused/undeclared uniform is free (GL strips it).

| Uniform | Type | Meaning | Effect | Generator |
|---------|------|---------|:------:|:---------:|
| `uTexture` | `sampler2D` | The layer's input texture (bound to unit 0) | ✅ | — (none bound) |
| `uResolution` | `vec2` | Render target size in pixels (aspect, pixel math) | ✅ | ✅ |
| `uBeatPhase` | `float` | Position within the current beat, 0..1 | ✅ | ✅ |
| `uBarPhase` | `float` | Position within the current bar, 0..1 | ✅ | ✅ |
| `uConfidence` | `float` | Beat-detection confidence, 0..1 (gate strobing on this) | ✅ | ✅ |
| `uBeatFlash` | `float` | Decaying pulse that peaks on each beat | ✅ | ✅ |
| `uRms` | `float` | Live master RMS level, 0..1 | ✅ | ✅ |
| `uPeak` | `float` | Live master peak level, 0..1 | ✅ | ✅ |
| `uLevel` | `float` | VU-ballistics level, 0..1 (fast attack / slow release — a meter's "needle") | ✅ | ✅ |

Plus every uniform your descriptor declares in `Parameters` (driven by its `Default`, or by a host
macro / Push knob mapped to it).

### Output: premultiplied alpha

The compositor blends layers with premultiplied-alpha factors. Emit color already scaled by alpha:

```glsl
fragColor = vec4(rgb * a, a);   // a = your coverage 0..1
```

A fully opaque generator (a meter that fills the frame) outputs `vec4(rgb, 1.0)`. A generator that should
let lower layers show through outputs a smaller `a` in transparent regions.

### Audio reactivity, done right

- **Smooth in the host, not the frame loop.** `uLevel` already has VU ballistics resolved in Core against
  the *audio* frame rate, so the needle's physics are independent of display FPS. Prefer `uLevel` for a
  meter needle; use `uPeak` for a fast peak indicator and `uRms` for raw energy.
- **Gate beat reactivity on `uConfidence`** so an unsure clock doesn't strobe.

## 5. How a generator reaches the screen

A scene layer references the generator by id:

```jsonc
// inside a saved scene (doc 13)
{
  "Name": "VU",
  "Source": { "Kind": "Generator", "Reference": "com.example.meters/vu-meter" },
  "Effects": [],
  "Blend": "Normal",
  "Opacity": 1.0
}
```

The compositor resolves the `Reference` against the effect registry, renders the generator into a
viewport-sized texture each frame (with all the uniforms above), then composites it with the layer's
**blend mode + opacity** — exactly like an image layer. So a generator stacks, fades, and blends with
images and other generators in the same scene.

## 6. Reference add-on — the analog VU meter

This is the built-in `liveolator.builtin/vu-meter`, shipped so a generator renders out of the box; the
same three files, zipped and signed, are a complete third-party pack.

**manifest.json**

```json
{
  "PackageId": "com.example.meters",
  "Version": "1.0.0",
  "RequiredApiVersion": "1.0.0",
  "Publisher": "Example Audio",
  "Content": "VisualEffects",
  "Dependencies": [],
  "Files": [
    { "Path": "visual-effects.json", "Sha256": "…", "Size": … },
    { "Path": "shaders/vu-meter.frag", "Sha256": "…", "Size": … }
  ]
}
```

**visual-effects.json** — see §3 above (Role `Generator`, one `redline` parameter).

**shaders/vu-meter.frag** — draws the meter purely from uniforms; the needle swings with `uLevel`, a peak
dot rides the arc from `uPeak`, and the scale turns red past `uRedline`:

```glsl
#version 330 core
in vec2 vTexCoord;
out vec4 fragColor;

uniform vec2  uResolution;
uniform float uLevel;    // smoothed VU level 0..1 (the needle)
uniform float uPeak;     // raw peak 0..1 (the peak dot)
uniform float uRedline;  // scale fraction where the arc turns red

float sdSegment(vec2 p, vec2 a, vec2 b) {
    vec2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

void main() {
    vec2 uv = vec2(vTexCoord.x, 1.0 - vTexCoord.y);          // y up
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 p = vec2((uv.x - 0.5) * aspect, uv.y);

    vec3 col = vec3(0.91, 0.87, 0.75);                       // cream meter face
    vec2  pivot  = vec2(0.0, 0.06);
    float radius = 0.62;
    float minAng = radians(140.0);                            // level 0 → up-left
    float maxAng = radians(40.0);                             // level 1 → up-right

    vec2  d = p - pivot; float r = length(d); float ang = atan(d.y, d.x);
    float t = clamp((minAng - ang) / (minAng - maxAng), 0.0, 1.0);
    if (ang <= minAng && ang >= maxAng) {                     // the scale arc
        float band = smoothstep(0.014, 0.0, abs(r - radius));
        col = mix(col, t < uRedline ? vec3(0.11) : vec3(0.78, 0.10, 0.08), band);
    }

    float needleAng = mix(minAng, maxAng, clamp(uLevel, 0.0, 1.0));   // needle tracks uLevel
    vec2  tip = pivot + vec2(cos(needleAng), sin(needleAng)) * (radius + 0.02);
    col = mix(col, vec3(0.06), smoothstep(0.013, 0.004, sdSegment(p, pivot, tip)));
    col = mix(col, vec3(0.10), smoothstep(0.05, 0.032, length(p - pivot)));            // hub

    float pkAng = mix(minAng, maxAng, clamp(uPeak, 0.0, 1.0));        // peak dot tracks uPeak
    vec2  pkPos = pivot + vec2(cos(pkAng), sin(pkAng)) * radius;
    col = mix(col, vec3(0.86, 0.10, 0.07), smoothstep(0.022, 0.0, length(p - pkPos)));

    fragColor = vec4(col, 1.0);                              // opaque meter face
}
```

The full source is `src/Liveolator.Visuals/Gl/VuMeterAddon.cs` (the host emits it to the asset cache).

## 7. Develop & test your add-on

1. Enable **Developer Mode** in Settings (lets you install unsigned packs).
2. Build the ZIP with `manifest.json` (correct hashes/sizes), `visual-effects.json`, and your shader.
3. Install it through the Extensions UI (or `IExtensionInstaller`). On install the loader runs the
   **isolated shader probe**, validates that every declared uniform exists in the compiled shader, and
   registers the effect/generator.
4. Reference your generator from a scene layer (§5) and open the visuals window — it renders live and
   reacts to the master audio.
5. For distribution, sign the manifest with your ECDSA key and register your public key with users.

> **Shader-probe helper is required to activate installed packs.** The isolated probe runs the native
> `liveolator-shader-probe` helper (a distribution artifact not stored in the repo, doc 21). When it is
> **absent, an installed pack's shaders are rejected, not silently run** — extension shaders are simply
> not activated (a deliberate safety default). The built-in VU meter is unaffected because it is registered
> in-process, bypassing the probe. So: ship/install the probe helper alongside the host to load third-party
> visual packs; without it, only built-in generators render.

## 8. Limits & current edges

- One declared parameter set per shader; ≤ 64 parameters; shader ≤ 512 KiB.
- A **generator with its own post-effect chain** currently renders that chain at a fixed size; the
  built-in VU meter (and most generators) have no chain, so this does not apply. (Tracked follow-up.)
- Audio reactivity exposes **RMS / peak / VU level** today; per-band spectrum uniforms are a planned
  addition. `Overlay` blend degrades to `Normal` (no fixed-function GL mapping yet).
- Video/camera generator sources and quantized clip launching remain deferred (doc 08).
