// @ts-check
import { defineConfig } from 'astro/config';

// https://astro.build/config
// `site` is the public origin. It lets Astro resolve absolute canonical and
// og:image URLs at build time. Keep in sync with site.siteUrl in src/data/site.ts.
export default defineConfig({
  site: "https://liveolator.zalmanim.com",
});
