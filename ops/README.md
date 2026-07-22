# Ops

Three scripts, installed on the server at `/opt/attendanceqr/ops/` and driven by cron.

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

## Logs

    /opt/attendanceqr/backups/backup.log
    /opt/attendanceqr/backups/restore-test.log
    /opt/attendanceqr/backups/watchdog.log

## Restoring for real

    gunzip -c backups/attendanceqr_YYYYMMDD_HHMMSS.sql.gz \
      | docker exec -i attendanceqr-db-1 psql -U attendanceqr -d attendanceqr

The dumps are taken with `--clean --if-exists`, so this replaces the current contents. Stop the
backend first unless you intend it to be writing during the restore.
