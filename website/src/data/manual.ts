/**
 * User-manual content for /manual. Structured so the page can render a table of
 * contents and so it's easy to keep in step with the app.
 *
 * When the app changes, update the relevant section here AND bump
 * `manualUpdated` in site.ts.
 */

export type ManualBlock =
  | { kind: "p"; text: string }
  | { kind: "list"; items: string[] }
  | { kind: "steps"; items: string[] };

export type ManualSection = {
  id: string;
  title: string;
  blocks: ManualBlock[];
};

export const manual: ManualSection[] = [
  {
    id: "install",
    title: "Install & first launch",
    blocks: [
      {
        kind: "p",
        text: "Liveolator is an early alpha and Windows-only for now. The installer is unsigned, so Windows SmartScreen may warn you the first time — choose More info, then Run anyway.",
      },
      {
        kind: "steps",
        items: [
          "Download the installer from the home page and run it.",
          "Follow the setup wizard; it installs Liveolator and the bundled audio engine.",
          "Launch it. You land on the LIVE tab with two empty decks.",
        ],
      },
      {
        kind: "p",
        text: "If audio doesn't play, open SETTINGS and pick the correct output device (see Settings, audio & logs below).",
      },
    ],
  },
  {
    id: "controllers",
    title: "Connecting a controller (any MIDI gear)",
    blocks: [
      {
        kind: "p",
        text: "Liveolator works with any class-compliant MIDI controller — a DJ controller, a pad grid, a mixer or a keyboard. Nothing is hardcoded to a specific device: you map controls yourself with MIDI-learn.",
      },
      {
        kind: "steps",
        items: [
          "Plug the controller in before launching (or reopen the app after connecting).",
          "Go to the MAPPINGS area and pick the control you want to bind.",
          "Click Learn, then move the knob, fader or pad on your hardware — Liveolator captures it.",
          "Repeat for the controls you use. Your mapping is saved automatically.",
        ],
      },
      {
        kind: "p",
        text: "Because mapping is learn-based, two people with completely different controllers can both drive the same actions. The Ableton Push 1 and Behringer CMD STUDIO 2A are known to work well.",
      },
    ],
  },
  {
    id: "workspace",
    title: "The workspace",
    blocks: [
      {
        kind: "p",
        text: "The app is organised into tabs along the top:",
      },
      {
        kind: "list",
        items: [
          "LIVE — your performance screen: both decks, the mixer and the visuals output together.",
          "DJ — the same two decks with more room for detailed deck work.",
          "STUDIO — a timeline for laying out and rendering a set.",
          "VJ — the visual assets and layers.",
          "LIBRARIES — your music catalog.",
          "SETTINGS — audio output, diagnostics and logs.",
        ],
      },
    ],
  },
  {
    id: "dj",
    title: "DJ: decks & mixer",
    blocks: [
      {
        kind: "p",
        text: "Each deck has a jog wheel, transport, pitch fader and a 3-band EQ plus filter on the mixer. Load a track by dragging it from a library, or queue it onto a playing deck.",
      },
      {
        kind: "list",
        items: [
          "Play / cue / sync transport, with a pitch fader for tempo.",
          "KEY LOCK keeps the musical key fixed while you change tempo.",
          "NUDGE is a momentary pitch-bend — hold it to push or pull the beat by hand.",
          "Hot cues: set and jump to cue points; numbered pads make them quick to trigger.",
          "Loops: set a beat-length loop and toggle it on the fly.",
          "Mixer: per-channel HI/MID/LOW EQ, a filter, channel faders and the crossfader, plus headphone CUE.",
        ],
      },
      {
        kind: "p",
        text: "The waveform shows a 3-band view with the kick emphasised, so you can line up beats by eye as well as by ear.",
      },
    ],
  },
  {
    id: "libraries",
    title: "Libraries: scan, analyze, import",
    blocks: [
      {
        kind: "steps",
        items: [
          "On LIBRARIES, add a music folder and press Scan to catalog your tracks.",
          "Liveolator analyzes BPM and musical key, and can auto-assign hot cues.",
          "Filter and sort by artist, genre, year, BPM or key to find the next track.",
        ],
      },
      {
        kind: "p",
        text: "Already use another DJ app? Import your existing library — tracks, cue points, beat grids, key and playlists — from Rekordbox (XML) and Traktor (NML). The import is read-only: it never touches your original files.",
      },
    ],
  },
  {
    id: "studio",
    title: "STUDIO: timeline",
    blocks: [
      {
        kind: "p",
        text: "STUDIO is a focused timeline for planning or rendering a set rather than playing live.",
      },
      {
        kind: "list",
        items: [
          "Drag a track onto a per-deck lane to place a clip.",
          "Drag a clip to move it; drag its edges to trim; use the top corner to fade.",
          "Draw automation for the crossfader, EQ, filter, volume and pitch.",
          "Preview live, then render the finished set to a file offline.",
        ],
      },
    ],
  },
  {
    id: "vj",
    title: "VJ: visuals",
    blocks: [
      {
        kind: "p",
        text: "The visual engine composites your own images, video clips and live camera input in layers, with GPU effects — Resolume-style. You bring the footage; there's no MilkDrop-style generator.",
      },
      {
        kind: "steps",
        items: [
          "On VJ, add a folder of images/videos and press Scan.",
          "Build up layers and apply effects to each.",
          "Everything reacts to the same beat clock the music runs on, so the visuals stay locked to the mix.",
        ],
      },
    ],
  },
  {
    id: "settings",
    title: "Settings, audio & logs",
    blocks: [
      {
        kind: "list",
        items: [
          "Pick your audio output device (and headphone/cue output where available).",
          "If something misbehaves, open SETTINGS → DIAGNOSTICS — Liveolator writes a rolling log to %APPDATA%\\Liveolator\\logs\\liveolator.log.",
          "That log file is the best thing to attach when you report a bug.",
        ],
      },
    ],
  },
  {
    id: "known-issues",
    title: "Known issues (it's alpha)",
    blocks: [
      {
        kind: "p",
        text: "This is early software and there are bugs. Expect rough edges, occasional crashes, and features that are still landing. When you hit one:",
      },
      {
        kind: "list",
        items: [
          "Note what you did just before it happened.",
          "Grab the log from %APPDATA%\\Liveolator\\logs.",
          "Send both via the feedback form — it genuinely shapes what gets fixed next.",
        ],
      },
    ],
  },
  {
    id: "help",
    title: "Getting help & sending feedback",
    blocks: [
      {
        kind: "p",
        text: "Questions, bug reports and efficiency suggestions are all welcome — use the feedback form on the home page. Your input directly decides what gets built and fixed next.",
      },
    ],
  },
];
