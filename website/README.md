# Liveolator — marketing site

A small, static landing page for Liveolator (free DJ + VJ performance app),
built with [Astro](https://astro.build/). One page, dark/amber theme that mirrors
the app, and a feedback form for inquiries and efficiency suggestions.

## Develop

```sh
cd website
npm install      # first time only
npm run dev      # http://localhost:4321
npm run build    # static output → website/dist/
npm run preview  # serve the built site locally
```

## Where to edit

| Want to change… | Edit |
|---|---|
| App name, tagline, links, **contact email**, download URL | `src/data/site.ts` |
| Feature cards / screenshot captions | `src/data/site.ts` |
| Colors, fonts, spacing | `src/styles/global.css` (CSS variables at top) |
| A section's markup | `src/components/*.astro` |
| Screenshots | `public/screenshots/` |

## Feedback form

The form (`src/components/Feedback.astro`) is server-less: on submit it builds a
`mailto:` link to the address in `site.contactEmail` and opens the visitor's email
app. To switch to a hosted form service later (e.g. Formspree), replace the form's
submit handler — the rest of the page is unaffected.

## Deploy (free options)

The build output is plain static files in `dist/`, so it hosts anywhere:

- **Netlify / Vercel / Cloudflare Pages** — point at this folder, build command
  `npm run build`, publish directory `dist`.
- **GitHub Pages** — push `dist/` (or use an Astro Pages action). If served from a
  sub-path, set `base` in `astro.config.mjs`.
