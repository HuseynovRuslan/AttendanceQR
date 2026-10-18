#!/usr/bin/env bash
# One-time — and safely re-runnable — server setup for production releases from GitHub Actions
# (ops/CLOUD-DEPLOY.md). Run as root on the production server, from a checkout of REVIEWED main:
#
#     sudo bash ops/install-prod-release-gate.sh /path/to/qrlog_actions_deploy.pub
#
# It creates a dedicated account that can do exactly one thing — ask for a preflight or a deploy of an
# exact main commit — and authorizes ONE dedicated public key for it:
#
#   qrlog-release user    system account, password locked, in no group with power (not sudo, not docker)
#   authorized_keys       root-owned, `restrict,command=<gate>`: no shell, no PTY, no forwarding, and the
#                         user cannot edit its own keys
#   the gate              /usr/local/bin/qrlog-prod-deploy-gate   (ops/prod-deploy-ssh-gate.sh)
#   the root helper       /usr/local/sbin/qrlog-prod-release      (ops/prod-release.sh)
#   sudoers               the release user may run the root helper, and nothing else
#
# The scripts are COPIED, root-owned, outside the repository: a later commit cannot change what the key
# runs until somebody with root reviews it and runs this again. It changes nothing in sshd, the
# firewall or any other account.
#
# To remove it all:  userdel qrlog-release; rm -rf /var/lib/qrlog-release /etc/sudoers.d/qrlog-release \
#                    /usr/local/bin/qrlog-prod-deploy-gate /usr/local/sbin/qrlog-prod-release
set -euo pipefail

readonly USER_NAME=qrlog-release
readonly HOME_DIR=/var/lib/qrlog-release
readonly GATE=/usr/local/bin/qrlog-prod-deploy-gate
readonly HELPER=/usr/local/sbin/qrlog-prod-release
readonly SUDOERS=/etc/sudoers.d/qrlog-release
here=$(cd "$(dirname "$0")" && pwd)

die() { echo "REFUSING: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run as root (sudo bash $0 <public key file>)."
[[ $# -eq 1 && -f "$1" ]] || die "usage: $0 /path/to/qrlog_actions_deploy.pub"
[[ -f "$here/prod-deploy-ssh-gate.sh" && -f "$here/prod-release.sh" ]] || die "run it from the repository's ops/ scripts."

# The key: one line, a bare ed25519 PUBLIC key — never a private key, never options of its own.
pub_file=$1
grep -q 'PRIVATE KEY' "$pub_file" && die "that is a PRIVATE key. Give the .pub file; the private key goes only into GitHub."
[[ $(grep -c . "$pub_file") -eq 1 ]] || die "the public key file must hold exactly one line."
key=$(grep . "$pub_file")
[[ "$key" =~ ^ssh-ed25519\ [A-Za-z0-9+/]+=*(\ [A-Za-z0-9._@-]+)?$ ]] || die "expected one 'ssh-ed25519 AAAA… comment' line."
ssh-keygen -lf "$pub_file" >/dev/null 2>&1 || die "ssh-keygen does not accept that key."
blob=$(cut -d' ' -f2 <<<"$key")

# A dedicated key, not one somebody already logs in with.
for f in /root/.ssh/authorized_keys /home/*/.ssh/authorized_keys; do
  [[ -f "$f" ]] || continue
  if grep -qF -- "$blob" "$f"; then
    die "this key is already authorized in $f — generate a dedicated deploy key."
  fi
done

# The account.
if ! id "$USER_NAME" >/dev/null 2>&1; then
  useradd --system --home-dir "$HOME_DIR" --no-create-home --shell /bin/sh \
    --comment "QRLog GitHub Actions release (forced command only)" "$USER_NAME"
fi
passwd -l "$USER_NAME" >/dev/null
for group in sudo admin wheel docker adm lxd; do
  gpasswd -d "$USER_NAME" "$group" >/dev/null 2>&1 || true
done

# The gate and the root helper — copies, root-owned.
install -o root -g root -m 0755 "$here/prod-deploy-ssh-gate.sh" "$GATE"
install -o root -g root -m 0755 "$here/prod-release.sh" "$HELPER"

# One sudoers line, checked before it is put in place.
tmp=$(mktemp)
trap 'rm -f "$tmp"' EXIT
cat >"$tmp" <<EOF
# QRLog release user: may run the root release helper and nothing else (ops/CLOUD-DEPLOY.md).
Defaults:$USER_NAME env_reset
$USER_NAME ALL=(root) NOPASSWD: $HELPER
EOF
visudo -cf "$tmp" >/dev/null || die "the generated sudoers file does not validate."
install -o root -g root -m 0440 "$tmp" "$SUDOERS"
visudo -c >/dev/null || die "sudoers as a whole does not validate — check $SUDOERS."

# The key, root-owned: the account can neither add keys nor drop the restriction.
install -d -o root -g root -m 0755 "$HOME_DIR" "$HOME_DIR/.ssh"
printf 'restrict,command="%s" %s\n' "$GATE" "$key" >"$HOME_DIR/.ssh/authorized_keys.new"
chown root:root "$HOME_DIR/.ssh/authorized_keys.new"
chmod 0644 "$HOME_DIR/.ssh/authorized_keys.new"
mv -f "$HOME_DIR/.ssh/authorized_keys.new" "$HOME_DIR/.ssh/authorized_keys"

echo "Installed."
echo "  account:      $USER_NAME (password locked; groups: $(id -nG "$USER_NAME"))"
echo "  key:          $(ssh-keygen -lf "$pub_file" | awk '{print $2}')"
echo "  sudo rights:  $(sudo -l -U "$USER_NAME" | grep -c NOPASSWD) line(s) — $HELPER only"
echo "  host key:     $(ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub | awk '{print $2, $NF}')"
echo
echo "Next: from your computer, check that the key can do nothing but this:"
echo "  ssh -i <private key> $USER_NAME@<server> 'preflight <40-hex sha of main>'   → runs the checks"
echo "  ssh -i <private key> $USER_NAME@<server> 'id'                              → REFUSING"
