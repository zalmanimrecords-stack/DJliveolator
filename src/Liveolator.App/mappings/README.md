# Shipped controller mappings

Every `*.json` file in this folder is loaded at startup and added to the controller-profile catalog,
so plugging in a matching device selects it automatically. **Adding a controller is a data file, not
a class.**

## The format

Exactly what the app writes — so the fastest way to author one is to learn it in
SETTINGS → MIDI mapping and press **Export**, then drop the file here:

```json
{
  "Version": 1,
  "Profile": {
    "Name": "DDJ-400 (Pioneer, default)",
    "DeviceHint": "DDJ-400",
    "Bindings": [
      {
        "TriggerType": "NoteOn",
        "Channel": 0,
        "Data1": 11,
        "Action": "DeckPlayPause",
        "InputMode": "Momentary",
        "Slot": 0
      }
    ]
  }
}
```

- `DeviceHint` is matched case-insensitively as a **substring** of the MIDI device name. The first
  profile in the catalog whose hint matches wins, so keep hints specific (`DDJ-400`, not `DDJ`).
- `Slot` is the deck: `0` = deck A, `1` = deck B. EQ bands take the band name in `Argument`
  (`Low` / `Mid` / `High`).
- Buttons are `Momentary` or `Toggle`, faders and knobs `Absolute`, endless encoders `Relative`
  (set `Relative` to the device's encoding and `RelativeTicksPerRevolution` for a jog).
- `SoftTakeover` belongs on a pitch fader, **never** on a channel fader or crossfader — there the
  physical position is the truth.
- Optional profile-level keys: `ActivationSysEx` / `DeactivationSysEx` (byte arrays, e.g. to put a
  device into user/programmer mode) and `UsesColorFeedback` for velocity-addressed colour pads.

## What ships today

| File | Device | Source | Notes |
| --- | --- | --- | --- |
| `ddj-400.json` | Pioneer DDJ-400 | AlphaTheta *List of MIDI messages* v1.00 | Also maps COLOR/FILTER and headphone mix + level |
| `ddj-sb3.json` | Pioneer DDJ-SB3 | AlphaTheta *List of MIDI messages* | Pads on channels 8/9 (1-based) |
| `ddj-rev1.json` | Pioneer DDJ-REV1 | AlphaTheta *List of MIDI messages* | |
| `ddj-flx2.json` | Pioneer DDJ-FLX2 | AlphaTheta *List of MIDI messages* | Hot-cue pad channel not stated unambiguously; pads unmapped |
| `ddj-200.json` | Pioneer DDJ-200 | AlphaTheta *List of MIDI messages* | No headphone output on the unit — cue needs a second audio interface |
| `ddj-1000.json` | Pioneer DDJ-1000 | AlphaTheta *List of MIDI messages* | 4-channel unit; only channels 1-2 are mapped, matching the two decks |
| `inpulse-300.json` | Hercules DJControl Inpulse 300 | Hercules *List of MIDI commands* V1.2 | Decks on MIDI channels 1/2, jog is two's complement |

Three more devices ship as profile classes in `Liveolator.Core/Mapping/Profiles`: Behringer CMD
STUDIO 2A, Pioneer DDJ-FLX4 and Ableton Push 1.

**Verified against the documents, not against the hardware.** Every number came from the
manufacturer's published map, and `ShippedControllerProfilesTests` drives real MIDI messages through
each profile. What no test can check is the feel of a specific unit — above all the jog's
ticks-per-revolution. If a wheel feels too fast or too slow on your device, that is the number to
change, and a corrected file dropped in here wins over the one that shipped.

## Rules for a profile shipped here

1. Note and CC numbers come from the **manufacturer's published MIDI map**, or from learn mode on
   the real hardware. Never guessed.
2. A profile that ships is one someone has run on the device. An untested map is worse than none —
   it looks supported and behaves wrong in a dark room.
3. Anything a file here gets wrong, the performer can override: their own learned profile for the
   same device is loaded first, and any single control can be re-learned.

A file that is corrupt, of an unknown version or non-JSON is skipped with a warning in the log; it
never stops the app from starting.
