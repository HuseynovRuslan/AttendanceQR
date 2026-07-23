#!/usr/bin/env bash
# Clears Docker's build leftovers.
#
# Every deploy leaves roughly 700 MB of build cache behind. Nothing removes it, so it grows with each
# release until the disk fills — and a full disk stops Postgres accepting writes, which presents as
# "nobody can check in" with no obvious cause. One day of releases had already left 11 GB.
#
# Only cache older than a week goes: recent layers are what make the next deploy fast, and throwing
# them away would turn every build into a full rebuild.
set -euo pipefail

APP_DIR=/opt/attendanceqr
LOG="$APP_DIR/backups/prune.log"

exec >>"$LOG" 2>&1
echo "--- $(date -Is) prune start"

BEFORE=$(df --output=used / | tail -1)

docker builder prune -af --filter 'until=168h' 2>&1 | tail -2
# Images no container references. Keeps the running ones; only superseded builds go.
docker image prune -af --filter 'until=168h' 2>&1 | tail -2

AFTER=$(df --output=used / | tail -1)
echo "freed $(( (BEFORE - AFTER) / 1024 )) MB · disk now $(df -h / | tail -1 | awk '{print $5}') full"
echo "--- $(date -Is) prune done"
