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
# The CODE comes from the branch; NOTHING ELSE the deploy reads does. This runs as root, from cron, on
# the production machine, and the clone is written by whoever can push to `staging`. So every input
# that decides what a container may do, or what it is handed, lives outside the clone:
#   - the compose file (PINNED_COMPOSE): it decides bind mounts, `privileged`, `network_mode: host`,
#     the docker socket. Taken from the branch, "can push to staging" meant "can read production's .env
#     and its database volume". The production checkout's copy only changes through a reviewed, gated
#     production deploy of `main`. A branch whose own copy differs is REFUSED rather than deployed with
#     the pinned one: whoever pushed expects their compose change to be live, and a staging quietly
#     running something else would be checked against the wrong thing.
#   - the env file (STAGING_ENV): compose interpolates it into stg-backend's environment. It used to sit
#     in the clone, untracked, and `git reset --hard` replaces an untracked file with whatever the
#     branch commits at that path — a branch that left the compose file alone but committed
#     `.env.staging` as a symlink to /opt/attendanceqr/.env handed production's JWT key, QR secret, R2
#     keys and database password to a container running that branch's code.
#   - symlinks: a branch is refused if it tracks any, and the clone checks them out as plain files even
#     so (core.symlinks=false). The repository has none, and a link is the one way a file in the clone
#     can point the build at a host path — `frontend` -> /opt/attendanceqr makes production's checkout,
#     .env and dumps included, the frontend's build context.
#
# The cron line must run THIS copy — /opt/attendanceqr/ops/staging-autodeploy.sh, the production
# checkout's — never /opt/qrlog-staging/ops/…: the branch's copy of this script is the branch's to write.
set -euo pipefail

APP_DIR=/opt/attendanceqr
DIR=/opt/qrlog-staging
LOG=$APP_DIR/backups/staging-deploy.log
BRANCH=staging
PINNED_COMPOSE=$APP_DIR/docker-compose.staging.yml
# Staging's own secrets, next to production's .env in the production checkout and with the same trust:
# nothing a branch pushes can write here. Never inside the clone (see above).
STAGING_ENV=$APP_DIR/.env.staging
# The commit last refused, and why, so a refusal is logged and alerted once — not every two minutes
# until somebody fixes it.
REFUSED_MARK=$APP_DIR/backups/.staging-refused

exec >>"$LOG" 2>&1

# One run at a time. A build outlasts the two-minute cron interval, and a second run fetching between
# the first one's checks and its reset could move the clone to a commit nobody checked. Locked on the
# clone directory itself, as deploy-prod.sh locks the checkout: the lock goes with the process, and
# there is no lock file to go stale. Production's deploy locks its own directory, not this one.
exec 9<"$DIR"
flock -n 9 || exit 0

cd "$DIR"
# Before anything is checked out: even a link the check below somehow missed lands as a text file.
git config core.symlinks false
# fd 9 closed for git: nothing it leaves running (a credential-cache daemon, a background gc) may keep
# holding the lock after this run has ended.
git -c gc.auto=0 fetch -q origin "$BRANCH" 9<&-

LOCAL=$(git rev-parse HEAD)
REMOTE=$(git rev-parse "origin/$BRANCH")
[ "$LOCAL" = "$REMOTE" ] && exit 0   # nothing new; stay quiet

# Every check runs against REMOTE before the working tree moves, so a refused push leaves the clone on
# what is actually running; the reset below moves to that same REMOTE, never to a re-read branch tip.
refuse() {
  local why=$1 detail=$2 evidence=${3:-}
  if [ "$(cat "$REFUSED_MARK" 2>/dev/null || true)" != "$REMOTE $why" ]; then
    echo "--- $(date -Is) REFUSED $REMOTE — $detail"
    git log -1 --pretty='  %an: %s' "$REMOTE"
    if [ -n "$evidence" ]; then printf '%s\n' "$evidence" | sed 's/^/  /'; fi
    echo "  staging stays on $LOCAL"
    if [ -x "$APP_DIR/ops/alert.sh" ]; then
      "$APP_DIR/ops/alert.sh" "staging deploy REFUSED: ${REMOTE:0:8} — $detail" || true
    fi
    echo "$REMOTE $why" >"$REFUSED_MARK"
  fi
  exit 1
}

# A missing pinned copy, or a branch without the file, refuses too.
if ! git show "$REMOTE:docker-compose.staging.yml" 2>/dev/null | cmp -s - "$PINNED_COMPOSE"; then
  refuse compose "it changes docker-compose.staging.yml — compose for staging ships through main and the production deploy, not the staging branch"
fi
# Plain `<mode> <type> <object>TAB<path>` output: ls-tree's --format needs git 2.36.
SYMLINKS=$(git ls-tree -r "$REMOTE" | awk -F'\t' 'index($1, "120000 ") == 1 { print $2 }')
if [ -n "$SYMLINKS" ]; then
  refuse symlink "it tracks a symlink — a link in the clone can point the build at a host path" "$SYMLINKS"
fi
if git cat-file -e "$REMOTE:.env.staging" 2>/dev/null; then
  refuse env-tracked "it commits .env.staging — staging's secrets live in $STAGING_ENV, never in git"
fi
# Not the commit's fault, but nothing can start without it — and a link here could only ever be a way
# to read somebody else's secrets.
if [ ! -f "$STAGING_ENV" ] || [ -L "$STAGING_ENV" ]; then
  refuse env-missing "$STAGING_ENV is missing or not a plain file — see ops/README.md, «Deploying to staging»"
fi
rm -f "$REFUSED_MARK"

echo "--- $(date -Is) deploying $REMOTE"
git reset -q --hard "$REMOTE"
git log -1 --pretty='  %an: %s'

# The pinned file, with the clone as the project directory: build contexts (`.`, `./frontend`) resolve
# to the branch's code, not to production's checkout. The project name and the env file are pinned
# too — COMPOSE_PROJECT_NAME in an env file outranks the file's own `name:`, and with --env-file given
# compose reads no `.env` from the project directory.
if docker compose -p qrlog-staging -f "$PINNED_COMPOSE" --project-directory "$DIR" \
    --env-file "$STAGING_ENV" up -d --build 2>&1 | tail -3; then
  echo "  deployed"
else
  # Left as-is on purpose. Staging is where things are allowed to be broken, and rolling back
  # automatically would hide the very failure the person is trying to see.
  echo "  BUILD FAILED — staging is left on the broken commit so it can be looked at"
fi
