#!/usr/bin/env bash
# Read-only helpers for the cutover night. NOTHING here changes a database, a container or DNS —
# it counts, dumps, hashes, compares and probes, and it says "STOP" when numbers disagree.
# Every subcommand is parameterised so the same script is rehearsed on staging and run on production.
#
#   snapshot   CONTAINER USER DB OUT.json           row counts of the tables that must match 1:1
#   dump       CONTAINER USER DB OUT.sql.gz [MIN_B] pg_dump | gzip + sha256; refuses a dump < MIN_B
#   compare    CONTAINER USER DB SNAPSHOT.json      counts in a RESTORED db vs the snapshot → exit 5 on mismatch
#   smoke      API_BASE ORIGIN FRONT_BASE           health, frontend, CSP header; with ADMIN_PHONE/ADMIN_PIN
#                                                   also login → /api/diag/queues + today board (read-only)
#   CURL_OPTS  extra curl flags (e.g. "-k --resolve test.qrlog.az:443:127.0.0.1")
set -uo pipefail
CMD=${1:-}; shift || true
TABLES='Tenants Employees Locations AttendanceRecords ProcessedScans DailySummaries PendingPhotoUploads LeaveRecords DeviceBindings AuditLogs EmployeeNotifications PushSubscriptions __EFMigrationsHistory'
psqlc() { docker exec "$1" psql -U "$2" -d "$3" -tAc "$4"; }

case "$CMD" in
snapshot)
  C=$1; U=$2; D=$3; OUT=$4
  { echo "{"; first=1
    for t in $TABLES; do n=$(psqlc "$C" "$U" "$D" "select count(*) from \"$t\";" 2>/dev/null || echo -1)
      [ $first = 1 ] || echo ","; first=0; printf '  "%s": %s' "$t" "$n"; done
    echo; echo "}"; } > "$OUT"
  echo "snapshot → $OUT"; cat "$OUT" ;;

dump)
  C=$1; U=$2; D=$3; OUT=$4; MINB=${5:-100000}
  docker exec "$C" pg_dump -U "$U" -d "$D" --no-owner --clean --if-exists | gzip -6 > "$OUT" || { echo "pg_dump FAILED" >&2; exit 3; }
  SZ=$(stat -c %s "$OUT"); SHA=$(sha256sum "$OUT" | cut -d' ' -f1)
  echo "dump → $OUT  size=$SZ  sha256=$SHA"; echo "$SHA  $(basename "$OUT")" > "$OUT.sha256"
  [ "$SZ" -ge "$MINB" ] || { echo "STOP: dump is only $SZ bytes (< $MINB) — a stub, not a backup" >&2; exit 3; }
  gzip -t "$OUT" || { echo "STOP: gzip integrity failed" >&2; exit 3; } ;;

compare)
  C=$1; U=$2; D=$3; SNAP=$4; bad=0
  for t in $TABLES; do
    want=$(python3 -c "import json,sys; print(json.load(open('$SNAP')).get('$t', -1))")
    have=$(psqlc "$C" "$U" "$D" "select count(*) from \"$t\";" 2>/dev/null || echo -1)
    if [ "$want" = "$have" ]; then printf "  ok   %-24s %s\n" "$t" "$have"; else printf "  !!   %-24s want=%s have=%s\n" "$t" "$want" "$have"; bad=1; fi
  done
  [ $bad = 0 ] && echo "COMPARE OK — every table matches the snapshot" || { echo "STOP: restored database does not match the snapshot" >&2; exit 5; } ;;

smoke)
  API=$1; ORG=$2; FRONT=$3; bad=0; CO=${CURL_OPTS:-}
  # shellcheck disable=SC2086
  h=$(curl -s $CO --max-time 15 "$API/health"); echo "$h" | grep -q '"ok"' && echo "  ok   api /health" || { echo "  !!   api /health → $h"; bad=1; }
  # shellcheck disable=SC2086
  fc=$(curl -s $CO -o /dev/null -w '%{http_code}' --max-time 15 "$FRONT/"); [ "$fc" = 200 ] && echo "  ok   frontend 200" || { echo "  !!   frontend $fc"; bad=1; }
  # shellcheck disable=SC2086
  curl -sI $CO --max-time 15 "$FRONT/" | grep -qi "content-security-policy" && echo "  ok   CSP header" || { echo "  !!   CSP header missing"; bad=1; }
  if [ -n "${ADMIN_PHONE:-}" ]; then
    # shellcheck disable=SC2086
    tok=$(curl -s $CO --max-time 15 -X POST "$API/api/auth/login" -H "Origin: $ORG" -H "Content-Type: application/json" -d "{\"email\":\"$ADMIN_PHONE\",\"password\":\"$ADMIN_PIN\"}" | python3 -c "import sys,json; print(json.load(sys.stdin).get('token',''))" 2>/dev/null)
    [ -n "$tok" ] && echo "  ok   admin login" || { echo "  !!   admin login failed"; bad=1; }
    if [ -n "$tok" ]; then
      # shellcheck disable=SC2086
      q=$(curl -s $CO --max-time 15 "$API/api/diag/queues" -H "Origin: $ORG" -H "Authorization: Bearer $tok")
      echo "$q" | python3 -c "import sys,json; p=json.load(sys.stdin)['photo']; ok = p['failed']==0 and p['dropped']==0; print(('  ok   ' if ok else '  !!   ')+'diag queues: pending=%s failed=%s dropped=%s' % (p['pending'],p['failed'],p['dropped'])); sys.exit(0 if ok else 1)" || bad=1
      # shellcheck disable=SC2086
      tc=$(curl -s $CO -o /dev/null -w '%{http_code} %{time_total}s' --max-time 30 "$API/api/reports/today" -H "Origin: $ORG" -H "Authorization: Bearer $tok"); echo "$tc" | grep -q '^200' && echo "  ok   today board $tc" || { echo "  !!   today board $tc"; bad=1; }
    fi
  fi
  [ $bad = 0 ] && echo "SMOKE OK" || { echo "STOP: smoke has failures" >&2; exit 6; } ;;
*) echo "usage: cutover-checklist.sh snapshot|dump|compare|smoke …" >&2; exit 2 ;;
esac
