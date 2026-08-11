#!/usr/bin/env bash
# Nightly database backup to Cloudflare R2.
#
# Until this existed the only copies were two hand-made dumps sitting on the same disk as the
# database they came from — which is not a backup, it is a second copy of the same single point of
# failure. One disk incident would have taken the company's attendance history, payroll and every
# customer relationship with it.
#
# The database is ~11 MB, so this is deliberately the simplest thing that works: dump, check the dump
# is real, ship it off the machine, prune. No incremental cleverness to go wrong at 03:00.
set -euo pipefail

APP_DIR=/opt/attendanceqr
ENV_FILE="$APP_DIR/.env"
WORK_DIR="$APP_DIR/backups"
LOG="$APP_DIR/backups/backup.log"
KEEP_LOCAL=7          # days of dumps kept on the box, for a fast restore
KEEP_REMOTE_DAYS=90   # days kept in R2
# A dump smaller than this means pg_dump failed and wrote a stub. Uploading it would silently replace
# good backups with garbage — the failure mode that turns "we have backups" into a discovery on the
# worst possible day.
MIN_BYTES=20000

mkdir -p "$WORK_DIR"
exec >>"$LOG" 2>&1
echo "--- $(date -Is) backup start"

# shellcheck disable=SC1090
set -a; . "$ENV_FILE"; set +a

# WHERE the backups go, and WITH WHOSE KEYS.
#
# These used to be the app's own R2 credentials, writing into the same bucket as the check-in
# selfies. That put every database backup behind a key the running application holds — and a backup
# exists precisely for the case where the application is compromised. This box has already had one
# intrusion (2026-07-10), and in that scenario the attacker would have found, in the container's
# environment, keys that delete every restore point the company has.
#
# So: a SEPARATE bucket and a SEPARATE R2 API token scoped to it alone. Set in .env:
#
#   Backup__R2__AccessKey=...
#   Backup__R2__SecretKey=...
#   Backup__R2__BucketName=qrlog-backups
#   Backup__R2__Endpoint=<account>.r2.cloudflarestorage.com   # optional, defaults to the app's
#
# Until they are set this falls back to the app's credentials and says so, loudly, every night. It
# does NOT refuse: an unseparated backup is a weaker backup, no backup at all is a lost company.
R2_KEY=${Backup__R2__AccessKey:-$Storage__Minio__AccessKey}
R2_SECRET=${Backup__R2__SecretKey:-$Storage__Minio__SecretKey}
R2_BUCKET=${Backup__R2__BucketName:-$Storage__Minio__BucketName}
R2_ENDPOINT=${Backup__R2__Endpoint:-$Storage__Minio__Endpoint}

if [ -z "${Backup__R2__AccessKey:-}" ]; then
  echo "WARNING: backups are using the APP's R2 credentials and bucket. Anyone who compromises the"
  echo "         app can delete every restore point. Create a separate bucket + scoped token and set"
  echo "         Backup__R2__* in .env — see ops/README.md."
  if [ -x "$APP_DIR/ops/alert.sh" ]; then
    "$APP_DIR/ops/alert.sh" "backups still share the app's R2 credentials — a compromised app can delete them" || true
  fi
fi

STAMP=$(date -u +%Y%m%d_%H%M%S)
FILE="$WORK_DIR/attendanceqr_${STAMP}.sql.gz"

docker exec attendanceqr-db-1 pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --clean --if-exists \
  | gzip -9 > "$FILE"

SIZE=$(stat -c%s "$FILE")
if [ "$SIZE" -lt "$MIN_BYTES" ]; then
  echo "FAIL: dump is only ${SIZE} bytes — keeping the previous backups untouched"
  rm -f "$FILE"
  exit 1
fi

# The archive must also be readable. A gzip that only decompresses halfway is worthless, and this
# costs a fraction of a second on a file this size.
if ! gzip -t "$FILE"; then
  echo "FAIL: ${FILE} is not a valid gzip archive"
  rm -f "$FILE"
  exit 1
fi

echo "dump ok: $(basename "$FILE") (${SIZE} bytes)"

# Off the machine, into the backup bucket (see the credential note above).
docker run --rm \
  -e AWS_ACCESS_KEY_ID="$R2_KEY" \
  -e AWS_SECRET_ACCESS_KEY="$R2_SECRET" \
  -e AWS_DEFAULT_REGION=auto \
  -v "$WORK_DIR":/backups \
  amazon/aws-cli:latest \
  s3 cp "/backups/$(basename "$FILE")" \
    "s3://${R2_BUCKET}/db-backups/$(basename "$FILE")" \
    --endpoint-url "https://${R2_ENDPOINT}" \
    --only-show-errors

echo "uploaded to r2: db-backups/$(basename "$FILE")"

find "$WORK_DIR" -name 'attendanceqr_*.sql.gz' -mtime +$KEEP_LOCAL -delete

# Prune R2 by age. Listing is cheap at this volume and avoids depending on bucket lifecycle rules,
# which are configured in a console nobody will remember to check.
CUTOFF=$(date -u -d "-${KEEP_REMOTE_DAYS} days" +%Y-%m-%d)
docker run --rm \
  -e AWS_ACCESS_KEY_ID="$R2_KEY" \
  -e AWS_SECRET_ACCESS_KEY="$R2_SECRET" \
  -e AWS_DEFAULT_REGION=auto \
  amazon/aws-cli:latest \
  s3 ls "s3://${R2_BUCKET}/db-backups/" \
    --endpoint-url "https://${R2_ENDPOINT}" \
  | awk -v cutoff="$CUTOFF" '$1 < cutoff { print $4 }' \
  | while read -r old; do
      [ -z "$old" ] && continue
      docker run --rm \
        -e AWS_ACCESS_KEY_ID="$R2_KEY" \
        -e AWS_SECRET_ACCESS_KEY="$R2_SECRET" \
        -e AWS_DEFAULT_REGION=auto \
        amazon/aws-cli:latest \
        s3 rm "s3://${R2_BUCKET}/db-backups/${old}" \
          --endpoint-url "https://${R2_ENDPOINT}" --only-show-errors
      echo "pruned r2: $old"
    done

echo "--- $(date -Is) backup done"
