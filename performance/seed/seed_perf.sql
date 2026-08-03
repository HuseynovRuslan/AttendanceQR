-- Seed representative attendance history for the perf baseline. LOCAL/DEV DB ONLY — never production.
-- Generates 30 / 365 / 1000 records for three test employees so we can see if latency grows with size.
--
-- Prereq: create three activated test employees (admin panel) named perf-30 / perf-365 / perf-1000,
-- with a known PIN, at one location, then paste their ids + the location/tenant ids below.
--
-- Idempotent per (EmployeeId, AttendanceDate) — re-running does not duplicate.

\set ten '00000000-0000-0000-0000-00000000ba01'
\set loc 'd5b05dbb-6c39-45dc-9ce8-ca5ab6757921'
\set e30   'SET-ME-perf30-employee-id'
\set e365  'SET-ME-perf365-employee-id'
\set e1000 'SET-ME-perf1000-employee-id'

-- Each row: a distinct past day, check-in 09:00 (05:00 UTC), check-out 18:00 (14:00 UTC), OnTime.
INSERT INTO "AttendanceRecords"
  ("Id","EmployeeId","LocationId","AttendanceDate","CheckInAtUtc","CheckOutAtUtc","Status","FaceMatchStatus","TenantId","WasOffline")
SELECT gen_random_uuid(), :'e30'::uuid, :'loc'::uuid, (CURRENT_DATE - g),
       (CURRENT_DATE - g) + time '05:00', (CURRENT_DATE - g) + time '14:00', 0, 0, :'ten'::uuid, false
FROM generate_series(1, 30) g
ON CONFLICT DO NOTHING;

INSERT INTO "AttendanceRecords"
  ("Id","EmployeeId","LocationId","AttendanceDate","CheckInAtUtc","CheckOutAtUtc","Status","FaceMatchStatus","TenantId","WasOffline")
SELECT gen_random_uuid(), :'e365'::uuid, :'loc'::uuid, (CURRENT_DATE - g),
       (CURRENT_DATE - g) + time '05:00', (CURRENT_DATE - g) + time '14:00', 0, 0, :'ten'::uuid, false
FROM generate_series(1, 365) g
ON CONFLICT DO NOTHING;

INSERT INTO "AttendanceRecords"
  ("Id","EmployeeId","LocationId","AttendanceDate","CheckInAtUtc","CheckOutAtUtc","Status","FaceMatchStatus","TenantId","WasOffline")
SELECT gen_random_uuid(), :'e1000'::uuid, :'loc'::uuid, (CURRENT_DATE - g),
       (CURRENT_DATE - g) + time '05:00', (CURRENT_DATE - g) + time '14:00', 0, 0, :'ten'::uuid, false
FROM generate_series(1, 1000) g
ON CONFLICT DO NOTHING;

\echo 'seeded (row counts):'
SELECT "EmployeeId", count(*) FROM "AttendanceRecords"
WHERE "EmployeeId" IN (:'e30'::uuid, :'e365'::uuid, :'e1000'::uuid) GROUP BY "EmployeeId";
