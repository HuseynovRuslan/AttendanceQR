#!/usr/bin/env bash
# Is commit CANDIDATE functionally the same application as BASELINE (what production runs today)?
#
# "Functionally the same" is defined by what actually reaches the production images — not just
# src/ and frontend/ but everything each build context copies, plus the files that shape the build
# and the runtime: Dockerfiles and .dockerignore (they decide what is copied and how), the prod
# compose file (env wiring, ports, volumes), the Caddyfile (what the edge does with requests), and
# the root-level .NET build inputs if any ever appear. A server move and an application change must
# never share a night; this is the gate that proves they don't.
#
#   bash ops/new-server/app-parity-check.sh 798601a            # against HEAD
#   bash ops/new-server/app-parity-check.sh 798601a <sha|tag>  # against a candidate
# Exit 0 = identical on every guarded path · 1 = differences (listed) · 2 = usage/git error
set -uo pipefail
BASE=${1:?usage: app-parity-check.sh <baseline-sha> [candidate]}
CAND=${2:-HEAD}
cd "$(git rev-parse --show-toplevel)" || exit 2
git rev-parse -q --verify "$BASE^{commit}" >/dev/null || { echo "baseline $BASE not found" >&2; exit 2; }
git rev-parse -q --verify "$CAND^{commit}" >/dev/null || { echo "candidate $CAND not found" >&2; exit 2; }

# Backend: context is the repo root but the Dockerfile copies src/ only; frontend: the whole
# ./frontend context minus .dockerignore. Everything else listed shapes the build or the edge.
GUARDED=(
  src
  frontend
  src/AttendanceQR.Api/Dockerfile
  frontend/Dockerfile
  .dockerignore
  frontend/.dockerignore
  docker-compose.prod.yml
  Caddyfile
  AttendanceQR.sln
  global.json
  Directory.Build.props
  Directory.Packages.props
  nuget.config
)

echo "baseline  : $(git rev-parse --short "$BASE")  $(git log -1 --format=%s "$BASE" | cut -c1-60)"
echo "candidate : $(git rev-parse --short "$CAND")  $(git log -1 --format=%s "$CAND" | cut -c1-60)"
echo "guarded   : ${GUARDED[*]}"
echo

CHANGED=$(git diff --name-status "$BASE" "$CAND" -- "${GUARDED[@]}")
if [ -z "$CHANGED" ]; then
  echo "PARITY OK — no difference on any guarded path."
  echo "(for the record, paths that DID change outside the guard: $(git diff --name-only "$BASE" "$CAND" | wc -l | tr -d ' ') file(s) — ops/docs only is expected)"
  git diff --name-only "$BASE" "$CAND" | sed 's/^/   /'
  exit 0
fi

echo "PARITY BROKEN — the candidate is NOT the same application as the baseline:"
echo "$CHANGED" | sed 's/^/   /'
echo
echo "Either build the new host from $BASE itself, or deploy $CAND to the CURRENT production first"
echo "(on a separate, ordinary deploy night) so the move carries a version that already runs."
exit 1
