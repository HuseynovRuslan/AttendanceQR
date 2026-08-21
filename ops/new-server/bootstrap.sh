#!/usr/bin/env bash
# Two-phase first-boot setup for the new QRLog host (VPS L). Idempotent: safe to re-run.
#
#   PHASE 1 (as root, fresh Ubuntu 24.04):   bash bootstrap.sh phase1 "ssh-ed25519 AAAA... comment"
#     → deploy user + its SSH key, firewall, fail2ban, auto-updates, Docker, swap, log caps, TZ.
#       Root and password logins are LEFT OPEN on purpose.
#   Then, from YOUR machine, in a SEPARATE session, prove the key works:
#       ssh -i ~/.ssh/qrlog_vps_l deploy@<ip> 'echo ok && sudo -n true && docker ps'
#   PHASE 2 (as root or deploy+sudo, keep the old session open):   bash bootstrap.sh phase2
#     → closes root login and every password/keyboard-interactive method. REFUSES to run unless
#       the auth journal shows a successful public-key login for deploy — so the door is never
#       locked before the key is proven to open it.
#
# Why two phases: a hardening script that locks sshd in the same breath it creates the key has one
# failure mode — a typo in the key — and its cost is a rebuild. The 2026-07-10 compromise is why
# password access must go; this ordering is why it can go safely.
set -euo pipefail

PHASE="${1:-}"
DEPLOY_USER=deploy
[ "$(id -u)" -eq 0 ] || { echo "run as root (or via sudo)" >&2; exit 2; }

phase1() {
  local PUBKEY="${1:-}"
  [ -n "$PUBKEY" ] || { echo "usage: bootstrap.sh phase1 '<ssh public key>'" >&2; exit 2; }
  case "$PUBKEY" in ssh-ed25519\ *|ssh-rsa\ *|ecdsa-*) ;; *) echo "that does not look like an SSH public key" >&2; exit 2;; esac

  export DEBIAN_FRONTEND=noninteractive
  apt-get update -q
  apt-get install -yq ufw fail2ban unattended-upgrades ca-certificates curl gnupg git htop jq

  # deploy user — sudo without password; the key IS the credential.
  id -u "$DEPLOY_USER" >/dev/null 2>&1 || adduser --disabled-password --gecos "" "$DEPLOY_USER"
  usermod -aG sudo "$DEPLOY_USER"
  echo "$DEPLOY_USER ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/90-deploy && chmod 440 /etc/sudoers.d/90-deploy
  install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" /home/$DEPLOY_USER/.ssh
  grep -qxF "$PUBKEY" /home/$DEPLOY_USER/.ssh/authorized_keys 2>/dev/null \
    || echo "$PUBKEY" >> /home/$DEPLOY_USER/.ssh/authorized_keys
  chmod 600 /home/$DEPLOY_USER/.ssh/authorized_keys && chown -R "$DEPLOY_USER:$DEPLOY_USER" /home/$DEPLOY_USER/.ssh
  # Make sure key auth is ON; touch nothing that would close the current door.
  printf 'PubkeyAuthentication yes\n' > /etc/ssh/sshd_config.d/10-pubkey.conf
  sshd -t && systemctl reload ssh

  # firewall: 22/80/443 only — Postgres, backends, staging ports are never on the internet.
  ufw --force reset >/dev/null
  ufw default deny incoming; ufw default allow outgoing
  ufw allow 22/tcp; ufw allow 80/tcp; ufw allow 443/tcp
  ufw --force enable

  cat > /etc/fail2ban/jail.d/sshd.local <<'F2B'
[sshd]
enabled = true
maxretry = 5
findtime = 10m
bantime = 1h
F2B
  systemctl enable --now fail2ban
  cat > /etc/apt/apt.conf.d/20auto-upgrades <<'APT'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT

  # Docker from Docker's own repo (the distro package lags and ships without compose v2).
  if ! command -v docker >/dev/null; then
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    chmod a+r /etc/apt/keyrings/docker.gpg
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
      > /etc/apt/sources.list.d/docker.list
    apt-get update -q
    apt-get install -yq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  fi
  usermod -aG docker "$DEPLOY_USER"
  cat > /etc/docker/daemon.json <<'DJ'
{ "log-driver": "json-file", "log-opts": { "max-size": "20m", "max-file": "5" } }
DJ
  systemctl enable --now docker && systemctl restart docker

  # swap (the old box's OOM lesson), journald cap, timezone
  if [ ! -f /swapfile ]; then
    fallocate -l 4G /swapfile && chmod 600 /swapfile && mkswap /swapfile >/dev/null && swapon /swapfile
    echo '/swapfile none swap sw 0 0' >> /etc/fstab
  fi
  sed -i 's/^#\?SystemMaxUse=.*/SystemMaxUse=500M/' /etc/systemd/journald.conf && systemctl restart systemd-journald
  timedatectl set-timezone Asia/Baku

  local IP; IP=$(hostname -I | awk '{print $1}')
  cat <<MSG

PHASE 1 done. Root/password login is STILL OPEN on purpose.
Now, from your own machine, in a NEW terminal, prove the key:
    ssh -i ~/.ssh/qrlog_vps_l $DEPLOY_USER@$IP 'echo ok && sudo -n true && docker ps'
If that prints "ok" and a (possibly empty) container list, run phase 2 — keeping THIS session open:
    sudo bash $(readlink -f "$0") phase2
MSG
}

phase2() {
  # Gate: the auth journal must show deploy getting in WITH A KEY. No proof, no lockdown.
  # grep -c, not grep -q: under pipefail a -q that exits on the first hit leaves journalctl with
  # SIGPIPE, the pipeline reads as failed, and a perfectly proven key gets refused.
  local hits
  hits=$(journalctl -u ssh --since "-24h" --no-pager 2>/dev/null | grep -c "Accepted publickey for $DEPLOY_USER" || true)
  if [ "${hits:-0}" -lt 1 ]; then
    echo "REFUSING: no successful public-key login for '$DEPLOY_USER' in the last 24h." >&2
    echo "Open a NEW terminal, log in with the key, then re-run phase2." >&2
    exit 3
  fi
  cat > /etc/ssh/sshd_config.d/00-hardening.conf <<'SSHD'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
PubkeyAuthentication yes
MaxAuthTries 3
X11Forwarding no
SSHD
  sshd -t && systemctl reload ssh
  cat <<'MSG'

PHASE 2 done: root login and all password methods are CLOSED; only the deploy key opens this host.
Before closing this session, confirm once more from a NEW terminal that the key still works.
MSG
}

case "$PHASE" in
  phase1) phase1 "${2:-}" ;;
  phase2) phase2 ;;
  *) echo "usage: bootstrap.sh phase1 '<pubkey>' | bootstrap.sh phase2" >&2; exit 2 ;;
esac
