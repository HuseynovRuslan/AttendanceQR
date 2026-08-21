#!/usr/bin/env bash
# Move Caddy's TLS state (certificates, private keys, ACME account) from the OLD host to the NEW one
# WITHOUT the private keys ever touching the operator's disk: the old host tars the volume to stdout,
# the bytes cross two SSH tunnels through a pipe in this process's memory, and the new host untars
# straight into a Docker volume. Nothing is written locally, so there is nothing to shred.
#
# Usage (from the operator machine, Git Bash):
#   OLD_SSH='ssh -o StrictHostKeyChecking=no root@62.84.179.39' \
#   NEW_SSH='ssh -i ~/.ssh/qrlog_vps_l -o BatchMode=yes -o IdentitiesOnly=yes deploy@94.20.153.137' \
#   TARGET_VOLUME=attendanceqr_caddy_data MIN_DAYS=20 \
#   bash ops/new-server/caddy-data-transfer.sh
#
#   Rehearsal: TARGET_VOLUME=caddy_data_cutover_test — a throwaway volume, removed by the caller after.
#
# Exit codes: 0 ok · 3 transfer/verify failed · 4 a LIVE certificate has fewer than MIN_DAYS left
# (the cutover must not start on a cert that Caddy will need to renew in the middle of the night).
set -euo pipefail
: "${OLD_SSH:?set OLD_SSH}" "${NEW_SSH:?set NEW_SSH}"
TARGET_VOLUME=${TARGET_VOLUME:-attendanceqr_caddy_data}
SOURCE_VOLUME=${SOURCE_VOLUME:-attendanceqr_caddy_data}
MIN_DAYS=${MIN_DAYS:-20}
# Only these names matter on the new host; stale certs (ecafe, katalog, sinaq, tlstest…) are ignored
# by the expiry gate but copied anyway (harmless, and Caddy simply never serves them).
LIVE_HOSTS=${LIVE_HOSTS:-"qrlog.az www.qrlog.az api.qrlog.az bax.qrlog.az app.qrlog.az ecaf.qrlog.az cleanfix.qrlog.az test.qrlog.az api-test.qrlog.az admin.qrlog.az pivezakuska.qrlog.az"}

if [ "${VERIFY_ONLY:-0}" != 1 ]; then
echo "== 1. stream $SOURCE_VOLUME (old) → $TARGET_VOLUME (new), no local file =="
# shellcheck disable=SC2086
$OLD_SSH "docker run --rm -v ${SOURCE_VOLUME}:/d:ro alpine tar czf - -C /d ." \
  | $NEW_SSH "docker volume create ${TARGET_VOLUME} >/dev/null && docker run --rm -i -v ${TARGET_VOLUME}:/d alpine sh -c 'tar xzf - -C /d && echo unpacked'" \
  || { echo "TRANSFER FAILED" >&2; exit 3; }
fi

echo "== 2. verify on new host: file count, ACME account, per-host expiry =="
# shellcheck disable=SC2086
$NEW_SSH "docker run --rm -i -v ${TARGET_VOLUME}:/d:ro -e LIVE='${LIVE_HOSTS}' -e MIN_DAYS=${MIN_DAYS} alpine sh -s" <<'REMOTE' | tee /tmp/caddy-verify.out
set -e
apk add --no-cache openssl >/dev/null 2>&1
echo "  files: $(find /d -type f | wc -l) · acme accounts: $(find /d/caddy/acme -name '*.json' 2>/dev/null | wc -l)"
bad=0
for crt in $(find /d/caddy/certificates -name '*.crt' | sort); do
  host=$(basename "$crt" .crt)
  end=$(openssl x509 -in "$crt" -noout -enddate | cut -d= -f2)
  # -checkend is portable (BusyBox date cannot parse the LE date format): 0 = still valid past MIN_DAYS
  if openssl x509 -in "$crt" -noout -checkend $((MIN_DAYS*86400)) >/dev/null; then ok=yes; else ok=no; fi
  live=no; for h in $LIVE; do [ "$h" = "$host" ] && live=yes; done
  flag="  "; if [ "$live" = yes ] && [ "$ok" = no ]; then flag="!!"; bad=1; fi
  printf "  %s %-26s bitir: %-24s >=%s gün: %-3s %s\n" "$flag" "$host" "$end" "$MIN_DAYS" "$ok" "$([ "$live" = yes ] && echo canlı || echo köhnə)"
done
for h in $LIVE; do [ -f "/d/caddy/certificates/acme-v02.api.letsencrypt.org-directory/$h/$h.crt" ] || { echo "  !! canlı host üçün sertifikat YOXDUR: $h"; bad=1; }; done
[ "$bad" = 0 ] || exit 4
REMOTE
grep -q "bitir:" /tmp/caddy-verify.out || { echo "VERIFY produced no certificate lines — treat as FAILED" >&2; exit 3; }
rm -f /tmp/caddy-verify.out
echo "== done: $TARGET_VOLUME on the new host carries every live certificate with ≥ ${MIN_DAYS} days left =="
