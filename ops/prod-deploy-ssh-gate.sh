#!/usr/bin/env bash
# The ONLY thing the GitHub Actions release key can run on the production server.
#
# Installed root-owned, outside the repo, at /usr/local/bin/qrlog-prod-deploy-gate, and named as the
# forced command of that key in the release user's authorized_keys:
#
#     restrict,command="/usr/local/bin/qrlog-prod-deploy-gate" ssh-ed25519 AAAA… qrlog-github-actions
#
# so whatever a client asks for arrives here as SSH_ORIGINAL_COMMAND, and nothing else ever runs.
# Two commands exist, each with an exact commit:
#
#     preflight <40-hex sha>   every check a deploy would make, changing nothing
#     deploy    <40-hex sha>   deploy exactly that commit (it must still be the tip of main)
#
# The release user has no shell use and no sudo except one line: the root helper below. The command is
# parsed here and again, independently, by the helper — sudo's argument list is the only thing that
# crosses, so nothing a client sends can reach a shell. See ops/CLOUD-DEPLOY.md.
set -euo pipefail

readonly ROOT_HELPER=/usr/local/sbin/qrlog-prod-release

# Prints "<mode> <sha>" for a well-formed command; nothing (and status 1) for anything else.
parse_command() {
  local cmd="${1-}"
  # One line, lowercase hex, single spaces — a newline, an uppercase letter or a trailing word is a
  # different command, and different commands are refused, not cleaned up.
  [[ "$cmd" == *$'\n'* ]] && return 1
  [[ "$cmd" =~ ^(preflight|deploy)\ ([0-9a-f]{40})$ ]] || return 1
  printf '%s %s\n' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}"
}

main() {
  local parsed mode sha client="${SSH_CLIENT-}"
  client="${client%% *}"
  if ! parsed=$(parse_command "${SSH_ORIGINAL_COMMAND-}"); then
    # The refused text is not logged: it is attacker-controlled.
    logger -t qrlog-release-gate -- "refused a malformed command from ${client:-?}" 2>/dev/null || true
    echo "REFUSING: allowed commands are 'preflight <sha>' and 'deploy <sha>' (40 lowercase hex)." >&2
    exit 2
  fi
  read -r mode sha <<<"$parsed"
  logger -t qrlog-release-gate -- "$mode $sha requested from ${client:-?}" 2>/dev/null || true
  exec /usr/bin/sudo -n "$ROOT_HELPER" "$mode" "$sha"
}

# Sourced by ops/tests/release-gate.test.sh for parse_command; run as the forced command otherwise.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main
fi
