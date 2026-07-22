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

# Off the machine. Same R2 account the check-in photos already use, under its own prefix.
docker run --rm \
  -e AWS_ACCESS_KEY_ID="$Storage__Minio__AccessKey" \
  -e AWS_SECRET_ACCESS_KEY="$Storage__Minio__SecretKey" \
  -e AWS_DEFAULT_REGION=auto \
  -v "$WORK_DIR":/backups \
  amazon/aws-cli:latest \
  s3 cp "/backups/$(basename "$FILE")" \
    "s3://${Storage__Minio__BucketName}/db-backups/$(basename "$FILE")" \
    --endpoint-url "https://${Storage__Minio__Endpoint}" \
    --only-show-errors

echo "uploaded to r2: db-backups/$(basename "$FILE")"

find "$WORK_DIR" -name 'attendanceqr_*.sql.gz' -mtime +$KEEP_LOCAL -delete

# Prune R2 by age. Listing is cheap at this volume and avoids depending on bucket lifecycle rules,
# which are configured in a console nobody will remember to check.
CUTOFF=$(date -u -d "-${KEEP_REMOTE_DAYS} days" +%Y-%m-%d)
docker run --rm \
  -e AWS_ACCESS_KEY_ID="$Storage__Minio__AccessKey" \
  -e AWS_SECRET_ACCESS_KEY="$Storage__Minio__SecretKey" \
  -e AWS_DEFAULT_REGION=auto \
  amazon/aws-cli:latest \
  s3 ls "s3://${Storage__Minio__BucketName}/db-backups/" \
    --endpoint-url "https://${Storage__Minio__Endpoint}" \
  | awk -v cutoff="$CUTOFF" '$1 < cutoff { print $4 }' \
  | while read -r old; do
      [ -z "$old" ] && continue
      docker run --rm \
        -e AWS_ACCESS_KEY_ID="$Storage__Minio__AccessKey" \
        -e AWS_SECRET_ACCESS_KEY="$Storage__Minio__SecretKey" \
        -e AWS_DEFAULT_REGION=auto \
        amazon/aws-cli:latest \
        s3 rm "s3://${Storage__Minio__BucketName}/db-backups/${old}" \
          --endpoint-url "https://${Storage__Minio__Endpoint}" --only-show-errors
      echo "pruned r2: $old"
    done

echo "--- $(date -Is) backup done"
