import committed from "./changelog.json";
import { mergeChangelog, type ReleaseEntry } from "./release-feed";
import { releaseFeed } from "./site";

/**
 * The changelog the site renders: entries managed in wp-admin → Product Sites
 * first, then the history committed in `changelog.json`. Old releases stay in
 * the repository — only new entries need to be added in WordPress.
 */
export const changelog: ReleaseEntry[] = mergeChangelog(
  releaseFeed?.changelog,
  committed as ReleaseEntry[],
);
