# QRLog — production releases from GitHub Actions (Claude Code Cloud, local Claude Code, GitHub UI)

One path to production for everybody: the **manual** workflow `QRLog production deploy`
(`.github/workflows/production-deploy.yml`). Claude Code Cloud, a local Claude Code session, the `gh` CLI
and the GitHub website all start the same run; a person approves it in GitHub; the server checks that
approval itself and deploys exactly one commit of `main`. Nothing deploys on push.

```
dispatch (mode=preflight|deploy, expected_sha)
  └─ verify  — tests the exact commit: backend, frontend build + tests, release-script tests
       └─ release  — environment "production": waits for a required reviewer; main only
            ├─ refuses a deploy from 30 minutes before a Baku scan peak to its end
            ├─ asks GitHub for this job's OIDC token (issued only to an approved production job)
            └─ ssh with a dedicated key, host key pinned  →  "<mode> <sha>", the token on stdin
                 server: forced command  qrlog-prod-deploy-gate        (parses; nothing else can run)
                         sudo, one line  qrlog-prod-release             (verifies the token, checks again)
                                         qrlog-verify-release-token     (GitHub's signature and claims)
                         the release's ops/deploy-prod.sh  (lock, backup gate, build, health, rollback)
```

## Modes

- **preflight** (default) — every check a deploy would make, on the server too, and **changes nothing**
  in production: time window, approval, the commit is the tip of `main`, the checkout is what the last
  deploy left running, it is in `main`'s history, no local edits, pending migrations (each must be
  purely additive), the release's compose file validates, no deploy running, Docker up, production
  healthy, disk, backup script, every company on Legacy authorization. Use it first, every time.
- **deploy** — the same checks; if every one passes, the release's `ops/deploy-prod.sh` for exactly that
  commit. Needs `expected_sha`: the full commit you reviewed. If `main` has moved, the run refuses.

## Launch

```bash
sha=$(gh api repos/HuseynovRuslan/AttendanceQR/commits/main --jq .sha)   # the commit you reviewed
gh workflow run production-deploy.yml --ref main -f mode=preflight -f expected_sha="$sha"
gh workflow run production-deploy.yml --ref main -f mode=deploy    -f expected_sha="$sha"
gh run watch "$(gh run list --workflow production-deploy.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
```

Or **Actions → QRLog production deploy → Run workflow** on github.com (the mobile app works too). Every run
stops at **Review deployments**; only the reviewer listed on the `production` environment can approve.

**Approving is the owner's act.** An agent may start runs and read their logs; it must never approve a
deployment — not in the UI, and not through the API (`…/pending_deployments`), which the owner's own
`gh` login technically allows. GitHub's "prevent self-review" cannot enforce this here: with one
reviewer it would also block the owner's own runs. Approval means main's `ops/` scripts run as root on
the server, so look at what changed in `ops/` since the last release before saying yes.

## Approval, checked on the server

The release key alone starts nothing. With every request the release job sends, on stdin, the OIDC
token GitHub issues to that job — and GitHub issues one naming `environment: production` only to a job
that passed the environment's protection rules. The server (`ops/verify-release-token.py`) checks
GitHub's signature against GitHub's published keys, then that the token names this repository (by
name and numeric id), `refs/heads/main`, the `production` environment, this workflow, a manual dispatch,
a GitHub-hosted runner, the exact commit, and the mode (a token from an approved preflight cannot start
a deploy); that it is unexpired and a few minutes old at most; and that it has not been used before. A
stolen key, without an approval in GitHub, gets `REFUSING` and learns nothing.

An administrator logged in to the server is root already and can run the same checks without a token:
`sudo /usr/local/sbin/qrlog-prod-release preflight <sha>`.

## What is refused, and where

| Situation | Workflow | Server helper | deploy-prod.sh |
| --- | --- | --- | --- |
| a branch other than `main` | ✔ (job condition + environment branch rule) | ✔ (token) | |
| no approval; a token for another commit, mode, branch, environment or workflow; a token used twice | | ✔ | |
| `expected_sha` missing or not the run's commit | ✔ | | |
| a deploy starting 07:00–09:30 or 16:30–19:00 Asia/Baku (the peaks and the half hour before) | ✔ (after approval) | ✔ | peaks only |
| the build ran into a peak | | | ✔ nothing restarts, checkout put back |
| the server cannot tell Baku time (missing zone data answers in UTC) | ✔ | ✔ | ✔ |
| any command but `preflight <sha>` / `deploy <sha>`, any non-hex or short SHA | | ✔ (gate, then helper) | |
| the SHA is not the tip of `main` (main moved after approval); a tag posing as `origin/main` | | ✔ | ✔ |
| the commit is already deployed | | ✔ | |
| the checkout is not what the last deploy left running (a failed deploy, a `git pull` without one) | | ✔ | |
| deployed commit not in `main`'s history; tracked files edited on the server | | ✔ | |
| a pending migration that does anything but add (drop, rename, alter, SQL, data change, hand-built operation); an edited old migration; a migration outside the migrations folder | | ✔ | |
| production unhealthy before the deploy; Docker down; < 5 GB free; compose invalid | | ✔ | |
| another deploy or release check running | | ✔ | ✔ (flock) |
| a company not on Legacy authorization | | ✔ (before and after) | |
| `FORCE=1` | | env cleared | ✔ refused for an approved release |
| the new Caddyfile does not validate | | | ✔ before anything changes |
| the safety backup fails | | | ✔ before the checkout moves |

## When a deploy fails

`deploy-prod.sh` does everything that can refuse — time, exact commit, Caddyfile, backup — before the
checkout moves, and builds before anything restarts. Once the checkout has moved, every way out ends
with the checkout, the containers and the deploy record on one commit:

| What failed | Where production ends up |
| --- | --- |
| the build, or the build ran into a peak | nothing restarted; the checkout back on the previous commit |
| the start (even half-way), the health check, an unexpected error, a kill | rebuilt and restarted on the previous commit |
| the rollback is unhealthy too | checkout on the previous commit, record `unknown` — **hands needed now** |
| the edge after a Caddyfile change | the code stays; the proxy and the checkout's Caddyfile go back to the old one; the next automated release refuses (edited checkout) until a person fixes it |

The rollback restores **code**, not the database: migrations apply forward at startup. That is why the
automated path takes only additive migrations — the old code ignores a new table or column. A migration
that drops, renames or alters something is deployed by a person, by hand, with the fresh dump and a
restore plan in front of them.

Started, a deploy runs on the server detached from the SSH session: a dropped runner connection or a
cancelled run does **not** stop it half-way (the preflight checks logind would not kill it either). The
public workflow log shows `deploy-prod.sh`'s progress lines only; the full log is kept on the server
in `/var/log/qrlog-release/`, and the gate and helper write to syslog
(`journalctl -t qrlog-release-gate -t qrlog-release`). What is running is recorded, root-owned, in
`/var/lib/qrlog-deploy/deployed-commit` — written only by `deploy-prod.sh`.

A `git pull` in `/opt/attendanceqr` (for the landing site, say) moves the checkout past what is
running; automated releases then refuse until a deploy by hand (`sudo ops/deploy-prod.sh`) brings the
two together again.

## One-time setup

Done once, by the owner — the steps that create credentials or change the server are not an agent's to do.

**1. GitHub environment `production`** — required reviewer (the owner), deployment branches `main` only,
administrators cannot bypass. Secrets: `PROD_SSH_HOST`, `PROD_SSH_KNOWN_HOSTS` (the server's ed25519
host key, checked against the server's own key file), and — step 5 — `PROD_SSH_PRIVATE_KEY`. Variables:
`PROD_SSH_USER` (`qrlog-release`), `PROD_SSH_PORT`. The host is a secret, not a variable, because
variables are printed in the public log. To check it:
`gh api repos/HuseynovRuslan/AttendanceQR/environments/production`.

**2. A dedicated deploy key**, on your own computer — never your personal key, never with a passphrase
(the runner cannot type one):

```bash
ssh-keygen -t ed25519 -N "" -C qrlog-github-actions -f ~/.ssh/qrlog_actions_deploy
```

**3. The server side** — after this is merged, as root on the production server, from reviewed `main`
(a copy of `ops/`, so the live checkout is not touched):

```bash
scp ~/.ssh/qrlog_actions_deploy.pub <you>@<server>:/tmp/qrlog_actions_deploy.pub
# on the server:
sudo git -C /opt/attendanceqr fetch -q --no-tags origin +refs/heads/main:refs/remotes/origin/main
tmp=$(mktemp -d) && sudo git -C /opt/attendanceqr archive refs/remotes/origin/main ops | tar -x -C "$tmp"
sudo bash "$tmp/ops/install-prod-release-gate.sh" /tmp/qrlog_actions_deploy.pub
rm -rf "$tmp" /tmp/qrlog_actions_deploy.pub
```

The installer creates the `qrlog-release` account (password locked, in no privileged group), installs the
gate, the helper and the token check root-owned outside the repository, adds one validated sudoers
line, and authorizes the key as `restrict,command="/usr/local/bin/qrlog-prod-deploy-gate"`, in a
root-owned file the account cannot edit. It touches nothing in sshd, the firewall or other accounts.
Re-run it after any change to `ops/prod-deploy-ssh-gate.sh`, `ops/prod-release.sh` or
`ops/verify-release-token.py` is merged — the server keeps running the reviewed copies until then.

**4. Check the key alone can do nothing** (from your computer):

```bash
ssh -i ~/.ssh/qrlog_actions_deploy qrlog-release@<server> id </dev/null                 # → REFUSING
ssh -i ~/.ssh/qrlog_actions_deploy qrlog-release@<server> "preflight $(gh api repos/HuseynovRuslan/AttendanceQR/commits/main --jq .sha)" </dev/null   # → REFUSING: no approval proof
```

**5. Give GitHub the private key, then remove your copy:**

```bash
gh secret set PROD_SSH_PRIVATE_KEY --env production --repo HuseynovRuslan/AttendanceQR < ~/.ssh/qrlog_actions_deploy
rm ~/.ssh/qrlog_actions_deploy
```

**6. Run a preflight from GitHub** (the command above) and approve it. A green preflight is the acceptance
test of the whole path — key, host key, approval proof, every check — and it changes nothing.

## Rules that do not change

- Never deploy in 07:30–09:30 or 17:00–19:00 Asia/Baku; an automated deploy does not even start in the
  half hour before. Never `FORCE=1` for an approved release.
- Every company stays on **Legacy** authorization; nothing here switches Shadow or Enforced on, and both
  checks report a company that is not on Legacy.
- The staging environment and its admin PIN are not touched by any of this.
- No secret, private key, token or address goes into a workflow log, a pull request, the repository or a
  chat.
- After a deploy, a person checks the real things: a QR check-in and check-out, the staff list, the
  monthly tabel, the exports.

## Revoking

Remove the key from GitHub (`gh secret delete PROD_SSH_PRIVATE_KEY --env production`) and from the server
(`sudo rm /var/lib/qrlog-release/.ssh/authorized_keys`); the uninstall line is at the top of
`ops/install-prod-release-gate.sh`.
