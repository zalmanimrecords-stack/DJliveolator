# 29 — Authoring FRKTL presets (`.frktl` files) + AI prompt

> Companion to doc 28 (the controllable preset generator add-on). FRKTL presets are **self-contained
> `.frktl` files** — each carries its own GLSL shader plus up to **5 controllable parameters**. Drop them
> in the FRKTL presets folder and they appear in the preset picker, each parameter a labelled knob you can
> turn by hand or bind to a MIDI controller.

## Where presets live

```
%APPDATA%\Liveolator\frktl-presets\          (Windows)
~/Library/Application Support/Liveolator/...  (macOS)
~/.config/Liveolator/frktl-presets/          (Linux)
    aurora-veil.frktl
    tunnel-pulse.frktl
    .cache/                ← auto-generated extracted shaders (do not edit)
```

The folder is created on first launch. Each `*.frktl` file is loaded, validated, and registered under the
package `liveolator.frktl.user`. The file name becomes the id (e.g. `aurora-veil.frktl` →
`liveolator.frktl.user/aurora-veil`). Invalid files are skipped and logged (Settings → DIAGNOSTICS), never
crash the app.

## `.frktl` file format

A `.frktl` file is JSON:

```jsonc
{
  "name": "Aurora Veil",            // shown in the preset picker (required)
  "author": "Your Name",            // optional
  "description": "Soft neon veil",  // optional
  "parameters": [                    // 0..5 controllable knobs
    { "id": "glow",  "uniform": "uGlow",  "label": "GLOW",  "min": 0.0, "max": 2.0,   "default": 1.0 },
    { "id": "warp",  "uniform": "uWarp",  "label": "WARP",  "min": 0.0, "max": 1.0,   "default": 0.4 },
    { "id": "decay", "uniform": "uDecay", "label": "DECAY", "min": 0.8, "max": 0.995, "default": 0.95 }
  ],
  "shader": "#version 330 core\n...full GLSL fragment shader...\n"
}
```

Rules (the loader rejects a file that breaks any):
- `name` is non-empty.
- **At most 5** `parameters`; each has a unique `id`, a unique `uniform`, a non-empty `label`,
  `max >= min`, and `min <= default <= max`.
- `shader` is **ASCII-only** (no smart quotes, em dashes, or accented letters — even in comments; they
  break some GL drivers), contains a `void main()` and writes `fragColor`, and **declares every
  parameter's `uniform`**.

## Shader contract (what the host gives you)

The shader is a `#version 330 core` fragment shader:

```glsl
in  vec2 vTexCoord;   // 0..1 across the frame; y runs top -> bottom
out vec4 fragColor;   // write your colour here (opaque: vec4(rgb, 1.0))
```

**Automatic uniforms** — declare only the ones you use; the host sets them every frame:

| Uniform | Type | Meaning |
|---|---|---|
| `uResolution` | `vec2` | render size in pixels |
| `uTime` | `float` | seconds since start (your animation clock) |
| `uBeatPhase` | `float` | 0..1 position within the current beat |
| `uBarPhase` | `float` | 0..1 position within the current bar |
| `uConfidence` | `float` | 0..1 beat-detection confidence (gate strobes with it) |
| `uBeatFlash` | `float` | decaying pulse that peaks on each beat |
| `uLevel` | `float` | 0..1 VU-ballistics master level (smooth — prefer for motion) |
| `uRms` | `float` | 0..1 raw block RMS energy |
| `uPeak` | `float` | 0..1 raw block peak |
| `uBass` `uLowMid` `uMid` `uHigh` | `float` | 0..1 smoothed spectrum bands |
| `uPreviousFrame` | `sampler2D` | **last frame's output** — declare it to get frame-feedback (trails/warp) |

**Feedback:** if (and only if) you declare `uniform sampler2D uPreviousFrame;`, the host feeds you the
previous frame on texture unit 0. Sample it at a warped/zoomed coordinate and blend it back in to get
MilkDrop-like trails. Without it, the generator is stateless each frame.

**Your parameters** arrive as the `uniform float`s you named (e.g. `uGlow`), already mapped into your
declared `[min, max]` range.

**Output:** write opaque `fragColor = vec4(rgb, 1.0)` for a full-frame look. Keep values roughly in 0..1
(a soft limiter like `col / (1.0 + col)` avoids harsh white clipping).

---

## THE PROMPT (copy this to an AI to generate a preset)

> Paste the block below into any capable AI (Claude, GPT, …), then add one line describing the look you
> want (e.g. *"a slow rotating tunnel of teal hexagons that pulses on the bass"*). The AI returns a
> ready-to-save `.frktl` file.

```text
You are authoring a FRKTL visual preset for the Liveolator VJ engine. Output ONE JSON object (a ".frktl"
file) and nothing else — no markdown fences, no commentary.

FORMAT (exact keys):
{
  "name": string,                       // short display name
  "author": string,                     // optional
  "description": string,                // optional, one line
  "parameters": [                        // 0 to 5 items, ALL become live knobs
    { "id": string, "uniform": string, "label": string, "min": number, "max": number, "default": number }
  ],
  "shader": string                       // a complete GLSL #version 330 core fragment shader
}

HARD RULES:
- At most 5 parameters. Each id and each uniform must be unique. label is UPPERCASE and short (<= 6 chars).
  max >= min, and min <= default <= max. Parameter uniforms must start with "u" (e.g. "uGlow").
- The shader MUST be ASCII-only (no curly quotes, em dashes, accented or non-Latin characters anywhere,
  including comments). It MUST start with "#version 330 core", declare `in vec2 vTexCoord;` and
  `out vec4 fragColor;`, contain `void main()`, write `fragColor`, and declare a `uniform float` for every
  parameter you list.
- End by writing an opaque colour: fragColor = vec4(rgb, 1.0). Keep rgb roughly in 0..1; apply a soft
  limiter like `col = col / (1.0 + col);` to avoid white clipping.

HOST UNIFORMS you MAY declare and use (set automatically every frame — declare only those you use):
  uniform vec2  uResolution;   // pixels
  uniform float uTime;         // seconds
  uniform float uBeatPhase;    // 0..1 within a beat
  uniform float uBarPhase;     // 0..1 within a bar
  uniform float uConfidence;   // 0..1 beat confidence
  uniform float uBeatFlash;    // decaying pulse, peaks on the beat
  uniform float uLevel;        // 0..1 smoothed master level (best for motion)
  uniform float uRms;          // 0..1 raw energy
  uniform float uPeak;         // 0..1 raw peak
  uniform float uBass, uLowMid, uMid, uHigh; // 0..1 spectrum bands
  uniform sampler2D uPreviousFrame; // OPTIONAL: declare to get last frame for trails/warp feedback

GUIDANCE:
- vTexCoord is 0..1 (y top->bottom). For centered math use `vec2 p = vTexCoord*2.0 - 1.0;` and correct
  aspect with `p.x *= uResolution.x / max(uResolution.y, 1.0);`.
- Make it react to music: drive brightness/scale with uLevel/uBass, add motion with uTime, and pulse on
  uBeatFlash / uBeatPhase.
- For MilkDrop-style trails: declare uPreviousFrame, sample it at a slightly zoomed/rotated coordinate,
  multiply by a decay (~0.9-0.97), then add new energy on top.
- Expose 3-5 expressive knobs (e.g. GLOW brightness, SPEED, WARP/ZOOM feedback, DECAY trail length, a
  COLOR/HUE shift). Wire each declared uniform into the shader so turning it visibly changes the look.

Now produce the .frktl JSON for this look: <DESCRIBE THE LOOK HERE>
```

---

## Generating presets via MCP (for AI agents)

An agent connected to the Liveolator MCP server (`Liveolator.Mcp`, doc 17) can author presets directly —
no copy-paste. Three tools on the visual concern:

- **`get_visual_preset_spec`** — returns the `.frktl` format, the host uniform contract, the rules, the
  folder presets are written to, and a complete example. **Call this first** so the generated shader and
  parameters are valid.
- **`create_visual_preset`** — takes the whole `.frktl` document as a JSON string (`presetJson`) plus an
  optional `overwrite` flag. It validates (same rules as above) and writes `<slug>.frktl` into the presets
  folder; on failure nothing is written and the reason comes back in `error`. Returns the derived
  `presetId` (`liveolator.frktl.user/<slug>`) and the file path.
- **`list_visual_presets`** — lists the installed presets (name + id + path).

Typical agent flow: `get_visual_preset_spec` → compose the JSON for the requested look → `create_visual_preset`.
The MCP server writes into the same folder the app reads, so a created preset appears in the picker on the
next launch / reload. (The MCP server's `--data-dir` must point at the same data root as the app.)

## Worked example — `aurora-veil.frktl`

```jsonc
{
  "name": "Aurora Veil",
  "author": "Liveolator",
  "description": "Drifting neon veil with feedback trails that bloom on the bass.",
  "parameters": [
    { "id": "glow",  "uniform": "uGlow",  "label": "GLOW",  "min": 0.0, "max": 2.0,   "default": 1.0 },
    { "id": "speed", "uniform": "uSpeed", "label": "SPEED", "min": 0.0, "max": 2.0,   "default": 0.8 },
    { "id": "warp",  "uniform": "uWarp",  "label": "WARP",  "min": 0.0, "max": 1.0,   "default": 0.5 },
    { "id": "decay", "uniform": "uDecay", "label": "DECAY", "min": 0.8, "max": 0.985, "default": 0.94 }
  ],
  "shader": "#version 330 core\nin vec2 vTexCoord;\nout vec4 fragColor;\nuniform vec2 uResolution;\nuniform float uTime;\nuniform float uBeatFlash;\nuniform float uLevel;\nuniform float uBass;\nuniform float uMid;\nuniform sampler2D uPreviousFrame;\nuniform float uGlow;\nuniform float uSpeed;\nuniform float uWarp;\nuniform float uDecay;\nvoid main(){\n  vec2 c = vTexCoord - 0.5;\n  float ang = uWarp * 0.05 * sin(uTime * 0.3 + length(c) * 6.2831);\n  float cs = cos(ang), sn = sin(ang);\n  vec2 prevUv = mat2(cs,-sn,sn,cs) * c * (1.0 - 0.02) + 0.5;\n  vec3 prev = texture(uPreviousFrame, prevUv).rgb * clamp(uDecay, 0.8, 0.985);\n  vec2 p = vTexCoord * 2.0 - 1.0;\n  p.x *= uResolution.x / max(uResolution.y, 1.0);\n  float t = uTime * (0.2 + uSpeed * 0.8);\n  float r = length(p);\n  float a = atan(p.y, p.x);\n  float wave = 0.5 + 0.5 * sin(a * 6.0 + t + uMid * 3.0);\n  float band = smoothstep(0.9, 0.2, r) * wave;\n  band += uBeatFlash * smoothstep(0.06, 0.0, abs(r - (0.2 + uBass * 0.4)));\n  vec3 neon = 0.5 + 0.5 * cos(6.2831 * (vec3(0.0,0.33,0.67) + a/6.2831 + t*0.05));\n  vec3 add = neon * band * (0.3 + uGlow * 0.7) * (0.4 + uLevel * 1.3);\n  vec3 col = prev + add;\n  col = col / (1.0 + col * 0.5);\n  fragColor = vec4(col, 1.0);\n}\n"
}
```

Save it into the `frktl-presets` folder, (re)launch, open the preset picker, pick **Aurora Veil**, and the
GLOW / SPEED / WARP / DECAY knobs appear — turn them or MIDI-learn them (MAPPINGS tab → `Visuals: Aurora
Veil - GLOW`).

> Tip: editing the `shader` string by hand is fiddly because of the `\n` escaping. Authoring with the AI
> prompt above (which emits the escaped JSON for you) is the easy path; otherwise write the GLSL normally
> and JSON-encode the newlines.
