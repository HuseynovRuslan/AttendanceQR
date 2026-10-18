# QRLog — production releases from GitHub Actions (Claude Code Cloud, local Claude Code, GitHub UI)

One path to production for everybody: the **manual** workflow `QRLog production deploy`
(`.github/workflows/production-deploy.yml`). Claude Code Cloud, a local Claude Code session, the `gh` CLI
and the GitHub website all start the same run; a person approves it in GitHub; the server checks it
again and deploys exactly one commit of `main`. Nothing deploys on push.

```
dispatch (mode=preflight|deploy, expected_sha)
  └─ verify  — tests the exact commit: backend, frontend build + tests, release-gate tests
       └─ release  — environment "production": waits for a required reviewer; main only
            ├─ refuses a deploy in the Baku peaks (07:30–09:30, 17:00–19:00 Asia/Baku)
            └─ ssh with a dedicated key, host key pinned  →  "<mode> <sha>"
                 server: forced command  qrlog-prod-deploy-gate   (parses; nothing else can run)
                         sudo, one line  qrlog-prod-release        (checks again; preflight or deploy)
                         ops/deploy-prod.sh  (lock, backup gate, build, health check, code rollback)
```

## Modes

- **preflight** (default) — every check a deploy would make, on the server too, and **changes nothing**:
  time window, the commit is the tip of `main`, the deployed commit is in `main`'s history, no local edits
  on the server, pending migrations (each must be additive), the release's compose file validates, no
  deploy running, Docker up, production healthy, disk, backup script, every company on Legacy
  authorization. Use it first, every time.
- **deploy** — the same checks; if every one passes, `ops/deploy-prod.sh` for exactly that commit. Needs
  `expected_sha`: the full commit you reviewed. If `main` has moved, the run refuses.

## Launch

```bash
sha=$(gh api repos/HuseynovRuslan/AttendanceQR/commits/main --jq .sha)   # the commit you reviewed
gh workflow run production-deploy.yml --ref main -f mode=preflight -f expected_sha="$sha"
gh workflow run production-deploy.yml --ref main -f mode=deploy    -f expected_sha="$sha"
gh run watch "$(gh run list --workflow production-deploy.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
```

Or **Actions → QRLog production deploy → Run workflow** on github.com (the mobile app works too). Every run
stops at **Review deployments**; only the reviewer listed on the `production` environment can approve.
An agent may start runs and read their logs; **approving is the owner's act** — an agent must never
approve a deployment, even where its token technically could.

## What is refused, and where

| Situation | Workflow | Gate | Helper | deploy-prod.sh |
| --- | --- | --- | --- | --- |
| a branch other than `main` | ✔ (job condition + environment branch rule) | | | |
| `expected_sha` missing or not the run's commit | ✔ | | | |
| a scan peak, Asia/Baku | ✔ (after approval) | | ✔ | ✔ |
| any command but `preflight <sha>` / `deploy <sha>`, any non-hex or short SHA | | ✔ | ✔ | |
| the SHA is not the tip of `main` (main moved after approval) | | | ✔ | ✔ |
| deployed commit not in `main`'s history; tracked files edited on the server | | | ✔ | |
| a pending migration that drops, renames, alters or runs SQL; an edited old migration | | | ✔ | |
| production unhealthy before the deploy; Docker down; < 5 GB free; compose invalid | | | ✔ | |
| another deploy running | | | ✔ | ✔ (flock) |
| a company not on Legacy authorization | | | ✔ (before and after) | |
| `FORCE=1` | | | env cleared | ✔ refused for an approved release |
| the safety backup fails | | | | ✔ stops before building |
| health fails after the build | | | | ✔ rolls the code back |

Started, a deploy runs on the server detached from the SSH session: a dropped runner connection or a
cancelled run does **not** stop it half-way — it completes, or rolls back by itself. Its log is kept on
the server as `backups/release-<time>-<sha>.log`; the gate and the helper also write to syslog
(`journalctl -t qrlog-release-gate -t qrlog-release`).

The rollback restores **code**, not the database: migrations apply forward at startup. That is why the
automated path takes only additive migrations — the old code ignores a new table or column. A migration
that drops, renames or alters something is deployed by a person, by hand, with the fresh dump and a
restore plan in front of them.

## One-time setup

Done once, by the owner — the steps that create credentials or change the server are not an agent's to do.

**1. GitHub environment `production`** — already configured: required reviewer (the owner), deployment
branches `main` only, administrators cannot bypass, variables `PROD_SSH_HOST`, `PROD_SSH_USER`
(`qrlog-release`), `PROD_SSH_PORT`, secret `PROD_SSH_KNOWN_HOSTS` (the server's ed25519 host key, checked
against the server's own key file and the existing trusted entry). To check it:
`gh api repos/HuseynovRuslan/AttendanceQR/environments/production`.

**2. A dedicated deploy key**, on your own computer — never your personal key, never with a passphrase
(the runner cannot type one):

```bash
ssh-keygen -t ed25519 -N "" -C qrlog-github-actions -f ~/.ssh/qrlog_actions_deploy
```

**3. The server side** — after this PR is merged, as root on the production server, from reviewed `main`
(a copy of `ops/`, so the live checkout is not touched):

```bash
scp ~/.ssh/qrlog_actions_deploy.pub <you>@<server>:/tmp/qrlog_actions_deploy.pub
# on the server:
sudo git -C /opt/attendanceqr fetch -q origin main
tmp=$(mktemp -d) && sudo git -C /opt/attendanceqr archive origin/main ops | tar -x -C "$tmp"
sudo bash "$tmp/ops/install-prod-release-gate.sh" /tmp/qrlog_actions_deploy.pub
rm -rf "$tmp" /tmp/qrlog_actions_deploy.pub
```

The installer creates the `qrlog-release` account (password locked, in no privileged group), installs the
gate and the helper root-owned outside the repository, adds one validated sudoers line, and authorizes
the key as `restrict,command="/usr/local/bin/qrlog-prod-deploy-gate"`, in a root-owned file the account
cannot edit. It touches nothing in sshd, the firewall or other accounts. Re-run it after any change to
`ops/prod-deploy-ssh-gate.sh` or `ops/prod-release.sh` is merged — the server keeps running the reviewed
copies until then.

**4. Check the key can do nothing else** (from your computer):

```bash
ssh -i ~/.ssh/qrlog_actions_deploy qrlog-release@<server> id            # → REFUSING
ssh -i ~/.ssh/qrlog_actions_deploy qrlog-release@<server> "preflight $(gh api repos/HuseynovRuslan/AttendanceQR/commits/main --jq .sha)"
```

**5. Give GitHub the private key, then remove your copy:**

```bash
gh secret set PROD_SSH_PRIVATE_KEY --env production --repo HuseynovRuslan/AttendanceQR < ~/.ssh/qrlog_actions_deploy
rm ~/.ssh/qrlog_actions_deploy
```

**6. Run a preflight from GitHub** (the command above) and approve it. A green preflight is the acceptance
test of the whole path; it changes nothing.

## Rules that do not change

- Never deploy in 07:30–09:30 or 17:00–19:00 Asia/Baku. Never `FORCE=1`.
- Every company stays on **Legacy** authorization; nothing here switches Shadow or Enforced on, and both
  checks report a company that is not on Legacy.
- The staging environment and its admin PIN are not touched by any of this.
- No secret, private key or address goes into a workflow log, a pull request, the repository or a chat.
- After a deploy, a person checks the real things: a QR check-in and check-out, the staff list, the
  monthly tabel, the exports.

## Revoking

Remove the key from GitHub (`gh secret delete PROD_SSH_PRIVATE_KEY --env production`) and from the server
(`sudo rm /var/lib/qrlog-release/.ssh/authorized_keys`); the uninstall line is at the top of
`ops/install-prod-release-gate.sh`.
