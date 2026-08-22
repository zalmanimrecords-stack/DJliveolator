# Build the static site, then serve it from a tiny nginx image.
# Stage 1 — build with Node (Astro requires Node >= 22.12).
FROM node:22-alpine AS build
WORKDIR /app

# Install deps from the lockfile for reproducible builds.
COPY package.json package-lock.json ./
RUN npm ci

# Build the static output into /app/dist.
COPY . .

# The release record is fetched from WordPress during the build, so an unchanged
# source tree can still owe a new build. SITE_REV carries the revision the
# publish agent wants and invalidates this layer whenever it changes — without
# it Docker would reuse the cached build and the site would never update.
ARG SITE_REV=local
RUN echo "building for release rev ${SITE_REV}" && npm run build

# Stage 2 — serve the static files. No Node at runtime.
FROM nginx:1.27-alpine AS runtime
# Config is a template: the image's entrypoint runs envsubst on it at start so
# the secure_link secret comes from the DOWNLOAD_LINK_SECRET env var, never git.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80

# Lightweight container healthcheck so the orchestrator knows it's up.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget -qO- http://127.0.0.1/ >/dev/null 2>&1 || exit 1
