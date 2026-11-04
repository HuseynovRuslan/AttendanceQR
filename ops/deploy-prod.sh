#!/usr/bin/env bash
# Deploys main to production — the one command that touches the system 114 people depend on.
#
# It refuses to run during the scan peaks. The rule lived only in a README, and a rule that depends
# on someone remembering it at 08:10 while rushing a fix is not a safeguard. A mistake in those
# windows means people cannot clock in, and the clock-in is what they are paid on.
#
# The order is the safety: everything that can refuse runs before the checkout moves (time, the exact
# commit, the new Caddyfile, the safety backup); the build runs before anything restarts; and once the
# checkout has moved, every way out — a failed build, a failed start, a dead health check, an
# unexpected error, Ctrl-C — ends with the checkout and the containers back on the commit that was
# running. The checkout never stays on a commit that is not serving.
set -euo pipefail

# An approved release (EXPECTED_SHA, set by the GitHub Actions release helper — see ops/CLOUD-DEPLOY.md)
# never takes an override: nobody is at the server to own the risk, so the peak window and the backup
# gate below are not optional. Checked before anything else, so a refused call touches nothing.
if [ -n "${EXPECTED_SHA:-}" ] && [ "${FORCE:-0}" != "0" ]; then
  echo "REFUSING: FORCE is not accepted for an approved release." >&2
  exit 1
fi

APP_DIR=/opt/attendanceqr
# What is actually running, written only here, root-owned and outside the checkout: the release helper
# (ops/prod-release.sh) refuses to build on a checkout that does not match it.
RECORD_DIR=/var/lib/qrlog-deploy
HEALTH_URL=https://api.qrlog.az/health
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

# One deploy at a time — a release from GitHub Actions, its checks, and one typed at the server must
# not interleave their resets and rebuilds. flock on the checkout directory itself, not the watchdog's
# marker file: a killed deploy leaves a stale file behind, but its lock goes with the process, and
# there is no lock file to create or to point somewhere else.
exec 9<"$APP_DIR"
if ! flock -n 9; then
  echo "REFUSING: another deploy (or a release check) is running right now."
  exit 1
fi

touch "$DEPLOY_LOCK"
TMP_DIR=$(mktemp -d)
MOVED=""     # set while the checkout is on the new commit — from then on, every exit must undo it
FINISHED=""
PREV=""

record_deployed() {
  install -d -m 0755 "$RECORD_DIR" &&
    printf '%s\n' "$1" >"$RECORD_DIR/deployed-commit.new" &&
    mv -f "$RECORD_DIR/deployed-commit.new" "$RECORD_DIR/deployed-commit"
}
note_deployed() { record_deployed "$1" || echo "  (could not write the deploy record in $RECORD_DIR)"; }

build_images() { docker compose -f docker-compose.prod.yml build backend frontend; }
start_containers() { docker compose -f docker-compose.prod.yml up -d --no-build backend frontend; }

# Waits for the API to answer through the edge. Healthy deploys pass in seconds. A DEAD backend now
# takes up to ~3 minutes to declare, not ~60s: the probe goes through Caddy, whose patient_proxy
# holds each failed probe for curl's full 6s instead of answering an instant 502. That stretch is
# accepted deliberately — it is extra headroom for a slow migration, during which a premature
# rollback is the worse outcome (see the watchdog note above).
wait_healthy() {
  for _ in $(seq 1 20); do
    if curl -fsS --max-time 6 "$HEALTH_URL" >/dev/null 2>&1; then
      return 0
    fi
    sleep 3
  done
  return 1
}

# Back to the commit that was running, rebuilt from it. Never exits; says where production ended up.
#
# ⚠️ This rolls back CODE, not the DATABASE. EF migrations are forward-only and have already applied.
# Additive migrations (a new nullable column, a new table) are fine — the old code simply ignores
# them. A migration that RENAMES or DROPS something is not: restore the dump this script just took.
roll_back() {
  echo "→ rolling back to ${PREV:0:7}"
  git reset -q --hard "$PREV"
  MOVED=""
  if build_images && start_containers && wait_healthy; then
    note_deployed "$PREV"
    echo "  rolled back to ${PREV:0:7} and healthy ✓ — the deploy did NOT stick."
    echo "  Check what broke:  docker logs attendanceqr-backend-1"
    return 0
  fi
  note_deployed unknown
  echo "  ROLLBACK IS ALSO UNHEALTHY — this needs hands now."
  echo "  docker logs attendanceqr-backend-1   (and check the migration in backups/)"
  return 1
}

# Puts the checkout back when nothing was restarted yet (a failed build, a peak reached during it).
restore_checkout() {
  git reset -q --hard "$PREV"
  MOVED=""
  echo "  nothing was restarted; the checkout is back on ${PREV:0:7}"
}

on_exit() {
  local status=$?
  set +e
  if [ -n "$MOVED" ] && [ -z "$FINISHED" ]; then
    echo "  STOPPED HALF-WAY (exit $status)"
    roll_back
    status=1
  fi
  rm -rf "$TMP_DIR"
  rm -f "$DEPLOY_LOCK"
  exit "$status"
}
trap on_exit EXIT

# The Asia/Baku minute, from ONE `date` call (so the hour and the minute belong to the same instant);
# empty when this machine cannot tell Baku time — it would silently answer in UTC, four hours off.
baku_minute() {
  local h m zone
  read -r h m zone <<<"$(TZ=Asia/Baku date '+%H %M %z')"
  [ "$zone" = "+0400" ] || return 0
  echo $((10#$h * 60 + 10#$m))
}
# 07:30–09:30 and 17:00–19:00 — or unknown, which counts as a peak.
in_peak() {
  local now
  now=$(baku_minute)
  [ -z "$now" ] && return 0
  { [ "$now" -ge 450 ] && [ "$now" -lt 570 ]; } || { [ "$now" -ge 1020 ] && [ "$now" -lt 1140 ]; }
}

if in_peak; then
  echo "REFUSING: $(TZ=Asia/Baku date +%H:%M) (Asia/Baku) is a scan peak (07:30–09:30 / 17:00–19:00), or this server cannot tell Baku time."
  echo "People are clocking in right now. Wait for the window to pass, or override with:"
  echo "    FORCE=1 $0"
  [ "${FORCE:-0}" = "1" ] || exit 1
  echo "FORCE set — proceeding anyway."
fi

# Where we are BEFORE anything moves — the commit to come back to if the new one cannot serve traffic.
PREV=$(git rev-parse --verify -q 'HEAD^{commit}')

echo "→ fetching main"
# Exactly the branch: no tags (a tag named "origin/main" would otherwise win over the branch of that
# name), into the full ref name, and the commit it names is what gets deployed — never a name that
# could move between the check and the reset. No background gc: it would inherit the lock.
git -c gc.auto=0 fetch -q --no-tags origin "+refs/heads/main:refs/remotes/origin/main" 9<&-
TARGET=$(git rev-parse --verify -q 'refs/remotes/origin/main^{commit}')
# An approved cloud release must deploy exactly its tested commit. If someone
# merged a newer commit after approval, stop before touching the live checkout.
if [ -n "${EXPECTED_SHA:-}" ]; then
  if [[ ! "$EXPECTED_SHA" =~ ^[0-9a-f]{40}$ ]]; then
    echo "REFUSING: malformed EXPECTED_SHA" >&2
    exit 1
  fi
  if [ "$TARGET" != "$EXPECTED_SHA" ]; then
    echo "REFUSING: main changed after release approval" >&2
    exit 1
  fi
fi
echo "  main is at ${TARGET:0:7}; the checkout is on ${PREV:0:7}"

# Did this release change the Caddyfile? It matters because the file is bind-mounted into the caddy
# container and git replaces its INODE on checkout — the running Caddy keeps reading the old copy, and
# even `caddy reload` inside the container sees the old file. The 2026-09-04 outage fix (the
# patient_proxy retry) would have silently never activated. So: validate the new file NOW, from the
# release itself, before anything is touched — an invalid config force-recreated later would take
# down every host behind this Caddy, the neighbour projects included — and force-recreate caddy only
# after the app proves healthy, at the very end.
CADDY_CHANGED=""
if ! git diff --quiet "$PREV" "$TARGET" -- Caddyfile; then
  CADDY_CHANGED=1
  echo "→ Caddyfile changed — validating the new config"
  git show "$TARGET:Caddyfile" >"$TMP_DIR/Caddyfile"
  if docker run --rm -v "$TMP_DIR/Caddyfile:/etc/caddy/Caddyfile:ro" \
       caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile >/dev/null 2>&1; then
    echo "  valid ✓ (caddy will be force-recreated after the app is healthy)"
  else
    echo "REFUSING: the new Caddyfile does not validate — deploy aborted before touching anything."
    echo "  git show ${TARGET:0:7}:Caddyfile > /tmp/Caddyfile && docker run --rm -v /tmp/Caddyfile:/etc/caddy/Caddyfile:ro caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile"
    exit 1
  fi
fi

# A dump right before the change, so a bad migration has a same-minute restore point that does not
# depend on last night's backup — taken before the checkout moves, so a failed backup leaves nothing
# to undo.
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

echo "→ checking out ${TARGET:0:7}"
MOVED=1
git reset -q --hard "$TARGET"
git log -1 --pretty='  now on: %h %s'

echo "→ building backend + frontend"
if ! build_images; then
  echo "  BUILD FAILED"
  restore_checkout
  exit 1
fi

# The build takes minutes. Check the window again right before anything restarts: a deploy that began
# at 07:25 must not recreate containers at 07:31.
if in_peak && [ "${FORCE:-0}" != "1" ]; then
  echo "REFUSING to restart: the build ran into a scan peak."
  restore_checkout
  exit 1
fi

echo "→ starting backend + frontend"
if start_containers && wait_healthy; then
  echo "  healthy ✓"
  note_deployed "$TARGET"
  FINISHED=1
  # Only now, with the app proven, does a changed Caddyfile reach the proxy. Recreate is the ONLY
  # way it can (the bind-mount inode problem above) and it drops every edge for a couple of
  # seconds — which is why it is last, behind the validate gate and the health gate.
  if [ -n "$CADDY_CHANGED" ]; then
    echo "→ Caddyfile changed — force-recreating caddy"
    if docker compose -f docker-compose.prod.yml up -d --force-recreate caddy && wait_healthy; then
      echo "  edge healthy ✓"
    else
      echo "  EDGE NOT HEALTHY after caddy recreate — rolling the Caddyfile back to ${PREV:0:7}"
      # The old file is left in the working tree ON PURPOSE: the tree then matches what the proxy is
      # actually running (the next deploy's reset --hard cleans it up, and its validate gate will
      # reject the broken file again if it is still on main).
      git checkout -q "$PREV" -- Caddyfile
      docker compose -f docker-compose.prod.yml up -d --force-recreate caddy || true
      echo "  the CODE deploy stood; the CADDYFILE did not. Fix it and redeploy."
      exit 1
    fi
  fi
  echo "DONE — deployed $(git rev-parse --short HEAD)"
  exit 0
fi

# Unhealthy, or the start itself failed (possibly half-way: one container recreated, the other not).
# Previously this printed a hint and exited, leaving the broken containers serving — so a failed
# deploy was an outage that waited for someone to notice. Go back to the last commit that worked and
# rebuild from it.
echo "  NOT HEALTHY after the start — rolling back"
roll_back || true
exit 1
