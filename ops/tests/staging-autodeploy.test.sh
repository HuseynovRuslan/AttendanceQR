#!/usr/bin/env bash
# Tests for ops/staging-autodeploy.sh, in a sandbox: a throwaway git remote with a `staging` branch, a
# stand-in production checkout holding the pinned compose file, the staging clone, and stand-ins for
# docker and alert.sh that record what the deploy did. No Docker, no network, no root.
#
# The property every case checks: what runs on the production host is decided by the compose file in
# the production checkout, never by the branch — a branch that brings its own is refused, loudly, once.
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

# A sandbox where staging is at S1, deployed, and the production checkout pins the same compose file.
new_sandbox() {
  T=$(mktemp -d)
  sandboxes+=("$T")
  mkdir -p "$T/bin" "$T/app/ops" "$T/app/backups"
  : >"$T/events"
  : >"$T/alerts"

  cat >"$T/bin/docker" <<EOF
#!/usr/bin/env bash
echo "docker \$* (in \$PWD)" >>"$T/events"
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
grep -q -- "--env-file $T/stg/.env.staging " "$T/events" && t_ok || t_no "the env file is an absolute path"
expect_eq "$(alerts)" "0" "a code change: no alert"

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

printf 'staging-autodeploy: %d passed, %d failed\n' "$pass" "$fail"
(( pass > 0 && fail == 0 ))
