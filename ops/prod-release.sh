#!/bin/bash
# The root half of a production release from GitHub Actions (ops/CLOUD-DEPLOY.md).
#
# Installed root-owned at /usr/local/sbin/qrlog-prod-release and reached only through
# /usr/local/bin/qrlog-prod-deploy-gate (the release key's forced command) via one sudoers line. sudo's
# argument list and stdin are all that cross from the gate, so everything is checked again here:
#
#     qrlog-prod-release preflight <sha>   every check a deploy would make; changes nothing
#     qrlog-prod-release deploy    <sha>   the same checks, then the release's ops/deploy-prod.sh
#
# A request through the release key must also carry, on stdin, the token GitHub issues to an APPROVED
# production job (ops/verify-release-token.py): the key alone can start nothing. An administrator who
# runs this directly with sudo is root already and needs no token.
#
# A deploy happens only when every check passes. What stays a human's job, on purpose:
#   - a migration that is not purely additive (the automated path only takes ones a code rollback
#     survives);
#   - a server whose checkout is not what was last deployed, is not in main's history, or has edits;
#   - production that is unhealthy before the deploy (the automatic rollback assumes the current
#     version works);
#   - any override: no FORCE, no peak-hour exception.
#
# The installed copy decides what may be asked and checks it; HOW a deploy is carried out is the
# release's own ops/deploy-prod.sh, reviewed on main like the rest of the commit being deployed.
#
# Everything printed here ends up in a PUBLIC workflow log (the repository is public): checks and
# commit ids only — never environment values, database contents, counts or addresses.
export LC_ALL=C
set -euo pipefail
umask 022

readonly APP_DIR=/opt/attendanceqr
readonly REPO_URL=https://github.com/HuseynovRuslan/AttendanceQR.git
readonly MIGRATIONS=src/AttendanceQR.Infrastructure/Persistence/Migrations
readonly HEALTH_URL=https://api.qrlog.az/health
readonly MIN_FREE_KB=$((5 * 1024 * 1024))
readonly RELEASE_USER=qrlog-release
readonly VERIFIER=/usr/local/sbin/qrlog-verify-release-token
# Root-owned, outside the checkout. deploy-prod.sh writes the record of what is actually running.
readonly STATE_DIR=/var/lib/qrlog-deploy
readonly RECORD=$STATE_DIR/deployed-commit
readonly USED_TOKENS=$STATE_DIR/release-tokens-used
readonly LOG_DIR=/var/log/qrlog-release

# EF operations that only ADD: the old code ignores a new table, column or index, so a code rollback
# stays safe. Anything else a pending migration does is named, and a human deploys it.
readonly ADDITIVE_OPS='AddColumn|AddForeignKey|AddPrimaryKey|AddUniqueConstraint|AddCheckConstraint|CreateIndex|CreateTable|CreateSequence|EnsureSchema|InsertData'

FAILED=0
WINDOW_FAILED=0
ok() { printf '  ✔ %s\n' "$*"; }
bad() { printf '  ✘ %s\n' "$*"; FAILED=$((FAILED + 1)); }
note() { printf '  · %s\n' "$*"; }

valid_args() { [[ $# -eq 2 && "$1" =~ ^(preflight|deploy)$ && "$2" =~ ^[0-9a-f]{40}$ ]]; }

# git in the live checkout, with nothing in it able to run code: no hooks, no fsmonitor, no automatic
# gc (a detached gc would also inherit, and hold, the deploy lock on fd 8), and no optional index
# writes — a check reads the checkout, it does not touch it.
g() {
  git --no-optional-locks -C "$APP_DIR" -c core.hooksPath=/dev/null -c core.fsmonitor=false \
    -c gc.auto=0 -c maintenance.auto=false "$@" 8<&-
}

# "HH MM +ZZZZ" (one `date` call, so the hour and minute belong to the same instant) → minute of the
# Asia/Baku day. Fails unless the offset is Baku's: a server missing the zone file silently answers in
# UTC, four hours off.
clock_minute() {
  local h m zone
  read -r h m zone <<<"$1"
  [[ "$zone" == "+0400" && "$h" =~ ^[0-9]{2}$ && "$m" =~ ^[0-9]{2}$ ]] || return 1
  echo $((10#$h * 60 + 10#$m))
}

# The scan peaks: 07:30–09:30 and 17:00–19:00 Asia/Baku, start inclusive, end exclusive.
peak_refusal() {
  local minute=$1
  if (( (minute >= 450 && minute < 570) || (minute >= 1020 && minute < 1140) )); then
    echo "scan peak (07:30–09:30 / 17:00–19:00 Asia/Baku)"
  fi
}

# When an automated deploy may not START: the peaks, and the half hour before each — a deploy started
# at 07:25 is still building, restarting or rolling back at 07:30.
start_refusal() {
  local minute=$1 peak
  peak=$(peak_refusal "$minute")
  if [[ -n "$peak" ]]; then
    echo "$peak"
  elif (( (minute >= 420 && minute < 450) || (minute >= 990 && minute < 1020) )); then
    echo "half hour before a scan peak (no deploy starts 07:00–09:30 / 16:30–19:00 Asia/Baku)"
  fi
}

# Reads one migration's C# source and prints every operation it performs that is not additive.
# Down() is skipped (it legitimately drops what Up() created) by counting braces, so a helper method
# written after Down() is still read. Calls split across lines are joined first; migrationBuilder
# handed to other code, or used for anything but a named operation, is reported too.
unsafe_migration_ops() {
  awk '
    /override void Down\(/ { skip = 1; depth = 0; opened = 0 }
    skip {
      line = $0
      o = gsub(/\{/, "{", line); c = gsub(/\}/, "}", line)
      depth += o - c
      if (o > 0) opened = 1
      if (opened && depth <= 0) skip = 0
      next
    }
    { print }
  ' | tr '\n' ' ' \
    | sed -E 's/MigrationBuilder[[:space:]]+migrationBuilder//g' \
    | grep -oE 'migrationBuilder([[:space:]]*\.[[:space:]]*[A-Za-z_][A-Za-z0-9_]*)?' \
    | sed -E 's/^migrationBuilder[[:space:]]*\.?[[:space:]]*//; s/^$/(migrationBuilder passed to other code)/' \
    | grep -vxE "$ADDITIVE_OPS" | sort -u || true
}

# Migration source files (not designers, not the snapshot) that <to> adds or changes relative to
# <from>. Fails — not "none" — when git cannot answer.
migration_changes() {
  local filter=$1 from=$2 to=$3 files
  files=$(g diff --name-only --diff-filter="$filter" "$from" "$to" -- "$MIGRATIONS") || return 1
  grep -E '\.cs$' <<<"$files" | grep -vE '(\.Designer\.cs|ModelSnapshot\.cs)$' || true
}

db_scalar() {
  # The database container's own variables name the user and database, so nothing here carries them.
  docker compose -f "$APP_DIR/docker-compose.prod.yml" --project-directory "$APP_DIR" exec -T db \
    sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAq -v ON_ERROR_STOP=1 -c "$1"' _ "$1" 2>/dev/null
}

# Every company must stay on Legacy authorization; nothing in a deploy may switch one on.
check_authz_modes() {
  local has others
  has=$(db_scalar "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Tenants' AND column_name = 'AuthzMode'") \
    || { bad "authorization mode: could not read the database"; return; }
  if [[ "$has" == "0" ]]; then
    note "authorization mode: column not created yet — every company starts on Legacy (default 0)"
    return
  fi
  others=$(db_scalar 'SELECT count(*) FROM "Tenants" WHERE "AuthzMode" <> 0') \
    || { bad "authorization mode: could not read the database"; return; }
  if [[ "$others" == "0" ]]; then
    ok "authorization mode: every company is on Legacy"
  else
    bad "authorization mode: a company is NOT on Legacy — check by hand"
  fi
}

check_window() {
  local minute refusal
  if ! minute=$(clock_minute "$(TZ=Asia/Baku date '+%H %M %z')"); then
    bad "time window: this server cannot tell the Asia/Baku time (zone data missing?)"
    WINDOW_FAILED=1
    return
  fi
  refusal=$(start_refusal "$minute")
  if [[ -n "$refusal" ]]; then
    bad "time window: $(printf '%02d:%02d' $((minute / 60)) $((minute % 60))) Asia/Baku — $refusal"
    WINDOW_FAILED=1
  else
    ok "time window: $(printf '%02d:%02d' $((minute / 60)) $((minute % 60))) Asia/Baku, a deploy may start"
  fi
}

run_checks() {
  local mode=$1 sha=$2 tip head record free migration src ops changed found

  check_window

  if ! g fetch -q --no-tags "$REPO_URL" "+refs/heads/main:refs/remotes/origin/main"; then
    bad "git: could not fetch main"
    return
  fi
  # The branch ref by its full name: a tag called origin/main would win over a bare "origin/main".
  tip=$(g rev-parse --verify -q 'refs/remotes/origin/main^{commit}') || { bad "git: main not found"; return; }
  if [[ "$tip" == "$sha" ]]; then
    ok "release: ${sha:0:12} is the tip of main"
  else
    bad "release: ${sha:0:12} is not the tip of main any more (main moved after approval)"
  fi

  head=$(g rev-parse --verify -q 'HEAD^{commit}') || { bad "git: no HEAD in the checkout"; return; }
  note "deployed now: ${head:0:12}"
  record=$(cat "$RECORD" 2>/dev/null || true)
  if [[ -z "$record" ]]; then
    note "deploy record: none yet (first release through this path) — the checkout is taken as what runs"
  elif [[ "$record" == "$head" ]]; then
    ok "deploy record: the checkout is what the last finished deploy left running"
  elif [[ "$record" == "unknown" ]]; then
    bad "deploy record: the last deploy ended with production unhealthy — check by hand"
  else
    bad "deploy record: the last finished deploy was ${record:0:12}, but the checkout is on ${head:0:12} (a failed deploy, or a git pull without one) — deploy by hand"
  fi

  if [[ "$head" == "$sha" ]]; then
    if [[ "$mode" == "deploy" ]]; then
      bad "release: ${sha:0:12} is already deployed — nothing to do"
    else
      note "release: ${sha:0:12} is already deployed — a deploy would refuse"
    fi
  elif g merge-base --is-ancestor "$head" "$sha" 2>/dev/null; then
    ok "history: the deployed commit is in main's history ($(g rev-list --count "$head..$sha") new commits)"
  else
    bad "history: the deployed commit is not in main's history — deploy by hand after checking what it is"
  fi

  local edits
  if ! edits=$(g status --porcelain --untracked-files=no); then
    bad "checkout: git could not read the checkout's state"
  elif [[ -z "$edits" ]]; then
    ok "checkout: no local edits to tracked files"
  else
    bad "checkout: tracked files are edited on the server — a deploy would discard them"
  fi

  # Migrations the release brings, judged before anything runs: they apply themselves at startup.
  if ! changed=$(migration_changes MDR "$head" "$sha"); then
    bad "migrations: git could not compare the deployed commit with the release"
  elif [[ -n "$changed" ]]; then
    bad "migrations: an existing migration is modified or removed — never deploy that automatically"
  fi
  # EF finds a migration class anywhere in the assembly, not only in the folder read below.
  local grep_rc=0
  found=$(g grep -lE '\[Migration\("' "$sha" -- '*.cs') || grep_rc=$?
  if (( grep_rc > 1 )); then
    bad "migrations: git could not search the release for migrations"
  elif grep -v "^$sha:$MIGRATIONS/" <<<"$found" | grep -q .; then
    bad "migrations: a [Migration] class lives outside $MIGRATIONS — deploy by hand"
  fi
  local added=0 unsafe=0 pending
  if pending=$(migration_changes A "$head" "$sha"); then
    while IFS= read -r migration; do
      [[ -z "$migration" ]] && continue
      added=$((added + 1))
      if ! src=$(g show "$sha:$migration"); then
        unsafe=$((unsafe + 1))
        bad "migration $(basename "$migration" .cs): could not be read"
        continue
      fi
      ops=$(unsafe_migration_ops <<<"$src" | tr '\n' ' ')
      if [[ -n "$ops" ]]; then
        unsafe=$((unsafe + 1))
        bad "migration $(basename "$migration" .cs): ${ops% } — needs a human deploy with a restore plan"
      else
        note "migration $(basename "$migration" .cs): additive"
      fi
    done <<<"$pending"
    if (( added == 0 )); then
      ok "migrations: none pending"
    elif (( unsafe == 0 )); then
      ok "migrations: $added pending, all additive (a code rollback stays safe)"
    fi
  else
    bad "migrations: git could not list the pending migrations"
  fi

  if g show "$sha:docker-compose.prod.yml" | docker compose --project-directory "$APP_DIR" -f - config -q >/dev/null 2>&1; then
    ok "compose: the release's docker-compose.prod.yml is valid with this server's environment"
  else
    bad "compose: the release's docker-compose.prod.yml does not validate here"
  fi
  if ! g diff --quiet "$head" "$sha" -- Caddyfile; then
    note "Caddyfile changes — deploy-prod.sh validates it first and recreates the proxy only after the app is healthy"
  fi

  if docker info >/dev/null 2>&1; then ok "docker: daemon reachable"; else bad "docker: daemon not reachable"; fi

  if curl -fsS --max-time 8 "$HEALTH_URL" >/dev/null 2>&1; then
    ok "health: production answers now"
  else
    bad "health: production is not healthy now — fix it by hand; the automatic rollback assumes the current version works"
  fi

  free=$(df -Pk / | awk 'NR == 2 {print $4}')
  if [[ "$free" =~ ^[0-9]+$ ]] && (( free >= MIN_FREE_KB )); then
    ok "disk: enough free space for the build and the safety backup"
  else
    bad "disk: less than 5 GB free"
  fi

  if [[ -x "$APP_DIR/ops/backup.sh" ]]; then
    ok "backup: ops/backup.sh is in place (the deploy takes a fresh dump first, and stops if it fails)"
  else
    bad "backup: ops/backup.sh is missing or not executable"
  fi

  # The detached deploy outlives the SSH session only if logind leaves a closed session's processes
  # alone (Ubuntu's default). Checked, not assumed.
  if [[ "$(busctl get-property org.freedesktop.login1 /org/freedesktop/login1 org.freedesktop.login1.Manager KillUserProcesses 2>/dev/null)" == "b false" ]]; then
    ok "session: a dropped connection cannot stop a running deploy"
  else
    bad "session: logind may kill a deploy when the connection drops (KillUserProcesses)"
  fi

  check_authz_modes
}

# The release key's requests carry GitHub's proof of approval on stdin; verified before anything else
# happens, so an unapproved request costs nothing and learns nothing.
require_approval() {
  local mode=$1 sha=$2 token="" out jti run_id
  IFS= read -r -t 10 -n 16384 token || true
  if [[ -z "$token" ]]; then
    echo "REFUSING: no approval proof — releases through this key start only from an approved GitHub run." >&2
    exit 2
  fi
  if ! out=$(printf '%s' "$token" | /usr/bin/python3 -I "$VERIFIER" --audience "qrlog-prod-$mode" --sha "$sha"); then
    echo "REFUSING: the approval proof did not verify." >&2
    exit 2
  fi
  read -r jti run_id <<<"$out"
  # One approval, one action: a token is good for a few minutes, and it is used once.
  install -d -o root -g root -m 0755 "$STATE_DIR"
  if grep -qxF -- "$jti" "$USED_TOKENS" 2>/dev/null; then
    echo "REFUSING: this approval was already used." >&2
    exit 2
  fi
  ( umask 077; echo "$jti" >>"$USED_TOKENS" )
  if (( $(wc -l <"$USED_TOKENS") > 1000 )); then
    tail -n 500 "$USED_TOKENS" >"$USED_TOKENS.new" && mv -f "$USED_TOKENS.new" "$USED_TOKENS"
  fi
  logger -t qrlog-release -- "$mode $sha approved by GitHub run $run_id" 2>/dev/null || true
  echo "approved: GitHub run $run_id"
}

# What a person needs after a failed deploy: where the checkout and production actually are.
report_state() {
  local sha=$1 now record
  now=$(g rev-parse HEAD 2>/dev/null || echo unknown)
  record=$(cat "$RECORD" 2>/dev/null || echo none)
  if [[ "$now" == "$sha" ]]; then note "checkout: on the release ${sha:0:12}"; else note "checkout: on ${now:0:12} (not the release)"; fi
  note "deploy record: ${record:0:12}"
  if curl -fsS --max-time 8 "$HEALTH_URL" >/dev/null 2>&1; then note "health: production answers"; else note "health: production DOES NOT answer"; fi
}

deploy() {
  local sha=$1 stamp log rc_file script pid rc
  stamp=$(date -u +%Y%m%dT%H%M%SZ)
  install -d -o root -g root -m 0750 "$LOG_DIR"
  log="$LOG_DIR/release-$stamp-${sha:0:12}.log"
  rc_file="$log.exit"

  # The RELEASE's deploy-prod.sh, read from the commit being deployed — with its own checks (the exact
  # commit, the lock, the backup gate, no FORCE) — into a root-only file the reset cannot rewrite
  # under bash. Removed afterwards.
  script=$(mktemp /root/.qrlog-deploy-prod.XXXXXX)
  g show "$sha:ops/deploy-prod.sh" >"$script"
  chmod 0700 "$script"

  echo
  echo "→ deploying ${sha:0:12} (full log on the server: $log)"
  logger -t qrlog-release -- "deploy $sha started" 2>/dev/null || true

  # deploy-prod.sh takes the deploy lock itself; let go of ours just before it starts.
  exec 8<&-

  # Detached, in its own session, ignoring hangups: a dropped runner connection or a cancelled
  # workflow must not kill a deploy half-way through recreating containers. Started, it completes —
  # or rolls back by itself (ops/deploy-prod.sh). The environment is rebuilt from nothing, so no
  # override (FORCE) can ride in. --wait: should setsid have to fork, $! still lives until the end.
  setsid --wait bash -c 'trap "" HUP PIPE
    env -i PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin HOME=/root LC_ALL=C.UTF-8 \
      EXPECTED_SHA="$1" RELEASE_SOURCE=github-actions \
      /bin/bash "$4" >"$2" 2>&1 </dev/null
    echo $? >"$3"
    rm -f "$4"' _ "$sha" "$log" "$rc_file" "$script" </dev/null >/dev/null 2>&1 &
  pid=$!

  # Stream deploy-prod.sh's own progress lines while it runs — not docker's or git's output, which
  # stays in the server log: this stream lands in a public workflow log. If the connection goes away,
  # only the streaming stops.
  tail -n +1 -F --pid="$pid" "$log" 2>/dev/null \
    | grep --line-buffered -E '^(→ |  [^ ]|REFUSING|DONE|FORCE)' || true
  wait "$pid" 2>/dev/null || true
  rc=$(cat "$rc_file" 2>/dev/null || echo 255)
  logger -t qrlog-release -- "deploy $sha finished with $rc" 2>/dev/null || true
  if [[ "$rc" != "0" ]]; then
    echo
    echo "DEPLOY FAILED (exit $rc). Where production is now:"
    report_state "$sha"
    echo "Details: the server log above."
    exit 1
  fi

  echo
  echo "→ verifying"
  FAILED=0
  if [[ "$(g rev-parse HEAD)" == "$sha" ]]; then ok "deployed commit is ${sha:0:12}"; else bad "deployed commit is not ${sha:0:12}"; fi
  if [[ "$(cat "$RECORD" 2>/dev/null)" == "$sha" ]]; then ok "deploy record is ${sha:0:12}"; else bad "deploy record is not ${sha:0:12}"; fi
  if curl -fsS --max-time 8 "$HEALTH_URL" >/dev/null 2>&1; then ok "health: production answers"; else bad "health: production does not answer"; fi
  check_authz_modes
  if (( FAILED )); then
    echo "DEPLOYED, BUT A CHECK FAILED — look at it now."
    exit 1
  fi
  echo "DEPLOYED ${sha:0:12}"
}

main() {
  if ! valid_args "$@"; then
    echo "usage: qrlog-prod-release preflight|deploy <40-hex sha>" >&2
    exit 2
  fi
  if [[ $EUID -ne 0 ]]; then
    echo "REFUSING: must run as root (through the release gate's sudo line)." >&2
    exit 2
  fi
  export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin HOME=/root
  local mode=$1 sha=$2

  echo "QRLog $mode — ${sha:0:12}"
  # sudo sets SUDO_USER itself; the release user cannot choose it.
  if [[ "${SUDO_USER-}" == "$RELEASE_USER" ]]; then
    require_approval "$mode" "$sha"
  fi

  # One release check or deploy at a time — the same lock deploy-prod.sh takes (the checkout directory
  # itself: nothing to create, nothing to point elsewhere). Held through the checks, so no git command
  # here can run beside a deploy's reset.
  exec 8<"$APP_DIR"
  if ! flock -n 8; then
    echo "REFUSING: a deploy or another release check is running right now — nothing was changed."
    exit 1
  fi

  run_checks "$mode" "$sha"
  if (( FAILED )); then
    if (( WINDOW_FAILED && FAILED == 1 )); then
      echo "PREFLIGHT FAILED on the time window only — every other check passed. Nothing was changed."
    else
      echo "PREFLIGHT FAILED — nothing was changed."
    fi
    exit 1
  fi
  if [[ "$mode" == "preflight" ]]; then
    echo "PREFLIGHT PASSED — a deploy of ${sha:0:12} started now would go ahead. Nothing was changed."
    exit 0
  fi
  deploy "$sha"
}

# Sourced by ops/tests/release-gate.test.sh for its functions; run through sudo otherwise.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
