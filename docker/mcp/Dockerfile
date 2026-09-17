# Liveolator MCP server — headless Linux image.
#
# The app is published SELF-CONTAINED (see scripts/deploy-mcp-server.ps1), so this image carries no
# .NET SDK or runtime: `runtime-deps` is the Microsoft image that holds exactly the native libraries
# a self-contained .NET app needs (ICU, OpenSSL, zlib) and nothing else.
#
# ffmpeg is the point of the image. There is no libbass.so for Linux here, so BassAudioDecoder
# catches DllNotFoundException, marks itself unusable, and CompositeAudioDecoder falls through to
# FfmpegAudioDecoder. That fallback is what lets the analyser run on this box at all — without
# ffmpeg present the server starts fine and then fails on the first track.
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Non-root. The data directory is a bind mount, so the deploy script chowns it to this uid on the
# host; the music mount is read-only and only needs read.
RUN useradd --system --uid 10001 --create-home liveolator

WORKDIR /app
COPY --chown=liveolator:liveolator publish/ ./
RUN chmod +x /app/liveolator-mcp

USER liveolator

# Loopback inside the container; the compose file is what decides whether the port is exposed
# beyond the host, and it publishes to 127.0.0.1 only.
EXPOSE 5175

ENTRYPOINT ["/app/liveolator-mcp"]
# --bind 0.0.0.0 is required INSIDE a container and is not a loosening of the default. Docker forwards
# a published port to the container's network interface, so a server bound to the container's own
# loopback accepts nothing and the port looks dead. The isolation is the compose file's
# "127.0.0.1:<port>:5175" mapping, which keeps it off the LAN just as firmly.
CMD ["--http", "--port", "5175", "--bind", "0.0.0.0", "--data", "/data", "--ffmpeg", "/usr/bin/ffmpeg"]
