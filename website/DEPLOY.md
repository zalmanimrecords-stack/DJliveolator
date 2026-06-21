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
  Dockerfile nginx.conf .dockerignore package.json package-lock.json \
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
```

No rebuild is needed just to swap the file (it's a live mount) — only to update
the version text shown on the page.

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
