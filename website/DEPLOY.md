# Deploying liveolator.zalmanim.com

**Status: deployed.** The site runs on the zalmanim VPS (`<VPS_HOST>`) at
`/docker/liveolator`, fronted by the VPS's existing **Traefik** proxy. The only
remaining one-time step is the DNS record (below), after which Traefik issues the
HTTPS certificate automatically.

## How it's wired

The VPS already runs Traefik (project `traefik-8e8e`) on ports 80/443, routing
several `*.zalmanim.com` sites by Docker labels and issuing Let's Encrypt certs
via the `letsencrypt` HTTP-challenge resolver. Our container plugs into that:

- builds the Astro site and serves it via nginx on internal port 80
- publishes **no** host ports and runs **no** proxy of its own
- carries Traefik labels (see `docker-compose.yml`) so Traefik routes
  `liveolator.zalmanim.com` to it — identical pattern to `lm.zalmanim.com` etc.

This is why deploying it can't disturb the other sites: it never touches 80/443.

## One-time DNS (the only manual step)

`zalmanim.com` is on **Cloudflare**. Add a record the same way as the sibling
subdomains (`lm`, `wags`, `artists`):

```
Type:   A
Name:   liveolator
Value:  <VPS_HOST>
Proxy:  Proxied (orange cloud)   # match the other subdomains
TTL:    Auto
```

Within a minute or two Traefik completes the HTTP-01 challenge and
**https://liveolator.zalmanim.com** goes live. Verify:

```sh
curl -I https://liveolator.zalmanim.com      # expect HTTP/2 200
```

## Updating the site later

From this `website/` folder, push the source to the VPS and rebuild:

```sh
# stream source (excludes node_modules/dist/.git), then rebuild on the VPS
tar czf - --exclude=node_modules --exclude=dist --exclude=.astro --exclude=.git \
  Dockerfile nginx.conf.template .dockerignore package.json package-lock.json \
  astro.config.mjs tsconfig.json src public \
| ssh -i ~/.ssh/<SSH_KEY> root@<VPS_HOST> \
    'tar xzf - -C /docker/liveolator && cd /docker/liveolator && docker compose up -d --build'
```

The `docker-compose.yml` already lives on the VPS in `/docker/liveolator`; it's
also kept here in the repo as the source of truth.

## The Windows installer (download)

The installer is served at `/downloads/` from a bind-mounted dir on the VPS
(`/docker/liveolator/downloads`), **not** baked into the image or committed to
git (it's a 36 MB binary). The compose mounts it read-only.

To publish a new build:

```sh
# 1. copy the new installer up
scp -i ~/.ssh/<SSH_KEY> \
  artifacts/dist/win-x64/LiveolatorSetup-<ver>.exe \
  root@<VPS_HOST>:/docker/liveolator/downloads/

# 2. point the site at it: bump version/downloadUrl/downloadSize in
#    website/src/data/site.ts, then redeploy (see "Updating the site later")

# 3. point WordPress at it too (see "Email-gated downloads" below) — the signed
#    link is built from WP's own per-product setting, so it must match the new
#    filename or the emailed link will 404.
```

A rebuild is needed after swapping the file so the version text updates — and,
unlike before, the new file is **not** publicly downloadable until step 3 keeps
the WordPress target in sync (downloads are now gated, see below).

## Email-gated downloads

Downloads are gated: visitors enter their email in the form on the home page and
WordPress (`zalmanim.com`, the Zalmanim Newsletter plugin) emails back a signed
link to `/downloads/...exe` that is valid for **24 hours**. nginx verifies that
signature with its built-in `secure_link` module, so a direct hit on
`/downloads/LiveolatorSetup-<ver>.exe` with no/expired/forged signature returns
**403/410** — the email step cannot be bypassed.

How it's wired:

- **`nginx.conf.template`** (rendered by the nginx image's `envsubst` at start)
  holds the `secure_link` check. The shared secret comes from the
  `DOWNLOAD_LINK_SECRET` env var, never git. `NGINX_ENVSUBST_FILTER` limits
  substitution to that one variable so nginx's own `$uri` etc. survive.
- **One-time on the VPS:** create `/docker/liveolator/.env` with the secret,
  identical to WordPress's `ZNL_DOWNLOAD_LINK_SECRET` (defined in `wp-config.php`):

  ```sh
  printf 'DOWNLOAD_LINK_SECRET=%s\n' '<the-shared-secret>' > /docker/liveolator/.env
  chmod 600 /docker/liveolator/.env
  cd /docker/liveolator && docker compose up -d --build
  ```

- **WordPress side:** in `wp-admin` → **Newsletter → Settings → Gated
  downloads**, the *Liveolator* origin/installer-path/version must point at the
  current file. Update it on each release (step 3 above). The signature is
  computed as `base64url(md5("<expires><uri> <secret>"))`, matching the nginx
  directive exactly — if links suddenly 403, the secret or the path is out of sync.

The "join the mailing list" checkbox on the form is optional and unticked by
default; ticking it starts a GDPR double opt-in (a separate confirmation email),
and the subscriber is tagged source `liveolator-download` so you can see in the
WordPress *Subscribers* screen where they came from. The download link is sent
either way.

## Release hook (installer build -> website)

`scripts/build-installer.ps1` calls `scripts/publish-website-release.ps1` after a
successful build (pass `-NoPublish` to skip). That publish step:

1. turns `website/RELEASE_NOTES_NEXT.md` (one bullet per line) into a dated entry
   in `website/src/data/changelog.json`, then resets the notes file;
2. updates `version` / `downloadUrl` / `downloadSize` in `website/src/data/site.ts`;
3. uploads the new installer + those two data files to the VPS and rebuilds the
   container — so https://liveolator.zalmanim.com/changelog and the download button
   reflect the new build automatically.

So the workflow for a release is: jot the "what's new" lines into
`website/RELEASE_NOTES_NEXT.md`, then run `scripts/build-installer.ps1`. The site
updates itself. Run `publish-website-release.ps1` directly (with `-NoDeploy` to
preview locally) for a website-only refresh.

The local file edits always happen even if the deploy fails, so commit them and
re-deploy manually if needed.

## Screenshots

The site's screenshots come from the app's UI-shot captures
(`artifacts/ui-shots/*.png`, produced by `dotnet test tests/Liveolator.App.Tests
--filter UiShots`). `scripts/sync-website-screenshots.ps1` maps them to the site's
filenames and copies them into `website/public/screenshots`:

- `publish-website-release.ps1` calls it on every release, so the site's
  screenshots follow each build automatically.
- Pass `-Capture` (or `build-installer.ps1 -RefreshShots`) to re-render the shots
  first — heavier, and best-effort (falls back to the latest existing captures).
- Run `sync-website-screenshots.ps1 -Deploy` for a screenshots-only push.

**Cache-busting:** the site is behind Cloudflare, which caches images at the edge.
Screenshot `<img>` URLs carry `?v={site.version}`, so a new build (new version)
is a new cache key and visitors get the fresh images without a manual purge. If
you re-capture screenshots *without* bumping the version, either bump it or purge
the Cloudflare cache for `/screenshots/*` so the edge refreshes.

## User manual

The manual at `/manual` is content-driven from `website/src/data/manual.ts`. When
app behaviour changes, update the relevant section there and bump `manualUpdated`
in `site.ts`, then redeploy (full source sync — see "Updating the site later").

## Useful checks on the VPS

```sh
ssh -i ~/.ssh/<SSH_KEY> root@<VPS_HOST>
docker ps --filter name=liveolator-site                       # health
docker logs liveolator-site --tail 50                         # nginx access/errors
docker logs traefik-8e8e-traefik-1 2>&1 | grep -i liveolator  # routing / cert
# route check before DNS exists (self-signed until cert issues, hence -k):
curl -sk --resolve liveolator.zalmanim.com:443:127.0.0.1 https://liveolator.zalmanim.com/ -I
```

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Cert not issued / browser TLS warning | DNS record not added/propagated yet. Confirm `nslookup liveolator.zalmanim.com` returns Cloudflare/origin, then wait a minute. |
| 404 from Traefik | Container down or label typo — `docker ps`, check the `Host(...)` rule matches exactly. |
| Old content after update | Rebuild without cache: `docker compose up -d --build` (add `--no-cache` on the build if needed). |
