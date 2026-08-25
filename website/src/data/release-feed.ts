/**
 * Build-time read of the release record managed in wp-admin.
 *
 * The site is static, so this runs once during `astro build`: WordPress
 * (Product Sites screen) owns the version, the installer path, the download size
 * and the newest changelog entries, and the built HTML bakes them in. The VPS
 * publish agent rebuilds this site whenever the feed's `rev` changes.
 *
 * If the feed cannot be read the build does NOT fail — it falls back to the
 * values committed in `site.ts` / `changelog.json` and warns loudly, so a
 * WordPress outage can never block a deploy. The build then publishes no
 * revision (see `/rev.txt`), which is what tells the publish agent the rebuild
 * did not take and must be retried.
 */

export type ReleaseEntry = {
  version: string;
  date: string;
  notes: string[];
};

import { get as httpGet } from "node:http";
import { get as httpsGet } from "node:https";

export type ReleaseFeed = {
  slug: string;
  version: string;
  downloadPath: string;
  downloadSize: string;
  changelog: ReleaseEntry[];
  rev: string;
};

const FEED_BASE =
  process.env.RELEASE_FEED_BASE ?? "https://zalmanim.com/wp-json/zalmanim/v1/product-site";

const FEED_TIMEOUT_MS = 10_000;

function isEntry(value: unknown): value is ReleaseEntry {
  if (typeof value !== "object" || value === null) return false;
  const entry = value as Record<string, unknown>;
  return (
    typeof entry.version === "string" &&
    entry.version !== "" &&
    typeof entry.date === "string" &&
    /^\d{4}-\d{2}-\d{2}$/.test(entry.date) &&
    Array.isArray(entry.notes) &&
    entry.notes.length > 0 &&
    entry.notes.every((note) => typeof note === "string")
  );
}

/**
 * Accept only a feed that is complete enough to publish. A half-valid feed is
 * treated as no feed at all: rendering a version without its installer path, or
 * an installer path without its version, would advertise a download that does
 * not exist.
 */
function parseFeed(slug: string, value: unknown): ReleaseFeed | null {
  if (typeof value !== "object" || value === null) return null;
  const feed = value as Record<string, unknown>;

  if (feed.slug !== slug) return null;
  if (typeof feed.rev !== "string" || !/^[0-9a-f]{12}$/.test(feed.rev)) return null;
  if (typeof feed.version !== "string" || !/^\d/.test(feed.version)) return null;
  if (typeof feed.downloadPath !== "string" || !feed.downloadPath.startsWith("/")) return null;
  if (typeof feed.downloadSize !== "string") return null;
  if (!Array.isArray(feed.changelog) || !feed.changelog.every(isEntry)) return null;

  return {
    slug,
    version: feed.version,
    downloadPath: feed.downloadPath,
    downloadSize: feed.downloadSize,
    changelog: feed.changelog as ReleaseEntry[],
    rev: feed.rev,
  };
}

/**
 * GET a JSON document, or null when it cannot be read.
 *
 * node:http(s) rather than fetch(), deliberately. A build-time fetch() leaves
 * undici's keep-alive socket behind, and Node then aborts at process teardown on
 * Windows (libuv assertion, src/win/async.c) — the build prints `Complete!` and
 * still exits non-zero. Measured on this site: four runs in four with fetch,
 * zero in three with the call removed. A false failure there fails
 * `docker compose up --build` and the release script for a site that built
 * perfectly. `agent: false` opens one connection for this request and closes it.
 */
function getJson(url: string, timeoutMs: number): Promise<unknown> {
  const request = url.startsWith("http://") ? httpGet : httpsGet;

  return new Promise((resolve) => {
    const call = request(
      url,
      { agent: false, headers: { Accept: "application/json" }, timeout: timeoutMs },
      (response) => {
        const status = response.statusCode ?? 0;

        if (status !== 200) {
          // Drained, not left hanging: an unread response keeps the socket open.
          response.resume();
          console.warn(`[release-feed] ${url} returned ${status}; using committed values.`);
          resolve(null);
          return;
        }

        let body = "";
        response.setEncoding("utf8");
        response.on("data", (chunk) => {
          body += chunk;
        });
        response.on("end", () => {
          try {
            resolve(JSON.parse(body));
          } catch (error) {
            console.warn(`[release-feed] ${url} is not JSON: ${String(error)}; using committed values.`);
            resolve(null);
          }
        });
      },
    );

    call.on("timeout", () => call.destroy(new Error(`no answer within ${timeoutMs}ms`)));
    call.on("error", (error) => {
      console.warn(`[release-feed] could not read ${url}: ${error.message}; using committed values.`);
      resolve(null);
    });
  });
}

/**
 * Read the release feed for a product. Never throws.
 */
export async function loadReleaseFeed(slug: string): Promise<ReleaseFeed | null> {
  const url = `${FEED_BASE}/${slug}`;
  const payload = await getJson(url, FEED_TIMEOUT_MS);

  if (payload === null) {
    return null;
  }

  const feed = parseFeed(slug, payload);
  if (feed === null) {
    console.warn(`[release-feed] ${url} returned an unusable payload; using committed values.`);
    return null;
  }

  console.log(`[release-feed] ${slug} rev ${feed.rev} (v${feed.version})`);

  return feed;
}

/**
 * Newest-first changelog: entries managed in WordPress, then the history
 * committed in the repository, with WordPress winning on a shared version.
 */
export function mergeChangelog(
  fromFeed: ReleaseEntry[] | undefined,
  committed: ReleaseEntry[],
): ReleaseEntry[] {
  const merged = [...(fromFeed ?? [])];
  const seen = new Set(merged.map((entry) => entry.version));

  for (const entry of committed) {
    if (!seen.has(entry.version)) {
      seen.add(entry.version);
      merged.push(entry);
    }
  }

  return merged;
}
