# Ops

Three cron scripts, installed on the server at `/opt/attendanceqr/ops/`, plus `build-landing.sh`,
which runs on deploy rather than on a schedule.

## backup.sh — nightly, 03:15

Dumps Postgres, checks the dump is real (size + gzip integrity), uploads it to Cloudflare R2 under
`db-backups/`, prunes local copies after 7 days and remote ones after 90.

Before this existed the only copies were two hand-made dumps on the same disk as the database — a
second copy of the same single point of failure. The database is ~11 MB, so nothing here is clever:
dump, verify, ship, prune.

## restore-test.sh — weekly, Sunday 04:00

Restores the newest dump into a throwaway database beside the live one and checks that Employees,
AttendanceRecords and Tenants actually came back with rows, then drops it. Never touches production.

An untested backup is a belief. This is the difference between having backups and finding out.

## watchdog.sh — every 5 minutes

Checks `/health` (which touches the database, so a running process with unreachable Postgres counts
as down), restarts a container that has stopped, warns at 85% disk, and warns if no backup has
appeared in 48 hours — the nightly job failing silently is the likeliest way to lose the safety net.

**It cannot tell you the machine is down.** Nothing running on the machine can. See below.

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

If the Caddyfile changed in the same pull, reload Caddy after it — but validate first, because an
invalid config takes qrlog.az, bax.qrlog.az and api.qrlog.az down together:

    docker compose -f docker-compose.prod.yml exec caddy caddy validate --config /etc/caddy/Caddyfile \
      && docker compose -f docker-compose.prod.yml exec caddy caddy reload --config /etc/caddy/Caddyfile

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
