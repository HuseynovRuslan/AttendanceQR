# AttendanceQR

QR-based attendance system with geofencing, per-device binding, role-based reporting, and a
nightly summary job. ASP.NET Core (.NET 10) backend + React (Vite) frontend.

Employees scan an HMAC-signed QR — in practice a **printed poster** at the site (a rotating kiosk
screen also exists). The backend verifies the signature, the token's version and short TTL, the
employee's bound device and the GPS geofence, then records the check-in/out.

The QR is deliberately **not** single-use: it is printed, so refusing a repeated token would mean
only one person per TTL window could check in. Replay is bounded by the TTL plus the geofence,
device binding and check-in photo — not by a nonce. See "Security notes" below for what these
controls do and do not prevent.

## Stack

- **Backend:** ASP.NET Core Web API, EF Core 10 + PostgreSQL (Npgsql), JWT auth, ClosedXML (Excel)
- **Frontend:** Vite + React + TypeScript + Tailwind, `html5-qrcode`, `qrcode.react`
- **Layers:** `Domain` (entities) · `Application` (reporting/business logic) · `Infrastructure`
  (EF, security, services) · `Api` (controllers, hosted job)

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- PostgreSQL 16 (local, or via Docker)

## Setup

### 1. Database

```bash
# throwaway container (or use a local PostgreSQL)
docker run -d --name aqr-pg -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=attendanceqr -p 5432:5432 postgres:16-alpine
```

### 2. Secrets (required — not committed)

`appsettings.json` ships with placeholder secrets. Provide real values for the JWT signing key
and the QR-token secret via environment variables or .NET user-secrets. Generate strong random
values, e.g.:

```bash
# JWT signing key + QR secret (any long random strings)
openssl rand -base64 64      # → Jwt__SigningKey
openssl rand -base64 48      # → QrToken__Secret
```

Then either export them:

```bash
export Jwt__SigningKey="<generated>"
export QrToken__Secret="<generated>"
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=attendanceqr;Username=postgres;Password=postgres"
```

or use user-secrets:

```bash
cd src/AttendanceQR.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<generated>"
dotnet user-secrets set "QrToken:Secret" "<generated>"
```

### 3. Migrations + run backend

```bash
dotnet ef database update --project src/AttendanceQR.Infrastructure
cd src/AttendanceQR.Api && dotnet run   # http://localhost:5103
```

In Development, `POST /api/dev/seed` creates demo locations + users (password `Passw0rd!`).

### 4. Frontend

```bash
cd frontend
cp .env.example .env      # VITE_API_URL=http://localhost:5103
npm install
npm run dev               # http://localhost:5173
```

> **Camera + GPS** (the `/scan` screen) require a secure context: they work on `localhost` and over
> HTTPS, but not over plain-HTTP LAN IPs. See `frontend/README.md` for phone-testing options.

## Screens

- `/login` · `/activate?token=…` — sign in / claim account (set password + bind device)
- `/scan` — employee camera scan → check-in/out
- `/kiosk/:locationId` — rotating QR board (no login)
- `/admin` — Today board, Reports + Excel export, Invite, Device approvals (role-scoped)

## Security notes

- Passwords: PBKDF2 (ASP.NET Core Identity hasher). Activation tokens: random + SHA256, single-use.
- QR tokens are HMAC-signed (constant-time compare) with a short TTL and a location QR version, so a
  regenerated poster invalidates the old one. They are **not** single-use — see above.
- **What these controls do not do.** GPS and the device fingerprint come from the client, and there
  is no device attestation on the web. An employee who is determined to can submit their site's
  coordinates from elsewhere. The geofence, device binding, photo and face-match raise the cost and
  leave a trail; they do not make a fake check-in impossible. Say this plainly to customers — the
  detection work (scoring and surfacing suspicious records) is tracked separately.
- Reporting/attendance scope is enforced server-side (Admin = all, Manager = managed locations,
  Employee = self).
- Real secrets are **never** committed — supply them via env/user-secrets (see above).

## Run the whole stack with Docker

```bash
cp .env.example .env      # then fill in Jwt__SigningKey / QrToken__Secret (openssl rand …)
docker compose up --build
```

- Frontend → http://localhost:8081 · Backend → http://localhost:8080 · Postgres → localhost:15432
- The backend **applies EF Core migrations automatically on startup** (retries while the DB warms up).
- Seed/dev endpoints (`/api/dev/*`) are compiled in but **404 outside Development**, so the
  Docker/production images never expose them.

## Deployment (Coolify)

The app ships as two images (both multi-stage, non-root where applicable) plus a managed Postgres:

| Component | Build pack | Base dir | Dockerfile | Port |
|-----------|-----------|----------|-----------|------|
| Backend   | Dockerfile | `/`        | `src/AttendanceQR.Api/Dockerfile` | 8080 |
| Frontend  | Dockerfile | `/frontend`| `Dockerfile`                      | 80   |

**Backend** environment variables (set as secrets in Coolify):

| Key | Example |
|-----|---------|
| `ConnectionStrings__DefaultConnection` | `Host=<pg-host>;Port=5432;Database=attendanceqr;Username=<u>;Password=<p>` |
| `Jwt__SigningKey`   | output of `openssl rand -base64 64` |
| `QrToken__Secret`   | output of `openssl rand -base64 48` |
| `Cors__AllowedOrigins` | `https://<frontend-domain>` (comma-separated for several) |
| `Bootstrap__AdminEmail`    | `admin@yourco.com` — first-run admin login (see below) |
| `Bootstrap__AdminPassword` | a 4-digit PIN (e.g. `4821`) — same format as employee login |

**Frontend** build argument (build-time — Vite inlines it):

| Key | Example |
|-----|---------|
| `VITE_API_URL` | `https://<backend-domain>` |

> Decide the two public subdomains first: the frontend's `VITE_API_URL` must point at the backend
> domain, and the backend's `Cors__AllowedOrigins` must list the frontend domain. TLS is terminated
> by Coolify's proxy — the containers speak plain HTTP internally (no `UseHttpsRedirection`).

### First run — bootstrap the admin

Production has no seed data (the `/api/dev/*` seed endpoints are Development-only). To get in the
first time, set `Bootstrap__AdminEmail` + `Bootstrap__AdminPassword` on the backend: on startup, if
no Admin exists, the app creates that admin plus a starter location. It is idempotent — once an
admin exists it is skipped, so the vars are safe to leave set.

Then log in as that admin and use **Lokasiyalar** (admin panel) to create/edit real locations, and
**İşçilər** to invite employees (each gets an activation link to set a 4-digit PIN + bind a device).
