# hr.test.qrlog.az — the HR module's own test environment

Vüqar Bəbirov builds the HR module (see `docs/hr-modulu.md`) and needs to see his work running without
waiting for anyone. Pushing to the `hr-modulu` branch deploys it to **https://hr.test.qrlog.az** within a
few minutes. He has no server access and needs none.

## What is where

| | Server | In this repo (record only) |
|---|---|---|
| Compose file | `/opt/qrlog-hr-infra/docker-compose.hr.yml` | `ops/hr/docker-compose.hr.yml` |
| Autodeploy | `/opt/qrlog-hr-infra/hr-autodeploy.sh` | `ops/hr/hr-autodeploy.sh` |
| Secrets | `/opt/qrlog-hr-infra/.env.hr` (root, 600) | `ops/hr/.env.hr.example` |
| Cron | `/etc/cron.d/qrlog-hr` — every 2 min, root | — |
| Source | `/opt/qrlog-hr` — checkout of `hr-modulu` | — |
| Log | `/var/log/qrlog-hr-deploy.log` | — |
| Caddy | `Caddyfile` blocks `hr.test` / `api-hr.test` | `Caddyfile` |

**The server copies are the ones that run, and they are deliberately not read from any branch.** The
branch supplies source code only. That is the whole security model of this environment: whoever can
push to `hr-modulu` can change what gets built, but not which containers exist, what they mount, which
network they sit on or what they may reach. Change the server copy and the repo copy together.

Contrast with staging (`test.qrlog.az`), whose autodeploy reads `docker-compose.staging.yml` from the
branch it deploys and joins production's docker network.

## Isolation

- Own database (`qrlog-hr-db`, volume `qrlog-hr_hrdata`), own JWT / QR / DB secrets.
- Own network `qrlog_hr_edge`, shared only with Caddy. The autodeploy re-attaches Caddy on every run,
  because recreating the Caddy container drops the attachment.
- Photo storage, face matching and push are hard-coded off.
- Memory/CPU caps: backend 1 GB / 1 CPU, frontend 256 MB, database 512 MB.

## Operating it

    sudo FORCE=1 /opt/qrlog-hr-infra/hr-autodeploy.sh      # redeploy now, even with no new commit
    sudo tail -f /var/log/qrlog-hr-deploy.log

Reset the database (drops everything in it, re-seeds the `hr` company on next start):

    sudo docker compose -f /opt/qrlog-hr-infra/docker-compose.hr.yml --project-directory /opt/qrlog-hr \
         --env-file /opt/qrlog-hr-infra/.env.hr down -v
    sudo FORCE=1 /opt/qrlog-hr-infra/hr-autodeploy.sh

Remove the environment entirely: the `down -v` above, then delete `/etc/cron.d/qrlog-hr`,
`/opt/qrlog-hr`, `/opt/qrlog-hr-infra`, the network `qrlog_hr_edge`, and the two Caddyfile blocks.
