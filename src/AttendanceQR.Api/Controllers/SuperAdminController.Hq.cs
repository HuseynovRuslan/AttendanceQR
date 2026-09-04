using AttendanceQR.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The group console: every company at once, on one screen, live.
///
/// The tenant panels answer "how is my company doing today". This answers a different question —
/// "how much is running on this system right now" — and it is the only view where the three companies
/// appear side by side. Every query here deliberately ignores the tenant filter, which is safe for
/// exactly one reason: the endpoint is gated on the super-admin allowlist, not on a role that a
/// company's own Admin could grant themselves.
/// </summary>
public partial class SuperAdminController
{
    /// <summary>Days of history behind the trend line — two working weeks reads as a trend without
    /// crushing the chart on a phone.</summary>
    private const int TrendDays = 14;

    /// <summary>
    /// Scans in the live feed.
    ///
    /// Was 14 — "enough that something moves while you watch". The owner asked for everyone: on a
    /// board the group head reads, the feed is not decoration, it is the day's register, and a
    /// director looking for one person should not have to guess whether they fell off the bottom.
    /// The panel scrolls now, so the length costs layout nothing.
    ///
    /// Still capped, because this is one JSON payload on a 20-second refresh: a day where every one
    /// of ~650 people checks in and out is ~1,300 rows, and the cap sits above that with room to
    /// grow rather than being a number the board can silently hit.
    /// </summary>
    private const int FeedSize = 2000;

    // GET /api/super/hq/records/{recordId}/photo-url — the selfie behind one feed row.
    //
    // The tenant panels have had this since the photo audit shipped; the group board had the rows
    // and not the faces, so the one screen that reads all five companies was the one place a
    // suspicious check-in could not be looked at.
    //
    // Cross-tenant by design and gated exactly like the rest of this board. Two things are NOT
    // relaxed for it: the URL is presigned and short-lived (the object itself stays private), and a
    // record id is the only key — there is no listing endpoint, so this cannot be walked.
    [HttpGet("hq/records/{recordId:guid}/photo-url")]
    public async Task<IActionResult> HqPhotoUrl(Guid recordId)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });
        if (_photoStorage is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "StorageNotConfigured" });

        var ct = HttpContext.RequestAborted;

        var record = await _db.AttendanceRecords.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        var employee = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == record.EmployeeId)
            .Select(e => new { e.FullName, e.ReferencePhotoKey })
            .FirstOrDefaultAsync(ct);

        var checkInUrl = record.CheckInPhotoKey is null
            ? null
            : await _photoStorage.GetPresignedUrlAsync(record.CheckInPhotoKey, ct);
        var referenceUrl = employee?.ReferencePhotoKey is null
            ? null
            : await _photoStorage.GetPresignedUrlAsync(employee.ReferencePhotoKey, ct);

        // Baku time in the title. The reader is standing in Baku and the record is stored in UTC —
        // the same four-hour trap that once had an admin "correcting" times to their own clock.
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_appOptions.TimeZone);
        var whenLocal = record.CheckInAtUtc is DateTime at
            ? TimeZoneInfo.ConvertTimeFromUtc(at, timeZone).ToString("dd.MM.yyyy HH:mm")
            : record.AttendanceDate.ToString("dd.MM.yyyy");

        return Ok(new
        {
            title = $"{employee?.FullName ?? "İşçi"} — {whenLocal}",
            referenceUrl,
            checkInUrl,
            checkInTakenAtUtc = record.CheckInPhotoTakenAtUtc ?? record.CheckInAtUtc,
            faceMatchStatus = record.FaceMatchStatus.ToString(),
            faceMatchScore = record.FaceMatchScore,
        });
    }

    // GET /api/super/hq/person/{employeeId} — one row of the live feed, opened.
    //
    // Written because of what the feed's place column contains for a «səyyar» visit. That column is
    // TargetLabel: free text the worker types. Measured on production it is already four spellings of
    // one thing — «Obyekdeyem», «Obyektdəyəm», «Obyekt deyem», «Obyektdeyem» — which is not even a
    // place, it is the sentence "I am at the site"; and «Nərimanov ofisi» / «Nerimanov»,
    // «Konqresin parkındakı kafe» / «Kongres parkindaki kafe» are the same site twice. The same
    // disease the job-title catalogue was created to cure.
    //
    // Worse: of ~70 visits only 4 carry a target coordinate, so for nearly all of them there is
    // nothing to measure the arrival against. The one thing that IS recorded and cannot be typed is
    // the check-in's own GPS. So the row opens to WHERE THEY ACTUALLY WERE, not to a better rendering
    // of what they wrote — a fact instead of a claim.
    //
    // Cross-tenant like everything else on this board, and gated the same way.
    [HttpGet("hq/person/{employeeId:guid}")]
    public async Task<IActionResult> PersonDay(Guid employeeId)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_appOptions.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));

        var employee = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.TenantId, e.FullName, e.Position, e.LocationId, e.PhoneNumber })
            .FirstOrDefaultAsync(ct);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == employee.TenantId)
            .Select(t => new { t.Name, t.DisplayName })
            .FirstOrDefaultAsync(ct);
        var branch = await _db.Locations.IgnoreQueryFilters()
            .Where(l => l.Id == employee.LocationId)
            .Select(l => new { l.Name, l.Latitude, l.Longitude, l.RadiusMeters })
            .FirstOrDefaultAsync(ct);

        // Yesterday too: a night shift's check-in belongs to yesterday's record and its check-out to
        // this morning, and the feed row the reader clicked may be either half of it.
        var from = today.AddDays(-1);

        var records = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate >= from)
            .OrderByDescending(r => r.AttendanceDate)
            .Select(r => new
            {
                date = r.AttendanceDate,
                checkInAtUtc = r.CheckInAtUtc,
                checkOutAtUtc = r.CheckOutAtUtc,
                lat = r.CheckInLatitude,
                lng = r.CheckInLongitude,
                wasOffline = r.WasOffline,
                manual = r.ManualByEmployeeId != null,
            })
            .ToListAsync(ct);

        var visits = await _db.FieldVisits.IgnoreQueryFilters()
            .Where(v => v.EmployeeId == employeeId && v.VisitDate >= from && v.CheckInAtUtc != null)
            .OrderByDescending(v => v.CheckInAtUtc)
            .Select(v => new
            {
                id = v.Id,
                date = v.VisitDate,
                // What they typed. Kept so the reader can see the claim beside the fact, not because
                // it is trusted.
                label = v.TargetLabel,
                checkInAtUtc = v.CheckInAtUtc,
                checkOutAtUtc = v.CheckOutAtUtc,
                lat = v.CheckInLatitude,
                lng = v.CheckInLongitude,
                targetLat = v.TargetLatitude,
                targetLng = v.TargetLongitude,
                distanceMeters = v.CheckInDistanceMeters,
                note = v.Note,
                hasPhoto = v.CheckInPhotoKey != null,
                selfReported = v.AssignedByEmployeeId == null,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            id = employee.Id,
            fullName = employee.FullName,
            position = employee.Position,
            phone = employee.PhoneNumber,
            company = tenant is null ? "" : string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Name : tenant.DisplayName,
            branch = branch?.Name ?? "",
            branchLat = branch?.Latitude,
            branchLng = branch?.Longitude,
            branchRadius = branch?.RadiusMeters,
            today,
            records,
            visits,
        });
    }

    // GET /api/super/hq/not-started — the people behind the board's largest unanswered number.
    //
    // 335 of 656 active staff have never once opened the app, 310 of them at a single company. The
    // board states that figure and, until now, there was nowhere at all to see WHO they are — which
    // makes it a number nobody can act on. Naming them is the first step of every question that
    // follows: no phone, no PIN handed out, or no poster on the wall at their site.
    //
    // Its own endpoint rather than part of the board payload, because the board refreshes every 20
    // seconds and this list changes about once a week: sending 335 rows on a loop to a screen that
    // shows them only when asked is a waste of every refresh.
    [HttpGet("hq/not-started")]
    public async Task<IActionResult> NotStarted()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_appOptions.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));

        // Same definition as the board's denominator, and it MUST stay the same: a person here is a
        // person excluded from the attendance percentage. Two definitions would mean the list and the
        // figure it explains could disagree.
        var everScanned = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.CheckInAtUtc != null).Select(r => r.EmployeeId).Distinct().ToListAsync(ct);
        var everField = await _db.FieldVisits.IgnoreQueryFilters()
            .Where(v => v.CheckInAtUtc != null).Select(v => v.EmployeeId).Distinct().ToListAsync(ct);
        var observed = everScanned.Concat(everField).ToHashSet();

        var tenantNames = await _db.Tenants.IgnoreQueryFilters()
            .Select(t => new { t.Id, t.Name, t.DisplayName })
            .ToDictionaryAsync(
                t => t.Id,
                t => string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName,
                ct);
        var locationNames = await _db.Locations.IgnoreQueryFilters()
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var rows = (await _db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive)
                .Select(e => new
                {
                    e.Id, e.TenantId, e.FullName, e.Position, e.LocationId,
                    e.PhoneNumber, e.MustChangePin, e.LastActiveAtUtc,
                })
                .ToListAsync(ct))
            .Where(e => !observed.Contains(e.Id))
            .Select(e => new
            {
                id = e.Id,
                fullName = e.FullName,
                company = tenantNames.GetValueOrDefault(e.TenantId, ""),
                companyId = e.TenantId,
                location = locationNames.GetValueOrDefault(e.LocationId, ""),
                position = e.Position,
                // The three fields that separate WHY, each belonging to a different person to fix.
                // ActivatedAtUtc is deliberately NOT among them: the bulk import stamps it for
                // everyone it creates, so it is true for all 335 of these people and separates
                // nothing. Measured on production before this list was cut — 309 of 309 at one
                // company were "activated", which would have made the whole panel one useless
                // bucket.
                //
                // Has a number at all. Without one nothing technical helps; the company must supply
                // it. (Two people, both at Green Garden.)
                hasPhone = !string.IsNullOrWhiteSpace(e.PhoneNumber),
                // Still on the temporary PIN from the import — handed an account and never logged in
                // with it. 239 people at Bakı Abadlıq: a distribution problem, not a technical one.
                neverLoggedIn = e.MustChangePin,
                // Opened the app at least once (LastActiveAtUtc is app-open, not login). Somebody who
                // got in, looked, and STILL never produced a scan is the interesting case: the
                // account works, so what failed is the poster, the geofence or the camera. 70 people
                // — and they are the ones worth phoning.
                openedApp = e.LastActiveAtUtc != null,
                daysSince = e.LastActiveAtUtc is DateTime a
                    ? today.DayNumber - DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(a, timeZone)).DayNumber
                    : (int?)null,
            })
            .OrderBy(x => x.company).ThenBy(x => x.location).ThenBy(x => x.fullName)
            .ToList();

        return Ok(new { total = rows.Count, rows });
    }

    // GET /api/super/hq — everything the group board shows, in one round trip. One call rather than
    // six because the board refreshes on a timer: six requests on a loop is six chances for the
    // screen to show half-old numbers mid-demo.
    [HttpGet("hq")]
    public async Task<IActionResult> GroupOverview()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_appOptions.TimeZone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var today = DateOnly.FromDateTime(nowLocal);
        var trendFrom = today.AddDays(-(TrendDays - 1));

        var tenants = await _db.Tenants
            .Where(t => t.IsActive)
            .OrderBy(t => t.CreatedAtUtc)
            .Select(t => new { t.Id, t.Slug, t.DisplayName, t.Name })
            .ToListAsync(ct);

        // IgnoreQueryFilters throughout: this screen exists to look across companies.
        var employees = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive)
            .Select(e => new { e.Id, e.TenantId, e.FullName, e.MonthlySalary, e.LocationId })
            .ToListAsync(ct);

        var allLocations = await _db.Locations.IgnoreQueryFilters()
            .Select(l => new { l.Id, l.TenantId, l.Name, l.Latitude, l.Longitude, l.RadiusMeters })
            .ToListAsync(ct);
        var locationCount = allLocations
            .GroupBy(l => l.TenantId)
            .ToDictionary(g => g.Key, g => g.Count());

        var todayRecords = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.AttendanceDate == today && r.CheckInAtUtc != null)
            .Select(r => new { r.TenantId, r.EmployeeId, r.LocationId, r.CheckOutAtUtc })
            .ToListAsync(ct);

        var trendRows = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.AttendanceDate >= trendFrom && r.AttendanceDate <= today && r.CheckInAtUtc != null)
            .Select(r => new { r.TenantId, r.AttendanceDate, r.EmployeeId })
            .ToListAsync(ct);

        // Everyone who has EVER recorded attendance, by any route. This is the group board's
        // denominator, and the reason it is not simply "active employees":
        //
        // 335 of the 656 active staff — more than half — have never scanned once. They were created
        // by the bulk import, in one case 514 people in an afternoon, and they are waiting on a phone,
        // a PIN or a poster at their site. Counting them as "did not come to work" put the group
        // attendance at 33% on a screen the owner of five companies reads, which is not what is
        // happening in those companies: it is the picture of a rollout that is half finished.
        //
        // AttendanceCalculator.IsStillOnboarding cannot answer this — it forgives the first fourteen
        // days after activation and then judges normally, which is right for payroll (nobody escapes
        // absence by never setting their phone up) and wrong here. The board is not paying anybody.
        // It is stating what share of the people the system can actually SEE turned up today, and a
        // person who has never once appeared in it is not evidence of anything.
        //
        // The moment somebody's first scan lands they join the denominator permanently — so this
        // cannot become a way to keep a real absence off the board.
        var everScanned = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.CheckInAtUtc != null)
            .Select(r => r.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        var everField = await _db.FieldVisits.IgnoreQueryFilters()
            .Where(v => v.CheckInAtUtc != null)
            .Select(v => v.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        var observed = everScanned.Concat(everField).ToHashSet();

        var byTenant = employees.GroupBy(e => e.TenantId).ToDictionary(g => g.Key, g => g.ToList());
        var presentByTenant = todayRecords.GroupBy(r => r.TenantId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());
        var onDutyByTenant = todayRecords.Where(r => r.CheckOutAtUtc == null)
            .GroupBy(r => r.TenantId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());

        var companies = tenants.Select(t =>
        {
            var staff = byTenant.GetValueOrDefault(t.Id, new());
            var present = presentByTenant.GetValueOrDefault(t.Id, 0);
            var started = staff.Count(e => observed.Contains(e.Id));
            return new
            {
                id = t.Id,
                slug = t.Slug,
                name = string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName,
                employees = staff.Count,
                present,
                onDuty = onDutyByTenant.GetValueOrDefault(t.Id, 0),
                locations = locationCount.GetValueOrDefault(t.Id, 0),
                // Imported but never once used the app. Shown rather than hidden: it is the number
                // that says how far this company's rollout actually got, and it is the only honest
                // way to leave them out of the percentage below.
                notStarted = staff.Count - started,
                // Share of the people the system can SEE who turned up today. Deliberately plain: a
                // director reads "94% came in" without being told what a scheduled work day is.
                attendancePct = started == 0 ? 0 : (int)Math.Round(present * 100.0 / started),
                payroll = staff.Sum(e => e.MonthlySalary ?? 0m),
            };
        }).ToList();

        // The trend is the group total per day — three overlapping lines on a projector is noise.
        var trend = Enumerable.Range(0, TrendDays)
            .Select(offset =>
            {
                var date = trendFrom.AddDays(offset);
                return new
                {
                    date,
                    present = trendRows.Where(r => r.AttendanceDate == date)
                        .Select(r => r.EmployeeId).Distinct().Count(),
                };
            })
            .ToList();

        // Where the work is happening right now. The point of putting this on a map is that a
        // director recognises their own sites instantly — a table of the same numbers does not carry
        // the same thing at all.
        var onDutyByLocation = todayRecords.Where(r => r.CheckOutAtUtc == null)
            .GroupBy(r => r.LocationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());
        var presentByLocation = todayRecords
            .GroupBy(r => r.LocationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());
        var tenantOrder = tenants.Select(t => t.Id).ToList();

        var sites = allLocations
            // A site with no coordinates cannot be drawn, and a marker at (0,0) lands in the Atlantic.
            .Where(l => l.Latitude != 0 || l.Longitude != 0)
            .Select(l => new
            {
                id = l.Id,
                name = l.Name,
                companyIndex = tenantOrder.IndexOf(l.TenantId),
                lat = l.Latitude,
                lng = l.Longitude,
                // The geofence itself, drawn on the map: it turns a scatter of dots into a picture of
                // the ground the company actually covers, and it is the only place the GPS rule is
                // ever visible rather than merely claimed.
                radiusMeters = l.RadiusMeters,
                onDuty = onDutyByLocation.GetValueOrDefault(l.Id, 0),
                present = presentByLocation.GetValueOrDefault(l.Id, 0),
                staff = employees.Count(e => e.LocationId == l.Id),
            })
            .OrderByDescending(s => s.onDuty)
            .ToList();

        var names = employees.ToDictionary(e => e.Id, e => e.FullName);
        var tenantNames = tenants.ToDictionary(
            t => t.Id, t => string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName);
        var locationNames = await _db.Locations.IgnoreQueryFilters()
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        // The feed is what makes the board look alive: rows arrive while someone is watching it.
        // Check-outs count as events too — a board that only ever shows arrivals goes still by noon.
        // The feed is TODAY'S. It is read as a running account of the day, so yesterday's rows sitting
        // among this morning's are not extra information — they are wrong information: a 22:49 «ÇIXIŞ»
        // above a 13:57 «GİRİŞ» reads as a broken record until you notice they are different days.
        //
        // But the RECORDS still have to be fetched from yesterday, and the filter is on the event's
        // own clock rather than on the record's date. A night worker checks in at 22:00 and out at
        // 06:00; that record is dated yesterday, while the check-out happened this morning and belongs
        // on this morning's board. Filtering by AttendanceDate would drop it; filtering by when the
        // event actually happened keeps it and leaves yesterday's check-in behind, which is right.
        var feedFrom = today.AddDays(-1);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), timeZone);
        var dayEndUtc = dayStartUtc.AddDays(1);
        bool IsToday(DateTime atUtc) => atUtc >= dayStartUtc && atUtc < dayEndUtc;
        var posterEvents = (await _db.AttendanceRecords.IgnoreQueryFilters()
                .Where(r => r.AttendanceDate >= feedFrom && r.CheckInAtUtc != null)
                .Select(r => new
                {
                    r.Id, r.TenantId, r.EmployeeId, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc,
                    // Carried so a row on the group board can open the selfie behind it, and so a
                    // face the system could not match is visible WITHOUT opening anything. On a
                    // board read across five companies, a flag nobody has to click for is the only
                    // one that gets noticed.
                    r.CheckInPhotoKey, r.FaceMatchStatus, r.FaceMatchScore,
                })
                .ToListAsync(ct))
            .SelectMany(r => new[]
            {
                new
                {
                    r.TenantId, r.EmployeeId, Place = locationNames.GetValueOrDefault(r.LocationId, ""),
                    At = r.CheckInAtUtc!.Value, Kind = "in",
                    RecordId = (Guid?)r.Id,
                    HasPhoto = r.CheckInPhotoKey != null,
                    Face = r.FaceMatchStatus.ToString(),
                    Score = r.FaceMatchScore,
                },
                r.CheckOutAtUtc is null
                    ? null
                    : new
                    {
                        r.TenantId, r.EmployeeId, Place = locationNames.GetValueOrDefault(r.LocationId, ""),
                        At = r.CheckOutAtUtc.Value, Kind = "out",
                        // The selfie belongs to the CHECK-IN. A check-out row must not offer it, or
                        // the board would show a morning photograph beside an evening time.
                        RecordId = (Guid?)null,
                        HasPhoto = false,
                        Face = r.FaceMatchStatus.ToString(),
                        Score = r.FaceMatchScore,
                    },
            })
            .Where(x => x is not null)
            .Select(x => x!);

        // Field visits belong in the same feed. A driver checking in at a site with no poster is
        // doing exactly what this board is showing — turning up to work — and leaving them out made
        // the whole «səyyar» route invisible on the one screen that claims to show the group live.
        // Their place is the target label they were sent to, or the free label they typed.
        var fieldEvents = (await _db.FieldVisits.IgnoreQueryFilters()
                .Where(v => v.VisitDate >= feedFrom && v.CheckInAtUtc != null)
                .Select(v => new { v.TenantId, v.EmployeeId, v.TargetLabel, v.CheckInAtUtc, v.CheckOutAtUtc })
                .ToListAsync(ct))
            .SelectMany(v => new[]
            {
                new
                {
                    v.TenantId, v.EmployeeId, Place = v.TargetLabel ?? "Ərazi",
                    At = v.CheckInAtUtc!.Value, Kind = "field-in",
                    // A field visit's selfie lives on FieldVisit, not AttendanceRecord, and the
                    // compare view is built around a record id. Out of scope here rather than faked:
                    // a camera button that opens nothing is worse than no button.
                    RecordId = (Guid?)null, HasPhoto = false, Face = "NotChecked", Score = (int?)null,
                },
                v.CheckOutAtUtc is null
                    ? null
                    : new
                    {
                        v.TenantId, v.EmployeeId, Place = v.TargetLabel ?? "Ərazi",
                        At = v.CheckOutAtUtc.Value, Kind = "field-out",
                        RecordId = (Guid?)null, HasPhoto = false, Face = "NotChecked", Score = (int?)null,
                    },
            })
            .Where(x => x is not null)
            .Select(x => x!);

        var feed = posterEvents.Concat(fieldEvents)
            .Where(x => IsToday(x.At))
            .OrderByDescending(x => x.At)
            .Take(FeedSize)
            .Select(x => new
            {
                // The id as well as the name: a feed row opens to that person's day, and two people
                // in five companies can share a name.
                employeeId = x.EmployeeId,
                fullName = names.GetValueOrDefault(x.EmployeeId, "—"),
                // The id as well as the name: the company panel filters this feed to one company, and
                // matching on a display name would put two identically-named tenants' scans into each
                // other's lists. Nothing stops two companies sharing a name.
                companyId = x.TenantId,
                company = tenantNames.GetValueOrDefault(x.TenantId, ""),
                location = x.Place,
                atUtc = x.At,
                kind = x.Kind,
                recordId = x.RecordId,
                hasPhoto = x.HasPhoto,
                faceMatchStatus = x.Face,
                faceMatchScore = x.Score,
            })
            .ToList();

        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            totals = new
            {
                companies = companies.Count,
                employees = companies.Sum(c => c.employees),
                present = companies.Sum(c => c.present),
                onDuty = companies.Sum(c => c.onDuty),
                locations = companies.Sum(c => c.locations),
                payroll = companies.Sum(c => c.payroll),
                notStarted = companies.Sum(c => c.notStarted),
                // Same denominator as each company's own figure: the group percentage has to be the
                // group's, not an average of five percentages weighted by nothing.
                attendancePct = companies.Sum(c => c.employees - c.notStarted) == 0
                    ? 0
                    : (int)Math.Round(companies.Sum(c => c.present) * 100.0
                                      / companies.Sum(c => c.employees - c.notStarted)),
                // Scans handled since the system went live — the number that says "this is in real
                // use", which is the whole point of showing the board to someone who is being sold to.
                totalScans = await _db.AttendanceRecords.IgnoreQueryFilters()
                    .CountAsync(r => r.CheckInAtUtc != null, ct),
                // Days since the first check-in ever recorded. "Running for N days without a break"
                // is the one reliability claim a director can weigh without being an engineer.
                daysLive = await _db.AttendanceRecords.IgnoreQueryFilters()
                        .OrderBy(r => r.AttendanceDate)
                        .Select(r => (DateOnly?)r.AttendanceDate)
                        .FirstOrDefaultAsync(ct) is { } firstDay
                    ? Math.Max(1, today.DayNumber - firstDay.DayNumber + 1)
                    : 0,
            },
            companies,
            sites,
            trend,
            feed,
        });
    }
}
