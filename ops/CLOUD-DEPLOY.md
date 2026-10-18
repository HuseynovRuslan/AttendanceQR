# QRLog — production deployment from Claude Code Cloud or local
This is a manual deployment workflow. No automatic production release happens on push.

## One-time secure setup
1. Review this PR before merging. Set GitHub repository environment `production` to **required reviewer approval** and restrict deployment branches to **main only**. Without these controls, DO NOT configure production credentials.
2. On a trusted local computer generate a **dedicated deploy key**, separate from your personal SSH key:
   `ssh-keygen -t ed25519 -f ~/.ssh/qrlog_actions_deploy -C qrlog-actions-deploy`
   Never paste the private key into a chat or repository.
3. Use a minimally privileged server deploy user with access to /opt/attendanceqr, git and Docker. Note Docker membership is effectively root-equivalent. Review account security.
4. Install the reviewed `ops/prod-deploy-ssh-gate.sh` outside the repo as root-owned executable:
   `sudo install -o root -g root -m 0755 ops/prod-deploy-ssh-gate.sh /usr/local/bin/qrlog-prod-deploy-gate`.
   In that deploy user's `~/.ssh/authorized_keys`, add the new public key prefixed by:
   `restrict,command="/usr/local/bin/qrlog-prod-deploy-gate"`.
   The deploy key must not permit ordinary shell commands or forwarding.
5. Verify the SSH **server host key** fingerprint against the server console, not simply by trusting ssh-keyscan output. Put verified known_hosts line in GitHub environment secret `PROD_SSH_KNOWN_HOSTS`.
6. In GitHub **production environment** add variables `PROD_SSH_HOST`, `PROD_SSH_USER`, optional `PROD_SSH_PORT`; and secrets `PROD_SSH_PRIVATE_KEY`, `PROD_SSH_KNOWN_HOSTS`. Do not change staging admin PIN.
7. Check that GitHub-hosted runners can actually reach SSH. If the firewall blocks them, use an approved tunnel/self-hosted runner rather than weakening firewall rules.

## Launch
After setup and merge to main, request using authenticated GitHub CLI:
```bash
gh workflow run production-deploy.yml --ref main
gh run list --workflow production-deploy.yml --limit 3
```
Or GitHub Actions → QRLog production deploy → Run workflow. Claude Code Cloud must have permission to trigger workflows; if not, the user triggers from GitHub. Human approval in the production environment is still required.

## Safety
The workflow tests the exact commit before release. Both the GitHub job and server deploy script block 07:30–09:30 and 17:00–19:00 **Asia/Baku**. The SSH gate checks that origin/main still matches the approved SHA. The existing deploy-prod.sh takes a fresh verified backup, applies backend/frontend builds, checks health, and automatically rolls back code upon health failure; it does NOT roll back already-applied database migrations.

After deploy, manually verify real QR check-in/out, staff list, monthly tabel, exports, and tenant AuthzMode 0 (Legacy). Do not automatically turn on Enforced mode. Never use FORCE=1. Secret material must never appear in workflow logs, PRs, or chat messages.
