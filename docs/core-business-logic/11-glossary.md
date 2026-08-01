# Glossary

- Last updated: 2026-08-01
- Scope analyzed: Recurring domain terms and their concrete code representations.
- Confidence note: High.

| Term | Meaning in Liveolator |
| --- | --- |
| Performance action | Serializable command shared by hardware, UI, studio, and automation |
| Dispatcher seam | Single routing boundary from intent to concern-specific handler |
| Beat clock | Source of BPM, beat/bar phase, confidence, and timeline state |
| Quantization | Scheduling an operation on a musical beat/bar boundary |
| Deck | Independent playback slot; A/B are live-facing and C/D support Studio |
| Cue | Saved or computed track position; headphone cue is a separate mixer concept |
| Sync lock | Tempo and optionally phase alignment between deck and shared clock |
| Live playlist | Per-deck editable Now/Next/Later performance queue |
| Camelot key | Harmonic-mixing notation used to compare musical-key compatibility |
| Scene | Named visual composition of layers, sources, blends, and effects |
| Bank | Collection of launchable visual scenes |
| Macro | Normalized control mapped to one or more visual parameters |
| Track visual program | Timed visual cue program associated with a music track |
| Autopilot | Rule engine that emits normal performance actions without direct engine access |
| Studio project | Timeline of deck clips, tempo, and automation for playback/rendering |
| Soft takeover | Controller behavior that prevents a physical knob from jumping a stored value |
| Add-on/extension | Validated installable package adding supported content or capabilities |
| MCP | Stdio server exposing selected music-intelligence and authoring tools to agents |

## Code References

- `src/Liveolator.Core/Actions/PerformanceAction.cs`
- `src/Liveolator.Core/Beat/BeatClockState.cs`
- `src/Liveolator.Core/Playlist/LivePlaylist.cs`
- `src/Liveolator.Core/Visuals/VisualScene.cs`
- `src/Liveolator.Core/Studio/StudioProject.cs`
- `src/Liveolator.Mcp/Program.cs`
