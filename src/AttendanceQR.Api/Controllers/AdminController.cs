using System.Security.Cryptography;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using ClosedXML.Excel;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/employees")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly InvitationOptions _invitationOptions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginLockoutStore _lockout;
    private readonly string[] _hiddenEmails;
    // Platform operators, by employee id. They usually live inside a tenant as ordinary-looking rows,
    // so a tenant admin can see them — but must never be able to take their credentials.
    private readonly Guid[] _operatorIds;
    private readonly IPhotoStorageService _photoStorage;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext db,
        IOptions<InvitationOptions> invitationOptions,
        IPasswordHasher passwordHasher,
        ILoginLockoutStore lockout,
        AppOptions appOptions,
        IPhotoStorageService photoStorage,
        ILogger<AdminController> logger)
    {
        _db = db;
        _invitationOptions = invitationOptions.Value;
        _passwordHasher = passwordHasher;
        _lockout = lockout;
        _hiddenEmails = appOptions.HiddenEmailList();
        _operatorIds = appOptions.SuperAdminIdList();
        _photoStorage = photoStorage;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        // Admins/managers ARE shown here now (so they can be managed), EXCEPT the system/root admin
        // accounts listed in AppOptions.HiddenEmails (e.g. admin@bms.az) — they're operators, not staff.
        var employees = await _db.Employees
            .Include(e => e.DeviceBindings)
            .Where(e => e.Email == null || !_hiddenEmails.Contains(e.Email.ToLower()))
            .OrderBy(e => e.FullName)
            .ToListAsync(HttpContext.RequestAborted);

        var locationNames = await _db.Locations
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

        // One dictionary, not one query per employee: the old per-row Schedules lookup below was a
        // SYNC query inside a deferred Select — at 2000 employees the list endpoint ran 2000 blocking
        // round-trips during JSON serialization.
        var scheduleNames = await _db.Schedules
            .ToDictionaryAsync(s => s.Id, s => s.Name, HttpContext.RequestAborted);

        // Which branches each manager oversees — the form needs it to show what is already ticked,
        // and the list needs it because a manager with none sees an empty panel and no explanation.
        var managedByEmployee = (await _db.ManagedLocations.ToListAsync(HttpContext.RequestAborted))
            .GroupBy(m => m.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.LocationId).ToList());

        // Who can actually be reached by a push (announcement or reminder) — an employee with no
        // subscription silently receives nothing, which the admin otherwise has no way to see.
        var pushEmployeeIds = (await _db.PushSubscriptions
                .Select(p => p.EmployeeId)
                .Distinct()
                .ToListAsync(HttpContext.RequestAborted))
            .ToHashSet();

        var result = employees.Select(e =>
        {
            // An employee may hold several contexts (Safari, the installed PWA). The list still shows
            // one label — the most recently used — plus how many are bound in total.
            var active = e.DeviceBindings.Where(d => d.IsActive).OrderByDescending(d => d.LastSeenAtUtc).ToList();
            var newest = active.FirstOrDefault();
            return new
            {
                id = e.Id,
                fullName = e.FullName,
                firstName = e.FirstName,
                lastName = e.LastName,
                fatherName = e.FatherName,
                position = e.Position,
                birthYear = e.BirthYear,
                birthDate = e.BirthDate,
                workStart = e.WorkStart?.ToString("HH:mm"),
                workEnd = e.WorkEnd?.ToString("HH:mm"),
                scheduleId = e.ScheduleId,
                scheduleName = e.ScheduleId is Guid sid ? scheduleNames.GetValueOrDefault(sid) : null,
                workCycleDays = e.WorkCycleDays,
                workCycleOnDays = e.WorkCycleOnDays,
                workCycleAnchor = e.WorkCycleAnchor,
                monthlySalary = e.MonthlySalary,
                photoExempt = e.PhotoExempt,
                canFieldCheckIn = e.CanFieldCheckIn,
                // Who has accepted the data-processing notice — the answer to "did this employee
                // agree, and when", which is the whole point of recording it.
                consentAcceptedAtUtc = e.ConsentAcceptedAtUtc,
                email = e.Email,
                role = e.Role.ToString(),
                phoneNumber = e.PhoneNumber,
                locationId = e.LocationId,
                locationName = locationNames.GetValueOrDefault(e.LocationId),
                // Only meaningful for a Manager. Empty on one is why their panel is blank.
                managedLocationIds = e.Role == EmployeeRole.Manager
                    ? managedByEmployee.GetValueOrDefault(e.Id, [])
                    : [],
                managedLocationNames = e.Role == EmployeeRole.Manager
                    ? managedByEmployee.GetValueOrDefault(e.Id, []).Select(id => locationNames.GetValueOrDefault(id, "")).ToList()
                    : [],
                isActive = e.IsActive,
                activated = e.ActivatedAtUtc != null,
                lastActiveAtUtc = e.LastActiveAtUtc,
                // Whether this employee will actually receive announcements/reminders on their phone.
                pushEnabled = pushEmployeeIds.Contains(e.Id),
                hasDevice = newest != null,
                deviceLabel = newest?.DeviceLabel,
                boundAtUtc = newest?.BoundAtUtc,
                deviceCount = active.Count,
                createdAtUtc = e.CreatedAtUtc
            };
        }).ToList(); // materialized — a deferred enumerable would run its lambdas during serialization
        return Ok(result);
    }

    // Photo audit: clear ONE employee's reference selfie so their next check-in re-seeds it with the
    // correct face. Needed because the reference is auto-seeded from the first check-in photo — if
    // that first scan was an admin's (their face), the reference is wrong. Nulling the key is enough:
    // the next check-in overwrites the object at reference/{id}.
    [HttpPost("{id:guid}/reset-reference-photo")]
    public async Task<IActionResult> ResetReferencePhoto(Guid id)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, HttpContext.RequestAborted);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        employee.ReferencePhotoKey = null;
        employee.ReferencePhotoTakenAtUtc = null;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { id = employee.Id });
    }

    // Bulk version — clears every employee's reference selfie in one shot (e.g. all references were
    // seeded from the admin's face during setup). Each re-seeds on that employee's next check-in.
    [HttpPost("reset-all-reference-photos")]
    public async Task<IActionResult> ResetAllReferencePhotos()
    {
        var employees = await _db.Employees
            .Where(e => e.ReferencePhotoKey != null)
            .ToListAsync(HttpContext.RequestAborted);
        foreach (var e in employees)
        {
            e.ReferencePhotoKey = null;
            e.ReferencePhotoTakenAtUtc = null;
        }
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { reset = employees.Count });
    }

    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteRequest request)
    {
        // A borrowed hour must not leave a permanent key behind. Every creation route hands the CALLER
        // the new account's credential (an activation token here, a temporary PIN in bulk-import), so an
        // admin created from an impersonation session is one the OPERATOR can sign in as long after the
        // session has expired — and nothing inside the tenant would show where it came from. The
        // customer's admins are the customer's to appoint; staff rows, which are the actual setup work,
        // are unaffected. Mirrored in bulk-invite/bulk-import (ResolveRowScope) and in Update below.
        if (User.IsImpersonating() && request.Role == EmployeeRole.Admin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" });

        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var (takenEmails, takenPhones) = await LoadTakenIdentifiersAsync();
        var (employee, token, error) = BuildInvite(
            request.FullName, request.Email, request.PhoneNumber, request.FatherName, request.Position,
            request.BirthYear, request.BirthDate, request.LocationId, request.Role, takenEmails, takenPhones);

        if (error is not null)
            return error is "EmailAlreadyExists" or "PhoneAlreadyExists"
                ? Conflict(new { error })
                : BadRequest(new { error });

        // Structured name: compose FullName from Ad + Soyad when the form sent them.
        (employee!.FullName, employee.FirstName, employee.LastName) =
            EmployeeName.Resolve(request.FirstName, request.LastName, request.FullName);
        employee.MonthlySalary = request.MonthlySalary;
        employee.CanFieldCheckIn = request.CanFieldCheckIn;
        employee.WorkStart = ParseTimeOrNull(request.WorkStart);
        employee.WorkEnd = ParseTimeOrNull(request.WorkEnd);
        if (WorkCycle.Apply(employee, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });
        if (await ApplyScheduleAsync(employee, request.ScheduleId) is { } scheduleError)
            return BadRequest(new { error = scheduleError });
        _db.Employees.Add(employee!);
        await RegisterPositionsAsync();
        await _db.SaveChangesAsync();

        // No email/SMS channel yet — return the PLAINTEXT token so it can be shared by hand.
        // (Base64Url is URL-safe, so it needs no additional encoding in the link.)
        return Ok(new
        {
            employeeId = employee!.Id,
            activationToken = token,
            activationUrl = $"/activate?token={token}"
        });
    }

    /// <summary>
    /// Resolves one row's branch and role: the row's own if it names them, otherwise the batch's.
    /// A spreadsheet carries a branch NAME, so it is matched against this tenant's branches — case
    /// and surrounding space are the typist's business, not theirs to get exactly right. A name that
    /// matches nothing fails that row alone rather than silently landing the person somewhere else.
    /// </summary>
    private static (Guid LocationId, EmployeeRole Role, string? Error) ResolveRowScope(
        BulkInviteRow row, Guid batchLocationId, EmployeeRole batchRole, Dictionary<string, Guid> locationsByName,
        bool impersonating)
    {
        var locationId = batchLocationId;
        if (!string.IsNullOrWhiteSpace(row.LocationName))
        {
            if (!locationsByName.TryGetValue(row.LocationName.Trim(), out var found))
                return (default, default, "LocationNotFound");
            locationId = found;
        }

        var role = batchRole;
        if (!string.IsNullOrWhiteSpace(row.RoleName))
        {
            var parsed = ParseRoleName(row.RoleName);
            if (parsed is null)
                return (default, default, "RoleNotRecognised");
            role = parsed.Value;
        }

        // Same rule as Invite: an impersonation session may fill the company with staff, but not appoint
        // its admins — the batch hands the caller each new account's temporary PIN. Failed per row, so
        // the rest of the spreadsheet still imports.
        if (impersonating && role == EmployeeRole.Admin)
            return (default, default, "NotDuringImpersonation");

        return (locationId, role, null);
    }

    /// <summary>Accepts what an admin actually types in a spreadsheet — the Azerbaijani labels the UI
    /// shows them, or the English enum names. Null when it is neither.</summary>
    private static EmployeeRole? ParseRoleName(string value) => value.Trim().ToLowerInvariant() switch
    {
        "işçi" or "isci" or "employee" => EmployeeRole.Employee,
        "menecer" or "manager" => EmployeeRole.Manager,
        "admin" => EmployeeRole.Admin,
        _ => null,
    };

    private async Task<Dictionary<string, Guid>> LocationsByNameAsync(CancellationToken ct) =>
        (await _db.Locations.Select(l => new { l.Id, l.Name }).ToListAsync(ct))
        .GroupBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

    // POST /api/admin/employees/bulk-invite — add many employees at once (one shared location + role).
    // Each row is validated on its own: a duplicate phone or missing name is reported back in `failed`
    // without blocking the others. All the good rows are saved in a single transaction.
    [HttpPost("bulk-invite")]
    public async Task<IActionResult> BulkInvite([FromBody] BulkInviteRequest request)
    {
        if (request.Rows is null || request.Rows.Count == 0)
            return BadRequest(new { error = "NoRows" });
        if (request.Rows.Count > 200)
            return BadRequest(new { error = "TooManyRows" });
        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var (takenEmails, takenPhones) = await LoadTakenIdentifiersAsync();
        var locationsByName = await LocationsByNameAsync(HttpContext.RequestAborted);
        var created = new List<object>();
        var failed = new List<object>();

        foreach (var row in request.Rows)
        {
            var (rowLocationId, rowRole, scopeError) = ResolveRowScope(
                row, request.LocationId, request.Role, locationsByName, User.IsImpersonating());
            if (scopeError is not null)
            {
                failed.Add(new { fullName = row.FullName, error = scopeError });
                continue;
            }

            var (employee, token, error) = BuildInvite(
                row.FullName, row.Email, row.PhoneNumber, row.FatherName, row.Position, row.BirthYear,
                row.BirthDate, rowLocationId, rowRole, takenEmails, takenPhones);

            if (error is not null)
            {
                failed.Add(new { fullName = row.FullName, error });
                continue;
            }

            _db.Employees.Add(employee!);
            created.Add(new
            {
                employeeId = employee!.Id,
                fullName = employee.FullName,
                phoneNumber = employee.PhoneNumber,
                activationToken = token,
                activationUrl = $"/activate?token={token}"
            });
        }

        if (created.Count > 0)
        {
            await RegisterPositionsAsync();
            await _db.SaveChangesAsync();
        }

        return Ok(new { createdCount = created.Count, failedCount = failed.Count, created, failed });
    }

    // POST /api/admin/employees/bulk-import — add many employees at once, each ACTIVATED with a random
    // temporary PIN the admin hands out (no activation link). The employee signs in with phone + temp
    // PIN and is forced to set their own PIN on first login (MustChangePin). The device binds later, at
    // the first scan inside the geofence. Same per-row validation as bulk-invite.
    [HttpPost("bulk-import")]
    public async Task<IActionResult> BulkImport([FromBody] BulkInviteRequest request)
    {
        if (request.Rows is null || request.Rows.Count == 0)
            return BadRequest(new { error = "NoRows" });
        if (request.Rows.Count > 200)
            return BadRequest(new { error = "TooManyRows" });
        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var (takenEmails, takenPhones) = await LoadTakenIdentifiersAsync();
        var locationsByName = await LocationsByNameAsync(HttpContext.RequestAborted);
        var created = new List<object>();
        var failed = new List<object>();

        foreach (var row in request.Rows)
        {
            var (rowLocationId, rowRole, scopeError) = ResolveRowScope(
                row, request.LocationId, request.Role, locationsByName, User.IsImpersonating());
            if (scopeError is not null)
            {
                failed.Add(new { fullName = row.FullName, error = scopeError });
                continue;
            }

            var (employee, tempPin, error) = BuildActivatedWithTempPin(
                row.FullName, row.Email, row.PhoneNumber, row.FatherName, row.Position, row.BirthYear,
                row.BirthDate, rowLocationId, rowRole, takenEmails, takenPhones);

            if (error is not null)
            {
                failed.Add(new { fullName = row.FullName, error });
                continue;
            }

            _db.Employees.Add(employee!);
            created.Add(new
            {
                employeeId = employee!.Id,
                fullName = employee.FullName,
                phoneNumber = employee.PhoneNumber,
                tempPin
            });
        }

        if (created.Count > 0)
        {
            await RegisterPositionsAsync();
            await _db.SaveChangesAsync();
        }

        return Ok(new { createdCount = created.Count, failedCount = failed.Count, created, failed });
    }

    /// <summary>The header text this importer understands, per field. Several spellings each, because
    /// the file comes back from a person who may have retyped the header or kept an older template.</summary>
    private static readonly (string Field, string[] Headers)[] XlsxColumns =
    [
        ("fullName", ["ad soyad ata adı", "ad soyad ata adi", "soyad ad ata adı", "s.a.a.", "s.a.a", "ad soyad", "ad, soyad", "adı soyadı", "ad", "full name", "fullname"]),
        ("phoneNumber", ["telefon", "telefon nömrəsi", "nömrə", "phone"]),
        ("position", ["vəzifə", "vezife", "position"]),
        ("fatherName", ["ata adı", "ata adi", "atasının adı", "father"]),
        ("birthYear", ["təvəllüd ili", "təvəllüd", "tevellud", "doğum ili", "birth year", "birthyear"]),
        ("birthDate", ["doğum tarixi", "dogum tarixi", "təvəllüd tarixi", "tevellud tarixi", "birth date", "birthdate"]),
        ("email", ["email", "e-mail", "poçt", "e-poçt"]),
        ("roleName", ["rol", "role"]),
        ("locationName", ["filial", "ərazi", "lokasiya", "location", "branch"]),
    ];

    /// <summary>
    /// "Ruslan Hüseynov Rasim oğlu" → ("Ruslan Hüseynov", "Rasim oğlu"). Every customer's own staff
    /// list writes the patronymic straight after the name, so the name column accepts it and the
    /// split happens here — but ONLY on an explicit «oğlu»/«qızı» suffix. A bare third word may be a
    /// second surname; guessing it into FatherName would corrupt names silently.
    /// </summary>
    private static (string FullName, string? FatherName) SplitPatronymic(string fullName)
    {
        var tokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length >= 3 && tokens[^1].ToLowerInvariant() is "oğlu" or "oglu" or "qızı" or "qizi")
            return (string.Join(' ', tokens[..^2]), $"{tokens[^2]} {tokens[^1]}");
        return (fullName, null);
    }

    // POST /api/admin/employees/parse-xlsx — read an uploaded .xlsx and return its rows so the admin can
    // review, then import, them. Parsing only — creates nothing.
    //
    // Columns are found by their HEADER TEXT, not their position. That is what lets an older
    // three-column file (Ad Soyad · Telefon · Vəzifə) still import correctly after the template grew
    // to eight: by position, its phone column would have landed in "Ata adı" — silently, and the
    // import would have looked like it worked. It also means the admin may reorder or delete columns.
    [HttpPost("parse-xlsx")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public IActionResult ParseXlsx(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "NoFile" });

        var rows = new List<object>();
        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.FirstOrDefault();
            if (ws is null)
                return BadRequest(new { error = "EmptyFile" });

            var used = ws.RowsUsed().ToList();
            if (used.Count == 0)
                return Ok(new { rows });

            // Map field -> column number from the header row. The header is no longer necessarily the
            // FIRST row — the template carries a title banner above it now — so scan the first few
            // rows for the one that says "Ad Soyad"; everything above it is banner, not data.
            var map = new Dictionary<string, int>();
            var headerIdx = -1;
            for (var i = 0; i < Math.Min(used.Count, 6) && headerIdx < 0; i++)
            {
                var candidate = new Dictionary<string, int>();
                foreach (var cell in used[i].CellsUsed())
                {
                    var text = cell.GetString().Trim().ToLowerInvariant();
                    var match = XlsxColumns.FirstOrDefault(c => c.Headers.Contains(text));
                    if (match.Field is not null && !candidate.ContainsKey(match.Field))
                        candidate[match.Field] = cell.Address.ColumnNumber;
                }
                if (candidate.ContainsKey("fullName")) { map = candidate; headerIdx = i; }
            }

            // No recognisable header (someone deleted it, or typed their own): fall back to the
            // original positional layout, which is what every file predating this change looks like.
            var hasHeader = headerIdx >= 0;
            if (!hasHeader)
                map = new Dictionary<string, int> { ["fullName"] = 1, ["phoneNumber"] = 2, ["position"] = 3 };

            string? Get(IXLRow row, string field)
            {
                if (!map.TryGetValue(field, out var col)) return null;
                var v = row.Cell(col).GetString().Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }

            foreach (var row in used.Skip(hasHeader ? headerIdx + 1 : 0))
            {
                var fullName = Get(row, "fullName");
                var phone = Get(row, "phoneNumber");

                // Headerless file: the old rule for spotting a title row it might still carry.
                if (!hasHeader && ReferenceEquals(row, used[0])
                    && !string.IsNullOrEmpty(phone) && !phone.Any(char.IsDigit))
                    continue;

                if (string.IsNullOrWhiteSpace(fullName))
                    continue;

                var birthYearText = Get(row, "birthYear");
                var birthYear = int.TryParse(birthYearText?.Split('.', ',')[0], out var by) ? by : (int?)null;

                // The date column may hold a REAL Excel date (the template formats it as one) or typed
                // text. The DataType check matters: TryGetValue<DateTime> on a plain number like 1990
                // would happily read it as an OADate in 1905. A bare year typed into the date column
                // degrades to birthYear instead of being dropped.
                DateOnly? birthDate = null;
                if (map.TryGetValue("birthDate", out var bdCol))
                {
                    var bdCell = row.Cell(bdCol);
                    if (bdCell.DataType == XLDataType.DateTime && bdCell.TryGetValue<DateTime>(out var dt) && dt.Year > 1900)
                        birthDate = DateOnly.FromDateTime(dt);
                    else
                    {
                        var s = bdCell.GetString().Trim();
                        if (DateTime.TryParseExact(s,
                                ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"],
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out var typed) && typed.Year > 1900)
                            birthDate = DateOnly.FromDateTime(typed);
                        else if (int.TryParse(s, out var yearOnly) && yearOnly > 1900)
                            birthYear ??= yearOnly;
                    }
                }

                // A separate Ata adı column (older files) wins; otherwise the name cell may carry the
                // patronymic and gives it up here.
                var fatherName = Get(row, "fatherName");
                if (string.IsNullOrWhiteSpace(fatherName))
                    (fullName, fatherName) = SplitPatronymic(fullName!);

                rows.Add(new
                {
                    fullName,
                    phoneNumber = phone,
                    position = Get(row, "position"),
                    fatherName,
                    birthYear = birthDate?.Year ?? birthYear,
                    birthDate = birthDate?.ToString("yyyy-MM-dd"),
                    email = Get(row, "email"),
                    roleName = Get(row, "roleName"),
                    locationName = Get(row, "locationName"),
                });

                if (rows.Count >= 200)
                    break;
            }
        }
        catch
        {
            return BadRequest(new { error = "ParseFailed" });
        }

        return Ok(new { rows });
    }

    // GET /api/admin/employees/xlsx-template — a ready-to-fill .xlsx carrying every field the
    // single-employee form collects, so importing a spreadsheet is not a lesser way to add someone.
    //
    // This file is also the one an operator SENDS TO A CUSTOMER to collect their staff list, so it
    // has to look like the product: brand banner, a «Təlimat» sheet, dropdowns for Rol/Filial, and a
    // phone column locked to text so Excel cannot eat the leading zero of "0501234567" — the single
    // most common way a customer-filled sheet used to come back broken.
    //
    // Layout contract with parse-xlsx: the banner rows carry no "Ad Soyad" cell, the header row does,
    // and the grey example row keeps column A EMPTY — that emptiness is exactly what stops it from
    // importing as an employee named "Nümunə". The «Təlimat» sheet must stay SECOND: the parser
    // reads only the first worksheet.
    [HttpGet("xlsx-template")]
    public async Task<IActionResult> XlsxTemplate()
    {
        const string LeafDark = "#4E7D26";
        const string Leaf = "#7CB342";
        const string LeafBg = "#F1F8E9";
        const string Grey = "#8A94A6";
        const int DataRows = 200; // matches the parse cap — styling more would promise more

        var locationNames = await _db.Locations
            .OrderBy(l => l.Name)
            .Select(l => l.Name)
            .ToListAsync(HttpContext.RequestAborted);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("İşçilər");
        ws.SetTabColor(XLColor.FromHtml(Leaf));

        // (header, width, grey example under the header). Column A's example stays null on purpose.
        // Name, surname and patronymic share ONE column — that is the shape of every customer's own
        // staff list («Ruslan Hüseynov Rasim oğlu»), and SplitPatronymic separates the «oğlu»/«qızı»
        // tail server-side. parse-xlsx finds columns by header text, so older files with a separate
        // Ata adı column keep importing unchanged.
        (string Header, int Width, string? Note)[] columns =
        [
            ("Ad Soyad Ata adı", 34, null),
            ("Doğum tarixi", 16, "15.03.1990"),
            ("Telefon", 18, "0501234567"),
            ("Vəzifə", 22, "Operator"),
            ("Email", 26, "istəyə bağlı"),
            ("Rol", 14, "boş = İşçi"),
            ("Filial", 24, "boş = səhifədə seçilən"),
        ];

        // Row 1 — brand banner. Row 2 — one-line instruction. Both merged; neither contains a header
        // word, so the parser's header scan walks straight past them.
        ws.Range(1, 1, 1, columns.Length).Merge();
        var title = ws.Cell(1, 1);
        title.Value = "QRLog — işçi siyahısı";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 15;
        title.Style.Font.FontColor = XLColor.White;
        title.Style.Fill.BackgroundColor = XLColor.FromHtml(LeafDark);
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Row(1).Height = 30;

        ws.Range(2, 1, 2, columns.Length).Merge();
        var sub = ws.Cell(2, 1);
        sub.Value = "Hər sətir bir işçidir. Ad və Telefon vacibdir (məs.: Ruslan Hüseynov Rasim oğlu — ata adı istəyə bağlıdır). Ətraflı: «Təlimat» vərəqi.";
        sub.Style.Font.Italic = true;
        sub.Style.Font.FontSize = 10;
        sub.Style.Font.FontColor = XLColor.FromHtml(LeafDark);
        sub.Style.Fill.BackgroundColor = XLColor.FromHtml(LeafBg);
        sub.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(2).Height = 18;

        // Row 3 — the header row parse-xlsx recognises. Row 4 — grey examples (column A empty).
        for (var i = 0; i < columns.Length; i++)
        {
            var cell = ws.Cell(3, i + 1);
            cell.Value = columns[i].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(Leaf);
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Column(i + 1).Width = columns[i].Width;

            if (columns[i].Note is not null)
            {
                var note = ws.Cell(4, i + 1);
                note.Value = columns[i].Note;
                note.Style.Font.Italic = true;
                note.Style.Font.FontColor = XLColor.FromHtml(Grey);
            }
        }
        ws.Row(3).Height = 20;

        // The fill-in area: light borders so it reads as a form, phone/year columns pre-formatted.
        // Text format on Telefon is the load-bearing part — without it Excel turns 0501234567 into
        // 501234567 the moment the customer types it.
        var dataRange = ws.Range(4, 1, 4 + DataRows, columns.Length);
        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#DCE3D6");
        dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#DCE3D6");
        ws.Range(4, 2, 4 + DataRows, 2).Style.NumberFormat.Format = "dd.mm.yyyy";
        ws.Range(4, 3, 4 + DataRows, 3).Style.NumberFormat.Format = "@";

        // Rol as an in-cell dropdown — the three labels ParseRoleName accepts, nothing to mistype.
        var roleDv = ws.Range(5, 6, 4 + DataRows, 6).CreateDataValidation();
        roleDv.List("\"İşçi,Menecer,Admin\"", true);

        // ---- «Təlimat»: the sheet the customer actually reads. Second on purpose. ----
        var help = wb.Worksheets.Add("Təlimat");
        help.SetTabColor(XLColor.FromHtml(LeafDark));
        help.ShowGridLines = false;
        help.Column(1).Width = 16;
        help.Column(2).Width = 10;
        help.Column(3).Width = 78;

        help.Range(1, 1, 1, 3).Merge();
        var ht = help.Cell(1, 1);
        ht.Value = "Təlimat — işçi siyahısının doldurulması";
        ht.Style.Font.Bold = true;
        ht.Style.Font.FontSize = 14;
        ht.Style.Font.FontColor = XLColor.White;
        ht.Style.Fill.BackgroundColor = XLColor.FromHtml(LeafDark);
        ht.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        help.Row(1).Height = 28;

        help.Range(2, 1, 2, 3).Merge();
        help.Cell(2, 1).Value = "«İşçilər» vərəqini doldurun və faylı sistemə qaytarın — hesablar avtomatik yaradılacaq.";
        help.Cell(2, 1).Style.Font.Italic = true;
        help.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml(Grey);

        (string Col, string Req, string Text)[] guide =
        [
            ("Ad Soyad Ata adı", "Bəli", "Ad və soyad; istəsəniz ata adını da yanına yazın: «Ruslan Hüseynov Rasim oğlu» — «oğlu»/«qızı» ilə bitən hissəni sistem özü ata adı kimi ayırır. Adı boş olan sətirlər nəzərə alınmır."),
            ("Doğum tarixi", "Xeyr", "Tam tarix: 15.03.1990. Yalnız ili bilinərsə, təkcə ili yazın (1990) — tam tarix ad günü təbrikləri üçün lazımdır."),
            ("Telefon", "Bəli", "Sistemə giriş bu nömrə ilədir; 0 ilə başlayır (məs. 0501234567) və təkrarlana bilməz."),
            ("Vəzifə", "Xeyr", "İşçinin vəzifəsi (məs. Operator, Təsərrüfat işçisi)."),
            ("Email", "Xeyr", "Boş qala bilər — giriş telefon nömrəsi ilə də mümkündür."),
            ("Rol", "Xeyr", "İşçi / Menecer / Admin. Boş qalsa, yükləmə səhifəsində seçilən rol tətbiq olunur."),
            ("Filial", "Xeyr", "Boş qalsa, yükləmə səhifəsində seçilən filial tətbiq olunur. Adlar aşağıdakı siyahıdakı kimi yazılmalıdır."),
        ];

        var r = 4;
        var gh = help.Range(r, 1, r, 3);
        help.Cell(r, 1).Value = "Sütun";
        help.Cell(r, 2).Value = "Vacib";
        help.Cell(r, 3).Value = "İzah";
        gh.Style.Font.Bold = true;
        gh.Style.Font.FontColor = XLColor.White;
        gh.Style.Fill.BackgroundColor = XLColor.FromHtml(Leaf);
        r++;
        foreach (var (col, req, text) in guide)
        {
            help.Cell(r, 1).Value = col;
            help.Cell(r, 1).Style.Font.Bold = true;
            help.Cell(r, 2).Value = req;
            if (req == "Bəli") help.Cell(r, 2).Style.Font.FontColor = XLColor.FromHtml(LeafDark);
            help.Cell(r, 3).Value = text;
            help.Cell(r, 3).Style.Alignment.WrapText = true;
            r++;
        }
        var guideRange = help.Range(4, 1, r - 1, 3);
        guideRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        guideRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        guideRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#DCE3D6");
        guideRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#DCE3D6");

        r += 1;
        foreach (var line in new[]
        {
            "• Sütunların yerini dəyişmək və ya lazımsızları silmək olar — sistem sütunu başlığına görə tanıyır.",
            "• Bir faylda ən çox 200 işçi oxunur; daha çox üçün ikinci fayl göndərin.",
            "• Yüklənəndən sonra hər işçiyə müvəqqəti PIN yaradılır — işçi ilk girişdə öz PIN-ini təyin edir.",
        })
        {
            help.Range(r, 1, r, 3).Merge();
            help.Cell(r, 1).Value = line;
            help.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }

        // The tenant's branch names, spelled exactly as they will match — and doubling as the source
        // range for the Filial dropdown on the first sheet (a formula list would choke on commas).
        if (locationNames.Count > 0)
        {
            r += 1;
            help.Cell(r, 1).Value = "Filiallar";
            help.Cell(r, 1).Style.Font.Bold = true;
            help.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml(LeafDark);
            r++;
            var listStart = r;
            foreach (var name in locationNames)
            {
                help.Cell(r, 1).Value = name;
                r++;
            }
            var locDv = ws.Range(5, 7, 4 + DataRows, 7).CreateDataValidation();
            locDv.List(help.Range(listStart, 1, r - 1, 1));
        }

        ws.SheetView.FreezeRows(3);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "isciler-sablon.xlsx");
    }

    // Emails + phones already in use, as sets, so a batch can check for collisions in memory (against
    // the DB and against earlier rows in the same batch) without a query per row.
    private async Task<(HashSet<string> Emails, HashSet<string> Phones)> LoadTakenIdentifiersAsync()
    {
        var emails = await _db.Employees.Select(e => e.Email).ToListAsync();
        var phones = await _db.Employees.Where(e => e.PhoneNumber != null).Select(e => e.PhoneNumber!).ToListAsync();
        return (new HashSet<string>(emails, StringComparer.Ordinal), new HashSet<string>(phones, StringComparer.Ordinal));
    }

    // Builds one invited employee (not yet added to the context) + its activation token, or returns an
    // error code. Mutates the taken-sets so the next call in a batch sees this row's identifiers. Shared
    // by the single and bulk invite paths so their validation can never drift apart.
    private (Employee? Employee, string? Token, string? Error) BuildInvite(
        string fullName, string? emailIn, string? phoneIn, string? fatherName, string? position, int? birthYear,
        DateOnly? birthDate, Guid locationId, EmployeeRole role, HashSet<string> takenEmails, HashSet<string> takenPhones)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, null, "NameRequired");

        var phone = PhoneNumbers.Normalize(phoneIn);
        var hasEmail = !string.IsNullOrWhiteSpace(emailIn);

        // At least one login identifier so the employee can sign in later (phone OR email).
        if (!hasEmail && phone is null)
            return (null, null, "NeedEmailOrPhone");

        // Phone-only employees keep a null email — no synthesised placeholder. Login works by phone.
        string? email = hasEmail ? emailIn!.Trim() : null;

        if (email is not null && takenEmails.Contains(email))
            return (null, null, "EmailAlreadyExists");
        if (phone is not null && takenPhones.Contains(phone))
            return (null, null, "PhoneAlreadyExists");

        var employee = new Employee
        {
            FullName = fullName.Trim(),
            Email = email,
            PhoneNumber = phone,
            FatherName = string.IsNullOrWhiteSpace(fatherName) ? null : fatherName.Trim(),
            Position = string.IsNullOrWhiteSpace(position) ? null : position.Trim(),
            BirthDate = birthDate,
            BirthYear = birthDate?.Year ?? birthYear,   // year stays in sync with the full date
            LocationId = locationId,
            Role = role,
            PasswordHash = string.Empty,       // set by the employee at activation
            IsActive = true,
            ActivatedAtUtc = null,             // not activated yet
            InvitationExpiresUtc = DateTime.UtcNow.AddHours(_invitationOptions.ExpiryHours)
        };

        // The token embeds the (non-secret) employee id so activation can look the account up by a key
        // that survives activation; only the random part's hash is stored.
        var (activationToken, randomHash) = ActivationToken.Create(employee.Id);
        employee.InvitationTokenHash = randomHash;

        takenEmails.Add(email);
        if (phone is not null) takenPhones.Add(phone);

        return (employee, activationToken, null);
    }

    // Builds one ACTIVATED employee (not yet added to the context) with a random temporary PIN, or an
    // error code. Mirrors BuildInvite's validation exactly, but instead of an activation token it sets a
    // hashed temp PIN + MustChangePin, so the employee can sign in immediately and is forced to pick
    // their own PIN. Mutates the taken-sets so later rows in the batch see this row's identifiers.
    private (Employee? Employee, string? TempPin, string? Error) BuildActivatedWithTempPin(
        string fullName, string? emailIn, string? phoneIn, string? fatherName, string? position, int? birthYear,
        DateOnly? birthDate, Guid locationId, EmployeeRole role, HashSet<string> takenEmails, HashSet<string> takenPhones)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, null, "NameRequired");

        var phone = PhoneNumbers.Normalize(phoneIn);
        var hasEmail = !string.IsNullOrWhiteSpace(emailIn);
        if (!hasEmail && phone is null)
            return (null, null, "NeedEmailOrPhone");

        string? email = hasEmail ? emailIn!.Trim() : null;
        if (email is not null && takenEmails.Contains(email))
            return (null, null, "EmailAlreadyExists");
        if (phone is not null && takenPhones.Contains(phone))
            return (null, null, "PhoneAlreadyExists");

        var tempPin = PinRules.Generate();
        var now = DateTime.UtcNow;
        var employee = new Employee
        {
            FullName = fullName.Trim(),
            Email = email,
            PhoneNumber = phone,
            FatherName = string.IsNullOrWhiteSpace(fatherName) ? null : fatherName.Trim(),
            Position = string.IsNullOrWhiteSpace(position) ? null : position.Trim(),
            BirthDate = birthDate,
            BirthYear = birthDate?.Year ?? birthYear,   // year stays in sync with the full date
            LocationId = locationId,
            Role = role,
            PasswordHash = _passwordHasher.Hash(tempPin),
            IsActive = true,
            ActivatedAtUtc = now,   // no activation link — the temp PIN is the credential
            MustChangePin = true,   // forced to set their own PIN on first login
            InvitationTokenHash = null,
            InvitationExpiresUtc = null
        };

        takenEmails.Add(email);
        if (phone is not null) takenPhones.Add(phone);

        return (employee, tempPin, null);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EmployeeUpdateRequest request)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        // You cannot lock yourself out. Deleting yourself is already blocked below, but deactivating
        // or demoting yourself was not — and the result is worse: login rejects an inactive account,
        // so the door closes silently and cannot be reopened from inside the tenant. CleanFix's only
        // admin did exactly this and left a 13-person company with nobody who could sign in; it took
        // a hand-written UPDATE to undo.
        if (id == User.EmployeeId())
        {
            if (!request.IsActive)
                return BadRequest(new { error = "CannotDeactivateSelf" });
            if (request.Role != employee.Role)
                return BadRequest(new { error = "CannotChangeOwnRole" });
        }

        // Promotion is account creation by another name — see Invite.
        if (User.IsImpersonating() && request.Role == EmployeeRole.Admin && employee.Role != EmployeeRole.Admin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" });

        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var phone = PhoneNumbers.Normalize(request.PhoneNumber);
        // Keep the current email if none supplied, so a phone-only edit doesn't wipe it.
        var email = string.IsNullOrWhiteSpace(request.Email) ? employee.Email : request.Email.Trim();

        // Only probe uniqueness for a real email — a null one (phone-only employee) makes
        // `e.Email == email` become `Email IS NULL`, which matches every other phone-only account and
        // falsely 409s "EmailAlreadyExists", blocking every edit for those employees.
        if (!string.IsNullOrWhiteSpace(email) && await _db.Employees.AnyAsync(e => e.Email == email && e.Id != id))
            return Conflict(new { error = "EmailAlreadyExists" });
        if (phone is not null && await _db.Employees.AnyAsync(e => e.PhoneNumber == phone && e.Id != id))
            return Conflict(new { error = "PhoneAlreadyExists" });

        // The identifier is the credential's other half — it is what Login matches on and where a
        // forgot-PIN reset is delivered. Repointing an admin's (or the borrowed account's own) from an
        // impersonation session aims the customer's login at the operator, and the customer's own number
        // stops working; see ImpersonationRefusal. Ordinary edits by the tenant's own admins, and edits
        // to staff rows, are untouched.
        var identifierChanged =
            !string.Equals(email, employee.Email, StringComparison.OrdinalIgnoreCase) || phone != employee.PhoneNumber;
        if (identifierChanged && ImpersonationRefusal(employee) is { } identifierRefusal)
            return identifierRefusal;

        // A token carries the role and never expires, and nothing re-checks it per request — so
        // changing either of these has to invalidate the sessions already issued. Without this a
        // demoted admin keeps the admin panel and a deactivated employee keeps scanning, both for as
        // long as they simply never log in again. Only bump when one of the two actually changed, so
        // an ordinary edit (name, position, hours) doesn't log the employee out for nothing.
        if (employee.Role != request.Role || employee.IsActive != request.IsActive)
            employee.TokenVersion++;

        (employee.FullName, employee.FirstName, employee.LastName) =
            EmployeeName.Resolve(request.FirstName, request.LastName, request.FullName);
        employee.Email = email;
        employee.PhoneNumber = phone;
        employee.FatherName = request.FatherName;
        employee.Position = request.Position;
        employee.PhotoExempt = request.PhotoExempt;
        employee.CanFieldCheckIn = request.CanFieldCheckIn;
        await RegisterPositionsAsync();
        employee.BirthDate = request.BirthDate;
        // Full date wins; keep the year in sync from it so the fallback display agrees.
        employee.BirthYear = request.BirthDate?.Year ?? request.BirthYear;
        employee.LocationId = request.LocationId;
        employee.Role = request.Role;
        employee.IsActive = request.IsActive;
        employee.WorkStart = ParseTimeOrNull(request.WorkStart);
        employee.WorkEnd = ParseTimeOrNull(request.WorkEnd);
        employee.MonthlySalary = request.MonthlySalary;
        if (WorkCycle.Apply(employee, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });
        if (await ApplyScheduleAsync(employee, request.ScheduleId) is { } scheduleError)
            return BadRequest(new { error = scheduleError });

        var scopeError = await ApplyManagedLocationsAsync(employee, request.ManagedLocationIds);
        if (scopeError is not null)
            return BadRequest(new { error = scopeError });

        await _db.SaveChangesAsync();
        return Ok(new { id = employee.Id });
    }

    /// <summary>
    /// Sets which branches a Manager may see in the reports. Null → leave alone; a list → replace.
    ///
    /// Cleared whenever the employee is not a Manager, so a demoted manager's old scope cannot come
    /// back to life if they are ever promoted again — a stale set is a silent grant.
    /// </summary>
    private async Task<string?> ApplyManagedLocationsAsync(Employee employee, IReadOnlyList<Guid>? wanted)
    {
        var existing = await _db.ManagedLocations
            .Where(m => m.EmployeeId == employee.Id)
            .ToListAsync(HttpContext.RequestAborted);

        if (employee.Role != EmployeeRole.Manager)
        {
            // An Admin already sees every branch and an Employee only their own record — a row here
            // would mean nothing for either, and would quietly apply again on a future promotion.
            _db.ManagedLocations.RemoveRange(existing);
            return null;
        }

        if (wanted is null)
            return null; // caller did not say — keep what they have

        var ids = wanted.Distinct().ToList();
        // Tenant-filtered, so this also rejects a branch belonging to another company.
        var validCount = await _db.Locations.CountAsync(l => ids.Contains(l.Id), HttpContext.RequestAborted);
        if (validCount != ids.Count)
            return "ManagedLocationNotFound";

        _db.ManagedLocations.RemoveRange(existing.Where(m => !ids.Contains(m.LocationId)));
        foreach (var locationId in ids.Where(i => existing.All(m => m.LocationId != i)))
            _db.ManagedLocations.Add(new ManagedLocation { EmployeeId = employee.Id, LocationId = locationId });

        return null;
    }

    // "HH:mm" (or empty) → TimeOnly?; empty/unparseable clears the per-employee override.
    /// <summary>
    /// Adds any title the catalogue is missing. Bulk import and the API accept a position as text, and
    /// a title that exists on an employee but not in the list is exactly how the duplicates started —
    /// the next person types it again, slightly differently, because nothing offered it to them.
    /// </summary>
    private async Task RegisterPositionsAsync()
    {
        // Read from the change tracker rather than each call site: every path that sets a position —
        // single invite, bulk invite, bulk import, edit — is covered without being remembered.
        var names = _db.ChangeTracker.Entries<Employee>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity.Position)
            .ToList()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct()
            .ToList();
        if (names.Count == 0) return;

        var known = await _db.JobPositions
            .Where(p => names.Contains(p.Name))
            .Select(p => p.Name)
            .ToListAsync(HttpContext.RequestAborted);

        foreach (var name in names.Except(known))
            _db.JobPositions.Add(new JobPosition { Name = name });
    }

    private static TimeOnly? ParseTimeOrNull(string? value)
        => TimeOnly.TryParse(value, out var t) ? t : null;

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force = false)
    {
        if (id == User.EmployeeId())
            return BadRequest(new { error = "CannotDeleteSelf" });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        // Attendance/summary/device-change FKs are Restrict — refuse to delete an employee with
        // history (it would fail at the DB anyway) unless the caller explicitly opts into a
        // force delete (e.g. wiping a test account), which purges that history first.
        var hasHistory = await _db.AttendanceRecords.AnyAsync(a => a.EmployeeId == id)
                         || await _db.DailySummaries.AnyAsync(d => d.EmployeeId == id)
                         || await _db.DeviceChangeRequests.AnyAsync(r => r.EmployeeId == id || r.ReviewedByEmployeeId == id);
        if (hasHistory && !force)
            return Conflict(new { error = "EmployeeHasHistory" });

        // Every photo of this person, collected BEFORE the rows that name them are deleted — after
        // that the keys are unrecoverable and the objects are orphans nobody can find, let alone
        // remove. The bucket is not self-cleaning here: the retention job prunes checkins/ by age and
        // deliberately never touches reference/, so a deleted employee's enrollment selfie stayed for
        // ever. qrlog.az/hesab-silinmesi/ tells people otherwise.
        var photoKeys = new List<string>();
        if (!string.IsNullOrWhiteSpace(employee.ReferencePhotoKey))
            photoKeys.Add(employee.ReferencePhotoKey);
        photoKeys.AddRange(await _db.AttendanceRecords
            .Where(a => a.EmployeeId == id && a.CheckInPhotoKey != null)
            .Select(a => a.CheckInPhotoKey!)
            .ToListAsync());
        // Field visits: two selfies and, on the check-out, a photo of the WORK. The work photo is
        // evidence for a customer invoice rather than a picture of a person — but it is filed under
        // this employee's visit and nothing else will ever come looking for it.
        //
        // Three columns pulled back and flattened HERE, not with a SelectMany over an array literal:
        // that does not translate to SQL and threw at runtime on every delete.
        var visits = await _db.FieldVisits.Where(v => v.EmployeeId == id).ToListAsync();
        photoKeys.AddRange(visits
            .SelectMany(v => new[] { v.CheckInPhotoKey, v.CheckOutPhotoKey, v.WorkPhotoKey })
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!));
        // FieldVisit has no FK to Employee, so these rows outlive the person. Clear the keys: they
        // would otherwise point at objects this request is about to delete, and the field-visit
        // screens would spend a presign call each on a 404.
        foreach (var visit in visits)
        {
            visit.CheckInPhotoKey = null;
            visit.CheckOutPhotoKey = null;
            visit.WorkPhotoKey = null;
        }

        if (hasHistory)
        {
            await _db.AttendanceRecords.Where(a => a.EmployeeId == id).ExecuteDeleteAsync();
            await _db.DailySummaries.Where(d => d.EmployeeId == id).ExecuteDeleteAsync();
            // Own requests are this employee's history — remove them. Requests they merely
            // reviewed belong to someone else's history — keep the request, just anonymize
            // the reviewer (mirrors the AuditLogs SetNull behavior on employee delete).
            await _db.DeviceChangeRequests.Where(r => r.EmployeeId == id).ExecuteDeleteAsync();
            await _db.DeviceChangeRequests.Where(r => r.ReviewedByEmployeeId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewedByEmployeeId, (Guid?)null));
        }

        // DeviceBinding and ManagedLocations cascade; AuditLogs are set null.
        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();

        // After the database, never before: object storage has no transaction to join, so a purge
        // that ran first would delete a living employee's face if the delete below then failed.
        // Best-effort by design — the row is already gone and re-adding it to keep the two in step
        // would be worse. A failure is logged loudly enough to be swept by hand.
        var photosDeleted = 0;
        if (photoKeys.Count > 0)
        {
            try
            {
                photosDeleted = await _photoStorage.DeleteObjectsAsync(photoKeys);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Employee {EmployeeId} deleted, but {Count} of their photos could not be removed from storage",
                    id, photoKeys.Count);
            }
        }

        return Ok(new { deleted = id, forced = hasHistory && force, photosDeleted });
    }

    // Testing/reset helper: clears an employee's check-in/check-out history so the same account +
    // device can be used to test the scan flow again from a clean slate. Keeps the employee,
    // activation state and device binding untouched — only attendance data is removed.
    [HttpPost("{id:guid}/reset-attendance")]
    public async Task<IActionResult> ResetAttendance(Guid id)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == id))
            return NotFound(new { error = "EmployeeNotFound" });

        var recordsDeleted = await _db.AttendanceRecords.Where(a => a.EmployeeId == id).ExecuteDeleteAsync();
        var summariesDeleted = await _db.DailySummaries.Where(d => d.EmployeeId == id).ExecuteDeleteAsync();
        return Ok(new { attendanceRecordsDeleted = recordsDeleted, summariesDeleted });
    }

    // Regenerate the activation link for a not-yet-activated employee (e.g. the original link was
    // lost or expired). Only the new token's hash is stored; the plaintext is returned once.
    [HttpPost("{id:guid}/reinvite")]
    public async Task<IActionResult> Reinvite(Guid id)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });
        if (employee.ActivatedAtUtc is not null)
            return Conflict(new { error = "AlreadyActivated" });
        // The activation token below IS a credential — /api/auth/activate turns it into an account with
        // a PIN of the holder's choosing. Same rule as reset-pin.
        if (ImpersonationRefusal(employee) is { } impersonationRefusal)
            return impersonationRefusal;

        var (activationToken, randomHash) = ActivationToken.Create(employee.Id);
        employee.InvitationTokenHash = randomHash;
        employee.InvitationExpiresUtc = DateTime.UtcNow.AddHours(_invitationOptions.ExpiryHours);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            employeeId = employee.Id,
            activationToken,
            activationUrl = $"/activate?token={activationToken}"
        });
    }

    /// <summary>
    /// 403 <c>NotDuringImpersonation</c> when an impersonation session reaches for a PRIVILEGED
    /// account's credential or login identifier; null otherwise.
    ///
    /// An impersonation token's "sub" is the CUSTOMER's own admin, borrowed for an hour so the operator
    /// can set the company up without enrolling themselves inside it. set-initial-pin and
    /// change-password already refuse that session — but they were not the only ways to the same place.
    /// reset-pin returns the new PIN in plaintext and reinvite returns an activation token, so either
    /// one turns the borrowed hour into an ordinary, never-expiring login for somebody else's admin —
    /// and the temporary PIN written on the customer's handover slip silently stops working. Repointing
    /// the account's email or phone is the same takeover by a quieter route: the operator receives the
    /// forgot-PIN traffic and the customer's own number no longer signs in.
    ///
    /// The line is drawn at ADMINS and at the borrowed account itself, not at every employee: handing
    /// out staff temporary PINs IS the setup work this feature exists for, and an ordinary employee's
    /// PIN buys nothing but their own scan screen.
    /// </summary>
    private IActionResult? ImpersonationRefusal(Employee target) =>
        User.IsImpersonating() && (target.Id == User.EmployeeId() || target.Role == EmployeeRole.Admin)
            ? StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" })
            : null;

    // POST /api/admin/employees/{id}/reset-pin — set a random temporary PIN for an activated employee
    // who forgot theirs (a hashed PIN can never be read back). Returns the plaintext temp PIN so the
    // admin can pass it on; the employee logs in and changes it from the menu. Also clears any login
    // lockout so they can sign in straight away. Not-yet-activated accounts use reinvite instead.
    [HttpPost("{id:guid}/reset-pin")]
    public async Task<IActionResult> ResetPin(Guid id)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });
        // A platform operator is not a tenant's to reset. They are allowlisted by employee id and
        // typically sit inside some tenant as a normal-looking row, so without this a tenant admin —
        // or a support session impersonating one — could reset that operator's PIN, read the
        // plaintext out of the response below, and sign in to the operator console with it. That
        // turns "help a customer back in" into a route to the whole platform. Refused before the
        // activation check, so the response cannot be used to probe which employees are operators.
        if (_operatorIds.Contains(employee.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "CannotManageOperator" });
        // Nor is a borrowed account's credential the borrower's to replace. See ImpersonationRefusal:
        // this endpoint returns the new PIN in plaintext, so without this an impersonation session
        // could convert its hour into a permanent login for the customer's admin.
        if (ImpersonationRefusal(employee) is { } impersonationRefusal)
            return impersonationRefusal;
        if (employee.ActivatedAtUtc is null)
            return Conflict(new { error = "NotActivated" });

        // Cryptographically random 4-digit PIN, zero-padded (0000–9999).
        var pin = PinRules.Generate();
        employee.PasswordHash = _passwordHasher.Hash(pin);
        employee.MustChangePin = true;   // the employee picks their own PIN on next login
        employee.TokenVersion++;         // kill any session still holding the old PIN's token
        await _db.SaveChangesAsync();

        // Clear the lockout for both identifiers they can log in with. No longer needs to guess at
        // spellings ("0"+phone, +994…): LoginIdentity collapses every spelling of a number onto one
        // key, which is the same key Login would have locked.
        var tenantId = _db.CurrentTenantId;
        _lockout.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.Email));
        if (employee.PhoneNumber is not null)
            _lockout.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.PhoneNumber));

        return Ok(new { tempPin = pin });
    }

    /// <summary>
    /// Assigns (or clears) the employee's named shift. Returns an error code, or null on success.
    ///
    /// A shift from another company would be an outright tenant leak, so the id is verified against
    /// the query-filtered set rather than trusted from the body.
    /// </summary>
    private async Task<string?> ApplyScheduleAsync(Employee employee, Guid? scheduleId)
    {
        if (scheduleId is not Guid id)
        {
            employee.ScheduleId = null;
            return null;
        }
        var schedule = await _db.Schedules
            .Where(s => s.Id == id)
            .Select(s => new { s.LocationId })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (schedule is null)
            return "ScheduleNotFound";

        // A shift pinned to a branch is offered only to that branch's staff — see ScheduleAssignmentRule,
        // which the manager path shares so the two can never drift apart.
        if (ScheduleAssignmentRule.Refusal(schedule.LocationId, employee.LocationId) is { } branchRefusal)
            return branchRefusal;

        employee.ScheduleId = id;
        return null;
    }
}
