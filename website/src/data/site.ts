/**
 * Single source of truth for site content and links.
 * Edit values here rather than in the markup.
 */

export const site = {
  name: "Liveolator",
  tagline: "Mix the music. Move the visuals. One beat.",
  description:
    "A cross-platform DJ + VJ performance app where the visuals are locked to the music on a single shared beat clock.",
  // Development stage shown across the site. Currently an early alpha.
  stage: "Alpha",
  // Windows installer, served from the site's own /downloads (bind-mounted on
  // the VPS — see website/DEPLOY.md). These three are updated automatically by
  // scripts/publish-website-release.ps1 each time an installer is built.
  version: "0.1.4",
  downloadUrl: "/downloads/LiveolatorSetup-0.1.4.exe",
  downloadSize: "38 MB",
  repoUrl: "https://github.com/zalmanimrecords-stack/Liveolator",
  // PayPal donate button (same one wired into the app's Donate action and the
  // Zalmanolator site).
  donateUrl: "https://www.paypal.com/donate/?hosted_button_id=APK7NELSVVMXL",
  // Inquiries + efficiency suggestions land here (mailto, see Feedback form).
  contactEmail: "zalmanimrecords@gmail.com",
  // Bump when the user manual page (/manual) is revised.
  manualUpdated: "2026-06-21",
  // Who operates the site / acts as data controller for the privacy policy.
  operator: "Liveolator (Zalmanim Records)",
  // Bump when the privacy policy (/privacy) is revised.
  privacyUpdated: "2026-06-21",
} as const;

export type Feature = {
  tag: string;
  title: string;
  body: string;
};

export const features: Feature[] = [
  {
    tag: "The link",
    title: "One shared beat clock",
    body: "Audio and visuals run off the same Ableton-Link-style clock, so every effect, cut and transition lands on the beat — automatically. This is what makes Liveolator one instrument instead of two apps side by side.",
  },
  {
    tag: "DJ",
    title: "Two-deck DJ engine",
    body: "Low-latency playback, a software mixer with per-channel EQ and filter, crossfader, hot cues, loops, live BPM detection and a built-in headphone cue. Beat-matching feels like hardware.",
  },
  {
    tag: "VJ",
    title: "Real-time visual compositor",
    body: "GPU GLSL effects layered over images, video clips and live camera input — composited Resolume-style and beat-synced to the same clock. No MilkDrop; you bring your own footage.",
  },
  {
    tag: "Studio",
    title: "STUDIO timeline",
    body: "A focused DAW timeline: drop clips onto per-deck lanes, draw automation for crossfader, EQ, filter, volume and pitch, preview live, then render the set offline.",
  },
  {
    tag: "Control",
    title: "Works with any MIDI controller",
    body: "Plug in whatever you own — any class-compliant DJ controller, pad grid, mixer or keyboard. MIDI-learn maps any control to any action, so nothing is hardcoded to a specific device.",
  },
  {
    tag: "Platform",
    title: "Windows today, macOS next",
    body: "Right now Liveolator runs on Windows. It's built on .NET 8 and Avalonia with a cross-platform audio engine, so a macOS build is on the way — no Windows lock-in by design.",
  },
];

export type Shot = {
  src: string;
  alt: string;
  label: string;
  caption: string;
};

export const shots: Shot[] = [
  {
    src: "/screenshots/live.png",
    alt: "Liveolator LIVE tab with dual waveforms, two decks, mixer and a visuals strip",
    label: "LIVE",
    caption: "Decks, mixer and visuals on one screen — everything you touch mid-set.",
  },
  {
    src: "/screenshots/dj.png",
    alt: "Liveolator DJ tab showing two decks with jog wheels, EQ and crossfader",
    label: "DJ",
    caption: "Full two-deck focus: jog wheels, 3-band EQ, filter, hot cues and loops.",
  },
  {
    src: "/screenshots/vj.png",
    alt: "Liveolator VJ tab asset browser for images and video",
    label: "VJ",
    caption: "Bring in your images and video, scan a folder, then composite in layers.",
  },
  {
    src: "/screenshots/studio.png",
    alt: "Liveolator STUDIO timeline with per-deck lanes and automation",
    label: "STUDIO",
    caption: "Lay a set out on the timeline, automate the mix and render it offline.",
  },
  {
    src: "/screenshots/libraries.png",
    alt: "Liveolator LIBRARIES tab with track list, BPM and key columns",
    label: "LIBRARIES",
    caption: "Your catalog with BPM, key and auto-cue analysis ready to load.",
  },
];
