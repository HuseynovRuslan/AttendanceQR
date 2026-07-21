using System.Security.Cryptography;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using ClosedXML.Excel;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
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

    public AdminController(
        AppDbContext db,
        IOptions<InvitationOptions> invitationOptions,
        IPasswordHasher passwordHasher,
        ILoginLockoutStore lockout,
        AppOptions appOptions)
    {
        _db = db;
        _invitationOptions = invitationOptions.Value;
        _passwordHasher = passwordHasher;
        _lockout = lockout;
        _hiddenEmails = appOptions.HiddenEmailList();
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        // Admins/managers ARE shown here now (so they can be managed), EXCEPT the system/root admin
        // accounts listed in AppOptions.HiddenEmails (e.g. admin@bms.az) — they're operators, not staff.
        var employees = await _db.Employees
            .Include(e => e.DeviceBindings)
            .Where(e => !_hiddenEmails.Contains(e.Email.ToLower()))
            .OrderBy(e => e.FullName)
            .ToListAsync(HttpContext.RequestAborted);

        var locationNames = await _db.Locations
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

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
                fatherName = e.FatherName,
                position = e.Position,
                birthYear = e.BirthYear,
                birthDate = e.BirthDate,
                workStart = e.WorkStart?.ToString("HH:mm"),
                workEnd = e.WorkEnd?.ToString("HH:mm"),
                monthlySalary = e.MonthlySalary,
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
        });
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
        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var (takenEmails, takenPhones) = await LoadTakenIdentifiersAsync();
        var (employee, token, error) = BuildInvite(
            request.FullName, request.Email, request.PhoneNumber, request.FatherName, request.Position,
            request.BirthYear, request.LocationId, request.Role, takenEmails, takenPhones);

        if (error is not null)
            return error is "EmailAlreadyExists" or "PhoneAlreadyExists"
                ? Conflict(new { error })
                : BadRequest(new { error });

        employee!.MonthlySalary = request.MonthlySalary;
        employee.BirthDate = request.BirthDate;
        if (request.BirthDate is { } dob)
            employee.BirthYear = dob.Year;   // keep the year in sync so the fallback display agrees
        employee.WorkStart = ParseTimeOrNull(request.WorkStart);
        employee.WorkEnd = ParseTimeOrNull(request.WorkEnd);
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
        BulkInviteRow row, Guid batchLocationId, EmployeeRole batchRole, Dictionary<string, Guid> locationsByName)
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
            var (rowLocationId, rowRole, scopeError) = ResolveRowScope(row, request.LocationId, request.Role, locationsByName);
            if (scopeError is not null)
            {
                failed.Add(new { fullName = row.FullName, error = scopeError });
                continue;
            }

            var (employee, token, error) = BuildInvite(
                row.FullName, row.Email, row.PhoneNumber, row.FatherName, row.Position, row.BirthYear,
                rowLocationId, rowRole, takenEmails, takenPhones);

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
            var (rowLocationId, rowRole, scopeError) = ResolveRowScope(row, request.LocationId, request.Role, locationsByName);
            if (scopeError is not null)
            {
                failed.Add(new { fullName = row.FullName, error = scopeError });
                continue;
            }

            var (employee, tempPin, error) = BuildActivatedWithTempPin(
                row.FullName, row.Email, row.PhoneNumber, row.FatherName, row.Position, row.BirthYear,
                rowLocationId, rowRole, takenEmails, takenPhones);

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
        ("fullName", ["ad soyad", "ad, soyad", "adı soyadı", "ad", "full name", "fullname"]),
        ("phoneNumber", ["telefon", "telefon nömrəsi", "nömrə", "phone"]),
        ("position", ["vəzifə", "vezife", "position"]),
        ("fatherName", ["ata adı", "ata adi", "atasının adı", "father"]),
        ("birthYear", ["təvəllüd ili", "təvəllüd", "tevellud", "doğum ili", "birth year", "birthyear"]),
        ("email", ["email", "e-mail", "poçt", "e-poçt"]),
        ("roleName", ["rol", "role"]),
        ("locationName", ["filial", "ərazi", "lokasiya", "location", "branch"]),
    ];

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

            // Map field -> column number from the header row.
            var map = new Dictionary<string, int>();
            var headerRow = used[0];
            foreach (var cell in headerRow.CellsUsed())
            {
                var text = cell.GetString().Trim().ToLowerInvariant();
                var match = XlsxColumns.FirstOrDefault(c => c.Headers.Contains(text));
                if (match.Field is not null && !map.ContainsKey(match.Field))
                    map[match.Field] = cell.Address.ColumnNumber;
            }

            // No recognisable header (someone deleted it, or typed their own): fall back to the
            // original positional layout, which is what every file predating this change looks like.
            var hasHeader = map.ContainsKey("fullName");
            if (!hasHeader)
                map = new Dictionary<string, int> { ["fullName"] = 1, ["phoneNumber"] = 2, ["position"] = 3 };

            string? Get(IXLRow row, string field)
            {
                if (!map.TryGetValue(field, out var col)) return null;
                var v = row.Cell(col).GetString().Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }

            foreach (var row in used.Skip(hasHeader ? 1 : 0))
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
                rows.Add(new
                {
                    fullName,
                    phoneNumber = phone,
                    position = Get(row, "position"),
                    fatherName = Get(row, "fatherName"),
                    // A year typed as "1990" or read back as "1990.0" — take the digits, ignore the rest.
                    birthYear = int.TryParse(birthYearText?.Split('.', ',')[0], out var by) ? by : (int?)null,
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
    // Headers only (no example data row — an unedited example would import as a real employee).
    //
    // The first three columns keep their original order so an older file, and the paste box, still
    // line up. parse-xlsx reads by header text anyway, so an admin may reorder or drop columns.
    [HttpGet("xlsx-template")]
    public async Task<IActionResult> XlsxTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("İşçilər");

        // (header, column width, note shown under the header). NOTHING goes under "Ad Soyad": row 2
        // survives only because its column A is empty, which is exactly how parse-xlsx skips it. A
        // note there would import as an employee called "məcburi".
        (string Header, int Width, string? Note)[] columns =
        [
            ("Ad Soyad", 28, null),
            ("Telefon", 18, "0501234567"),
            ("Vəzifə", 22, null),
            ("Ata adı", 20, null),
            ("Təvəllüd ili", 14, "1990"),
            ("Email", 26, "istəyə bağlı"),
            ("Rol", 14, "İşçi / Menecer / Admin"),
            ("Filial", 22, "boş = səhifədə seçilən"),
        ];

        for (var i = 0; i < columns.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = columns[i].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
            ws.Column(i + 1).Width = columns[i].Width;

            // Row 2 is guidance, greyed and italic. parse-xlsx drops it: it has no name in column A.
            if (columns[i].Note is not null)
            {
                var note = ws.Cell(2, i + 1);
                note.Value = columns[i].Note;
                note.Style.Font.Italic = true;
                note.Style.Font.FontColor = XLColor.FromHtml("#8A94A6");
            }
        }

        // Spell out the branch names that will match, so nobody has to guess at the spelling.
        var locationNames = await _db.Locations
            .OrderBy(l => l.Name)
            .Select(l => l.Name)
            .ToListAsync(HttpContext.RequestAborted);
        if (locationNames.Count > 0)
        {
            var hint = ws.Cell(2, columns.Length);
            hint.Value = "boş = səhifədəki · " + string.Join(" / ", locationNames);
        }

        ws.SheetView.FreezeRows(1);

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
        Guid locationId, EmployeeRole role, HashSet<string> takenEmails, HashSet<string> takenPhones)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, null, "NameRequired");

        var phone = PhoneNumbers.Normalize(phoneIn);
        var hasEmail = !string.IsNullOrWhiteSpace(emailIn);

        // At least one login identifier so the employee can sign in later (phone OR email).
        if (!hasEmail && phone is null)
            return (null, null, "NeedEmailOrPhone");

        // Email stays non-null (it's a JWT claim); synthesize a unique placeholder when only a phone
        // was given. Login works by either identifier.
        var email = hasEmail ? emailIn!.Trim() : $"emp-{Guid.NewGuid().ToString("N")[..10]}@baki.local";

        if (takenEmails.Contains(email))
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
            BirthYear = birthYear,
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
        Guid locationId, EmployeeRole role, HashSet<string> takenEmails, HashSet<string> takenPhones)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, null, "NameRequired");

        var phone = PhoneNumbers.Normalize(phoneIn);
        var hasEmail = !string.IsNullOrWhiteSpace(emailIn);
        if (!hasEmail && phone is null)
            return (null, null, "NeedEmailOrPhone");

        var email = hasEmail ? emailIn!.Trim() : $"emp-{Guid.NewGuid().ToString("N")[..10]}@baki.local";
        if (takenEmails.Contains(email))
            return (null, null, "EmailAlreadyExists");
        if (phone is not null && takenPhones.Contains(phone))
            return (null, null, "PhoneAlreadyExists");

        var tempPin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
        var now = DateTime.UtcNow;
        var employee = new Employee
        {
            FullName = fullName.Trim(),
            Email = email,
            PhoneNumber = phone,
            FatherName = string.IsNullOrWhiteSpace(fatherName) ? null : fatherName.Trim(),
            Position = string.IsNullOrWhiteSpace(position) ? null : position.Trim(),
            BirthYear = birthYear,
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

        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var phone = PhoneNumbers.Normalize(request.PhoneNumber);
        // Keep the current email if none supplied, so a phone-only edit doesn't wipe it.
        var email = string.IsNullOrWhiteSpace(request.Email) ? employee.Email : request.Email.Trim();

        if (await _db.Employees.AnyAsync(e => e.Email == email && e.Id != id))
            return Conflict(new { error = "EmailAlreadyExists" });
        if (phone is not null && await _db.Employees.AnyAsync(e => e.PhoneNumber == phone && e.Id != id))
            return Conflict(new { error = "PhoneAlreadyExists" });

        // A token carries the role and never expires, and nothing re-checks it per request — so
        // changing either of these has to invalidate the sessions already issued. Without this a
        // demoted admin keeps the admin panel and a deactivated employee keeps scanning, both for as
        // long as they simply never log in again. Only bump when one of the two actually changed, so
        // an ordinary edit (name, position, hours) doesn't log the employee out for nothing.
        if (employee.Role != request.Role || employee.IsActive != request.IsActive)
            employee.TokenVersion++;

        employee.FullName = request.FullName;
        employee.Email = email;
        employee.PhoneNumber = phone;
        employee.FatherName = request.FatherName;
        employee.Position = request.Position;
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
        return Ok(new { deleted = id, forced = hasHistory && force });
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
        if (employee.ActivatedAtUtc is null)
            return Conflict(new { error = "NotActivated" });

        // Cryptographically random 4-digit PIN, zero-padded (0000–9999).
        var pin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
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
}
