#!/usr/bin/env bash
# Publishes the `staging` branch to test.qrlog.az, by itself, every couple of minutes.
#
# It exists so that nobody needs a server password to try their work. Handing out root so a colleague
# can deploy is not a trust question — one wrong command destroys production, and someone who does
# not write code has no way to tell which command that is. They push to a branch; the server does the
# rest, and can only ever build that branch.
#
# Runs from its own clone at /opt/qrlog-staging. Production's checkout is never touched: if staging
# and production shared a working copy, a production deploy would quietly build whatever branch was
# last checked out — the exact accident this is meant to prevent.
set -euo pipefail

DIR=/opt/qrlog-staging
LOG=/opt/attendanceqr/backups/staging-deploy.log
BRANCH=staging

exec >>"$LOG" 2>&1

cd "$DIR"
git fetch -q origin "$BRANCH"

LOCAL=$(git rev-parse HEAD)
REMOTE=$(git rev-parse "origin/$BRANCH")
[ "$LOCAL" = "$REMOTE" ] && exit 0   # nothing new; stay quiet

echo "--- $(date -Is) deploying $REMOTE"
git reset -q --hard "origin/$BRANCH"
git log -1 --pretty='  %an: %s'

if docker compose -f docker-compose.staging.yml --env-file .env.staging up -d --build 2>&1 | tail -3; then
  echo "  deployed"
else
  # Left as-is on purpose. Staging is where things are allowed to be broken, and rolling back
  # automatically would hide the very failure the person is trying to see.
  echo "  BUILD FAILED — staging is left on the broken commit so it can be looked at"
fi
