#!/usr/bin/env bash
# Tests for the production release path (ops/CLOUD-DEPLOY.md): the SSH gate, the root helper and the
# override rule in deploy-prod.sh. No root, no Docker, no server — every case here is a refusal or a
# pure function, so it runs anywhere bash does, and in CI before every release.
#
#     bash ops/tests/release-gate.test.sh
set -uo pipefail

here=$(cd "$(dirname "$0")/.." && pwd)
pass=0
fail=0
# t_ok / t_no, not ok / bad: the sourced helper defines functions of those names.
t_ok() { pass=$((pass + 1)); }
t_no() { fail=$((fail + 1)); printf 'FAIL: %s\n' "$*"; }
expect_eq() { if [[ "$1" == "$2" ]]; then t_ok; else t_no "$3 — expected [$2], got [$1]"; fi; }

SHA=0123456789abcdef0123456789abcdef01234567

# ─── the gate: exactly two commands, each with an exact commit ───────────────────────────────────────
# shellcheck source=../prod-deploy-ssh-gate.sh
source "$here/prod-deploy-ssh-gate.sh"
set +e  # the sourced script turns -e on; a test expects refusals

expect_eq "$(parse_command "deploy $SHA")" "deploy $SHA" "gate accepts deploy <sha>"
expect_eq "$(parse_command "preflight $SHA")" "preflight $SHA" "gate accepts preflight <sha>"

refused=(
  ""
  "deploy"
  "deploy abc"
  "deploy ${SHA^^}"
  "deploy ${SHA:0:39}"
  "deploy ${SHA}0"
  "deploy  $SHA"
  " deploy $SHA"
  "deploy $SHA "
  "deploy $SHA extra"
  "deploy $SHA; id"
  "deploy $SHA && id"
  "deploy \$(id)"
  "deploy \`id\`"
  $'deploy '"$SHA"$'\nid'
  "DEPLOY $SHA"
  "rollback $SHA"
  "id"
  "bash -i"
  "sh -c id"
  "scp -t /tmp"
  "git-upload-pack /opt/attendanceqr"
)
for cmd in "${refused[@]}"; do
  if parse_command "$cmd" >/dev/null; then t_no "gate accepted [$cmd]"; else t_ok; fi
done

# The gate as the forced command: a malformed request is refused before anything runs.
out=$(SSH_ORIGINAL_COMMAND='id' bash "$here/prod-deploy-ssh-gate.sh" 2>&1)
expect_eq "$?" "2" "gate exits 2 for a malformed command"
[[ "$out" == *REFUSING* ]] && t_ok || t_no "gate says REFUSING for a malformed command"
out=$(env -u SSH_ORIGINAL_COMMAND bash "$here/prod-deploy-ssh-gate.sh" 2>&1)
expect_eq "$?" "2" "gate exits 2 with no command at all (an interactive login)"

# ─── the root helper ────────────────────────────────────────────────────────────────────────────────
for args in "" "deploy" "deploy abc" "deploy $SHA extra" "rm $SHA" "preflight ${SHA^^}"; do
  # shellcheck disable=SC2086
  bash "$here/prod-release.sh" $args >/dev/null 2>&1
  expect_eq "$?" "2" "helper refuses arguments [$args]"
done
if [[ ${EUID:-$(id -u)} -ne 0 ]]; then
  out=$(bash "$here/prod-release.sh" preflight "$SHA" 2>&1)
  expect_eq "$?" "2" "helper refuses to run as a non-root user"
  [[ "$out" == *"must run as root"* ]] && t_ok || t_no "helper explains it needs root"
fi

# shellcheck source=../prod-release.sh
source "$here/prod-release.sh"
set +e

valid_args deploy "$SHA" && t_ok || t_no "valid_args accepts deploy <sha>"
valid_args preflight "$SHA" && t_ok || t_no "valid_args accepts preflight <sha>"
valid_args deploy "$SHA" x && t_no "valid_args accepted three arguments" || t_ok
valid_args shell "$SHA" && t_no "valid_args accepted an unknown mode" || t_ok

# The peaks, at the minute: 07:30–09:30 and 17:00–19:00 Asia/Baku, start inclusive, end exclusive.
for minute in 0 449 570 1019 1140 1439; do
  expect_eq "$(peak_refusal "$minute")" "" "minute $minute is outside the peaks"
done
for minute in 450 500 569 1020 1100 1139; do
  [[ -n "$(peak_refusal "$minute")" ]] && t_ok || t_no "minute $minute must be refused as a peak"
done

# Migration risk: only Up() counts, and anything not purely additive is named.
additive='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<bool>(name: "X", table: "T", nullable: false, defaultValue: false);
    migrationBuilder.CreateTable(name: "Y", columns: table => new { });
    migrationBuilder.CreateIndex(name: "IX", table: "Y", column: "Id");
}
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(name: "Y");
    migrationBuilder.DropColumn(name: "X", table: "T");
}'
expect_eq "$(destructive_ops <<<"$additive")" "" "an additive migration has no destructive operations"

destructive='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "X", table: "T");
    migrationBuilder.RenameColumn(name: "A", table: "T", newName: "B");
    migrationBuilder.Sql("UPDATE \"T\" SET \"B\" = 1");
    migrationBuilder.AlterColumn<string>(name: "C", table: "T", maxLength: 10);
}
protected override void Down(MigrationBuilder migrationBuilder) { }'
expect_eq "$(destructive_ops <<<"$destructive" | tr '\n' ' ')" "AlterColumn DropColumn RenameColumn Sql " \
  "a destructive migration is named operation by operation"

# The migrations the next release brings today must all pass as additive.
for name in 20261015083814_AddStructures 20261015090651_AddAccessRoles 20261016073130_AddAttendanceSelfCorrections; do
  file="$here/../src/AttendanceQR.Infrastructure/Persistence/Migrations/$name.cs"
  [[ -f "$file" ]] || continue
  expect_eq "$(destructive_ops <"$file")" "" "$name is additive"
done

# ─── deploy-prod.sh: an approved release never takes FORCE ──────────────────────────────────────────
out=$(EXPECTED_SHA="$SHA" FORCE=1 bash "$here/deploy-prod.sh" 2>&1)
expect_eq "$?" "1" "deploy-prod.sh refuses FORCE for an approved release"
[[ "$out" == *"FORCE is not accepted"* ]] && t_ok || t_no "deploy-prod.sh says why it refuses FORCE"

printf '%d passed, %d failed\n' "$pass" "$fail"
# Zero checks run is a broken suite, not a passing one.
(( pass > 0 && fail == 0 ))
