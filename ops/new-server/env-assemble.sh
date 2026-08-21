#!/usr/bin/env bash
# Assemble the NEW host's production /opt/attendanceqr/.env without any secret ever appearing in a
# terminal, a chat, Git or a log:
#   • PRESERVED values (QR secret, VAPID, FCM, Telegram, CORS, ids…) stream old host → this process →
#     new host through two SSH pipes (never written here);
#   • GENERATED values (POSTGRES_PASSWORD, Jwt__SigningKey) are created ON the new host with openssl
#     and reused on every re-run (the DB volume was initialised with that password);
#   • ROTATED third-party keys (R2 photo + backup, Rekognition, OpenAI) come from an operator-written
#     local intake file that is streamed to the new host and then overwritten+deleted here.
# Re-runnable: each run rewrites the file from the same three sources; nothing is printed but key
# names and whether each is filled.
#
#   OLD_SSH='ssh -o StrictHostKeyChecking=no root@62.84.179.39' \
#   NEW_SSH='ssh -i ~/.ssh/qrlog_vps_l -o BatchMode=yes -o IdentitiesOnly=yes deploy@94.20.153.137' \
#   CARRY_OLD_KEYS=1                                          # carry production's current keys (pipe)
#   NEW_SECRETS_FILE="$USERPROFILE/.qrlog/new-secrets.env"   # or: operator intake file; neither → EMPTY
#   bash ops/new-server/env-assemble.sh
#
# Intake file format (exactly these keys, one per line, no quotes):
#   Storage__Minio__AccessKey=   Storage__Minio__SecretKey=
#   Backup__R2__AccessKey=       Backup__R2__SecretKey=
#   Rekognition__AccessKey=      Rekognition__SecretKey=
#   Assistant__ApiKey=
set -euo pipefail
: "${OLD_SSH:?}" "${NEW_SSH:?}"
ROTATED='POSTGRES_PASSWORD Jwt__SigningKey Storage__Minio__AccessKey Storage__Minio__SecretKey Backup__R2__AccessKey Backup__R2__SecretKey Rekognition__AccessKey Rekognition__SecretKey Assistant__ApiKey'
EXCL=$(echo "$ROTATED" | tr ' ' '|')

echo "== 1/3 preserved values: old → new (pipe) =="
# shellcheck disable=SC2086
$OLD_SSH "grep -E '^[A-Za-z_][A-Za-z0-9_]*=' /opt/attendanceqr/.env | grep -vE '^($EXCL)=' | tr -d '\r'" \
  | $NEW_SSH 'umask 077; sudo install -d -m 750 -o deploy -g deploy /opt/attendanceqr; cat > /opt/attendanceqr/.env.preserved.tmp; echo "   preserved: $(wc -l < /opt/attendanceqr/.env.preserved.tmp) keys"'

echo "== 2/3 rotated third-party keys: intake file → new (pipe) =="
KEYS_RE='^(Storage__Minio__(AccessKey|SecretKey)|Backup__R2__(AccessKey|SecretKey)|Rekognition__(AccessKey|SecretKey)|Assistant__ApiKey)=.+'
if [ "${CARRY_OLD_KEYS:-0}" = 1 ]; then
  # Decision 2026-08-21: third-party rotation is NOT a cutover blocker — the production keys are
  # carried over old → new through the same memory pipe, and rotated as separate mandatory work
  # after the move (old keys revoked at T+7 only once the new ones are verified).
  $OLD_SSH "grep -E '$KEYS_RE' /opt/attendanceqr/.env | tr -d '\r'" | $NEW_SSH 'umask 077; cat > /opt/attendanceqr/.env.rotated.tmp; echo "   carried from old production (pipe): $(cut -d= -f1 /opt/attendanceqr/.env.rotated.tmp | tr "\n" " ")"'
elif [ -n "${NEW_SECRETS_FILE:-}" ] && [ -s "$NEW_SECRETS_FILE" ]; then
  tr -d '\r' < "$NEW_SECRETS_FILE" | grep -E "$KEYS_RE" \
    | $NEW_SSH 'umask 077; cat > /opt/attendanceqr/.env.rotated.tmp; echo "   rotated keys received: $(cut -d= -f1 /opt/attendanceqr/.env.rotated.tmp | tr "\n" " ")"'
  # overwrite then delete the local intake file — it has done its one job
  SZ=$(stat -c %s "$NEW_SECRETS_FILE"); head -c "$SZ" /dev/urandom > "$NEW_SECRETS_FILE" && rm -f "$NEW_SECRETS_FILE"
  echo "   local intake file overwritten and deleted"
else
  $NEW_SSH 'umask 077; : > /opt/attendanceqr/.env.rotated.tmp'
  echo "   no intake file — rotated keys will be EMPTY placeholders (backend treats empty storage/rekognition/assistant as feature-off)"
fi

echo "== 3/3 assemble on the new host =="
$NEW_SSH bash -s <<'REMOTE'
set -euo pipefail; umask 077; cd /opt/attendanceqr
ENV=.env
old_val() { [ -f "$ENV" ] && grep -E "^$1=" "$ENV" | head -1 | cut -d= -f2- || true; }
PG=$(old_val POSTGRES_PASSWORD); [ -n "$PG" ] || PG=$(openssl rand -base64 36 | tr -d '/+=\n' | cut -c1-40)
JWT=$(old_val Jwt__SigningKey); [ -n "$JWT" ] || JWT=$(openssl rand -base64 64 | tr -d '/+=\n' | cut -c1-72)
rot_val() { grep -E "^$1=" .env.rotated.tmp 2>/dev/null | head -1 | cut -d= -f2- || true; }
{
  echo "# QRLog production .env — NEW HOST. Assembled by ops/new-server/env-assemble.sh; never commit."
  cat .env.preserved.tmp
  echo "POSTGRES_PASSWORD=$PG"
  echo "Jwt__SigningKey=$JWT"
  for k in Storage__Minio__AccessKey Storage__Minio__SecretKey Backup__R2__AccessKey Backup__R2__SecretKey Rekognition__AccessKey Rekognition__SecretKey Assistant__ApiKey; do
    v=$(rot_val "$k"); [ -n "$v" ] || v=$(old_val "$k")
    echo "$k=$v"
  done
} > .env.new
mv .env.new "$ENV"; chmod 600 "$ENV"; shred -u .env.preserved.tmp .env.rotated.tmp 2>/dev/null || rm -f .env.preserved.tmp .env.rotated.tmp
echo "   /opt/attendanceqr/.env written: $(wc -l < $ENV) lines, mode $(stat -c %a $ENV), owner $(stat -c %U $ENV)"
echo "   filled / EMPTY:"
for k in POSTGRES_PASSWORD Jwt__SigningKey Storage__Minio__AccessKey Storage__Minio__SecretKey Backup__R2__AccessKey Backup__R2__SecretKey Rekognition__AccessKey Rekognition__SecretKey Assistant__ApiKey; do
  v=$(grep -E "^$k=" $ENV | cut -d= -f2-); printf "     %-28s %s\n" "$k" "$([ -n "$v" ] && echo filled || echo EMPTY)"
done
REMOTE
