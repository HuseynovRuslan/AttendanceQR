#!/usr/bin/env bash
# First-boot hardening + Docker for the new QRLog host (VPS L). Idempotent: safe to re-run.
#
# Run ONCE as root on a fresh Ubuntu 24.04 LTS:
#   curl -fsSL <raw-url>/ops/new-server/bootstrap.sh | bash -s -- "<ssh-public-key>"
# or copy the file over and:  bash bootstrap.sh "ssh-ed25519 AAAA... comment"
#
# What it does, in order, and why each step exists:
#   1. deploy user + SSH key   — nobody works as root; the key is the only way in.
#   2. sshd: keys only         — the 2026-07-10 compromise started with a password-reachable box.
#   3. ufw 22/80/443 only      — Postgres, backends, staging ports are never on the internet.
#   4. fail2ban + auto updates — brute force and unpatched CVEs are the two boring ways in.
#   5. Docker + compose plugin — pinned to Docker's own repo, not the distro's stale package.
#   6. swap 4G, journald/docker log caps, Asia/Baku — the OOM killer and a full disk are the
#      two ways the OLD box nearly died; neither gets a second chance here.
set -euo pipefail

PUBKEY="${1:-}"
DEPLOY_USER=deploy
[ -n "$PUBKEY" ] || { echo "usage: bootstrap.sh '<ssh public key>'" >&2; exit 2; }
[ "$(id -u)" -eq 0 ] || { echo "run as root" >&2; exit 2; }

export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get install -yq ufw fail2ban unattended-upgrades ca-certificates curl gnupg git htop jq

# 1. deploy user (sudo without password — the key IS the credential) -----------------------------
id -u "$DEPLOY_USER" >/dev/null 2>&1 || adduser --disabled-password --gecos "" "$DEPLOY_USER"
usermod -aG sudo "$DEPLOY_USER"
echo "$DEPLOY_USER ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/90-deploy && chmod 440 /etc/sudoers.d/90-deploy
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" /home/$DEPLOY_USER/.ssh
grep -qxF "$PUBKEY" /home/$DEPLOY_USER/.ssh/authorized_keys 2>/dev/null \
  || echo "$PUBKEY" >> /home/$DEPLOY_USER/.ssh/authorized_keys
chmod 600 /home/$DEPLOY_USER/.ssh/authorized_keys && chown -R "$DEPLOY_USER:$DEPLOY_USER" /home/$DEPLOY_USER/.ssh

# 2. sshd: keys only, no root login ------------------------------------------------------------
cat > /etc/ssh/sshd_config.d/90-hardening.conf <<'SSHD'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
PubkeyAuthentication yes
MaxAuthTries 3
X11Forwarding no
SSHD
sshd -t && systemctl reload ssh

# 3. firewall -----------------------------------------------------------------------------------
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

# 4. fail2ban + unattended security upgrades ------------------------------------------------------
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

# 5. Docker from Docker's repo ----------------------------------------------------------------
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
# Container logs: capped so a chatty container cannot fill the disk.
cat > /etc/docker/daemon.json <<'DJ'
{ "log-driver": "json-file", "log-opts": { "max-size": "20m", "max-file": "5" } }
DJ
systemctl enable --now docker
systemctl restart docker

# 6. swap, journald cap, timezone -------------------------------------------------------------
if [ ! -f /swapfile ]; then
  fallocate -l 4G /swapfile && chmod 600 /swapfile && mkswap /swapfile >/dev/null && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
sed -i 's/^#\?SystemMaxUse=.*/SystemMaxUse=500M/' /etc/systemd/journald.conf && systemctl restart systemd-journald
timedatectl set-timezone Asia/Baku

echo
echo "bootstrap done. Next: ssh -i <key> $DEPLOY_USER@$(hostname -I | awk '{print $1}')"
echo "Root SSH and password logins are now CLOSED — keep that key safe."
