#!/usr/bin/env bash
# Publishes the `hr-modulu` branch to hr.test.qrlog.az, by itself, every couple of minutes.
#
# THE COPY THAT RUNS IS /opt/qrlog-hr-infra/hr-autodeploy.sh (root-owned; cron: /etc/cron.d/qrlog-hr).
# This file is the record of it. Nothing this script executes is read from the branch it deploys: the
# compose file and the env file are the server's own, and the branch contributes only the source that
# gets built. See docker-compose.hr.yml for why the line is drawn exactly there.
set -euo pipefail

SRC=/opt/qrlog-hr
INFRA=/opt/qrlog-hr-infra
LOG=/var/log/qrlog-hr-deploy.log
BRANCH=hr-modulu

exec >>"$LOG" 2>&1

# One run at a time. A cold .NET + Vite build outlasts the two-minute cron, and two builds of the same
# project racing each other is how an environment ends up half one commit and half another.
exec 9>/var/lock/qrlog-hr-deploy.lock
flock -n 9 || exit 0

# Caddy reaches this environment over its own network. Any recreate of the Caddy container (a production
# deploy that changes the Caddyfile does one) drops that attachment, so every run puts it back: at worst
# hr.test.qrlog.az is dark for two minutes, and production is never involved.
docker network inspect qrlog_hr_edge >/dev/null 2>&1 || docker network create qrlog_hr_edge >/dev/null
docker network connect qrlog_hr_edge attendanceqr-caddy-1 >/dev/null 2>&1 || true

cd "$SRC"
git fetch -q origin "$BRANCH"

LOCAL=$(git rev-parse HEAD)
REMOTE=$(git rev-parse "origin/$BRANCH")
if [ "$LOCAL" = "$REMOTE" ] && [ -z "${FORCE:-}" ]; then
  exit 0
fi

echo "--- $(date -Is) deploying $REMOTE"
git reset -q --hard "origin/$BRANCH"
# Only what is committed gets built — no leftover file from an earlier commit rides along.
git clean -qfdx
git log -1 --pretty='  %an: %s'

# --env-file replaces Compose's automatic .env, and an explicit -f disables override files, so a `.env`
# or `docker-compose.override.yml` committed to the branch is ignored rather than obeyed.
if docker compose -f "$INFRA/docker-compose.hr.yml" --project-directory "$SRC" \
     --env-file "$INFRA/.env.hr" up -d --build 2>&1 | tail -3; then
  echo "  deployed"
else
  # Left as-is, like staging: a broken test environment is there to be looked at.
  echo "  BUILD FAILED — hr.test.qrlog.az is left on the previous containers"
fi
