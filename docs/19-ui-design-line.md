# 19 — UI Design Line (canonical)

> **Purpose:** end the ambiguity that made the app "feel far from the mockups." There were
> three competing mockups in `design/mockups/`; this doc names the **one** canonical line and the
> rule for using the others. Every tab and control targets this. Last updated: **2026-06-06**.

## Canonical: navy "analog-rack"

The canonical look is **`design/mockups/live-mode.html`** — a dark navy analog-rack aesthetic
with gradients, soft shadows, glowing LEDs, and a **multi-signal accent palette**. This was chosen
as the product's design line (2026-06-06). It is glossy on purpose — gradients/box-shadow/glow are
**in-brand here** (unlike the spartan line, which forbade them).

### The other mockups are SUPERSEDED for palette, reused for layout
- `live-mode-clean.html`, `libraries.html`, `dj.html` — **spartan amber**. **Superseded palette.**
  Their **module maps / layouts are still the reference** for the Libraries and DJ tabs — reuse the
  structure, recolour to the navy tokens below.
- `live-mode-vintage.html` — vintage brass/cream. **Superseded** entirely (kept only as an alt idea).

Do **not** introduce amber `#d99a3c` as the primary accent again. Cue/warning amber (`#F5A623`)
exists as one signal among several (below).

## Tokens (source of truth: `src/Liveolator.App/App.axaml`)

| Token | Hex | Use |
|-------|-----|-----|
| `Bg` | `#0A0C10` | app background |
| `S1` | `#1A1F29` | module surface |
| `S2` | `#12161D` | inset / well (waveform, meters) |
| `S3` | `#232A36` | control well |
| `S4` | `#2C3442` | control hover / knob track |
| `Hair` | `#2C3442` | hairline border |
| `Text` / `Dim` / `Faint` | `#E8EDF4` / `#9AA6B8` / `#5D6B80` | text tiers |

### Multi-signal accents (use the meaning, not just the colour)
| Token | Hex | Meaning |
|-------|-----|---------|
| `Accent` (cyan) | `#2FD6E0` | **primary** — beat / active / selection |
| `Green` | `#46D369` | play / locked |
| `Amber` | `#F5A623` | cue / warning |
| `Violet` | `#9B6BFF` | visual / scenes |
| `Red` | `#FF5470` | blackout / strobe / rec |

`AccentInk` (`#06232A`) is the dark text/icon colour on a filled accent. Controls bind these tokens
(e.g. `Knob.ArcBrush` / `Fader.FillBrush` → `{StaticResource Accent}`), so retinting is one-token-deep.

## Finish rules
- **Corner radius:** rounded (panels ~8–16px, controls ~8px) — the navy line is soft, not the
  spartan 2px.
- **Gradients / shadows / glow:** allowed and encouraged (module top-down gradient, inset highlight +
  drop shadow, glowing LEDs/value dots). Keep them subtle.
- **Type:** Inter (sans) + a mono (Consolas/JetBrains/SF Mono) for numbers/IDs. Section labels are
  10–11px, uppercase, letter-spaced.
- **Accent discipline:** colour never carries state alone — pair it with text/icon (a11y).

## Verification loop (use it before claiming UI parity)
`tests/Liveolator.App.Tests/Ui/UiShots.cs` renders every shell tab headlessly to
`artifacts/ui-shots/*.png` (gitignored). Run it and compare against the mockup:

```
dotnet test tests/Liveolator.App.Tests --filter UiShots
```

Do not build UI "blind" — render, compare to `design/mockups/live-mode.html`, then iterate.

## Status vs the canonical mock (2026-06-06)
- ✅ Accent aligned to cyan + signal palette; surfaces aligned to the navy values.
- ✅ Live tab structure matches the mock (program out, beat engine, decks/mixer, scene grid).
- ⬜ Multi-signal finish: green play/locked, amber cue, violet scene pads (tokens ready; apply in views).
- ⬜ Libraries: add the Sources column (3-col) + footer status bar; real waveform render.
- ⬜ Footer status bar across Live/Libraries; DJ tab recolour pass.
