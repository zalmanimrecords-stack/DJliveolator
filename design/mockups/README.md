# Design mockups

HTML/CSS design prototypes (no logic). These predate the Avalonia app and explore several looks.

## Canonical line → `live-mode.html`

**`live-mode.html`** (navy "analog-rack") is the **canonical design line**. The app's tokens and
controls target it. See [`docs/19-ui-design-line.md`](../../docs/19-ui-design-line.md) for the token
palette, signal semantics, and finish rules.

| File | Status |
|------|--------|
| `live-mode.html` | ✅ **Canonical** — navy, cyan primary accent, multi-signal palette, glossy |
| `libraries.html` | ⚠️ Layout reference for the Libraries tab; **palette superseded** (recolour to navy tokens) |
| `dj.html` | ⚠️ Layout reference for the DJ tab; **palette superseded** (recolour to navy tokens) |
| `live-mode-clean.html` | ⛔ Superseded — spartan amber line (kept for history) |
| `live-mode-vintage.html` | ⛔ Superseded — vintage brass/cream alternative (kept for history) |

**Do not** reintroduce the spartan amber `#d99a3c` as the primary accent. Reuse the *layouts* of the
superseded mockups, but apply the navy tokens from `docs/19`.

To compare the running app against the canonical mock, render every tab with the headless harness:

```
dotnet test tests/Liveolator.App.Tests --filter UiShots   # writes artifacts/ui-shots/*.png
```
