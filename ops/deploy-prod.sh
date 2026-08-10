#!/usr/bin/env bash
# Deploys main to production — the one command that touches the system 114 people depend on.
#
# It refuses to run during the scan peaks. The rule lived only in a README, and a rule that depends
# on someone remembering it at 08:10 while rushing a fix is not a safeguard. A mistake in those
# windows means people cannot clock in, and the clock-in is what they are paid on.
set -euo pipefail

APP_DIR=/opt/attendanceqr
cd "$APP_DIR"

# Local wall-clock hour (server is UTC+2, same as the staff).
HOUR=$(date +%-H)
MIN=$(date +%-M)
NOWMIN=$((HOUR * 60 + MIN))
in_window() { [ "$NOWMIN" -ge "$1" ] && [ "$NOWMIN" -lt "$2" ]; }

# 07:30–09:30 and 17:00–19:00.
if in_window 450 570 || in_window 1020 1140; then
  echo "REFUSING: $(date +%H:%M) is a scan peak (07:30–09:30 / 17:00–19:00)."
  echo "People are clocking in right now. Wait for the window to pass, or override with:"
  echo "    FORCE=1 $0"
  [ "${FORCE:-0}" = "1" ] || exit 1
  echo "FORCE set — proceeding anyway."
fi

# Where we are BEFORE the pull — the commit to come back to if the new one cannot serve traffic.
# Captured first, because after the reset below there is no other record of it.
PREV=$(git rev-parse HEAD)

echo "→ pulling main"
git fetch -q origin main
git reset -q --hard origin/main
git log -1 --pretty='  now on: %h %s'

# A dump right before the change, so a bad migration has a same-minute restore point that does not
# depend on last night's backup.
#
# This is a GATE, not a warning. It used to be `... || echo "WARNING"`, which meant: the one safeguard
# against a bad migration fails, and we deploy anyway — into migrations that apply themselves at
# startup. The only moment that dump matters is the moment we would be skipping it.
echo "→ safety backup"
if ./ops/backup.sh >/dev/null 2>&1; then
  echo "  backed up"
else
  echo "REFUSING: the safety backup failed — see backups/backup.log"
  echo "  Migrations apply themselves at startup; without this dump a bad one has no restore point."
  echo "  Fix the backup, or override with:  FORCE=1 $0"
  [ "${FORCE:-0}" = "1" ] || exit 1
  echo "  FORCE set — deploying with NO safety backup."
fi

start_containers() { docker compose -f docker-compose.prod.yml up -d --build backend frontend; }

# ~60s for the API to answer. Returns non-zero if it never does.
wait_healthy() {
  for _ in $(seq 1 20); do
    if curl -fsS --max-time 6 https://api.qrlog.az/health >/dev/null 2>&1; then
      return 0
    fi
    sleep 3
  done
  return 1
}

echo "→ building & starting backend + frontend"
start_containers

echo "→ waiting for health"
if wait_healthy; then
  echo "  healthy ✓"
  echo "DONE — deployed $(git rev-parse --short HEAD)"
  exit 0
fi

# Unhealthy. Previously this printed a hint and exited, leaving the broken containers serving — so a
# failed deploy was an outage that waited for someone to notice. Go back to the last commit that
# worked and rebuild from it.
#
# ⚠️ This rolls back CODE, not the DATABASE. EF migrations are forward-only and have already applied.
# Additive migrations (a new nullable column, a new table) are fine — the old code simply ignores
# them. A migration that RENAMES or DROPS something is not: restore the dump this script just took.
echo "  NOT HEALTHY after ~60s — rolling back to ${PREV:0:7}"
git reset -q --hard "$PREV"
start_containers

if wait_healthy; then
  echo "  rolled back to ${PREV:0:7} and healthy ✓ — the deploy did NOT stick."
  echo "  Check what broke:  docker logs attendanceqr-backend-1"
  exit 1
fi

echo "  ROLLBACK IS ALSO UNHEALTHY — this needs hands now."
echo "  docker logs attendanceqr-backend-1   (and check the migration in backups/)"
exit 1
