# AttendanceQR / QRLog

QR-based staff attendance, sold as a SaaS. Three companies use it in production
(Bakı Abadlıq Xidməti, CleanFix, EastCaf) — around 114 employees. **Everything user-facing is in
Azerbaijani.**

An employee scans a printed QR poster at their site; the app checks where they are, which device
they are on, and takes a selfie, then records the check-in. Admins get attendance boards, reports,
payroll and announcements.

## Stack

- **Backend** — .NET 10, ASP.NET Core, EF Core + Npgsql. Migrations apply themselves at startup.
- **Frontend** — React 19 + Vite + TypeScript. Employee screens use Tailwind; the admin panel uses
  the semantic classes in `frontend/src/theme.css`.
- **Infra** — Docker Compose on one VM, Caddy for TLS, Cloudflare R2 for photos, AWS Rekognition for
  face matching, Web Push for notifications.

## Layout

    src/AttendanceQR.Domain          entities, enums
    src/AttendanceQR.Application     interfaces, options
    src/AttendanceQR.Infrastructure  EF Core, services, migrations
    src/AttendanceQR.Api             controllers, background jobs
    frontend/src/pages               employee screens + pages/admin
    ops/                             backups, watchdog, staging deploy

Plain layered architecture — controllers, contracts, services. No MediatR, no CQRS, despite what the
folder names might suggest.

## Rules that are not obvious

**Multi-tenancy is fail-closed.** Every tenant-scoped entity has a global query filter. An API
request that cannot be attributed to a company is rejected, not defaulted — it used to fall through
to one company's data. `IgnoreQueryFilters()` is allowed in exactly two places: the super-admin
controller and the group console, both gated on an employee-id allowlist rather than a role.

**A check-in is never blocked by anything optional.** Not by a missing photo, not by face detection,
not by notifications. Someone whose camera fails must still be able to record that they came to
work — their pay depends on that record. Optional checks flag; they do not stop.

**`EmployeeUpdateRequest` null-defaults every field.** A partial update blanks what it omits. Adding
a field to `Employee` means adding it to *every* `updateEmployee(` caller in the frontend.

**A rotation replaces the weekly calendar, it does not layer on it.** `Location.WorkDaysMask` is a
7-day bitmask, so it cannot express "every other day" (a 2-day cycle drifts across the week). An
employee with `WorkCycleDays` set ignores the mask entirely — see `AttendanceCalculator`
`.IsScheduledWorkingDay`, the one place this is decided, and `WorkCycle.Apply`, the one place it is
written. Holidays (`NonWorkingDay`) still apply on top.

**A value in `.env` alone reaches nothing.** Compose only passes variables named in the service's
`environment:` block. Add it in both places.

**Never reintroduce:** a lateness ("Gecikmə") display — removed on purpose, every employee has their
own hours; the Sora font — it has no Azerbaijani `ə`; a single-use QR nonce — it breaks the printed
poster; a free-text position input — job titles come from the `JobPositions` catalogue.

## Working on it

    docker compose up --build          # local: frontend 8081, backend 8080, postgres 15432
    dotnet test                        # 134 backend tests
    cd frontend && npm run build       # typecheck + build

Push to `staging` and it deploys itself to https://test.qrlog.az within a couple of minutes.
Production is deployed by hand from `main`, and **never between 07:30–09:30 or 17:00–19:00** — those
are the scan peaks, and a mistake there means nobody can clock in.

See `ops/README.md` for backups, monitoring and the staging environment.
