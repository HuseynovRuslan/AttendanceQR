#!/usr/bin/env bash
# Tests for ops/staging-autodeploy.sh, in a sandbox: a throwaway git remote with a `staging` branch, a
# stand-in production checkout holding the pinned compose file, the staging clone, and stand-ins for
# docker and alert.sh that record what the deploy did. No Docker, no network, no root.
#
# The property every case checks: what runs on the production host, and what it is handed, is decided
# by the production checkout — its compose file and its staging env file — never by the branch. A branch
# that brings its own compose file, a symlink or an env file is refused, loudly, once.
#
#     bash ops/tests/staging-autodeploy.test.sh
set -uo pipefail

here=$(cd "$(dirname "$0")/.." && pwd)
pass=0
fail=0
t_ok() { pass=$((pass + 1)); }
t_no() { fail=$((fail + 1)); printf 'FAIL: %s\n' "$*"; }
expect_eq() { if [[ "$1" == "$2" ]]; then t_ok; else t_no "$3 — expected [$2], got [$1]"; fi; }

export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@example.invalid
export GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@example.invalid
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null
sandboxes=()
trap 'rm -rf "${sandboxes[@]}"' EXIT

COMPOSE_V1='services: { stg-backend: { build: { context: . } } }'

# A sandbox where staging is at S1, deployed, and the production checkout pins the same compose file and
# holds staging's env file next to production's own .env.
new_sandbox() {
  T=$(mktemp -d)
  sandboxes+=("$T")
  mkdir -p "$T/bin" "$T/app/ops" "$T/app/backups"
  : >"$T/events"
  : >"$T/alerts"

  # Records the command and, like compose, reads the env file it is given — so what a container would
  # have been handed is in the events too.
  cat >"$T/bin/docker" <<EOF
#!/usr/bin/env bash
echo "docker \$* (in \$PWD)" >>"$T/events"
while [ \$# -gt 0 ]; do
  if [ "\$1" = "--env-file" ]; then echo "  env: \$(cat "\$2" 2>&1)" >>"$T/events"; fi
  shift
done
exit 0
EOF
  cat >"$T/app/ops/alert.sh" <<EOF
#!/usr/bin/env bash
echo "\$*" >>"$T/alerts"
EOF
  chmod +x "$T/bin/docker" "$T/app/ops/alert.sh"

  sed -e "s#^APP_DIR=/opt/attendanceqr\$#APP_DIR=$T/app#" \
      -e "s#^DIR=/opt/qrlog-staging\$#DIR=$T/stg#" \
      "$here/staging-autodeploy.sh" >"$T/staging-autodeploy.sh"
  grep -qx "APP_DIR=$T/app" "$T/staging-autodeploy.sh" && grep -qx "DIR=$T/stg" "$T/staging-autodeploy.sh" \
    || { t_no "the sandbox could not point staging-autodeploy.sh at itself"; return 1; }

  git init -q --bare -b staging "$T/remote.git"
  git init -q -b staging "$T/work"
  git -C "$T/work" remote add origin "$T/remote.git"
  echo "$COMPOSE_V1" >"$T/work/docker-compose.staging.yml"
  echo v1 >"$T/work/app.txt"
  git -C "$T/work" add docker-compose.staging.yml app.txt
  S1=$(commit_push "v1")
  git clone -q -b staging "$T/remote.git" "$T/stg"
  echo "$COMPOSE_V1" >"$T/app/docker-compose.staging.yml"
  echo "Jwt__SigningKey=STAGING_JWT_KEY" >"$T/app/.env.staging"
  echo "Jwt__SigningKey=PROD_JWT_KEY" >"$T/app/.env"
}

change() { printf '%s\n' "$2" >"$T/work/$1"; git -C "$T/work" add "$1"; }
commit_push() { git -C "$T/work" commit -q -m "$1" && git -C "$T/work" push -q origin staging && git -C "$T/work" rev-parse HEAD; }

run_deploy() {
  (cd "$T" && env PATH="$T/bin:$PATH" bash "$T/staging-autodeploy.sh" >"$T/out" 2>&1)
  echo $?
}

head_of() { git -C "$T/stg" rev-parse HEAD; }
log_has() { grep -q -- "$1" "$T/app/backups/staging-deploy.log"; }
alerts() { wc -l <"$T/alerts" | tr -d ' '; }

# ─── nothing new ────────────────────────────────────────────────────────────────────────────────────
new_sandbox
expect_eq "$(run_deploy)" "0" "nothing new: quiet success"
[[ -s "$T/events" ]] && t_no "nothing new: nothing may run" || t_ok

# ─── a code change deploys, with the PINNED compose file ────────────────────────────────────────────
new_sandbox
change app.txt v2; S2=$(commit_push "v2")
expect_eq "$(run_deploy)" "0" "a code change deploys"
expect_eq "$(head_of)" "$S2" "a code change: the clone moves to the new commit"
grep -q -- "-f $T/app/docker-compose.staging.yml " "$T/events" && t_ok \
  || t_no "the deploy uses the production checkout's compose file, not the branch's: $(cat "$T/events")"
grep -q -- "--project-directory $T/stg " "$T/events" && t_ok \
  || t_no "the project directory is the staging clone, so build contexts are the branch's code"
grep -q -- "-p qrlog-staging " "$T/events" && t_ok || t_no "the project name is pinned"
# The env file is the production checkout's, never a path inside the clone the branch writes.
grep -q -- "--env-file $T/app/.env.staging " "$T/events" && t_ok \
  || t_no "the env file comes from the production checkout: $(cat "$T/events")"
grep -q "env: Jwt__SigningKey=STAGING_JWT_KEY" "$T/events" && t_ok || t_no "staging is handed staging's secrets"
expect_eq "$(alerts)" "0" "a code change: no alert"
expect_eq "$(git -C "$T/stg" config core.symlinks)" "false" "the clone checks symlinks out as plain files"

# ─── a branch that brings its own compose file is refused, once ─────────────────────────────────────
new_sandbox
change docker-compose.staging.yml 'services: { stg-backend: { privileged: true, volumes: ["/opt/attendanceqr:/host"] } }'
change app.txt v2; S2=$(commit_push "v2 with its own compose")
expect_eq "$(run_deploy)" "1" "a changed compose file is refused"
expect_eq "$(head_of)" "$S1" "refused: the clone stays on what is running"
[[ -s "$T/events" ]] && t_no "refused: nothing may build or start: $(cat "$T/events")" || t_ok
expect_eq "$(alerts)" "1" "refused: one alert"
grep -q "REFUSED" "$T/alerts" && grep -q "docker-compose.staging.yml" "$T/alerts" && t_ok \
  || t_no "refused: the alert says what and why: $(cat "$T/alerts")"
log_has "REFUSED $S2" && t_ok || t_no "refused: the log names the commit"
expect_eq "$(run_deploy)" "1" "refused again two minutes later"
expect_eq "$(alerts)" "1" "refused again: no second alert for the same commit"
expect_eq "$(grep -c "REFUSED" "$T/app/backups/staging-deploy.log")" "1" "refused again: logged once"
# The same change, shipped through the production checkout: now it deploys.
git -C "$T/stg" show "$S2:docker-compose.staging.yml" >"$T/app/docker-compose.staging.yml"
expect_eq "$(run_deploy)" "0" "once production carries the same compose file, staging deploys"
expect_eq "$(head_of)" "$S2" "after the production deploy: the clone moves"
[[ -e "$T/app/backups/.staging-refused" ]] && t_no "after the production deploy: the refusal is cleared" || t_ok

# A newer refused commit after an older one alerts again.
new_sandbox
change docker-compose.staging.yml 'services: { a: { network_mode: host } }'; commit_push "a" >/dev/null
run_deploy >/dev/null
change docker-compose.staging.yml 'services: { b: { pid: host } }'; commit_push "b" >/dev/null
run_deploy >/dev/null
expect_eq "$(alerts)" "2" "a new refused commit alerts again"

# ─── a missing pinned copy, or a branch without the file, refuses too ───────────────────────────────
new_sandbox
rm "$T/app/docker-compose.staging.yml"
change app.txt v2; commit_push "v2" >/dev/null
expect_eq "$(run_deploy)" "1" "no pinned compose file: refused"
[[ -s "$T/events" ]] && t_no "no pinned compose file: nothing may run" || t_ok

new_sandbox
git -C "$T/work" rm -q docker-compose.staging.yml; commit_push "drop the compose file" >/dev/null
expect_eq "$(run_deploy)" "1" "a branch without the compose file: refused"
expect_eq "$(head_of)" "$S1" "a branch without the compose file: the clone stays"

# ─── a branch that links .env.staging to production's .env is refused ────────────────────────────────
# The compose file is untouched, so the compose check passes. Before, `git reset --hard` replaced the
# clone's untracked .env.staging with the link, and compose interpolated production's secrets into a
# container running the branch's code.
new_sandbox
ln -s "$T/app/.env" "$T/work/.env.staging"
git -C "$T/work" add .env.staging
change app.txt v2; S2=$(commit_push "v2, and a link")
expect_eq "$(run_deploy)" "1" "a symlinked .env.staging: refused"
expect_eq "$(head_of)" "$S1" "a symlinked .env.staging: the clone stays on what is running"
[[ -L "$T/stg/.env.staging" ]] && t_no "a symlinked .env.staging: the link must never reach the clone" || t_ok
[[ -s "$T/events" ]] && t_no "a symlinked .env.staging: nothing may build or start: $(cat "$T/events")" || t_ok
grep -q "symlink" "$T/alerts" && t_ok || t_no "a symlinked .env.staging: the alert says why: $(cat "$T/alerts")"
log_has "\.env\.staging" && t_ok || t_no "a symlinked .env.staging: the log names the link"
expect_eq "$(run_deploy)" "1" "a symlinked .env.staging: refused again"
expect_eq "$(alerts)" "1" "a symlinked .env.staging: one alert for the commit"
grep -q "PROD_JWT_KEY" "$T/events" "$T/out" "$T/app/backups/staging-deploy.log" \
  && t_no "production's secrets must not appear anywhere the deploy wrote" || t_ok

# Any symlink, wherever it is: `frontend` -> the production checkout would make it the build context.
new_sandbox
mkdir -p "$T/work/sub"
ln -s "$T/app" "$T/work/sub/frontend"
git -C "$T/work" add sub/frontend; commit_push "a link to the production checkout" >/dev/null
expect_eq "$(run_deploy)" "1" "a symlink anywhere in the tree: refused"
[[ -s "$T/events" ]] && t_no "a symlink anywhere: nothing may run" || t_ok
log_has "sub/frontend" && t_ok || t_no "a symlink anywhere: the log names it"

# A plain .env.staging committed to the branch is refused too: staging's secrets are not git's to hold.
new_sandbox
change .env.staging "Jwt__SigningKey=FROM_THE_BRANCH"; commit_push "commit an env file" >/dev/null
expect_eq "$(run_deploy)" "1" "a committed .env.staging: refused"
[[ -s "$T/events" ]] && t_no "a committed .env.staging: nothing may run" || t_ok
grep -q "\.env\.staging" "$T/alerts" && t_ok || t_no "a committed .env.staging: the alert says why"

# ─── no staging env file in the production checkout: refused, the clone left alone ──────────────────
new_sandbox
rm "$T/app/.env.staging"
change app.txt v2; S2=$(commit_push "v2")
expect_eq "$(run_deploy)" "1" "no staging env file: refused"
expect_eq "$(head_of)" "$S1" "no staging env file: the clone stays"
[[ -s "$T/events" ]] && t_no "no staging env file: nothing may run" || t_ok
grep -q "$T/app/.env.staging" "$T/alerts" && t_ok || t_no "no staging env file: the alert names the path: $(cat "$T/alerts")"
echo "Jwt__SigningKey=STAGING_JWT_KEY" >"$T/app/.env.staging"
expect_eq "$(run_deploy)" "0" "once the env file is in place, the same commit deploys"
expect_eq "$(head_of)" "$S2" "env file in place: the clone moves"

# ...and a link in its place is no env file at all.
new_sandbox
rm "$T/app/.env.staging"; ln -s "$T/app/.env" "$T/app/.env.staging"
change app.txt v2; commit_push "v2" >/dev/null
expect_eq "$(run_deploy)" "1" "a symlinked staging env file: refused"
[[ -s "$T/events" ]] && t_no "a symlinked staging env file: nothing may run" || t_ok

# ─── one run at a time ──────────────────────────────────────────────────────────────────────────────
# A build outlasts the cron interval; a second run must leave the first one's clone and commit alone.
new_sandbox
change app.txt v2; commit_push "v2" >/dev/null
exec 8<"$T/stg"
flock -n 8 || t_no "the test could not take the lock"
expect_eq "$(run_deploy)" "0" "while another run holds the lock: quiet success"
expect_eq "$(head_of)" "$S1" "while another run holds the lock: the clone does not move"
[[ -s "$T/events" ]] && t_no "while another run holds the lock: nothing may run" || t_ok
exec 8<&-
expect_eq "$(run_deploy)" "0" "after the lock is released, the next run deploys"

printf 'staging-autodeploy: %d passed, %d failed\n' "$pass" "$fail"
(( pass > 0 && fail == 0 ))
