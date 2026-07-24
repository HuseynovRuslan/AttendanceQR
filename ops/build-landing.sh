#!/usr/bin/env bash
# Builds the marketing site (landing/) and publishes it into ./landing-dist — the directory Caddy
# bind-mounts as /srv/qrlog and serves at qrlog.az.
#
# The build runs inside node:22-alpine, so the VM needs no Node, no npm and no version of either
# that happens to match the developer's. Nothing here touches the app, the API or the database:
# the worst this script can do is leave qrlog.az showing the previous build.
set -euo pipefail

APP_DIR=${APP_DIR:-/opt/attendanceqr}
cd "$APP_DIR"

OUT="$APP_DIR/landing-dist"
TMP=$(mktemp -d "$APP_DIR/.landing-build.XXXXXX")
trap 'rm -rf "$TMP"' EXIT

echo "→ building landing/"
DOCKER_BUILDKIT=1 docker build --target export --output "type=local,dest=$TMP" ./landing

# A build that "succeeds" but emits nothing would otherwise wipe the live site on the next line.
[ -s "$TMP/index.html" ] || {
	echo "FAILED: the build produced no index.html — landing-dist left untouched."
	exit 1
}

mkdir -p "$OUT"
# Sync into the existing directory instead of replacing it: Caddy holds this path as a bind mount,
# and swapping the directory itself would leave the container reading the old, now-orphaned inode.
if command -v rsync >/dev/null 2>&1; then
	rsync -a --delete "$TMP"/ "$OUT"/
else
	find "$OUT" -mindepth 1 -delete
	cp -a "$TMP"/. "$OUT"/
fi

echo "DONE — $(find "$OUT" -name '*.html' | wc -l) pages published to landing-dist"
