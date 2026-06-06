# Liveolator.Midi — module rules

**Purpose:** the MIDI binding — implements the Core mapping seams
(`IMidiInput` / `IMidiOutput` / `IMidiDeviceProvider`, doc 05) over a cross-platform
MIDI library, translating to/from the library-agnostic `MidiMessage`. No mapping
logic, profiles, or engine knowledge lives here — that is all in `Liveolator.Core`.

**Design source of truth:** [`docs/05`](../../docs/05-controller-mapping-engine.md) ·
[`docs/06`](../../docs/06-push-profile.md) ·
[`docs/07`](../../docs/07-dj-controller-profile.md). (doc 05's body still names
DryWetMidi — historical; the seams it defines are library-agnostic and unchanged.)

## Library decision

**RtMidi.Core** (NuGet, MIT). Managed P/Invoke wrapper over `thestk/rtmidi`, bundling
native binaries for **Windows + macOS** — the only two supported targets (root
`CLAUDE.md`). Chosen over DryWetMidi (whose device layer is Windows-leaning) because
Mac is a hard requirement, and it supports input, output, and SysEx (needed for Push
LCD/mode, doc 06). It pulls in Serilog as a transitive dependency; our own logging
still goes through `Microsoft.Extensions.Logging`.

## Iron rules

1. **Implements the Core seams only.** Core depends on `IMidiInput`/`IMidiOutput`/
   `IMidiDeviceProvider`, never on this assembly or on RtMidi.Core.
2. **Native is isolated, translation is pure.** The native rtmidi library loads only
   when a device is enumerated or opened. The device *list* sits behind the internal
   `IRtMidiDeviceManager` seam (mirrors the Audio binding's `IBassPlayback`) so the
   provider's lookup logic unit-tests with a fake. Message translation
   (`RtMidiMessageTranslator`) is a pure value-to-value mapping over RtMidi's managed
   message structs — covered by tests with **no device and no native library** in CI.
3. **Feedback never blocks input.** Output open/send failures are logged and swallowed
   (doc 06); input errors are logged with the device name and never thrown back into
   the native callback thread.

## What is intentionally NOT here yet (deferred)

- **Device profiles** (Push 1 / CMD STUDIO 2A CC+note maps, docs 06/07) — these are
  **captured via MIDI learn** against real hardware, not hardcoded. The default
  `ControllerMappingProfile`s live in Core once captured; this binding only moves bytes.
- **Push 1 feedback adapter / SysEx formatting** (color palette, LCD text, User-mode
  switch, doc 06) — `SendSysEx` carries raw bytes; the byte formatting is a separate
  device-specific concern to add with the Push profile.
- **Relative-encoder encoding per device** — handled in Core's `ControlValueConverter`
  via the binding's `RelativeEncoding`; nothing device-specific belongs here.

## Public surface

- `RtMidiDeviceProvider` — the entry point. Implements `IMidiDeviceProvider` (now incl.
  `OpenInput(name)` / `OpenOutput(name)` on the Core seam) to open a named device as a
  Core `IMidiInput` / `IMidiOutput`. The App composes one provider, lists devices for the
  Settings tab, and `ServiceConfig.WireMidiInput` opens the chosen one into a Core
  `MidiInputPipeline` (router → mapper → the one dispatcher; feedback back out).

**Tests:** `tests/Liveolator.Midi.Tests` — pure translation + provider lookup with a
fake device manager. The native rtmidi library is **not** required to build or test.

## MANUAL hardware verification — CMD STUDIO 2A live control (NOT automatable)

The native rtmidi path + real controller cannot run in CI. Verify on a box with the
controller connected and the per-platform rtmidi native present (RtMidi.Core bundles
it). Run the App, set the controller + (optional) feedback output in the **Settings**
tab, restart, then confirm:

1. **Device opens, no crash.** App launches; Trace shows neither "not found" nor
   "Opening MIDI controller … failed". Unplug the controller and relaunch → app still
   runs (catalog browser), Trace logs the missing device. No startup throw.
2. **Profile auto-selects.** With the device named "CMD Studio 2A", the default
   `CmdStudio2AProfile` is the active profile (it matches the `DeviceHint`).
3. **Transport.** Press Deck A / Deck B play-pause pads → the matching deck toggles
   play/pause (needs native BASS for audible playback; otherwise verify the dispatcher
   feedback / UI deck state changes).
4. **Sync.** Press the Deck A/B sync buttons → `DeckSyncLockToggle` latches; the Live-tab
   SYNC button reflects it.
5. **Mixer.** Move the crossfader, the two channel faders, the per-deck 3-band EQ knobs,
   and the filter knob → the mixer state / UI tracks each (`MixerCrossfade`,
   `MixerChannelGain`, `MixerEqBand` Low/Mid/High, `MixerFilter`).
6. **Jog nudge.** Turn a jog wheel slowly → `BeatNudgeForward` fires (tempo/phase nudge).
7. **LED feedback (if a feedback output was selected).** Toggling sync from the UI lights
   the controller's sync LED; the LED follows the toggle state.
8. **MIDI learn override.** Arm learn for an action, move ANY control → that control
   rebinds (the default CC numbers are starting defaults, not gospel — doc 05/07).

The default CC/note numbers in `CmdStudio2AProfile` are a documented best-effort layout;
if a control does not match the hardware, re-capture it via MIDI learn and (later) persist
the profile (doc 13). Do not treat the numbers as confirmed until checked against the
device's MIDI implementation chart.
