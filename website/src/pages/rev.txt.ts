import type { APIRoute } from "astro";
import { buildRev } from "../data/site";

// Prerendered like every other asset — the site has no server runtime.
export const prerender = true;

/**
 * The wp-admin release revision this build was made from.
 *
 * The VPS publish agent compares this with the revision WordPress is serving to
 * confirm a rebuild actually took, and the Product Sites screen shows the same
 * comparison as its live/pending status. `none` means the build could not read
 * WordPress and fell back to the committed values.
 *
 * This is prerendered to a file, so nginx — not this handler — decides its
 * caching: `nginx.conf.template` serves it `no-store`, otherwise a poll could
 * read an edge copy of the previous build and never see the rebuild land.
 */
export const GET: APIRoute = () =>
  new Response(buildRev, {
    headers: { "Content-Type": "text/plain; charset=utf-8" },
  });
