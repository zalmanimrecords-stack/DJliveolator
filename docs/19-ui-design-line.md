# 19 — UI Design Line (canonical)

> **Purpose:** name the **one** canonical visual line so the app stops "feeling far from the
> reference," and give the rule for every tab and control. Every surface targets this.
> Last updated: **2026-06-06**.

## Canonical: single-accent **blue** performance line

The canonical look is a **deep-navy, skeuomorphic DJ line with ONE blue accent** — rotary knobs
with a glowing blue value arc, linear faders with a dB scale, rounded panels with a subtle top-down
gradient. The reference is the **user-supplied DJ deck image** (Deck A · Mixer · Deck B with blue
ring knobs, channel faders + crossfader). This was chosen as the product's design line and supersedes
the earlier cyan multi-accent proposal (2026-06-06).

**One accent, one reserved.** A single blue carries *all* active/signal state (playing, locked,
selected pad, encoder arc, playhead, value dot). One reserved **red** is for destructive only
(blackout / strobe / rec). Never color-only — pair accent with text/icon/position (a11y).

### The other mockups: layout reference only
- `design/mockups/live-mode-clean.html`, `libraries.html`, `dj.html`, `live-mode.html`,
  `live-mode-vintage.html` — **palette superseded**. Their **module maps / layouts** are still useful
  as structural reference; recolour to the blue tokens below.
- Do **not** reintroduce amber `#d99a3c` or cyan `#2FD6E0` as the primary accent. The accent is blue.

## Tokens (source of truth: `src/Liveolator.App/App.axaml`)

| Token | Hex | Use |
|-------|-----|-----|
| `Bg` | `#0A0D13` | app background |
| `S1` | `#141A26` | module surface |
| `S2` | `#0C1017` | inset / well (waveform, meters) |
| `S3` | `#1A2130` | control well |
| `S4` | `#26303F` | control hover / knob + fader track |
| `Hair` | `#232B38` | hairline border |
| `Text` / `Dim` / `Faint` | `#E7ECF3` / `#8B95A7` / `#5A6573` | text tiers |

### Accents
| Token | Hex | Meaning |
|-------|-----|---------|
| `Accent` (blue) | `#2F80F6` | **the one accent** — active / signal / selection / value |
| `AccentInk` | `#FFFFFF` | text/icon on a filled accent |
| `Red` | `#E5544A` | **the one reserved** — blackout / strobe / rec |
| `Green` / `Amber` / `Violet` | `#2F80F6` | **unified to the blue accent** (kept only so older views resolve; do not rely on them as distinct hues) |

Controls bind these tokens (e.g. `Knob.ArcBrush` / `Fader.FillBrush` → `{StaticResource Accent}`), so
retinting is one-token-deep. **Fluent's `SystemAccentColor*` is also retinted blue** in `App.axaml` so
standard controls (ComboBox, ProgressBar, selection, scrollbar) follow the line automatically.

## Components & finish rules
- **Continuous controls are rotary `Knob`s** (`Controls/Knob.cs`) — never bare sliders. A 270° track
  arc, a blue value arc with a soft glow + value dot, a pointer tick, and a radial-gradient body.
  Drag vertically (or arrow keys) to change; disabled = neutral gray.
- **Levels / crossfade are `Fader`s** (`Controls/Fader.cs`) — track with dB tick marks, blue fill, a
  wide cap with a blue centre line. Vertical for channels, horizontal for the crossfader.
- **Corner radius:** rounded — panels ~16px, buttons/controls ~8px, pads ~6px.
- **Gradients / glow:** subtle and in-brand — module top-down panel gradient, knob-body radial
  gradient, glowing value arc/dot. Keep restrained; no heavy drop shadows.
- **Type:** Inter (sans) + a mono (Consolas/JetBrains/SF Mono) for numbers/IDs/values. Section labels
  are 10–11px, uppercase, letter-spaced. Tab labels uppercase.

## Verification loop (use it before claiming UI parity)
`tests/Liveolator.App.Tests/Ui/UiShots.cs` renders every shell tab headlessly to
`artifacts/ui-shots/*.png` (gitignored). Render, **compare to the blue DJ reference**, then iterate —
do not build UI "blind".

```
dotnet test tests/Liveolator.App.Tests --filter UiShots
```

## Status vs the canonical line (2026-06-06)
- ✅ Single blue accent across the app; surfaces on the navy values; Fluent retinted blue.
- ✅ DJ tab matches the reference: Deck A · Mixer · Deck B, ring knobs (HI/MID/LOW/FLT/PITCH),
  channel faders + dB scale, crossfader, blue badge, waveform strip, blue play triangle.
- ✅ Decks/mixer shared into the Live tab; Push encoders + Master/Swing are knobs; phase bars themed.
- ✅ Settings on the line (padded panels, accent Save).
- ⬜ Real waveform render from decoded peaks (placeholder strip in place).
- ⬜ Footer status bar across Live/Libraries.
