#!/usr/bin/env bash
# Tests for the off-box half of the backups, in a sandbox: ops/backup.sh and ops/watchdog.sh pointed at
# a throwaway APP_DIR, with stand-ins for docker, curl, df and alert.sh that record what happened. No
# Docker, no network, no R2.
#
# The properties: a dump on disk is readable by root alone; an upload to R2 that fails reaches a human
# that night; and the watchdog notices when no dump has reached R2 for 48h, however green the local
# check looks.
#
#     bash ops/tests/backup-offbox.test.sh
set -uo pipefail

here=$(cd "$(dirname "$0")/.." && pwd)
pass=0
fail=0
t_ok() { pass=$((pass + 1)); }
t_no() { fail=$((fail + 1)); printf 'FAIL: %s\n' "$*"; }
expect_eq() { if [[ "$1" == "$2" ]]; then t_ok; else t_no "$3 — expected [$2], got [$1]"; fi; }

# The default umask of a cron job, so the scripts — not the test — decide what a dump's mode is.
umask 022
sandboxes=()
trap 'rm -rf "${sandboxes[@]}"' EXIT

new_sandbox() {
  T=$(mktemp -d)
  sandboxes+=("$T")
  mkdir -p "$T/bin" "$T/flags" "$T/app/ops" "$T/app/backups"
  : >"$T/events"
  : >"$T/alerts"

  # pg_dump writes incompressible bytes, so the gzip clears backup.sh's size floor; the upload fails
  # on request; the R2 listing is empty; a container is always running.
  cat >"$T/bin/docker" <<EOF
#!/usr/bin/env bash
echo "docker \$*" >>"$T/events"
case "\$1" in
  exec) head -c 60000 /dev/urandom; exit 0 ;;
  run) [[ " \$* " == *" s3 cp "* && -e "$T/flags/UPLOAD_FAILS" ]] && exit 1; exit 0 ;;
  inspect) echo running; exit 0 ;;
esac
exit 0
EOF
  printf '#!/usr/bin/env bash\nexit 0\n' >"$T/bin/curl"
  printf '#!/usr/bin/env bash\nprintf " Use%%%%\\n 10%%%%\\n"\n' >"$T/bin/df"
  printf '#!/usr/bin/env bash\nexit 0\n' >"$T/bin/sleep"
  # backup.sh runs alert.sh; watchdog.sh sources it and calls send_alert.
  cat >"$T/app/ops/alert.sh" <<EOF
#!/usr/bin/env bash
send_alert() { echo "\$*" >>"$T/alerts"; }
if [ "\${BASH_SOURCE[0]}" = "\$0" ]; then send_alert "\$*"; fi
EOF
  chmod +x "$T/bin/"* "$T/app/ops/alert.sh"

  cat >"$T/app/.env" <<'EOF'
POSTGRES_USER=attendanceqr
POSTGRES_DB=attendanceqr
Backup__R2__AccessKey=backup-key
Backup__R2__SecretKey=backup-secret
Backup__R2__BucketName=qrlog-backups
Backup__R2__Endpoint=example.r2.cloudflarestorage.com
Storage__Minio__AccessKey=app-key
Storage__Minio__SecretKey=app-secret
Storage__Minio__BucketName=attendance-photos
Storage__Minio__Endpoint=example.r2.cloudflarestorage.com
EOF

  for script in backup.sh watchdog.sh; do
    sed -e "s#^APP_DIR=/opt/attendanceqr\$#APP_DIR=$T/app#" "$here/$script" >"$T/app/ops/$script"
    grep -qx "APP_DIR=$T/app" "$T/app/ops/$script" || { t_no "the sandbox could not point $script at itself"; return 1; }
  done
}

run() { (cd "$T" && env PATH="$T/bin:$PATH" HEALTH_URL=http://health.invalid bash "$T/app/ops/$1" >"$T/out" 2>&1); echo $?; }
alerts() { wc -l <"$T/alerts" | tr -d ' '; }
newest_dump() { ls -t "$T/app/backups"/attendanceqr_*.sql.gz 2>/dev/null | head -1; }
mode() { stat -c %a "$1"; }
MARK() { echo "$T/app/backups/.last-offbox-ok"; }

# ─── backup.sh ──────────────────────────────────────────────────────────────────────────────────────
new_sandbox
old="$T/app/backups/attendanceqr_20261101_231500.sql.gz"
echo old >"$old"; chmod 644 "$old"
expect_eq "$(run backup.sh)" "0" "a healthy backup succeeds"
dump=$(newest_dump)
[[ -n "$dump" && "$dump" != "$old" ]] && t_ok || t_no "a healthy backup writes a dump"
expect_eq "$(mode "$dump")" "600" "a new dump is readable by its owner alone"
expect_eq "$(mode "$old")" "600" "a dump written before the fix is tightened too"
[[ -e "$(MARK)" ]] && t_ok || t_no "a successful upload writes the off-box marker"
grep -q "$(basename "$dump")" "$(MARK)" && t_ok || t_no "the marker names the dump it records"
expect_eq "$(alerts)" "0" "a healthy backup: no alert"

new_sandbox
touch "$T/flags/UPLOAD_FAILS"
echo "an earlier upload" >"$(MARK)"; touch -d '3 days ago' "$(MARK)"
before=$(stat -c %Y "$(MARK)")
expect_eq "$(run backup.sh)" "1" "a failed upload fails the backup (deploy-prod.sh gates on it)"
expect_eq "$(alerts)" "1" "a failed upload alerts"
grep -q "NOT uploaded to R2" "$T/alerts" && t_ok || t_no "a failed upload: the alert says so: $(cat "$T/alerts")"
expect_eq "$(stat -c %Y "$(MARK)")" "$before" "a failed upload leaves the off-box marker alone"
grep -q "FAIL: upload" "$T/app/backups/backup.log" && t_ok || t_no "a failed upload is in the log"
dump=$(newest_dump)
[[ -n "$dump" ]] && t_ok || t_no "a failed upload keeps the local dump as a restore point"
expect_eq "$(mode "$dump")" "600" "the kept dump is readable by its owner alone"

# ─── watchdog.sh ────────────────────────────────────────────────────────────────────────────────────
new_sandbox
touch "$T/app/backups/attendanceqr_20261122_231500.sql.gz"   # the LOCAL check is green throughout
echo ok >"$(MARK)"
expect_eq "$(run watchdog.sh)" "0" "watchdog: a healthy run"
expect_eq "$(alerts)" "0" "watchdog: a fresh off-box marker raises nothing"

touch -d '3 days ago' "$(MARK)"
run watchdog.sh >/dev/null
expect_eq "$(alerts)" "1" "watchdog: no upload in 48h alerts, though a local dump is fresh"
grep -q "OFF-BOX" "$T/alerts" && t_ok || t_no "watchdog: the alert names the off-box copy: $(cat "$T/alerts")"
run watchdog.sh >/dev/null
expect_eq "$(alerts)" "1" "watchdog: the same fault is not re-sent every run"
touch "$(MARK)"
run watchdog.sh >/dev/null
expect_eq "$(alerts)" "2" "watchdog: a fresh upload sends the all-clear"
tail -1 "$T/alerts" | grep -q "✅" && t_ok || t_no "watchdog: the all-clear is marked as one"

new_sandbox
touch "$T/app/backups/attendanceqr_20261122_231500.sql.gz"
run watchdog.sh >/dev/null
expect_eq "$(alerts)" "0" "watchdog: no marker yet (first run after this shipped) is not an alert"
[[ -e "$(MARK)" ]] && t_ok || t_no "watchdog: no marker yet — the 48h clock is started"
touch -d '3 days ago' "$(MARK)"
run watchdog.sh >/dev/null
expect_eq "$(alerts)" "1" "watchdog: ...and 48h later with no upload it is one"

printf 'backup-offbox: %d passed, %d failed\n' "$pass" "$fail"
(( pass > 0 && fail == 0 ))
