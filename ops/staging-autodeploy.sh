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
#
# The CODE comes from the branch; the COMPOSE FILE does not. This runs as root, from cron, on the
# production machine, and the compose file is what decides what a container may do to that machine: a
# bind mount of /opt/attendanceqr, `privileged: true`, `network_mode: host`, the docker socket. Taken
# from the branch, "can push to staging" meant "can read production's .env and its database volume".
# So the deploy uses the copy in the production checkout (PINNED_COMPOSE), which only changes through
# a reviewed, gated production deploy of `main`. A branch whose own copy differs is REFUSED rather than
# deployed with the pinned one: whoever pushed expects their compose change to be live, and a staging
# quietly running something else would be checked against the wrong thing.
set -euo pipefail

APP_DIR=/opt/attendanceqr
DIR=/opt/qrlog-staging
LOG=$APP_DIR/backups/staging-deploy.log
BRANCH=staging
PINNED_COMPOSE=$APP_DIR/docker-compose.staging.yml
# The commit last refused, so a refusal is logged and alerted once — not every two minutes until
# somebody fixes it.
REFUSED_MARK=$APP_DIR/backups/.staging-refused

exec >>"$LOG" 2>&1

cd "$DIR"
git fetch -q origin "$BRANCH"

LOCAL=$(git rev-parse HEAD)
REMOTE=$(git rev-parse "origin/$BRANCH")
[ "$LOCAL" = "$REMOTE" ] && exit 0   # nothing new; stay quiet

# Compared against the commit before the working tree moves, so a refused push leaves the clone on
# what is actually running. A missing pinned copy, or a branch without the file, refuses too.
if ! git show "origin/$BRANCH:docker-compose.staging.yml" 2>/dev/null | cmp -s - "$PINNED_COMPOSE"; then
  if [ "$(cat "$REFUSED_MARK" 2>/dev/null || true)" != "$REMOTE" ]; then
    echo "--- $(date -Is) REFUSED $REMOTE — its docker-compose.staging.yml differs from $PINNED_COMPOSE"
    git log -1 --pretty='  %an: %s' "$REMOTE"
    echo "  compose changes for staging ship through main and the production deploy; staging stays on $LOCAL"
    if [ -x "$APP_DIR/ops/alert.sh" ]; then
      "$APP_DIR/ops/alert.sh" "staging deploy REFUSED: ${REMOTE:0:8} changes docker-compose.staging.yml — compose for staging ships through main and the production deploy, not the staging branch" || true
    fi
    echo "$REMOTE" >"$REFUSED_MARK"
  fi
  exit 1
fi
rm -f "$REFUSED_MARK"

echo "--- $(date -Is) deploying $REMOTE"
git reset -q --hard "origin/$BRANCH"
git log -1 --pretty='  %an: %s'

# The pinned file, with the clone as the project directory: build contexts (`.`, `./frontend`) resolve
# to the branch's code, not to production's checkout. The project name and the env file are pinned
# too — COMPOSE_PROJECT_NAME in an env file outranks the file's own `name:`, and a relative env file
# is read from wherever this happens to run.
if docker compose -p qrlog-staging -f "$PINNED_COMPOSE" --project-directory "$DIR" \
    --env-file "$DIR/.env.staging" up -d --build 2>&1 | tail -3; then
  echo "  deployed"
else
  # Left as-is on purpose. Staging is where things are allowed to be broken, and rolling back
  # automatically would hide the very failure the person is trying to see.
  echo "  BUILD FAILED — staging is left on the broken commit so it can be looked at"
fi
