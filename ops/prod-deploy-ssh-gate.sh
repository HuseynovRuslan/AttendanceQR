#!/usr/bin/env bash
# Install OUTSIDE the repo at /usr/local/bin/qrlog-prod-deploy-gate.
set -euo pipefail
if [[ ! "${SSH_ORIGINAL_COMMAND:-}" =~ ^deploy[[:space:]]([0-9a-f]{40})$ ]]; then
  echo 'REFUSING: deploy requires exact 40-character SHA.' >&2
  exit 2
fi
release_sha="${BASH_REMATCH[1]}"
cd /opt/attendanceqr
git fetch -q origin main
if [[ "$(git rev-parse origin/main)" != "$release_sha" ]]; then
  echo 'REFUSING: main moved after approval.' >&2
  exit 1
fi
exec env EXPECTED_SHA="$release_sha" /bin/bash /opt/attendanceqr/ops/deploy-prod.sh
