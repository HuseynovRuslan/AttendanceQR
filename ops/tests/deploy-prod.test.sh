#!/usr/bin/env bash
# Tests for ops/deploy-prod.sh's failure paths, in a sandbox: a throwaway git remote and checkout, and
# stand-ins for docker, curl, date and sleep that record what the deploy did. No Docker, no network, no
# root — and nothing real is ever called: the copy under test points at the sandbox.
#
# The property every case checks: wherever a deploy stops, the checkout, the containers ("running")
# and the deploy record agree on one commit — the release if it is healthy, the previous one if not.
#
#     bash ops/tests/deploy-prod.test.sh
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
real_date=$(command -v date)
real_flock=$(command -v flock || true)
sandboxes=()
trap 'rm -rf "${sandboxes[@]}"' EXIT

# A sandbox with main at C1, deployed and running.
new_sandbox() {
  T=$(mktemp -d)
  sandboxes+=("$T")
  mkdir -p "$T/bin" "$T/flags" "$T/state"
  : >"$T/events"

  cat >"$T/bin/docker" <<EOF
#!/usr/bin/env bash
T="$T"
echo "docker \$*" >>"\$T/events"
case "\$1" in
  run)
    for a in "\$@"; do case "\$a" in *:/etc/caddy/Caddyfile:ro) f=\${a%%:/etc/caddy/Caddyfile:ro} ;; esac; done
    grep -q INVALID "\$f" && exit 1
    exit 0 ;;
  compose)
    shift; [[ "\$1" == -f ]] && shift 2
    case "\$1" in
      build)
        [[ -e BUILD_FAILS ]] && exit 1
        echo "built \$(git rev-parse HEAD)" >>"\$T/events"; exit 0 ;;
      up)
        if [[ " \$* " == *" caddy "* ]]; then cp Caddyfile "\$T/caddy-running"; echo "caddy recreated" >>"\$T/events"; exit 0; fi
        git rev-parse HEAD >"\$T/running"
        echo "started \$(git rev-parse HEAD)" >>"\$T/events"
        if [[ -e "\$T/flags/KILL_ON_UP" ]]; then rm -f "\$T/flags/KILL_ON_UP"; kill -TERM "\$PPID"; exit 1; fi
        [[ -e UP_FAILS ]] && exit 1
        exit 0 ;;
    esac ;;
esac
exit 0
EOF
  cat >"$T/bin/curl" <<EOF
#!/usr/bin/env bash
T="$T"
[[ -e "\$T/flags/ALL_UNHEALTHY" ]] && exit 22
git -C "\$T/app" cat-file -e "\$(cat "\$T/running"):UNHEALTHY" 2>/dev/null && exit 22
grep -q EDGE_BROKEN "\$T/caddy-running" 2>/dev/null && exit 22
exit 0
EOF
  # Baku time from \$T/clock, one line per reading (the last one repeats); noon by default.
  cat >"$T/bin/date" <<EOF
#!/usr/bin/env bash
T="$T"
reading="12 00 +0400"
if [[ -s "\$T/clock" ]]; then
  reading=\$(head -n 1 "\$T/clock")
  if (( \$(wc -l <"\$T/clock") > 1 )); then tail -n +2 "\$T/clock" >"\$T/clock.next"; mv "\$T/clock.next" "\$T/clock"; fi
fi
case "\${1-}" in
  "+%H %M %z") echo "\$reading" ;;
  "+%H:%M") set -- \$reading; echo "\$1:\$2" ;;
  *) exec "$real_date" "\$@" ;;
esac
EOF
  printf '#!/usr/bin/env bash\nexit 0\n' >"$T/bin/sleep"
  [[ -n "$real_flock" ]] || printf '#!/usr/bin/env bash\nexit 0\n' >"$T/bin/flock"
  chmod +x "$T/bin/"*

  sed -e "s#^APP_DIR=/opt/attendanceqr\$#APP_DIR=$T/app#" \
      -e "s#^RECORD_DIR=/var/lib/qrlog-deploy\$#RECORD_DIR=$T/state#" \
      "$here/deploy-prod.sh" >"$T/deploy-prod.sh"
  grep -qx "APP_DIR=$T/app" "$T/deploy-prod.sh" && grep -qx "RECORD_DIR=$T/state" "$T/deploy-prod.sh" \
    || { t_no "the sandbox could not point deploy-prod.sh at itself"; return 1; }

  git init -q --bare -b main "$T/remote.git"
  git init -q -b main "$T/work"
  git -C "$T/work" remote add origin "$T/remote.git"
  mkdir -p "$T/work/ops"
  cat >"$T/work/ops/backup.sh" <<EOF
#!/usr/bin/env bash
echo "backup at \$(git rev-parse HEAD)" >>"$T/events"
[[ -e "$T/flags/BACKUP_FAILS" ]] && exit 1
exit 0
EOF
  echo "services: {}" >"$T/work/docker-compose.prod.yml"
  echo "ok v1" >"$T/work/Caddyfile"
  echo v1 >"$T/work/app.txt"
  git -C "$T/work" add --chmod=+x ops/backup.sh
  git -C "$T/work" add docker-compose.prod.yml Caddyfile app.txt
  C1=$(commit_push "v1")
  git clone -q "$T/remote.git" "$T/app"
  echo "$C1" >"$T/state/deployed-commit"
  echo "$C1" >"$T/running"
  cp "$T/app/Caddyfile" "$T/caddy-running"
}

change() { printf '%s\n' "$2" >"$T/work/$1"; git -C "$T/work" add "$1"; }
commit_push() { git -C "$T/work" commit -q -m "$1" && git -C "$T/work" push -q origin main && git -C "$T/work" rev-parse HEAD; }

run_deploy() {
  (cd "$T" && env PATH="$T/bin:$PATH" "$@" bash "$T/deploy-prod.sh" >"$T/out" 2>&1)
  echo $?
}

head_of() { git -C "$T/app" rev-parse HEAD; }
record() { cat "$T/state/deployed-commit" 2>/dev/null; }
running() { cat "$T/running"; }
events_has() { grep -q -- "$1" "$T/events"; }
out_has() { grep -q -- "$1" "$T/out"; }
# Everything lands on one commit.
expect_on() {
  expect_eq "$(head_of)" "$1" "$2: checkout"
  expect_eq "$(running)" "$1" "$2: running containers"
  expect_eq "$(record)" "$1" "$2: deploy record"
}

# ─── a healthy release ──────────────────────────────────────────────────────────────────────────────
new_sandbox
change app.txt v2; C2=$(commit_push "v2")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "0" "a healthy release succeeds"
expect_on "$C2" "a healthy release"
out_has "DONE — deployed" && t_ok || t_no "a healthy release says DONE"
# The safety dump is taken while the checkout is still on the running commit.
events_has "backup at $C1" && t_ok || t_no "the backup runs before the checkout moves"
[[ $(grep -n "backup at" "$T/events" | cut -d: -f1) -lt $(grep -n "^built" "$T/events" | head -1 | cut -d: -f1) ]] \
  && t_ok || t_no "the backup runs before the build"
[[ ! -e "$T/app/backups/.deploying" ]] && t_ok || t_no "the watchdog marker is removed"

# ─── refusals before anything moves ─────────────────────────────────────────────────────────────────
new_sandbox
touch "$T/flags/BACKUP_FAILS"
change app.txt v2; C2=$(commit_push "v2")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a failed backup stops the deploy"
expect_on "$C1" "a failed backup"
events_has "compose" && t_no "a failed backup must not build or start anything" || t_ok

new_sandbox
change app.txt v2; C2=$(commit_push "v2")
change app.txt v3; commit_push "v3" >/dev/null
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a release refuses when main moved after approval"
expect_on "$C1" "main moved"
events_has "backup at" && t_no "main moved: nothing may run, not even the backup" || t_ok
out_has "main changed after release approval" && t_ok || t_no "main moved: says why"

new_sandbox
echo "08 00 +0400" >"$T/clock"
change app.txt v2; C2=$(commit_push "v2")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a release refuses in the morning peak"
expect_on "$C1" "the morning peak"
[[ -s "$T/events" ]] && t_no "the morning peak: nothing may run" || t_ok

new_sandbox
echo "17 59 +0400" >"$T/clock"
change app.txt v2; C2=$(commit_push "v2")
expect_eq "$(run_deploy)" "1" "a manual deploy refuses in the evening peak"
expect_eq "$(run_deploy FORCE=1)" "0" "a manual deploy may still be forced by a person at the server"
expect_on "$C2" "a forced manual deploy"

new_sandbox
echo "12 00 +0000" >"$T/clock"
change app.txt v2; C2=$(commit_push "v2")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a server that cannot tell Baku time refuses"
expect_on "$C1" "no Baku time"

new_sandbox
change Caddyfile "INVALID"; change app.txt v2; C2=$(commit_push "v2 with a broken Caddyfile")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "an invalid Caddyfile stops the deploy"
expect_on "$C1" "an invalid Caddyfile"
events_has "backup at" && t_no "an invalid Caddyfile: refused before the backup" || t_ok

# A tag named origin/main (pulled in by a plain `git pull`) must not decide what "main" is.
new_sandbox
change app.txt v2; C2=$(commit_push "v2")
git -C "$T/work" checkout -q -b side "$C1"
change app.txt side; SIDE=$(git -C "$T/work" commit -q -m side && git -C "$T/work" rev-parse HEAD)
git -C "$T/work" tag origin/main "$SIDE"
git -C "$T/work" push -q origin side refs/tags/origin/main
git -C "$T/app" fetch -q --tags origin
expect_eq "$(run_deploy EXPECTED_SHA="$SIDE")" "1" "a tag named origin/main cannot be deployed as main"
expect_on "$C1" "the origin/main tag, approved commit"
expect_eq "$(run_deploy)" "0" "with a tag named origin/main, a manual deploy still takes the branch"
expect_on "$C2" "the origin/main tag, manual deploy"

if [[ -n "$real_flock" ]]; then
  new_sandbox
  change app.txt v2; C2=$(commit_push "v2")
  flock "$T/app" sleep 5 &
  holder=$!
  sleep 0.5 2>/dev/null || command sleep 1
  expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a second deploy refuses while one holds the lock"
  expect_on "$C1" "the lock is held"
  kill "$holder" 2>/dev/null; wait "$holder" 2>/dev/null
fi

# ─── failures after the checkout moved: always back to C1, and agreeing about it ────────────────────
new_sandbox
change app.txt v2; touch "$T/work/BUILD_FAILS"; git -C "$T/work" add BUILD_FAILS; C2=$(commit_push "v2 does not build")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a failed build fails the deploy"
expect_on "$C1" "a failed build"
events_has "started" && t_no "a failed build: nothing may restart" || t_ok

new_sandbox
printf '12 00 +0400\n07 31 +0400\n' >"$T/clock"
change app.txt v2; C2=$(commit_push "v2")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a build that runs into the peak does not restart anything"
expect_on "$C1" "the build ran into the peak"
events_has "started" && t_no "the build ran into the peak: nothing may restart" || t_ok

new_sandbox
change app.txt v2; touch "$T/work/UNHEALTHY"; git -C "$T/work" add UNHEALTHY; C2=$(commit_push "v2 is unhealthy")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "an unhealthy release fails the deploy"
expect_on "$C1" "an unhealthy release, rolled back"
out_has "rolled back to" && t_ok || t_no "an unhealthy release says it rolled back"

new_sandbox
change app.txt v2; touch "$T/work/UP_FAILS"; git -C "$T/work" add UP_FAILS; C2=$(commit_push "v2 fails to start")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a failed start (half the containers recreated) fails the deploy"
expect_on "$C1" "a failed start, rolled back"

new_sandbox
touch "$T/flags/KILL_ON_UP"
change app.txt v2; C2=$(commit_push "v2")
# bash re-raises the signal after the exit trap, so the status is the signal's (143), not 1.
[[ "$(run_deploy EXPECTED_SHA="$C2")" != "0" ]] && t_ok || t_no "a deploy killed half-way must not report success"
expect_on "$C1" "a deploy killed half-way, rolled back by the exit trap"
out_has "STOPPED HALF-WAY" && t_ok || t_no "a deploy killed half-way says so"

new_sandbox
touch "$T/flags/ALL_UNHEALTHY"
change app.txt v2; C2=$(commit_push "v2")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a deploy whose rollback is also unhealthy fails"
expect_eq "$(head_of)" "$C1" "the rollback is also unhealthy: the checkout is back on C1"
expect_eq "$(record)" "unknown" "the rollback is also unhealthy: the record says nobody knows what serves"
out_has "ROLLBACK IS ALSO UNHEALTHY" && t_ok || t_no "the rollback is also unhealthy: says so"

# ─── the Caddyfile ──────────────────────────────────────────────────────────────────────────────────
new_sandbox
change Caddyfile "ok v2"; C2=$(commit_push "v2 Caddyfile")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "0" "a valid Caddyfile change deploys"
expect_on "$C2" "a Caddyfile change"
expect_eq "$(cat "$T/caddy-running")" "ok v2" "the proxy runs the new Caddyfile"
[[ $(grep -n "^started" "$T/events" | head -1 | cut -d: -f1) -lt $(grep -n "caddy recreated" "$T/events" | cut -d: -f1) ]] \
  && t_ok || t_no "caddy is recreated only after the app is up"

new_sandbox
change Caddyfile "ok EDGE_BROKEN"; C2=$(commit_push "v2 Caddyfile breaks the edge")
expect_eq "$(run_deploy EXPECTED_SHA="$C2")" "1" "a Caddyfile that breaks the edge fails the deploy"
expect_on "$C2" "the edge broke: the code stays"
expect_eq "$(cat "$T/caddy-running")" "ok v1" "the edge broke: the proxy is back on the old Caddyfile"
expect_eq "$(cat "$T/app/Caddyfile")" "ok v1" "the edge broke: the checkout shows the Caddyfile the proxy runs"

printf 'deploy-prod: %d passed, %d failed\n' "$pass" "$fail"
(( pass > 0 && fail == 0 ))
