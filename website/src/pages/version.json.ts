import type { APIRoute } from "astro";
import { site } from "../data/site";
import { changelog } from "../data/changelog";

// Prerender to a static /version.json so nginx serves it like any other asset
// (the site has no server runtime — see website/DEPLOY.md).
export const prerender = true;

/**
 * The update manifest every installed copy of Liveolator polls on startup
 * (Liveolator.App → UpdateManifestUrl → this file at the site root).
 *
 * Generated rather than committed: since the release record moved to wp-admin →
 * Product Sites, a version bump can happen without anyone touching this
 * repository. A hand-maintained `public/version.json` then kept advertising the
 * previous version, so the site's Download button offered a new build that no
 * running app was ever told about — silently, since nothing compares the two.
 *
 * ⚠️ `public/version.json` must stay deleted, here and on the VPS. Astro copies
 * `public/` over the built output, so a leftover copy would shadow this route
 * and bring the drift straight back.
 *
 * The keys are the ones the app parses (`UpdateManifest`) — version, downloadUrl,
 * notes — and nothing else.
 */
export const GET: APIRoute = () => {
  const latest = Array.isArray(changelog) && changelog.length > 0 ? changelog[0] : null;

  const body = {
    version: site.version,
    // The DOWNLOAD PAGE, not the direct installer path: downloads are
    // email-gated and nginx 403s any unsigned /downloads/ hit, so the app has to
    // send the user through the same gate the site's own button uses.
    downloadUrl: new URL("/#download", site.siteUrl).href,
    notes: latest?.notes ?? [],
  };

  return new Response(JSON.stringify(body, null, 2) + "\n", {
    headers: { "Content-Type": "application/json; charset=utf-8" },
  });
};
