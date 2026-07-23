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

echo "→ pulling main"
git fetch -q origin main
git reset -q --hard origin/main
git log -1 --pretty='  now on: %h %s'

# A dump right before the change, so a bad migration has a same-minute restore point that does not
# depend on last night's backup.
echo "→ safety backup"
./ops/backup.sh >/dev/null 2>&1 && echo "  backed up" || echo "  WARNING: backup failed — check backups/backup.log before continuing"

echo "→ building & starting backend + frontend"
docker compose -f docker-compose.prod.yml up -d --build backend frontend

echo "→ waiting for health"
for i in $(seq 1 20); do
  if curl -fsS --max-time 6 https://api.qrlog.az/health >/dev/null 2>&1; then
    echo "  healthy ✓"
    echo "DONE — deployed $(git rev-parse --short HEAD)"
    exit 0
  fi
  sleep 3
done
echo "  STILL NOT HEALTHY after ~60s — check: docker logs attendanceqr-backend-1"
exit 1
