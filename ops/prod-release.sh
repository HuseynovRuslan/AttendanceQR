#!/usr/bin/env bash
# The root half of a production release from GitHub Actions (ops/CLOUD-DEPLOY.md).
#
# Installed root-owned at /usr/local/sbin/qrlog-prod-release and reached only through
# /usr/local/bin/qrlog-prod-deploy-gate (the release key's forced command) via one sudoers line. sudo's
# argument list is all that crosses from the gate, so everything is checked again here:
#
#     qrlog-prod-release preflight <sha>   every check a deploy would make; changes nothing
#     qrlog-prod-release deploy    <sha>   the same checks, then ops/deploy-prod.sh for exactly <sha>
#
# A deploy happens only when every check passes. What stays a human's job, on purpose:
#   - a migration that drops, renames, alters or runs SQL (the automated path only takes additive ones);
#   - a server whose deployed commit is not in main's history, or whose checkout has local edits;
#   - production that is unhealthy before the deploy (the automatic rollback assumes the current
#     version works);
#   - any override: no FORCE, no peak-hour exception.
#
# Everything printed here ends up in a PUBLIC workflow log (the repository is public): checks and
# commit ids only — never environment values, database contents or addresses.
set -euo pipefail
umask 022

readonly APP_DIR=/opt/attendanceqr
readonly MIGRATIONS=src/AttendanceQR.Infrastructure/Persistence/Migrations
readonly HEALTH_URL=https://api.qrlog.az/health
readonly MIN_FREE_KB=$((5 * 1024 * 1024))

FAILED=0
ok() { printf '  ✔ %s\n' "$*"; }
bad() { printf '  ✘ %s\n' "$*"; FAILED=1; }
note() { printf '  · %s\n' "$*"; }

valid_args() { [[ $# -eq 2 && "$1" =~ ^(preflight|deploy)$ && "$2" =~ ^[0-9a-f]{40}$ ]]; }

# The scan peaks, in minutes of the Asia/Baku day: prints why a deploy would be refused, or nothing.
peak_refusal() {
  local minute=$1
  if (( (minute >= 450 && minute < 570) || (minute >= 1020 && minute < 1140) )); then
    echo "scan peak (07:30–09:30 / 17:00–19:00 Asia/Baku)"
  fi
}

baku_minute() {
  local h m
  h=$(TZ=Asia/Baku date +%-H)
  m=$(TZ=Asia/Baku date +%-M)
  echo $((h * 60 + m))
}

# Reads one migration's C# source and prints the non-additive operations in its Up() method — Down()
# legitimately drops what Up() created. Anything printed means a human deploys it, with the restore
# plan in front of them.
destructive_ops() {
  awk '/protected override void Up\(/{up=1} /protected override void Down\(/{up=0} up' \
    | grep -oE 'migrationBuilder\.(Drop[A-Za-z]*|Rename[A-Za-z]*|AlterColumn|AlterTable|AlterSequence|Sql|DeleteData|UpdateData)(<[^>(]*>)?\(' \
    | sed -E 's/^migrationBuilder\.//; s/[<(].*$//' \
    | sort -u || true
}

# Migration source files (not designers, not the snapshot) that <to> adds or changes relative to <from>.
migration_changes() {
  local filter=$1 from=$2 to=$3
  git -C "$APP_DIR" diff --name-only --diff-filter="$filter" "$from" "$to" -- "$MIGRATIONS" \
    | grep -E '\.cs$' | grep -vE '(\.Designer\.cs|ModelSnapshot\.cs)$' || true
}

db_scalar() {
  # The database container's own variables name the user and database, so nothing here carries them.
  docker compose -f "$APP_DIR/docker-compose.prod.yml" --project-directory "$APP_DIR" exec -T db \
    sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAq -v ON_ERROR_STOP=1 -c "$1"' _ "$1" 2>/dev/null
}

# Every company must stay on Legacy authorization; nothing in a deploy may switch one on.
check_authz_modes() {
  local has total others
  has=$(db_scalar "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Tenants' AND column_name = 'AuthzMode'") \
    || { bad "authorization mode: could not read the database"; return; }
  if [[ "$has" == "0" ]]; then
    note "authorization mode: column not created yet — every company starts on Legacy (default 0)"
    return
  fi
  total=$(db_scalar 'SELECT count(*) FROM "Tenants"') || { bad "authorization mode: could not read"; return; }
  others=$(db_scalar 'SELECT count(*) FROM "Tenants" WHERE "AuthzMode" <> 0') || { bad "authorization mode: could not read"; return; }
  if [[ "$others" == "0" ]]; then
    ok "authorization mode: all $total companies on Legacy"
  else
    bad "authorization mode: $others of $total companies are NOT on Legacy — check by hand"
  fi
}

run_checks() {
  local sha=$1 head minute refusal free migration ops changed

  minute=$(baku_minute)
  refusal=$(peak_refusal "$minute")
  if [[ -n "$refusal" ]]; then
    bad "time window: $(TZ=Asia/Baku date +%H:%M) Asia/Baku is a $refusal"
  else
    ok "time window: $(TZ=Asia/Baku date +%H:%M) Asia/Baku is outside the scan peaks"
  fi

  cd "$APP_DIR"
  if ! git fetch -q origin main; then
    bad "git: could not fetch main"
    return
  fi
  if [[ "$(git rev-parse origin/main)" == "$sha" ]]; then
    ok "release: ${sha:0:12} is the tip of main"
  else
    bad "release: ${sha:0:12} is not the tip of main any more (main moved after approval)"
  fi

  head=$(git rev-parse HEAD)
  note "deployed now: ${head:0:12}"
  if [[ "$head" == "$sha" ]]; then
    note "this commit is already deployed — a deploy would rebuild the same code"
  elif git merge-base --is-ancestor "$head" "$sha" 2>/dev/null; then
    ok "history: the deployed commit is in main's history ($(git rev-list --count "$head..$sha") new commits)"
  else
    bad "history: the deployed commit is not in main's history — deploy by hand after checking what it is"
  fi

  if [[ -z "$(git status --porcelain --untracked-files=no)" ]]; then
    ok "checkout: no local edits to tracked files"
  else
    bad "checkout: tracked files are edited on the server — a deploy would discard them"
  fi

  # Migrations the release brings, judged before anything runs: they apply themselves at startup.
  changed=$(migration_changes MDR "$head" "$sha")
  if [[ -n "$changed" ]]; then
    bad "migrations: an existing migration is modified or removed — never deploy that automatically"
  fi
  local added=0 destructive=0
  while IFS= read -r migration; do
    [[ -z "$migration" ]] && continue
    added=$((added + 1))
    ops=$(git show "$sha:$migration" | destructive_ops | tr '\n' ' ')
    if [[ -n "$ops" ]]; then
      destructive=$((destructive + 1))
      bad "migration $(basename "$migration" .cs): ${ops% } — needs a human deploy with a restore plan"
    else
      note "migration $(basename "$migration" .cs): additive"
    fi
  done <<<"$(migration_changes A "$head" "$sha")"
  if (( added == 0 )); then
    ok "migrations: none pending"
  elif (( destructive == 0 )); then
    ok "migrations: $added pending, all additive (a code rollback stays safe)"
  fi

  if git show "$sha:docker-compose.prod.yml" | docker compose --project-directory "$APP_DIR" -f - config -q >/dev/null 2>&1; then
    ok "compose: the release's docker-compose.prod.yml is valid with this server's environment"
  else
    bad "compose: the release's docker-compose.prod.yml does not validate here"
  fi
  if ! git diff --quiet "$head" "$sha" -- Caddyfile; then
    note "Caddyfile changes — deploy-prod.sh validates it first and recreates the proxy only after the app is healthy"
  fi

  if flock -n "$APP_DIR/backups/.deploy.flock" true 2>/dev/null; then
    ok "lock: no other deploy is running"
  else
    bad "lock: another deploy is running"
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

  check_authz_modes
}

deploy() {
  local sha=$1 stamp log rc_file pid rc
  stamp=$(date -u +%Y%m%dT%H%M%SZ)
  log="$APP_DIR/backups/release-$stamp-${sha:0:12}.log"
  rc_file="$log.exit"

  echo
  echo "→ deploying ${sha:0:12} (log on the server: backups/$(basename "$log"))"
  logger -t qrlog-release -- "deploy $sha started" 2>/dev/null || true

  # Detached, in its own session, ignoring hangups: a dropped runner connection or a cancelled
  # workflow must not kill a deploy half-way through recreating containers. Started, it completes —
  # or rolls back by itself (ops/deploy-prod.sh). The environment is rebuilt from nothing, so no
  # override (FORCE) can ride in.
  setsid bash -c 'trap "" HUP PIPE
    env -i PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin HOME=/root LC_ALL=C.UTF-8 \
      EXPECTED_SHA="$1" RELEASE_SOURCE=github-actions \
      /bin/bash /opt/attendanceqr/ops/deploy-prod.sh >"$2" 2>&1 </dev/null
    echo $? >"$3"' _ "$sha" "$log" "$rc_file" </dev/null >/dev/null 2>&1 &
  pid=$!

  # Stream the log until the deploy process ends. If this connection goes away, only the streaming stops.
  tail -n +1 -F --pid="$pid" "$log" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  rc=$(cat "$rc_file" 2>/dev/null || echo 255)
  logger -t qrlog-release -- "deploy $sha finished with $rc" 2>/dev/null || true
  if [[ "$rc" != "0" ]]; then
    echo "DEPLOY FAILED (exit $rc) — see the log above; deploy-prod.sh rolls the code back by itself when health fails."
    exit 1
  fi

  echo
  echo "→ verifying"
  FAILED=0
  if [[ "$(git -C "$APP_DIR" rev-parse HEAD)" == "$sha" ]]; then ok "deployed commit is ${sha:0:12}"; else bad "deployed commit is not ${sha:0:12}"; fi
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
  export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin HOME=/root LC_ALL=C.UTF-8
  local mode=$1 sha=$2

  echo "QRLog $mode — ${sha:0:12}"
  run_checks "$sha"
  if (( FAILED )); then
    echo "PREFLIGHT FAILED — nothing was changed."
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
