#!/usr/bin/env bash
# Catches the failure that actually happens: the box is fine, a container is not.
#
# It cannot page anyone when the whole machine is down — nothing running ON the machine can. That job
# belongs to an external checker (see ops/README.md). What this covers is the common case: the API
# stops answering, or Postgres goes unhealthy, while everything else looks normal and nobody notices
# until an employee cannot check in.
set -euo pipefail

APP_DIR=/opt/attendanceqr
LOG="$APP_DIR/backups/watchdog.log"
# The public URL rather than the container: the backend port is not published to the host, and
# checking end-to-end also covers Caddy and the certificate, which are just as capable of taking the
# service down as the API itself.
HEALTH_URL=${HEALTH_URL:-https://api.qrlog.az/health}

exec >>"$LOG" 2>&1

fail() { echo "$(date -Is) $*"; }

# The API, as a user meets it.
if ! curl -fsS --max-time 10 "$HEALTH_URL" >/dev/null 2>&1; then
  fail "API not answering — restarting backend"
  docker restart attendanceqr-backend-1 >/dev/null 2>&1 || fail "restart failed"
  sleep 20
  if curl -fsS --max-time 10 "$HEALTH_URL" >/dev/null 2>&1; then
    fail "backend recovered after restart"
  else
    fail "STILL DOWN after restart — needs a human"
  fi
fi

# Any container that stopped and did not come back.
for name in attendanceqr-db-1 attendanceqr-backend-1 attendanceqr-frontend-1 attendanceqr-caddy-1; do
  state=$(docker inspect -f '{{.State.Status}}' "$name" 2>/dev/null || echo missing)
  if [ "$state" != "running" ]; then
    fail "$name is $state — starting"
    docker start "$name" >/dev/null 2>&1 || fail "could not start $name"
  fi
done

# Disk. Postgres and Docker both fail in confusing ways once the disk fills, and it fills slowly
# enough that a warning at 85% is days of notice rather than minutes.
USED=$(df --output=pcent / | tail -1 | tr -dc '0-9')
if [ "$USED" -ge 85 ]; then
  fail "disk at ${USED}% — clear space before it becomes an outage"
fi

# Backups are only a safeguard while they are still being taken. Silence here is the failure mode:
# the cron breaks, nobody notices for months, and the discovery happens during a restore.
NEWEST=$(find "$APP_DIR/backups" -name 'attendanceqr_*.sql.gz' -mtime -2 2>/dev/null | head -1 || true)
if [ -z "$NEWEST" ]; then
  fail "NO BACKUP in the last 48h — the nightly job is not running"
fi
