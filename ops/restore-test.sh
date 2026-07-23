#!/usr/bin/env bash
# Proves the backups can actually be restored.
#
# An untested backup is a belief, not a safeguard, and the belief is always discovered to be wrong on
# the day it matters. This restores the newest dump into a throwaway database beside the live one and
# checks the rows are really there, then drops it. It never touches the production database.
set -euo pipefail

APP_DIR=/opt/attendanceqr
ENV_FILE="$APP_DIR/.env"
LOG="$APP_DIR/backups/restore-test.log"
SCRATCH=restore_check

exec >>"$LOG" 2>&1
echo "--- $(date -Is) restore test start"

# shellcheck disable=SC1090
set -a; . "$ENV_FILE"; set +a

LATEST=$(ls -t "$APP_DIR"/backups/attendanceqr_*.sql.gz 2>/dev/null | head -1 || true)
if [ -z "$LATEST" ]; then
  echo "FAIL: no backup to test"
  exit 1
fi
echo "testing $(basename "$LATEST")"

docker exec attendanceqr-db-1 psql -U "$POSTGRES_USER" -d postgres -q \
  -c "DROP DATABASE IF EXISTS $SCRATCH;" -c "CREATE DATABASE $SCRATCH;"

gunzip -c "$LATEST" | docker exec -i attendanceqr-db-1 psql -U "$POSTGRES_USER" -d "$SCRATCH" -q >/dev/null

# Restoring without error is not the same as restoring the data. These are the tables the business
# cannot lose; a dump that produces empty ones has failed even though every command succeeded.
FAILED=0
for pair in "Employees:1" "AttendanceRecords:1" "Tenants:1"; do
  table="${pair%%:*}"; least="${pair##*:}"
  n=$(docker exec attendanceqr-db-1 psql -U "$POSTGRES_USER" -d "$SCRATCH" -t -A \
        -c "SELECT count(*) FROM \"$table\";" 2>/dev/null || echo 0)
  echo "  $table = $n"
  if [ "$n" -lt "$least" ]; then
    echo "  FAIL: $table came back empty"
    FAILED=1
  fi
done

docker exec attendanceqr-db-1 psql -U "$POSTGRES_USER" -d postgres -q -c "DROP DATABASE $SCRATCH;"

if [ "$FAILED" -ne 0 ]; then
  echo "--- $(date -Is) RESTORE TEST FAILED"
  exit 1
fi
echo "--- $(date -Is) restore test passed"
