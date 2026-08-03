# QRLog — Performance baseline

> Status: **static analysis complete; measured baseline PENDING a local DB.**
> No optimization implemented (per the task).

## Environment

| | |
|---|---|
| Commit SHA | `9e627dfb57e0` (branch `main`) |
| Date | 2026-08-03 |
| OS | Windows 11 (10.0.26200), Git-Bash/MINGW64 |
| .NET | 10.0.301 |
| Node | v22.17.1 |
| CPU | 12 cores |
| PostgreSQL | project uses **postgres:15** via docker-compose (host port 15432) |
| Photo storage | Cloudflare **R2** (S3 API via MinioPhotoStorageService), remote |
| Face match | AWS Rekognition, remote (advisory, backgrounded via FaceMatchWorker) |
| Build config | Release (target for measured runs) |
| DB connection | Npgsql → local docker postgres (for benchmarks) |

### ⚠️ Blocker for the MEASURED baseline
On this machine **Docker is not running and there is no local `psql`**, so a local isolated
PostgreSQL + Release backend cannot be started here to collect k6/EF numbers. The measured baseline
(flows A–F, p50/p95/p99, SQL counts, payload sizes) must be run once a local DB is available —
scripts under `performance/` are ready for that (see `performance/README.md`).

Production must **never** be used for load tests (task rule).

---

## Static-analysis findings (evidence from the current code)

The task's core hypothesis — *"do endpoints get slower as attendance history grows?"* — is answered
**yes**, and the reason is concrete:

### 🔴 Bottleneck #1 — `GET /api/attendance/me` is UNBOUNDED (returns the whole history)
`AttendanceQueryService.QueryRecordsAsync` selects the employee's **entire** attendance history with
**no `Take()` / pagination**:

```csharp
// src/AttendanceQR.Infrastructure/Services/AttendanceQueryService.cs:29
_db.AttendanceRecords
   .Where(r => r.EmployeeId == employeeId)
   .OrderByDescending(r => r.AttendanceDate)
   .Select(...)               // no .Take(N)
   .ToListAsync(ct);
```
- Grows **linearly** with history: 30 rows → trivial; 365 → a year; 1000 → every scan ever.
- **Latency type:** backend + database + response-payload size.

### 🔴 Bottleneck #2 — that same unbounded query runs on BOTH Home and Scan, and blocks camera-start
- Home fires it in a `Promise.all` on mount: `getMyProfile, getMyAttendance, getMySummary`
  (`frontend/src/pages/HomePage.tsx:40`).
- The **Scan page also imports and calls `getMyAttendance`** before the camera
  (`frontend/src/pages/ScanPage.tsx:10`) — so the full history is re-fetched just to derive
  "today's status", delaying camera-start as history grows.
- **Latency type:** backend/DB (the same `/me` query) sitting on the Home render AND the
  time-to-camera path.

### 🟠 Bottleneck #3 — `/me` does a second query per call (manual-by N+1)
After loading every row, it issues a second `Employees` query to resolve "əl ilə yazan" names
(`AttendanceQueryService.cs:45-48`). Small next to #1, but it is a second round-trip on the hot path
that also scales with how many distinct manual editors appear in the (unbounded) result.

### Notes / smaller items
- Home's critical fan-out is **3 parallel requests** (profile + me + summary), not the ~6 the task
  guessed — announcements / missed-checkout / push-status are **not** on the Home critical path here
  (verify per screen).
- `/reports/summary` reads DailySummaries (finished days) + computes today live — bounded by the
  month, not by total history. Lower risk than #1.
- Check-in/out (`POST /api/attendance/scan`) — flow not yet measured; suspected costs are the photo
  base64 decode + R2 upload (network) and the face-match enqueue. To be measured in flow C/D/E.

---

## The three biggest bottlenecks (summary)

| # | Bottleneck | Evidence | Latency type |
|---|-----------|----------|--------------|
| 1 | Unbounded `/api/attendance/me` (whole history, no paging) | `AttendanceQueryService.cs:29` — no `Take()` | backend + DB + payload |
| 2 | Full history re-fetched on Scan, blocks camera-start | `ScanPage.tsx:10` calls `getMyAttendance` | backend/DB on time-to-camera |
| 3 | `/me` second query (manual-by N+1) | `AttendanceQueryService.cs:45` | DB round-trip |

**Measured evidence (p50/p95/p99, SQL counts, payload KB at 30/365/1000 rows) is still to be
collected** — run `performance/` once a local DB is up to confirm the linear growth quantitatively.
