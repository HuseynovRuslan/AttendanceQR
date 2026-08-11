#!/usr/bin/env bash
# Catches the failure that actually happens: the box is fine, a container is not.
#
# It cannot page anyone when the whole machine is down — nothing running ON the machine can. That job
# belongs to an external checker (see ops/README.md). What this covers is the common case: the API
# stops answering, or Postgres goes unhealthy, while everything else looks normal and nobody notices
# until an employee cannot check in.
#
# Everything it finds is ALSO sent to a human (ops/alert.sh). Until then this wrote to
# backups/watchdog.log and stopped there — a file nobody opens, which made it a detector with no one
# on the other end. Its own note about backups said the failure mode is silence; that applied to the
# watchdog itself more than to anything it watched.
set -euo pipefail

APP_DIR=/opt/attendanceqr
LOG="$APP_DIR/backups/watchdog.log"
STATE_DIR="$APP_DIR/backups/watchdog-state"
# The public URL rather than the container: the backend port is not published to the host, and
# checking end-to-end also covers Caddy and the certificate, which are just as capable of taking the
# service down as the API itself.
HEALTH_URL=${HEALTH_URL:-https://api.qrlog.az/health}
# While something stays broken, remind this often. Every run (5 min) would be 288 messages a day and
# would teach everyone to mute the channel, which costs more than it buys.
REPEAT_HOURS=${ALERT_REPEAT_HOURS:-6}

exec >>"$LOG" 2>&1
mkdir -p "$STATE_DIR"

# shellcheck source=/dev/null
. "$(dirname "$0")/alert.sh"

fail() { echo "$(date -Is) $*"; }

# A problem, tagged with a stable key so the same fault is recognisable across runs.
#
# Alerts on the first sighting, then at most every REPEAT_HOURS while it persists. `|| true`
# throughout: this script runs under `set -e`, and a failed send must never abort the checks that
# come after it — a broken alert channel is not a reason to stop watching the disk.
problem() {
  local key=$1; shift
  fail "$*"
  local stamp="$STATE_DIR/$key"
  if [ -f "$stamp" ] && [ -n "$(find "$stamp" -mmin "-$((REPEAT_HOURS * 60))" 2>/dev/null)" ]; then
    return 0 # already reported recently — stay quiet
  fi
  send_alert "$*" || true
  touch "$stamp" || true
}

# The counterpart: a key that was failing and now is not. Worth a message of its own — "it is back"
# is the half that lets someone stop worrying, and without it the only signal is an absence.
resolved() {
  local key=$1; shift
  local stamp="$STATE_DIR/$key"
  [ -f "$stamp" ] || return 0
  rm -f "$stamp" || true
  fail "$*"
  send_alert "✅ $*" || true
}

# The API, as a user meets it.
if ! curl -fsS --max-time 10 "$HEALTH_URL" >/dev/null 2>&1; then
  fail "API not answering — restarting backend"
  docker restart attendanceqr-backend-1 >/dev/null 2>&1 || fail "restart failed"
  sleep 20
  if curl -fsS --max-time 10 "$HEALTH_URL" >/dev/null 2>&1; then
    # Still worth telling someone: the API went down and something had to restart it. A self-healing
    # event that nobody hears about is how a daily crash goes unnoticed for a month.
    problem api-restarted "API was not answering — backend restarted and recovered"
  else
    problem api-down "API STILL DOWN after a restart — needs a human"
  fi
else
  resolved api-down "API is answering again"
  resolved api-restarted "API healthy"
fi

# Any container that stopped and did not come back.
for name in attendanceqr-db-1 attendanceqr-backend-1 attendanceqr-frontend-1 attendanceqr-caddy-1; do
  state=$(docker inspect -f '{{.State.Status}}' "$name" 2>/dev/null || echo missing)
  if [ "$state" != "running" ]; then
    if docker start "$name" >/dev/null 2>&1; then
      problem "container-$name" "$name was $state — started again"
    else
      problem "container-$name" "$name is $state and WILL NOT START — needs a human"
    fi
  else
    resolved "container-$name" "$name is running again"
  fi
done

# Disk. Postgres and Docker both fail in confusing ways once the disk fills, and it fills slowly
# enough that a warning at 85% is days of notice rather than minutes.
USED=$(df --output=pcent / | tail -1 | tr -dc '0-9')
if [ "$USED" -ge 85 ]; then
  problem disk "disk at ${USED}% — clear space before it becomes an outage"
else
  resolved disk "disk back below 85% (now ${USED}%)"
fi

# Backups are only a safeguard while they are still being taken. Silence here is the failure mode:
# the cron breaks, nobody notices for months, and the discovery happens during a restore.
NEWEST=$(find "$APP_DIR/backups" -name 'attendanceqr_*.sql.gz' -mtime -2 2>/dev/null | head -1 || true)
if [ -z "$NEWEST" ]; then
  problem backup "NO BACKUP in the last 48h — the nightly job is not running"
else
  resolved backup "backups are running again"
fi
