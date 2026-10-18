#!/usr/bin/env bash
# Tests for the production release path (ops/CLOUD-DEPLOY.md): the SSH gate, the root helper, GitHub's
# approval proof and the override rule in deploy-prod.sh. No root, no Docker, no server — every case
# here is a refusal or a pure function, so it runs anywhere bash, python3 and openssl do, and in CI
# before every release. deploy-prod.sh's own failure paths are in deploy-prod.test.sh.
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
# A client's locale does not change what the parser accepts.
if LC_ALL=tr_TR.UTF-8 bash -c "source '$here/prod-deploy-ssh-gate.sh'; parse_command 'DEPLOY $SHA'" >/dev/null 2>&1; then
  t_no "gate accepted DEPLOY under another locale"
else
  t_ok
fi

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

# The clock: one reading, and only a Baku offset is believed.
expect_eq "$(clock_minute "07 30 +0400")" "450" "07:30 Baku is minute 450"
expect_eq "$(clock_minute "00 00 +0400")" "0" "midnight is minute 0"
expect_eq "$(clock_minute "23 59 +0400")" "1439" "23:59 is minute 1439"
for reading in "07 30 +0000" "07 30 +0300" "07 30" "7 30 +0400" "xx yy +0400" ""; do
  clock_minute "$reading" >/dev/null && t_no "clock_minute believed [$reading]" || t_ok
done

# The peaks, at the minute: 07:30–09:30 and 17:00–19:00 Asia/Baku, start inclusive, end exclusive.
for minute in 0 449 570 1019 1140 1439; do
  expect_eq "$(peak_refusal "$minute")" "" "minute $minute is outside the peaks"
done
for minute in 450 500 569 1020 1100 1139; do
  [[ -n "$(peak_refusal "$minute")" ]] && t_ok || t_no "minute $minute must be refused as a peak"
done
# No automated deploy STARTS in the peaks or the half hour before them (07:00 / 16:30).
for minute in 0 419 570 989 1140 1439; do
  expect_eq "$(start_refusal "$minute")" "" "a deploy may start at minute $minute"
done
for minute in 420 449 450 569 990 1019 1020 1139; do
  [[ -n "$(start_refusal "$minute")" ]] && t_ok || t_no "a deploy must not start at minute $minute"
done

# Migration risk: everything outside Down() counts, and anything not purely additive is named.
additive='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<bool>(name: "X", table: "T", nullable: false, defaultValue: false);
    migrationBuilder.CreateTable(name: "Y", columns: table => new { Id = table.Column<int>(nullable: false) },
        constraints: table => { table.PrimaryKey("PK_Y", x => x.Id); });
    migrationBuilder.CreateIndex(name: "IX", table: "Y", column: "Id");
}
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(name: "Y");
    migrationBuilder.DropColumn(name: "X", table: "T");
}'
expect_eq "$(unsafe_migration_ops <<<"$additive")" "" "an additive migration has no unsafe operations"

destructive='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "X", table: "T");
    migrationBuilder.RenameColumn(name: "A", table: "T", newName: "B");
    migrationBuilder.Sql("UPDATE \"T\" SET \"B\" = 1");
    migrationBuilder.AlterColumn<string>(name: "C", table: "T", maxLength: 10);
}
protected override void Down(MigrationBuilder migrationBuilder) { }'
expect_eq "$(unsafe_migration_ops <<<"$destructive" | tr '\n' ' ')" "AlterColumn DropColumn RenameColumn Sql " \
  "a destructive migration is named operation by operation"

split_call='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder
        .DropColumn(name: "X", table: "T");
}'
expect_eq "$(unsafe_migration_ops <<<"$split_call")" "DropColumn" "a call split across lines is still read"

by_hand='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Operations.Add(new DropTableOperation { Name = "T" });
    migrationBuilder.RestartSequence("S");
}'
expect_eq "$(unsafe_migration_ops <<<"$by_hand" | tr '\n' ' ')" "Operations RestartSequence " \
  "operations added by hand, and sequence restarts, are named"

helper_after_down='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<int>(name: "X", table: "T", nullable: true);
    Wipe(migrationBuilder);
}
protected override void Down(MigrationBuilder migrationBuilder)
{
    if (true) { migrationBuilder.DropColumn(name: "X", table: "T"); }
}
private static void Wipe(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DeleteData(table: "T", keyColumn: "Id", keyValue: 1);
}'
expect_eq "$(unsafe_migration_ops <<<"$helper_after_down" | tr '\n' ' ')" "(migrationBuilder passed to other code) DeleteData " \
  "a helper method after Down() is read, and handing migrationBuilder on is named"

alter_db='protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:citext", ",,");
}'
expect_eq "$(unsafe_migration_ops <<<"$alter_db")" "AlterDatabase" "AlterDatabase is named"

# The migrations the next release brings today must all pass as additive.
for name in 20261015083814_AddStructures 20261015090651_AddAccessRoles 20261016073130_AddAttendanceSelfCorrections; do
  file="$here/../src/AttendanceQR.Infrastructure/Persistence/Migrations/$name.cs"
  [[ -f "$file" ]] || continue
  expect_eq "$(unsafe_migration_ops <"$file")" "" "$name is additive"
done

# ─── deploy-prod.sh: an approved release never takes FORCE ──────────────────────────────────────────
out=$(EXPECTED_SHA="$SHA" FORCE=1 bash "$here/deploy-prod.sh" 2>&1)
expect_eq "$?" "1" "deploy-prod.sh refuses FORCE for an approved release"
[[ "$out" == *"FORCE is not accepted"* ]] && t_ok || t_no "deploy-prod.sh says why it refuses FORCE"

# ─── GitHub's approval proof ────────────────────────────────────────────────────────────────────────
py=""
for candidate in python3 python; do
  if "$candidate" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 8) else 1)' >/dev/null 2>&1; then
    py=$candidate
    break
  fi
done
if [[ -n "$py" ]] && command -v openssl >/dev/null 2>&1; then
  if "$py" -I "$here/tests/verify-release-token.test.py"; then t_ok; else t_no "the approval-proof verifier tests"; fi
elif [[ -n "${CI:-}" ]]; then
  t_no "python3 and openssl are required in CI for the approval-proof tests"
else
  echo "skipped: approval-proof tests (no python3 or openssl here)"
fi

printf '%d passed, %d failed\n' "$pass" "$fail"
# Zero checks run is a broken suite, not a passing one.
(( pass > 0 && fail == 0 ))
