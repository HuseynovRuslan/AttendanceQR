# QRLog performance baseline

Reproducible baseline for QR scan, check-in/out and the employee Home page — **against a local /
isolated dev database only, never production.**

## Prerequisites
1. Local stack up (spins up postgres:15 on 15432, backend 8080, frontend 8081):
   ```bash
   docker compose up --build            # from repo root
   ```
2. `k6` installed (https://k6.io) — or use Bombardier/NBomber.
3. Backend built in **Release** for the measured run:
   ```bash
   dotnet run -c Release --project src/AttendanceQR.Api
   ```

## 1. Seed representative data
Create three test employees with 30 / 365 / 1000 attendance records so we can see whether latency
grows with history. Edit the IDs at the top of the script to match your dev seed's tenant + location,
then:
```bash
docker exec -i <db-container> psql -U <user> -d <db> < performance/seed/seed_perf.sql
```
The script is idempotent (`ON CONFLICT DO NOTHING`) and only touches employees named `perf-*`.

## 2. Warm up + measure
```bash
# single-user latency (>=5 warmup, >=30 measured, p50/p95/p99, SQL counts via server logs)
k6 run performance/k6/single-user.js

# concurrency 1 / 10 / 25, >=30s each, separate seeded employees
k6 run -e VUS=1  performance/k6/concurrency.js
k6 run -e VUS=10 performance/k6/concurrency.js
k6 run -e VUS=25 performance/k6/concurrency.js
```
First request is discarded (warm-up). Do **not** submit simultaneous scans for the *same* employee
unless testing idempotency.

## 3. SQL command counts + DB time
Temporarily enable EF command logging for the measured run only (NOT normal prod config):
```jsonc
// appsettings.Development.json
"Logging": { "LogLevel": { "Microsoft.EntityFrameworkCore.Database.Command": "Information" } }
```
Count commands + summed durations per request from the logs. Disable again afterwards.

## 4. Browser (Home + Scan) with Playwright
Add `performance.mark(...)` around: Home mount → profile/attendance/summary loaded → Home critical
complete; Scan mount → today resolved → device/GPS complete → camera-start attempted. Report the
deliberate selfie-countdown UX delay **separately** from backend latency.

## Output
Results go to `performance/results/baseline.json` (machine) and `baseline.md` (human) — commit SHA,
env, seeded counts, per-endpoint + full-flow numbers, SQL counts, payload sizes, errors, observations.

See `results/baseline.md` for the current static-analysis findings (the measured numbers are pending a
local DB on this machine — Docker/psql were unavailable when the baseline was scaffolded).
