#!/bin/bash
# Hourly on the music host (cron): scan the library through the local MCP server, then publish a
# verified snapshot of the catalog next to the music, where every PC sees it as <share>/.liveolator.
#
# Why here and not on the PCs: over SMB a track reads at ~1 MB/s, so analysis belongs where the files
# are. PCs only pull the snapshot (pull_server_catalog / the App), never scan over the network.
#
# The snapshot is taken with SQLite's online backup from an IMMUTABLE read (no locks, no -wal/-shm
# written beside the server's live database), checked with integrity_check, switched to a single file
# and renamed into place, so a reader on the share never sees a torn or half-written catalog.
set -uo pipefail

URL=${LIVEOLATOR_MCP_URL:-http://127.0.0.1:5175/}
# Host paths are deployment-specific and not kept in the public repo; the crontab line sets them.
MUSIC=${LIVEOLATOR_MUSIC_DIR:?set LIVEOLATOR_MUSIC_DIR to the music folder on this host}
CATALOG=${LIVEOLATOR_CATALOG:?set LIVEOLATOR_CATALOG to the MCP server catalog.db on this host}
LOG=${LIVEOLATOR_SCAN_LOG:?set LIVEOLATOR_SCAN_LOG to the scan log path on this host}
LAST=${LOG%.log}-last.json
PUBLISH_DIR="$MUSIC/.liveolator"

exec 9>/tmp/liveolator-scan.lock
flock -n 9 || { echo "$(date -Is) skipped: previous scan still running" >> "$LOG"; exit 0; }

log() { echo "$(date -Is) $*" >> "$LOG"; }
H=(-H "Content-Type: application/json" -H "Accept: application/json, text/event-stream")

SID=$(curl -s -m 30 -D - -o /dev/null -X POST "$URL" "${H[@]}" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"scan-and-publish","version":"1"}}}' \
  | tr -d '\r' | grep -i '^mcp-session-id' | cut -d' ' -f2)
if [ -z "$SID" ]; then log "FAILED: MCP server at $URL did not open a session"; exit 1; fi
curl -s -m 15 -X POST "$URL" "${H[@]}" -H "Mcp-Session-Id: $SID" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' > /dev/null

curl -s --no-buffer -m 172800 -X POST "$URL" "${H[@]}" -H "Mcp-Session-Id: $SID" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"scan_music_folders\",\"arguments\":{\"folders\":[\"$MUSIC\"]}}}" \
  > "$LAST" 2>&1

SUMMARY=$(python3 - "$LAST" <<'PY'
import json, sys
text = open(sys.argv[1], encoding="utf-8", errors="replace").read()
data = [l[5:].strip() for l in text.splitlines() if l.startswith("data:")] or [text.strip()]
try:
    msg = json.loads(data[-1])
except ValueError:
    print("FAILED: unreadable scan response"); sys.exit(1)
if "error" in msg:
    print("FAILED: " + json.dumps(msg["error"])[:300]); sys.exit(1)
result = msg.get("result", {})
if result.get("isError"):
    print("FAILED: " + result["content"][0]["text"][:300]); sys.exit(1)
body = json.loads(result["content"][0]["text"])
print("scan ok " + " ".join(f"{k}={v}" for k, v in body.items() if isinstance(v, (int, float))))
PY
)
STATUS=$?
log "$SUMMARY"
[ $STATUS -eq 0 ] || exit 1

mkdir -p "$PUBLISH_DIR"
PUBLISHED=$(python3 - "$CATALOG" "$PUBLISH_DIR" "$MUSIC" 2>&1 <<'PY'
import os, sqlite3, sys, urllib.parse
source, target_dir, music = sys.argv[1], sys.argv[2], sys.argv[3]
tmp = os.path.join(target_dir, "catalog.db.tmp")
if os.path.exists(tmp):
    os.remove(tmp)
src = sqlite3.connect("file:" + urllib.parse.quote(source) + "?immutable=1", uri=True)
dst = sqlite3.connect(tmp)
src.backup(dst)
src.close()
verdict = dst.execute("PRAGMA integrity_check(1)").fetchone()[0]
if verdict != "ok":
    dst.close(); os.remove(tmp)
    print("snapshot NOT published, integrity_check: " + verdict); sys.exit(1)
dst.execute("PRAGMA journal_mode=DELETE")
# The one scan folder a reader rebases paths from (ServerSnapshotSync); the server's own list is empty.
dst.execute("DELETE FROM folders WHERE kind = 'scan'")
dst.execute("INSERT INTO folders(kind, path) VALUES('scan', ?)", (music,))
dst.execute("DELETE FROM tracks WHERE path NOT LIKE ? ESCAPE '\\'",
            (music.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_") + "/%",))
dst.commit()
tracks = dst.execute("SELECT count(*) FROM tracks").fetchone()[0]
dst.close()
os.replace(tmp, os.path.join(target_dir, "catalog.db"))
print(f"snapshot published: {tracks} tracks")
PY
)
STATUS=$?
log "$PUBLISHED"
exit $STATUS
