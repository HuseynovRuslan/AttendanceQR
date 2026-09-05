#!/usr/bin/env bash
# Deploys main to production — the one command that touches the system 114 people depend on.
#
# It refuses to run during the scan peaks. The rule lived only in a README, and a rule that depends
# on someone remembering it at 08:10 while rushing a fix is not a safeguard. A mistake in those
# windows means people cannot clock in, and the clock-in is what they are paid on.
set -euo pipefail

APP_DIR=/opt/attendanceqr
cd "$APP_DIR"

# Tell the watchdog to keep its hands off while this runs.
#
# It cost us a real one: the watchdog fires every 5 minutes, and its first act when /health does not
# answer is `docker restart backend`. During a deploy /health is legitimately down for a few seconds
# — longer when a migration is applying, because migrations run at startup — so on 2026-08-11 at
# 09:40, one minute after a deploy, the watchdog restarted a backend that was in the middle of adding
# a column. It got away with it (and did the same on 2026-07-22), but restarting a process mid-
# migration is how a half-applied schema happens.
#
# The trap covers every exit, including the rollback paths and a Ctrl-C, so a crashed deploy cannot
# leave the watchdog muted.
DEPLOY_LOCK="$APP_DIR/backups/.deploying"
mkdir -p "$APP_DIR/backups"
touch "$DEPLOY_LOCK"
trap 'rm -f "$DEPLOY_LOCK"' EXIT

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

# Did this pull change the Caddyfile? It matters because the file is bind-mounted into the caddy
# container and git replaces its INODE on pull — the running Caddy keeps reading the old copy, and
# even `caddy reload` inside the container sees the old file. The 2026-09-04 outage fix (the
# patient_proxy retry) would have silently never activated. So: validate the new file NOW, before
# anything is touched — an invalid config force-recreated later would take down every host behind
# this Caddy, the neighbour projects included — and force-recreate caddy only after the app proves
# healthy, at the very end.
CADDY_CHANGED=""
if ! git diff --quiet "$PREV" HEAD -- Caddyfile; then
  CADDY_CHANGED=1
  echo "→ Caddyfile changed — validating the new config"
  if docker run --rm -v "$APP_DIR/Caddyfile:/etc/caddy/Caddyfile:ro" \
       caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile >/dev/null 2>&1; then
    echo "  valid ✓ (caddy will be force-recreated after the app is healthy)"
  else
    echo "REFUSING: the new Caddyfile does not validate — deploy aborted before touching anything."
    echo "  docker run --rm -v $APP_DIR/Caddyfile:/etc/caddy/Caddyfile:ro caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile"
    git reset -q --hard "$PREV"
    exit 1
  fi
fi

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

# Waits for the API to answer through the edge. Healthy deploys pass in seconds. A DEAD backend now
# takes up to ~3 minutes to declare, not ~60s: the probe goes through Caddy, whose patient_proxy
# holds each failed probe for curl's full 6s instead of answering an instant 502. That stretch is
# accepted deliberately — it is extra headroom for a slow migration, during which a premature
# rollback is the worse outcome (see the watchdog note above).
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
  # Only now, with the app proven, does a changed Caddyfile reach the proxy. Recreate is the ONLY
  # way it can (the bind-mount inode problem above) and it drops every edge for a couple of
  # seconds — which is why it is last, behind the validate gate and the health gate.
  if [ -n "$CADDY_CHANGED" ]; then
    echo "→ Caddyfile changed — force-recreating caddy"
    docker compose -f docker-compose.prod.yml up -d --force-recreate caddy
    if wait_healthy; then
      echo "  edge healthy ✓"
    else
      echo "  EDGE NOT HEALTHY after caddy recreate — rolling the Caddyfile back to ${PREV:0:7}"
      # The old file is left in the working tree ON PURPOSE: the tree then matches what the proxy is
      # actually running (the next deploy's reset --hard cleans it up, and its validate gate will
      # reject the broken file again if it is still on main).
      git checkout -q "$PREV" -- Caddyfile
      docker compose -f docker-compose.prod.yml up -d --force-recreate caddy
      echo "  the CODE deploy stood; the CADDYFILE did not. Fix it and redeploy."
      exit 1
    fi
  fi
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
