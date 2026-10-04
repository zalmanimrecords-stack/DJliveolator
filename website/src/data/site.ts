import { loadReleaseFeed } from "./release-feed";

/**
 * Single source of truth for site content and links.
 * Edit values here rather than in the markup.
 *
 * Exception: the release fields (`version`, `downloadUrl`, `downloadSize`) are
 * managed in wp-admin → Product Sites and overlaid at build time — see the
 * bottom of this file. The values below stay as the offline fallback for
 * `astro dev` and for a build that cannot reach WordPress.
 */

const defaults = {
  name: "Liveolator",
  tagline: "Mix the music. Move the visuals. One beat.",
  description:
    "Liveolator is a free DJ app for Windows with visuals built in. Mix on two decks and the visuals move with the music on their own, with no second laptop and no second person.",
  // Public canonical origin. Used to build absolute canonical, og:url and
  // og:image URLs for search engines and social crawlers. Must match astro
  // config `site` and carry no trailing slash.
  siteUrl: "https://liveolator.zalmanim.com",
  // Social share image, resolved against siteUrl. A real screenshot reads best
  // in link previews; 1200x630 is the recommended size.
  ogImage: "/screenshots/dj.png",
  // Development stage shown across the site. Currently an early alpha.
  stage: "Alpha",
  // Windows installer, served from the site's own /downloads (bind-mounted on
  // the VPS — see website/DEPLOY.md). These three are updated automatically by
  // scripts/publish-website-release.ps1 each time an installer is built.
  version: "0.10.4",
  downloadUrl: "/downloads/LiveolatorSetup-0.10.4.exe",
  downloadSize: "38 MB",
  // Cache-buster for the screenshots specifically. Bump this when the images are
  // re-rendered WITHOUT a version bump, so Cloudflare's edge (which keys on the query)
  // serves the fresh files instead of the cached ones (see website/DEPLOY.md).
  shotRev: "6",
  // Email-gated download: the button posts the visitor's email here and the WP
  // backend (zalmanim.com) emails back a signed, 24h link to `downloadUrl`.
  // `productSlug` must match a product key in the newsletter plugin settings.
  downloadApiUrl: "https://zalmanim.com/wp-json/zalmanim/v1/request-download",
  productSlug: "liveolator",
  // Public, open-source home of the project (GPLv3). NOTE: this is the public
  // `DJliveolator` repo, not the private dev mirror — visitor-facing links must
  // resolve, so keep this pointed at the public repo.
  repoUrl: "https://github.com/zalmanimrecords-stack/DJliveolator",
  // Contributor entry points on the public repo (both enabled): bugs/features go
  // to Issues, open-ended questions to Discussions.
  repoIssuesUrl: "https://github.com/zalmanimrecords-stack/DJliveolator/issues",
  repoDiscussionsUrl: "https://github.com/zalmanimrecords-stack/DJliveolator/discussions",
  repoContributingUrl:
    "https://github.com/zalmanimrecords-stack/DJliveolator/blob/main/CONTRIBUTING.md",
  // Software licence, surfaced in copy and in the SoftwareApplication JSON-LD.
  license: "GPLv3",
  licenseUrl: "https://www.gnu.org/licenses/gpl-3.0.html",
  // PayPal donate button (same one wired into the app's Donate action and the
  // Zalmanolator site).
  donateUrl: "https://www.paypal.com/donate/?hosted_button_id=APK7NELSVVMXL",
  // Inquiries + efficiency suggestions land here (mailto, see Feedback form).
  contactEmail: "zalmanimrecords@gmail.com",
  // Bump when the user manual page (/manual) is revised.
  manualUpdated: "2026-08-09",
  // Who operates the site / acts as data controller for the privacy policy.
  operator: "Liveolator (Zalmanim Records)",
  // Bump when the privacy policy (/privacy) is revised.
  privacyUpdated: "2026-06-21",
} as const;

/**
 * The release record from wp-admin, or null when it could not be read. Fetched
 * once here (ES modules are evaluated once) and reused by `changelog.ts`.
 */
export const releaseFeed = await loadReleaseFeed(defaults.productSlug);

/**
 * The revision this build was made from, published at `/rev.txt` so the publish
 * agent and the wp-admin screen can both tell whether what is live matches what
 * is saved. A build that fell back to the committed values deliberately reports
 * no revision rather than a stale one — that mismatch is the retry signal.
 */
export const buildRev = releaseFeed?.rev ?? "none";

export const site = {
  ...defaults,
  ...(releaseFeed
    ? {
        version: releaseFeed.version,
        downloadUrl: releaseFeed.downloadPath,
        downloadSize: releaseFeed.downloadSize,
      }
    : {}),
};

export type Feature = {
  tag: string;
  title: string;
  body: string;
};

export const features: Feature[] = [
  {
    tag: "In time",
    title: "Everything on one beat",
    body: "Your music and your visuals follow the same beat, so every flash, cut and transition lands in time without you touching a thing. It's one instrument, not two apps trying to keep up with each other.",
  },
  {
    tag: "DJ",
    title: "Two decks that feel like the real thing",
    body: "Mix on two decks with EQ and filter on every channel, a crossfader, hot cues, loops and live BPM. Hit SYNC or match by ear. Press AUTO and the crossfader glides across for you. Headphone cueing is built in.",
  },
  {
    tag: "Dig",
    title: "See what fits next",
    body: "Right under deck A, Liveolator lists the tracks that mix with what's playing, by key, tempo and genre. Tap A or B to send one to a deck. If that deck is playing, the track waits in the queue instead of cutting in.",
  },
  {
    tag: "Visuals",
    title: "Visuals that move with the music",
    body: "Beat-synced visuals that react to your tracks and hit with the drop. Pick a look, tweak its controls while you play, and let it ride. They run off the same clock as your decks, so they never drift.",
  },
  {
    tag: "Studio",
    title: "Plan a set, then render it",
    body: "Lay your tracks out on a timeline, draw the crossfader, EQ and filter moves, listen back, then render the finished mix to a file.",
  },
  {
    tag: "Control",
    title: "Bring your own controller",
    body: "Plug in whatever you own: a DJ controller, pad grid, mixer or keyboard. Teach Liveolator each knob in a few seconds by moving it. There's no fixed list of supported gear.",
  },
  {
    tag: "Gig-ready",
    title: "Take your set anywhere",
    body: "Copy a playlist to your laptop with its BPMs, keys and beat grids, then play without Wi-Fi or your music drive. No greyed-out titles at the gig.",
  },
  {
    tag: "Library",
    title: "Bring your library with you",
    body: "Import your tracks, cue points, beat grids and playlists from Rekordbox and Traktor. The import only reads, so your originals stay untouched.",
  },
  {
    tag: "Free",
    title: "Free, for real",
    body: "No trial, no watermark, no locked features, and every update is free. Liveolator is also open source (GPLv3), so it can never be taken away from you.",
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
    src: "/screenshots/dj.png",
    alt: "Liveolator DJ PRO tab with two decks, mixer, hot cues, FX and a list of tracks that match the deck",
    label: "DJ PRO",
    caption: "Two decks, 3-band EQ, hot cues and loops, plus the tracks that fit what is playing.",
  },
];
