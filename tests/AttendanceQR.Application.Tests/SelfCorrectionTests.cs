using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// One's own day (the owner, 2026-10-16): nobody corrects the day they are paid for — unless the company has
/// nobody else who could. Then it goes ahead in the open: a reason, never a deletion, the old times kept for good,
/// an audit event of its own, «öz düzəlişi» wherever the day shows, and — where the company asks — a second
/// approval first. Company A of AccessWorld has two admins; «alone» switches the second one off.
/// </summary>
public class SelfCorrectionTests
{
    private static readonly DateOnly Day = new(2026, 9, 14);
    private static DateTime At(int hour, int minute = 0) => new(2026, 9, 14, hour, minute, 0, DateTimeKind.Utc);

    private sealed class NoSink : IAuthzShadowSink
    {
        public Task RecordAsync(AppDbContext db, ShadowRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private static AppDbContext Db(AccessWorld w, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null) =>
        w.Context(AccessWorld.TenantA, new AuthzObserver(new NoSink(), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthzObserver>.Instance, new OperatorAllowlist(new HashSet<Guid> { w.Operator })), interceptor);

    private static ControllerContext As(Guid who, string role, bool impersonating = false)
    {
        var claims = new List<Claim> { new("sub", who.ToString()), new("role", role) };
        if (impersonating)
            claims.Add(new Claim("imp", "operator"));
        return new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
    }

    private static AdminAttendanceController Desk(AppDbContext db, Guid who, string role = "Admin", bool impersonating = false) =>
        new(db, new NoSummary(), new NoQueue(), new NoPhotos(), NullLogger<AdminAttendanceController>.Instance,
            new AppOptions { TimeZone = "UTC" })
        { ControllerContext = As(who, role, impersonating) };

    private static async Task<Guid> RecordAsync(AccessWorld w, Guid employee, DateTime? checkIn, DateTime? checkOut)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var home = db.Employees.Single(e => e.Id == employee).LocationId;
        var record = new AttendanceRecord
        {
            TenantId = AccessWorld.TenantA, EmployeeId = employee, LocationId = home, AttendanceDate = Day,
            CheckInAtUtc = checkIn, CheckOutAtUtc = checkOut, Status = AttendanceStatus.OnTime,
        };
        db.AttendanceRecords.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }

    /// <summary>Company A with one admin: the second is switched off.</summary>
    private static async Task AloneAsync(AccessWorld w)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Employees.Single(e => e.Id == w.Admin2).IsActive = false;
        await db.SaveChangesAsync();
    }

    private static string? ErrorOf(IActionResult r) =>
        r is ObjectResult { Value: { } v } ? v.GetType().GetProperty("error")?.GetValue(v) as string : null;

    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;

    [Fact]
    public async Task With_a_second_admin_one_still_may_not_correct_ones_own_day_and_is_told_who_can()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        var record = await RecordAsync(w, w.Admin1, At(9), At(18));

        using var db = Db(w);
        var result = await Desk(db, w.Admin1).Update(record, new AdminAttendanceUpdateRequest(null, At(19), "Gec çıxdım"));

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, refused.StatusCode);
        Assert.Equal("OwnAttendance", ErrorOf(result));
        // The world names everybody by the first eight characters of their id.
        Assert.Contains(w.Admin2.ToString()[..8], (IEnumerable<string>)Prop<object>(refused.Value!, "correctors"));
        Assert.Empty(w.Context(AccessWorld.TenantA).AttendanceSelfCorrections.ToList());
    }

    [Fact]
    public async Task The_only_admin_corrects_their_own_day_in_the_open()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var record = await RecordAsync(w, w.Admin1, At(9), At(18));

        using (var db = Db(w))
            Assert.IsType<OkObjectResult>(await Desk(db, w.Admin1).Update(record, new AdminAttendanceUpdateRequest(null, At(19, 30), "Gec çıxdım, telefon oturmuşdu")));

        using var check = w.Context(AccessWorld.TenantA);
        var day = check.AttendanceRecords.AsNoTracking().Single(r => r.Id == record);
        Assert.Equal(At(19, 30), day.CheckOutAtUtc);
        Assert.Equal(w.Admin1, day.ManualByEmployeeId);
        // The old times kept for good, with the reason.
        var ledger = Assert.Single(check.AttendanceSelfCorrections.AsNoTracking().ToList());
        Assert.Equal((w.Admin1, w.Admin1, SelfCorrection.Update), (ledger.EmployeeId, ledger.ActorEmployeeId, ledger.Kind));
        Assert.Equal((At(9), At(18)), (ledger.CheckInBeforeUtc!.Value, ledger.CheckOutBeforeUtc!.Value));
        Assert.Equal(At(19, 30), ledger.CheckOutAfterUtc);
        Assert.False(ledger.AwaitsApproval);
        Assert.Contains("telefon", ledger.Reason);
        // Its own audit event, not the everyday correction's.
        var audit = check.AuditLogs.AsNoTracking().Where(a => a.EmployeeId == w.Admin1).ToList();
        Assert.Contains(audit, a => a.EventType == AuditEventType.AttendanceSelfCorrected && a.Reason!.Contains("→"));
        Assert.DoesNotContain(audit, a => a.EventType == AuditEventType.RecordEditedByAdmin);
    }

    [Fact]
    public async Task The_only_admin_still_needs_a_reason_and_never_deletes_their_own_day()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var record = await RecordAsync(w, w.Admin1, At(9), At(18));

        using var db = Db(w);
        Assert.Equal(CorrectionReason.Required, ErrorOf(await Desk(db, w.Admin1).Update(record, new AdminAttendanceUpdateRequest(null, At(19), "  "))));
        Assert.Equal("OwnAttendance", ErrorOf(await Desk(db, w.Admin1).Delete(record, new AttendanceCorrectionRequest("Səhv gün"))));
        Assert.NotNull(w.Context(AccessWorld.TenantA).AttendanceRecords.AsNoTracking().SingleOrDefault(r => r.Id == record));
    }

    [Fact]
    public async Task A_support_session_never_corrects_the_borrowed_admins_day()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var record = await RecordAsync(w, w.Admin1, At(9), At(18));

        using var db = Db(w);
        Assert.Equal("OwnAttendance", ErrorOf(await Desk(db, w.Admin1, impersonating: true)
            .Update(record, new AdminAttendanceUpdateRequest(null, At(19), "Dəstək"))));
    }

    [Fact]
    public async Task A_manager_never_corrects_their_own_day_while_an_admin_can()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var record = await RecordAsync(w, w.M2, At(9), At(18));

        using var db = Db(w);
        var result = await Desk(db, w.M2, "Manager").Update(record, new AdminAttendanceUpdateRequest(null, At(19), "Gec çıxdım"));
        Assert.Equal("OwnAttendance", ErrorOf(result));
    }

    [Fact]
    public async Task A_second_approval_can_be_switched_on_only_where_somebody_can_give_it()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);

        using var db = Db(w);
        Assert.Equal("NoApprover", ErrorOf(await Desk(db, w.Admin1).SetSelfCorrectionSetting(new SelfCorrectionSettingRequest(true))));
        Assert.False(w.Context(AccessWorld.TenantA).Tenants.AsNoTracking().Single(t => t.Id == AccessWorld.TenantA).SelfCorrectionNeedsApproval);
    }

    /// <summary>An Enforced company with one admin and an owner who looks and approves (observer + the approval right).</summary>
    private static async Task<Guid> OwnerApproverAsync(AccessWorld w)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Tenants.Single(t => t.Id == AccessWorld.TenantA).AuthzMode = AuthzMode.Enforced;
        var approve = new AccessRole
        {
            TenantId = AccessWorld.TenantA, Name = "Təsdiqləyici", Description = "",
            Permissions = [AccessPermissions.AttendanceView, AccessPermissions.EmployeesView, AccessPermissions.AttendanceSelfCorrectionApprove],
        };
        db.AccessRoles.Add(approve);
        db.RoleAssignments.Add(new RoleAssignment
        {
            TenantId = AccessWorld.TenantA, EmployeeId = w.E5, AccessRoleId = approve.Id, Scope = AccessScopeType.Company,
            CreatedByEmployeeId = w.Admin1,
        });
        await db.SaveChangesAsync();
        return w.E5;
    }

    [Fact]
    public async Task With_a_second_approval_the_correction_waits_and_the_approver_applies_it_once()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var owner = await OwnerApproverAsync(w);
        var record = await RecordAsync(w, w.Admin1, At(9), At(18));

        using (var db = Db(w))
            Assert.IsType<OkObjectResult>(await Desk(db, w.Admin1).SetSelfCorrectionSetting(new SelfCorrectionSettingRequest(true)));
        using (var db = Db(w))
            Assert.IsType<AcceptedResult>(await Desk(db, w.Admin1).Update(record, new AdminAttendanceUpdateRequest(null, At(20), "Gec çıxdım")));

        // Nothing applied yet; the request is on the list, and the owner may decide it — the admin may not.
        Assert.Equal(At(18), w.Context(AccessWorld.TenantA).AttendanceRecords.AsNoTracking().Single(r => r.Id == record).CheckOutAtUtc);
        var waiting = w.Context(AccessWorld.TenantA).AttendanceSelfCorrections.AsNoTracking().Single(c => c.AwaitsApproval);
        using (var db = Db(w))
            Assert.Equal("OwnAttendance", ErrorOf(await Desk(db, w.Admin1).DecideSelfCorrection(waiting.Id, new SelfCorrectionDecisionRequest(true, "Özüm"))));
        using (var db = Db(w))
        {
            var list = Prop<IEnumerable<object>>(Assert.IsType<OkObjectResult>(await Desk(db, owner, "Employee").SelfCorrections()).Value!, "items").ToList();
            var item = Assert.Single(list);
            Assert.True(Prop<bool>(item, "awaitsApproval"));
            Assert.True(Prop<bool>(item, "mayDecide"));
        }

        using (var db = Db(w))
            Assert.IsType<OkObjectResult>(await Desk(db, owner, "Employee").DecideSelfCorrection(waiting.Id, new SelfCorrectionDecisionRequest(true, "Doğrudur, gördüm")));
        Assert.Equal(At(20), w.Context(AccessWorld.TenantA).AttendanceRecords.AsNoTracking().Single(r => r.Id == record).CheckOutAtUtc);
        using (var db = Db(w))
            Assert.Equal("AlreadyReviewed", ErrorOf(await Desk(db, owner, "Employee").DecideSelfCorrection(waiting.Id, new SelfCorrectionDecisionRequest(true, "Yenə"))));

        var audit = w.Context(AccessWorld.TenantA).AuditLogs.AsNoTracking().ToList();
        Assert.Contains(audit, a => a.EventType == AuditEventType.SelfCorrectionSettingChanged);
        Assert.Contains(audit, a => a.EventType == AuditEventType.SelfCorrectionDecided && a.EmployeeId == w.Admin1);
    }

    [Fact]
    public async Task An_approval_never_overwrites_a_day_that_changed_after_the_request()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var owner = await OwnerApproverAsync(w);
        var record = await RecordAsync(w, w.Admin1, At(9), null);

        using (var db = Db(w))
            Assert.IsType<OkObjectResult>(await Desk(db, w.Admin1).SetSelfCorrectionSetting(new SelfCorrectionSettingRequest(true)));
        using (var db = Db(w))
            Assert.IsType<AcceptedResult>(await Desk(db, w.Admin1).Update(record, new AdminAttendanceUpdateRequest(null, At(18), "Çıxışı unutdum")));
        // Meanwhile the day is closed by a scan.
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.AttendanceRecords.Single(r => r.Id == record).CheckOutAtUtc = At(17, 45);
            await db.SaveChangesAsync();
        }
        var waiting = w.Context(AccessWorld.TenantA).AttendanceSelfCorrections.AsNoTracking().Single(c => c.AwaitsApproval);
        using (var db = Db(w))
            Assert.Equal("RecordChanged", ErrorOf(await Desk(db, owner, "Employee").DecideSelfCorrection(waiting.Id, new SelfCorrectionDecisionRequest(true, "Təsdiq"))));
        Assert.Equal(At(17, 45), w.Context(AccessWorld.TenantA).AttendanceRecords.AsNoTracking().Single(r => r.Id == record).CheckOutAtUtc);
    }

    [Fact]
    public async Task The_day_says_öz_düzəlişi_on_the_board_the_tabel_and_the_payroll()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var record = await RecordAsync(w, w.Admin1, At(9), At(18));
        using (var db = Db(w))
            Assert.IsType<OkObjectResult>(await Desk(db, w.Admin1).Update(record, new AdminAttendanceUpdateRequest(null, At(19), "Gec çıxdım")));

        using var rdb = Db(w);
        var reports = new ReportQueryService(rdb, new AppOptions { TimeZone = "UTC" });
        var board = await reports.GetTodayAttendanceAsync(w.Admin1, EmployeeRole.Admin, Day);
        Assert.True(board.Single(r => r.EmployeeId == w.Admin1).SelfCorrected);
        Assert.False(board.Single(r => r.EmployeeId == w.E1).SelfCorrected);

        var (_, tabel) = await reports.GetTabelAsync(Day.Year, Day.Month, null, w.Admin1, EmployeeRole.Admin);
        Assert.Equal([Day.Day], tabel!.Rows.Single(r => r.EmployeeId == w.Admin1).SelfCorrectedDays);

        var (_, payroll) = await reports.GetPayrollAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, w.Admin1, EmployeeRole.Admin);
        var mine = payroll!.Rows.SingleOrDefault(r => r.EmployeeId == w.Admin1);
        if (mine is not null)
            Assert.Equal(1, mine.SelfCorrectedDays);
    }

    [Fact]
    public async Task The_only_admin_decides_their_own_missed_check_out_in_the_open()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var record = await RecordAsync(w, w.Admin1, At(9), null);
        Guid request;
        using (var db = w.Context(AccessWorld.TenantA))
        {
            var mc = new MissedCheckoutRequest
            {
                TenantId = AccessWorld.TenantA, EmployeeId = w.Admin1, AttendanceRecordId = record, AttendanceDate = Day,
                RequestedCheckOutAtUtc = At(18), Reason = "Unutdum",
            };
            db.MissedCheckoutRequests.Add(mc);
            await db.SaveChangesAsync();
            request = mc.Id;
        }

        using (var db = Db(w))
        {
            var desk = new AdminMissedCheckoutController(db, new NoSummary()) { ControllerContext = As(w.Admin1, "Admin") };
            Assert.IsType<OkObjectResult>(await desk.Approve(request, new AttendanceCorrectionRequest("Doğrudur")));
        }
        using var check = w.Context(AccessWorld.TenantA);
        Assert.Equal(At(18), check.AttendanceRecords.AsNoTracking().Single(r => r.Id == record).CheckOutAtUtc);
        Assert.Equal(SelfCorrection.MissedCheckout, check.AttendanceSelfCorrections.AsNoTracking().Single().Kind);
    }

    // ─────────────────────────── a day in two stretches ───────────────────────────
    //
    // A split shift's second block, a night after a morning, «Elastik qrafik» sessions: one day, two rows, the
    // earlier closed and the later open. Undoing the earlier one's check-out would leave the person checked in
    // twice, which IX_AttendanceRecords_OneOpenPerDay forbids — on Postgres the UPDATE failed with a 23505 that
    // nothing caught (a bare 500). The in-memory store enforces no index, so these pin the question asked first
    // and, with an interceptor standing in for the database, the 23505 answered as the same refusal.

    [Fact]
    public async Task Undoing_a_check_out_while_a_later_stretch_of_the_day_is_open_is_refused_with_the_reason()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        var morning = await RecordAsync(w, w.E1, At(7), At(11));
        var night = await RecordAsync(w, w.E1, At(22), null);

        using (var db = Db(w))
        {
            var refused = Assert.IsType<ConflictObjectResult>(
                await Desk(db, w.Admin1).ClearCheckOut(morning, new AttendanceCorrectionRequest("Səhv skan")));
            Assert.Equal("AnotherBlockOpen", ErrorOf(refused));
        }
        using (var check = w.Context(AccessWorld.TenantA))
        {
            Assert.Equal(At(11), check.AttendanceRecords.AsNoTracking().Single(r => r.Id == morning).CheckOutAtUtc);
            Assert.DoesNotContain(check.AuditLogs.AsNoTracking().ToList(), a => a.EventType == AuditEventType.RecordEditedByAdmin);
        }

        // Once the later stretch is closed, the same undo goes through.
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.AttendanceRecords.Single(r => r.Id == night).CheckOutAtUtc = At(23, 30);
            await db.SaveChangesAsync();
        }
        using (var db = Db(w))
            Assert.IsType<OkObjectResult>(await Desk(db, w.Admin1).ClearCheckOut(morning, new AttendanceCorrectionRequest("Səhv skan")));
        Assert.Null(w.Context(AccessWorld.TenantA).AttendanceRecords.AsNoTracking().Single(r => r.Id == morning).CheckOutAtUtc);
    }

    [Fact]
    public async Task A_later_stretch_opened_between_the_question_and_the_save_is_still_a_409_not_a_500()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        var morning = await RecordAsync(w, w.E1, At(7), At(11));

        // Nothing else is open when the controller asks; the "database" then refuses the save as Postgres
        // would if a scan had opened the later stretch in between.
        using var db = Db(w, new SecondOpenStretchRefused());
        var refused = Assert.IsType<ConflictObjectResult>(
            await Desk(db, w.Admin1).ClearCheckOut(morning, new AttendanceCorrectionRequest("Səhv skan")));
        Assert.Equal("AnotherBlockOpen", ErrorOf(refused));
    }

    [Fact]
    public async Task An_approved_undo_waits_while_a_later_stretch_is_open_and_the_request_stays_undecided()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await AloneAsync(w);
        var owner = await OwnerApproverAsync(w);
        var morning = await RecordAsync(w, w.Admin1, At(7), At(11));

        using (var db = Db(w))
            Assert.IsType<OkObjectResult>(await Desk(db, w.Admin1).SetSelfCorrectionSetting(new SelfCorrectionSettingRequest(true)));
        using (var db = Db(w))
            Assert.IsType<AcceptedResult>(await Desk(db, w.Admin1).ClearCheckOut(morning, new AttendanceCorrectionRequest("Səhv skan")));
        var waiting = w.Context(AccessWorld.TenantA).AttendanceSelfCorrections.AsNoTracking().Single(c => c.AwaitsApproval);

        // Meanwhile the night stretch opens.
        await RecordAsync(w, w.Admin1, At(22), null);

        using (var db = Db(w))
            Assert.Equal("AnotherBlockOpen", ErrorOf(await Desk(db, owner, "Employee")
                .DecideSelfCorrection(waiting.Id, new SelfCorrectionDecisionRequest(true, "Doğrudur"))));
        using (var check = w.Context(AccessWorld.TenantA))
        {
            Assert.Equal(At(11), check.AttendanceRecords.AsNoTracking().Single(r => r.Id == morning).CheckOutAtUtc);
            Assert.False(check.AttendanceSelfCorrections.AsNoTracking().Any(c => c.DecisionForId == waiting.Id));
        }

        // A new request is refused at once rather than filed to fail later.
        using (var db = Db(w))
            Assert.Equal("AnotherBlockOpen", ErrorOf(await Desk(db, w.Admin1).ClearCheckOut(morning, new AttendanceCorrectionRequest("Yenə"))));

        // The night closes; then the approval's own save is refused by the "database", as it would be if the
        // night had re-opened in between — the same 409, and still nothing decided.
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.AttendanceRecords.Single(r => r.EmployeeId == w.Admin1 && r.Id != morning).CheckOutAtUtc = At(23);
            await db.SaveChangesAsync();
        }
        using (var db = Db(w, new SecondOpenStretchRefused()))
            Assert.Equal("AnotherBlockOpen", ErrorOf(await Desk(db, owner, "Employee")
                .DecideSelfCorrection(waiting.Id, new SelfCorrectionDecisionRequest(true, "Doğrudur"))));
        Assert.False(w.Context(AccessWorld.TenantA).AttendanceSelfCorrections.AsNoTracking().Any(c => c.DecisionForId == waiting.Id));
    }

    /// <summary>Postgres' answer to a day with two open stretches, which the in-memory store would never give:
    /// any save that re-opens a record fails with the 23505 of IX_AttendanceRecords_OneOpenPerDay.</summary>
    private sealed class SecondOpenStretchRefused : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AttendanceRecord>()
                .Any(e => e.State == EntityState.Modified && e.Entity.CheckOutAtUtc == null))
                throw new DbUpdateException("simulated: a second open stretch",
                    new Npgsql.PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR",
                        Npgsql.PostgresErrorCodes.UniqueViolation, constraintName: "IX_AttendanceRecords_OneOpenPerDay"));
            return ValueTask.FromResult(result);
        }
    }

    // ─────────────────────────── stubs ───────────────────────────

    private sealed class NoSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

    private sealed class NoPhotos : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }
}
