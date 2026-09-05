# Ops

Three cron scripts, installed on the server at `/opt/attendanceqr/ops/`, plus `alert.sh` (used by
the watchdog, not scheduled) and `build-landing.sh`, which runs on deploy rather than on a schedule.

## backup.sh — nightly, 03:15

Dumps Postgres, checks the dump is real (size + gzip integrity), uploads it to Cloudflare R2 under
`db-backups/`, prunes local copies after 7 days and remote ones after 90.

Before this existed the only copies were two hand-made dumps on the same disk as the database — a
second copy of the same single point of failure. The database is ~11 MB, so nothing here is clever:
dump, verify, ship, prune.

**Done 2026-08-11.** Backups now go to bucket `qrlog-backups` in the *domain* account
(`bb45c8206dd50205e11924f4b66953d0`), with a token scoped to that bucket alone — a different
Cloudflare account from the one holding the photos, which is stronger than the same-account split
originally planned: even losing the whole photo account leaves the restore points intact.

The 112 historical dumps were copied across (verified by name, size and `gzip -t` on all 112) and then
**deleted from the photo bucket**. That last step closed the part that mattered most and was not in
the original framing: the app's own R2 key could *read* those dumps. A dump is the entire database —
every employee, phone number, salary and PIN hash — so anyone who compromised the application could
have taken the lot by downloading one file, without touching Postgres at all.

Proven end to end on the same day: a backup was pulled from the new bucket, restored into a scratch
database, and checked (132 employees, 2 070 attendance records, 4 tenants), then dropped.

The original instructions, kept because they are what to repeat if this ever has to be redone:

**Give it its own bucket and its own token — 5 minutes, do this once.** The backups went into the
same bucket as the check-in selfies, using the credentials the running app holds. A backup exists for
the case where the app is compromised, so keeping it behind the app's own key defeats the point —
and this box has already had one intrusion (2026-07-10), where those keys sat in the container's
environment.

1. Cloudflare → R2 → **Create bucket** → `qrlog-backups`
2. R2 → **Manage API tokens** → Create token → Object Read & Write, **scoped to that bucket only**
3. Add to `/opt/attendanceqr/.env` (no compose entry — this is a shell script):

       Backup__R2__AccessKey=...
       Backup__R2__SecretKey=...
       Backup__R2__BucketName=qrlog-backups

4. `ops/backup.sh` → the nightly log should no longer print the WARNING

Until then it keeps working with the app's credentials and warns every night, in the log and through
`alert.sh`. It deliberately does not refuse: an unseparated backup is a weaker backup, no backup at
all is a lost company.

## Where things actually live — TWO Cloudflare accounts

Written down because it cost an afternoon to rediscover: the domain and the storage are in
**different Cloudflare accounts**, and nothing in the dashboard tells you that. Someone looking for
the photos logs in, sees `qrlog.az`, opens R2, is offered "Get started with R2", and reasonably
concludes the bucket was never created.

| What | Where |
|---|---|
| `qrlog.az` DNS (nameservers `sage`/`courtney.ns.cloudflare.com`) | account **A** — the one you log into normally |
| R2 bucket `attendance-photos` — every selfie, every face baseline, and (today) the DB backups | account **B**, id `e2c22f9a7a16e3485fa8e3156dd053ba` |

The account id is not hidden — it is the first label of the S3 endpoint in `.env`
(`<account-id>.r2.cloudflarestorage.com`), and `dash.cloudflare.com/<account-id>` jumps straight to
it. That is the fastest way to answer "which account is this?" for any Cloudflare resource.

What is in the bucket, as of 2026-08-11:

    checkins/     2 215 objects   60 MB   daily check-in selfies (pruned by PhotoCleanupJob)
    reference/      142 objects  5.9 MB   face-match baselines — NEVER pruned
    db-backups/     112 objects   64 MB   nightly dumps

**The separation is accidental, not designed** — but for the backups it is an advantage, which is why
`qrlog-backups` goes in account A rather than beside the photos: an attacker who takes the whole
photo account still cannot reach the restore points.

**Custody was the question that mattered, and it is settled: both accounts belong to the company**
(confirmed 2026-08-11). Employee face photographs are biometric data and qrlog.az/mexfilik/ names the
employer as the data controller, so a contractor-owned bucket would have meant migrating 2 357
objects off someone else's account. It does not. The photos stay where they are — moving them would
touch the face-match baselines on a live system to buy nothing but tidiness.

What the split does still cost is memory. Keep **both** account ids somewhere findable, put a
recovery address the company controls (info@qrlog.az, which now has MX) on both, and enable 2FA on
both — the failure this section exists to prevent is not an attacker, it is nobody being able to find
the account that holds 114 people's faces.

## restore-test.sh — weekly, Sunday 04:00

Restores the newest dump into a throwaway database beside the live one and checks that Employees,
AttendanceRecords and Tenants actually came back with rows, then drops it. Never touches production.

An untested backup is a belief. This is the difference between having backups and finding out.

## watchdog.sh — every 5 minutes

Checks `/health` (which touches the database, so a running process with unreachable Postgres counts
as down), restarts a container that has stopped, warns at 85% disk, and warns if no backup has
appeared in 48 hours — the nightly job failing silently is the likeliest way to lose the safety net.

Everything it finds is **sent to a human** via `alert.sh`. It used to write to
`backups/watchdog.log` and stop there, which made it a detector with nobody on the other end — the
same silent-failure mode it was written to catch in the backup job.

**It cannot tell you the machine is down.** Nothing running on the machine can. See below.

## alert.sh — where the watchdog's findings go

Sends one line to Telegram or to a generic webhook. Configure **one** of these in
`/opt/attendanceqr/.env` (no compose entry needed — this is a shell script, not a container, so it
reads the file directly):

    ALERT_TELEGRAM_TOKEN=123456:AA...      # from @BotFather
    ALERT_TELEGRAM_CHAT=-1001234567890     # the group id the bot was added to

    # ...or anything that speaks {"text": "..."} — Slack, Discord (+/slack), Mattermost, n8n:
    ALERT_WEBHOOK_URL=https://...

Test the wiring in one command, rather than by breaking something:

    ops/alert.sh "test"

With neither set it is a **no-op that says so in the log** — an unconfigured alert channel must
never be able to make the watchdog itself fail.

**Repetition is throttled.** A fault alerts on first sighting, then at most every 6 hours while it
persists (`ALERT_REPEAT_HOURS`), and sends a ✅ line when it clears. Alerting every run would be 288
messages a day for one broken container, and a channel people mute is worse than no channel.

## External uptime alerting — 3 minutes, do this once

The watchdog covers "a container broke". It cannot cover "the server is off", because it is on the
server. For that, point any external checker at the health endpoint:

1. Sign up at https://uptimerobot.com (free tier is enough)
2. Add Monitor → HTTP(s)
3. URL: `https://api.qrlog.az/health`
4. Interval: 5 minutes
5. Alert contact: email + phone

It will then tell you the API is down before a customer does — which is the entire point.

## build-landing.sh — on deploy, not on a timer

Builds `landing/` (the qrlog.az marketing site) inside `node:22-alpine` and syncs the output into
`landing-dist/`, which Caddy bind-mounts as `/srv/qrlog`. The VM needs no Node installed.

The site is static, so it is the one part of the stack that compose does **not** rebuild — without
this script `qrlog.az` keeps serving whatever was in `landing-dist` the last time somebody ran it.
Run it on its own whenever the marketing copy changes — it touches nothing else, so it needs none
of the release ceremony the app does:

    cd /opt/attendanceqr && git pull && bash ops/build-landing.sh

If the Caddyfile changed in the same pull, `caddy reload` will NOT pick it up — the file is
bind-mounted, and git replaces its inode on pull, so anything run inside the container (validate
included) still reads the OLD file. Validate the new file from the host with a throwaway container,
then force-recreate — and validate first, because an invalid config takes every host behind this
Caddy down together, the neighbour projects included:

    docker run --rm -v /opt/attendanceqr/Caddyfile:/etc/caddy/Caddyfile:ro \
      caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile \
      && docker compose -f docker-compose.prod.yml up -d --force-recreate caddy

(`ops/deploy-prod.sh` now does exactly this on its own when it sees the Caddyfile changed; the
commands above are for a Caddyfile-only change shipped outside a deploy.)

It writes to a temp directory first and refuses to publish a build with no `index.html`, so a broken
build leaves the live site alone. It touches nothing but `landing-dist/` — never the app, the API or
the database.

## Logs

    /opt/attendanceqr/backups/backup.log
    /opt/attendanceqr/backups/restore-test.log
    /opt/attendanceqr/backups/watchdog.log

## Restoring for real

    gunzip -c backups/attendanceqr_YYYYMMDD_HHMMSS.sql.gz \
      | docker exec -i attendanceqr-db-1 psql -U attendanceqr -d attendanceqr

The dumps are taken with `--clean --if-exists`, so this replaces the current contents. Stop the
backend first unless you intend it to be writing during the restore.

---

# Staging — test.qrlog.az

Same code, its own database, on the same machine. Production and staging share nothing but the
Docker network Caddy uses to reach them.

Until this existed, every change went straight to the system 114 people use to record that they came
to work. That held only because nothing had gone wrong yet.

## How it is reached

`test.qrlog.az` and `api-test.qrlog.az` are two blocks in the repository's `Caddyfile`, pointing at
the `stg-frontend` / `stg-backend` aliases on the shared Docker network.

They belong in git, and the reason is not tidiness. They previously existed only as a hand edit of
the file on the server, so recreating the Caddy container for an unrelated change was enough to take
staging off the internet — with the staging deploy still logging "deployed" every time, because
building and starting had in fact worked. Nothing reported a problem until someone tried the URL.

## Deploying to staging

    cd /opt/attendanceqr
    git pull
    docker compose -f docker-compose.staging.yml --env-file .env.staging up -d --build

Then open https://test.qrlog.az and check the change. Log in with the seeded admin from
`.env.staging` (`TenantSeed__AdminPhone` / `TenantSeed__AdminPin`).

## Then production

    docker compose -f docker-compose.prod.yml up -d --build backend frontend

## What staging deliberately cannot do

Photo upload, face matching and push notifications are switched off — the app no-ops each rather
than failing. Staging must never send a notification to a real employee, write into the real photo
bucket, or spend money on a face API. If a change touches those paths, that part is verified in
production during a quiet hour, with the change already proven everywhere else.

## Release rule

Never deploy to production between **07:30–09:30** or **17:00–19:00**. Those are the scan peaks: a
mistake there means people cannot record that they came to work, and the record is what they are
paid on.
