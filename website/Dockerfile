# Build the static site, then serve it from a tiny nginx image.
# Stage 1 — build with Node (Astro requires Node >= 22.12).
FROM node:22-alpine AS build
WORKDIR /app

# Install deps from the lockfile for reproducible builds.
COPY package.json package-lock.json ./
RUN npm ci

# Build the static output into /app/dist.
COPY . .
RUN npm run build

# Stage 2 — serve the static files. No Node at runtime.
FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80

# Lightweight container healthcheck so the orchestrator knows it's up.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget -qO- http://127.0.0.1/ >/dev/null 2>&1 || exit 1
