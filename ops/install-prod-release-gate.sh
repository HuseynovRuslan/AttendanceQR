#!/usr/bin/env bash
# One-time — and safely re-runnable — server setup for production releases from GitHub Actions
# (ops/CLOUD-DEPLOY.md). Run as root on the production server, from a copy of REVIEWED main:
#
#     sudo bash ops/install-prod-release-gate.sh /path/to/qrlog_actions_deploy.pub
#
# It creates a dedicated account that can do exactly one thing — ask for a preflight or a deploy of an
# exact main commit, with GitHub's proof that the run was approved — and authorizes ONE dedicated
# public key for it:
#
#   qrlog-release user    system account, password locked, in no group with power (not sudo, not docker)
#   authorized_keys       root-owned, `restrict,command=<gate>`: no shell, no PTY, no forwarding, and the
#                         user cannot edit its own keys
#   the gate              /usr/local/bin/qrlog-prod-deploy-gate     (ops/prod-deploy-ssh-gate.sh)
#   the root helper       /usr/local/sbin/qrlog-prod-release        (ops/prod-release.sh)
#   the approval check    /usr/local/sbin/qrlog-verify-release-token (ops/verify-release-token.py)
#   sudoers               the release user may run the root helper, and nothing else
#
# The scripts are COPIED, root-owned, outside the repository: what the key may ask for, and every check
# made before a deploy, stay the reviewed copies until somebody with root runs this again. The deploy
# itself is the release's own ops/deploy-prod.sh — part of the commit being deployed, reviewed on main.
# It changes nothing in sshd, the firewall or any other account.
#
# To remove it all:  userdel qrlog-release; rm -rf /var/lib/qrlog-release /etc/sudoers.d/qrlog-release \
#                    /usr/local/bin/qrlog-prod-deploy-gate /usr/local/sbin/qrlog-prod-release \
#                    /usr/local/sbin/qrlog-verify-release-token
set -euo pipefail

readonly USER_NAME=qrlog-release
readonly HOME_DIR=/var/lib/qrlog-release
readonly GATE=/usr/local/bin/qrlog-prod-deploy-gate
readonly HELPER=/usr/local/sbin/qrlog-prod-release
readonly VERIFIER=/usr/local/sbin/qrlog-verify-release-token
readonly SUDOERS=/etc/sudoers.d/qrlog-release
here=$(cd "$(dirname "$0")" && pwd)

die() { echo "REFUSING: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run as root (sudo bash $0 <public key file>)."
[[ $# -eq 1 && -f "$1" ]] || die "usage: $0 /path/to/qrlog_actions_deploy.pub"
for f in prod-deploy-ssh-gate.sh prod-release.sh verify-release-token.py; do
  [[ -f "$here/$f" ]] || die "run it from the repository's ops/ scripts ($f is missing)."
done
[[ -x /usr/bin/python3 ]] || die "/usr/bin/python3 is needed to verify GitHub's approval tokens."
/usr/bin/python3 -I -c 'import ast, sys; ast.parse(open(sys.argv[1]).read())' "$here/verify-release-token.py" 2>/dev/null \
  || die "ops/verify-release-token.py does not parse with this server's python3."

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

# The account — created, or brought back to exactly this shape if it exists: sshd looks for the keys
# under its home and runs the forced command through its shell, so either one being different would
# silently disable the key.
if ! id "$USER_NAME" >/dev/null 2>&1; then
  useradd --system --home-dir "$HOME_DIR" --no-create-home --shell /bin/sh \
    --comment "QRLog GitHub Actions release (forced command only)" "$USER_NAME"
fi
usermod --home "$HOME_DIR" --shell /bin/sh "$USER_NAME"
passwd -l "$USER_NAME" >/dev/null
for group in sudo admin wheel docker adm lxd; do
  gpasswd -d "$USER_NAME" "$group" >/dev/null 2>&1 || true
done

# The gate, the root helper and the approval check — copies, root-owned.
install -o root -g root -m 0755 "$here/prod-deploy-ssh-gate.sh" "$GATE"
install -o root -g root -m 0755 "$here/prod-release.sh" "$HELPER"
install -o root -g root -m 0755 "$here/verify-release-token.py" "$VERIFIER"

# One sudoers line, checked before it is put in place — and taken out again if sudoers as a whole
# then fails to validate, so a bad run never leaves sudo broken.
tmp=$(mktemp)
trap 'rm -f "$tmp"' EXIT
cat >"$tmp" <<EOF
# QRLog release user: may run the root release helper and nothing else (ops/CLOUD-DEPLOY.md).
Defaults:$USER_NAME env_reset
$USER_NAME ALL=(root) NOPASSWD: $HELPER
EOF
visudo -cf "$tmp" >/dev/null || die "the generated sudoers file does not validate."
install -o root -g root -m 0440 "$tmp" "$SUDOERS"
if ! visudo -c >/dev/null; then
  rm -f "$SUDOERS"
  die "sudoers as a whole did not validate with the new file — it was removed again; check /etc/sudoers."
fi

# The key, root-owned: the account can neither add keys nor drop the restriction.
install -d -o root -g root -m 0755 "$HOME_DIR" "$HOME_DIR/.ssh"
printf 'restrict,command="%s" %s\n' "$GATE" "$key" >"$HOME_DIR/.ssh/authorized_keys.new"
chown root:root "$HOME_DIR/.ssh/authorized_keys.new"
chmod 0644 "$HOME_DIR/.ssh/authorized_keys.new"
mv -f "$HOME_DIR/.ssh/authorized_keys.new" "$HOME_DIR/.ssh/authorized_keys"

echo "Installed."
echo "  account:      $USER_NAME (password locked; shell $(getent passwd "$USER_NAME" | cut -d: -f7); groups: $(id -nG "$USER_NAME"))"
echo "  key:          $(ssh-keygen -lf "$pub_file" | awk '{print $2}')"
echo "  sudo rights:  $(sudo -l -U "$USER_NAME" | grep -c NOPASSWD) line(s) — $HELPER only"
echo "  host key:     $(ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub | awk '{print $2, $NF}')"
echo
echo "Next: from your computer, check that the key alone can do nothing:"
echo "  ssh -i <private key> $USER_NAME@<server> id </dev/null                        → REFUSING (not a command)"
echo "  ssh -i <private key> $USER_NAME@<server> 'preflight <sha of main>' </dev/null → REFUSING (no approval proof)"
echo "A preflight that runs needs an approved GitHub run (ops/CLOUD-DEPLOY.md, step 6)."
if [[ ! -s /var/lib/qrlog-deploy/deployed-commit ]]; then
  echo
  echo "Not yet recorded: which commit production runs. Releases refuse until it is —"
  echo "see ops/CLOUD-DEPLOY.md, one-time setup, step 3."
fi
