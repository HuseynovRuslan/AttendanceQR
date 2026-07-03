#!/usr/bin/env bash
# Trigger a Coolify deploy from the terminal and stream the result — no dashboard clicking.
#
# Usage:
#   scripts/coolify-deploy.sh backend
#   scripts/coolify-deploy.sh frontend
#   scripts/coolify-deploy.sh both        # default
#
# Requires ~/.coolify/env (NOT in this repo) defining:
#   COOLIFY_URL, COOLIFY_TOKEN, COOLIFY_BACKEND_UUID, COOLIFY_FRONTEND_UUID
#
# All Coolify API responses are piped straight into python via stdin (never written to a temp
# file first) — on Windows/git-bash, curl and python disagree on path syntax (/c/... vs C:\...),
# so file-based handoff breaks silently. Streaming sidesteps that entirely.
set -euo pipefail

ENV_FILE="$HOME/.coolify/env"
if [ ! -f "$ENV_FILE" ]; then
  echo "Missing $ENV_FILE — create it with COOLIFY_URL / COOLIFY_TOKEN / COOLIFY_BACKEND_UUID / COOLIFY_FRONTEND_UUID" >&2
  exit 1
fi
# shellcheck disable=SC1090
source "$ENV_FILE"

TARGET="${1:-both}"

deploy_one() {
  local name="$1" uuid="$2"
  echo "==> Deploying $name ($uuid)…"

  local dep_uuid
  dep_uuid=$(curl -s -X POST "$COOLIFY_URL/api/v1/deploy?uuid=$uuid" \
    -H "Authorization: Bearer $COOLIFY_TOKEN" -H "Accept: application/json" \
    | python -c "import json,sys;print(json.load(sys.stdin)['deployments'][0]['deployment_uuid'])" 2>/dev/null || true)
  if [ -z "$dep_uuid" ]; then
    echo "!! Failed to queue deploy for $name" >&2
    return 1
  fi
  echo "    deployment_uuid=$dep_uuid — polling…"

  local status="queued"
  for _ in $(seq 1 60); do
    status=$(curl -s "$COOLIFY_URL/api/v1/deployments/$dep_uuid" \
      -H "Authorization: Bearer $COOLIFY_TOKEN" -H "Accept: application/json" \
      | python -c "import json,sys;print(json.load(sys.stdin).get('status','?'))" 2>/dev/null || echo "?")
    [ "$status" = "finished" ] || [ "$status" = "failed" ] && break
    sleep 5
  done

  if [ "$status" = "finished" ]; then
    echo "    ✓ $name deploy finished"
    return 0
  fi

  echo "    ✗ $name deploy status=$status — dumping log:" >&2
  curl -s "$COOLIFY_URL/api/v1/deployments/applications/$uuid" \
    -H "Authorization: Bearer $COOLIFY_TOKEN" -H "Accept: application/json" \
    | python -c "
import json, sys
d = json.load(sys.stdin)
item = next((x for x in d['deployments'] if x['deployment_uuid'] == '$dep_uuid'), d['deployments'][0])
logs = json.loads(item['logs']) if isinstance(item['logs'], str) else item['logs']
for e in logs:
    print(e.get('output', ''))
"
  return 1
}

rc=0
case "$TARGET" in
  backend)  deploy_one backend "$COOLIFY_BACKEND_UUID" || rc=1 ;;
  frontend) deploy_one frontend "$COOLIFY_FRONTEND_UUID" || rc=1 ;;
  both)
    deploy_one backend "$COOLIFY_BACKEND_UUID" || rc=1
    deploy_one frontend "$COOLIFY_FRONTEND_UUID" || rc=1
    ;;
  *)
    echo "Usage: $0 [backend|frontend|both]" >&2
    exit 2
    ;;
esac

exit $rc
