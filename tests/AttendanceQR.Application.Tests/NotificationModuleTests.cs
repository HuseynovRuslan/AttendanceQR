using System.Security.Claims;
using System.Text.Json;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Api.Notifications;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Domain.Notifications;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The notification module: outbox event → recipients (settings × scope × branch pick) → center line, owed push, live
/// pop-up; the push sender's bursts and retries; lateness by the person's own shift; and the center/settings API.
///
/// Runs on <see cref="AccessWorld"/>: two companies, five branches, admins, managers of different branches, plain staff
/// and another company's manager — so who-may-receive-what is tested against the same people the permission tests use.
/// </summary>
public class NotificationModuleTests
{
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    private static readonly DateOnly Monday = new(2026, 11, 23);

    /// <summary>A Baku wall-clock moment on <see cref="Monday"/>, as UTC.</summary>
    private static DateTime At(int hour, int minute, int dayOffset = 0) =>
        new DateTime(Monday.Year, Monday.Month, Monday.Day, hour, minute, 0, DateTimeKind.Utc).AddDays(dayOffset).AddHours(-4);

    private static NotificationDispatcher Dispatcher(AccessWorld w) =>
        new(new AppOptions { TimeZone = "Asia/Baku", HiddenEmails = w.Options.HiddenEmails }, NullLogger.Instance);

    // --- settings, records and events in the world ------------------------------------------------------------

    private static void Prefs(
        AccessWorld w, Guid who, string[] types, Guid[]? branches = null,
        bool inApp = true, bool push = true, bool web = true, Guid? tenant = null)
    {
        using var db = w.Context(tenant ?? AccessWorld.TenantA);
        db.NotificationPreferences.Add(new NotificationPreference
        {
            EmployeeId = who,
            EnabledTypes = NotificationTypes.ToCsv(types),
            BranchMode = branches is null ? NotificationBranchMode.AllPermitted : NotificationBranchMode.Selected,
            InApp = inApp, Push = push, Web = web,
        });
        foreach (var b in branches ?? [])
            db.NotificationBranchPreferences.Add(new NotificationBranchPreference { EmployeeId = who, LocationId = b });
        db.SaveChanges();
    }

    private static AttendanceRecord CheckIn(
        AccessWorld w, Guid who, DateTime atUtc, DateOnly? day = null, bool offline = false, string kind = NotificationEventKinds.CheckIn,
        DateTime? eventAt = null, Guid? tenant = null)
    {
        using var db = w.Context(tenant ?? AccessWorld.TenantA);
        var home = db.Employees.AsNoTracking().Single(e => e.Id == who).LocationId;
        var record = new AttendanceRecord
        {
            EmployeeId = who, LocationId = home, AttendanceDate = day ?? Monday, CheckInAtUtc = atUtc,
            Status = AttendanceStatus.OnTime, WasOffline = offline,
        };
        db.AttendanceRecords.Add(record);
        db.NotificationEvents.Add(new NotificationEvent
        {
            Kind = kind, SubjectEmployeeId = who, AttendanceRecordId = record.Id, LocationId = home,
            OccurredAtUtc = eventAt ?? atUtc, WasOffline = offline, CreatedAtUtc = atUtc, NextAttemptUtc = atUtc,
        });
        db.SaveChanges();
        return record;
    }

    private static async Task<(DispatchOutcome Outcome, IReadOnlyList<LiveNotification> Live)> Dispatch(
        AccessWorld w, DateTime nowUtc, Guid? tenant = null, IInterceptor? interceptor = null)
    {
        using var db = w.Context(tenant ?? AccessWorld.TenantA, interceptor: interceptor);
        return await Dispatcher(w).ProcessAsync(db, nowUtc);
    }

    private static List<Notification> Lines(AccessWorld w, Guid? tenant = null) =>
        w.Context(tenant ?? AccessWorld.TenantA).Notifications.AsNoTracking().ToList();

    private static List<NotificationDelivery> Pushes(AccessWorld w) =>
        w.Context(AccessWorld.TenantA).NotificationDeliveries.AsNoTracking().ToList();

    private static void Shift(AccessWorld w, Guid branch, TimeOnly start, TimeOnly end, int grace = 15)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var l = db.Locations.Single(x => x.Id == branch);
        (l.ShiftStart, l.ShiftEnd, l.LateThresholdMinutes) = (start, end, grace);
        db.SaveChanges();
    }

    // --- who receives ------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_admin_who_switched_check_ins_on_gets_the_line_the_push_and_the_live_pop_up()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        CheckIn(w, w.E1, At(9, 2));

        var (outcome, live) = await Dispatch(w, At(9, 2));

        var line = Assert.Single(Lines(w));
        Assert.Equal(w.Admin1, line.RecipientEmployeeId);
        Assert.Equal(NotificationTypes.CheckIn, line.TypeKey);
        Assert.Equal("İşçi giriş etdi", line.Title);
        Assert.Equal($"{w.Context(AccessWorld.TenantA).Employees.Single(e => e.Id == w.E1).FullName} işə giriş etdi.", line.Body);
        Assert.Equal("B1", line.BranchName);
        Assert.Equal(w.B1, line.LocationId);
        Assert.Equal($"/admin/employees/{w.E1}", line.Url);
        Assert.Null(line.ReadAtUtc);

        var push = Assert.Single(Pushes(w));
        Assert.Equal(line.Id, push.NotificationId);
        Assert.EndsWith("B1 · 09:02", push.Body);
        Assert.Equal(NotificationDeliveryStatus.Pending, push.Status);

        Assert.Equal(w.Admin1, Assert.Single(live).RecipientId);
        Assert.Equal(1, outcome.Events);
        Assert.All(w.Context(AccessWorld.TenantA).NotificationEvents.AsNoTracking(), e => Assert.Equal(NotificationEventStatus.Done, e.Status));
    }

    [Fact]
    public async Task Nothing_is_sent_to_anybody_who_did_not_switch_the_type_on()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, []);                                    // a row with everything off
        Prefs(w, w.Admin2, [NotificationTypes.CheckOut]);          // another type only
        CheckIn(w, w.E1, At(9, 2));

        await Dispatch(w, At(9, 2));

        Assert.Empty(Lines(w));
        Assert.Empty(Pushes(w));
    }

    [Fact]
    public async Task A_branch_manager_hears_only_about_the_branches_they_manage()
    {
        using var w = new AccessWorld();
        Prefs(w, w.M2, [NotificationTypes.CheckIn]);               // manages B2
        CheckIn(w, w.E1, At(9, 0));                                // home B1
        CheckIn(w, w.E2, At(9, 1));                                // home B2

        await Dispatch(w, At(9, 2));

        var line = Assert.Single(Lines(w));
        Assert.Equal(w.M2, line.RecipientEmployeeId);
        Assert.Equal(w.E2, line.SubjectEmployeeId);
    }

    [Fact]
    public async Task A_plain_employee_with_a_settings_row_still_receives_nothing_about_colleagues()
    {
        using var w = new AccessWorld();
        Prefs(w, w.E3, [NotificationTypes.CheckIn, NotificationTypes.CheckOut, NotificationTypes.Late]);
        CheckIn(w, w.E1, At(9, 2));

        await Dispatch(w, At(9, 2));

        Assert.Empty(Lines(w));
    }

    [Fact]
    public async Task Only_the_picked_branches_when_the_person_chose_some()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn], branches: [w.B2]);
        CheckIn(w, w.E1, At(9, 0));
        CheckIn(w, w.E2, At(9, 1));

        await Dispatch(w, At(9, 2));

        Assert.Equal(w.E2, Assert.Single(Lines(w)).SubjectEmployeeId);
    }

    [Fact]
    public async Task A_deactivated_account_or_a_branch_taken_away_stops_the_notifications_at_once()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        Prefs(w, w.M1, [NotificationTypes.CheckIn]);               // manages B1, B2
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.Employees.Single(e => e.Id == w.Admin1).IsActive = false;
            db.ManagedLocations.RemoveRange(db.ManagedLocations.Where(m => m.EmployeeId == w.M1 && m.LocationId == w.B1));
            db.SaveChanges();
        }
        CheckIn(w, w.E1, At(9, 2));                                // B1

        await Dispatch(w, At(9, 2));

        Assert.Empty(Lines(w));
    }

    [Fact]
    public async Task Nobody_is_told_about_their_own_check_in_nor_about_an_account_the_board_does_not_track()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        Prefs(w, w.Admin2, [NotificationTypes.CheckIn]);
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.Employees.Single(e => e.Id == w.M4).PanelOnly = true;
            db.SaveChanges();
        }
        CheckIn(w, w.Admin1, At(9, 0));                            // their own
        CheckIn(w, w.M4, At(9, 1));                                // a panel-only account

        await Dispatch(w, At(9, 2));

        var line = Assert.Single(Lines(w));                        // Admin2 hears about Admin1, nobody about M4
        Assert.Equal((w.Admin2, w.Admin1), (line.RecipientEmployeeId, line.SubjectEmployeeId!.Value));
    }

    [Fact]
    public async Task Another_company_never_hears_about_this_one_whatever_its_settings()
    {
        using var w = new AccessWorld();
        Prefs(w, w.OtherCompanyManager, [NotificationTypes.CheckIn], tenant: AccessWorld.TenantB);
        CheckIn(w, w.E1, At(9, 2));

        await Dispatch(w, At(9, 2));                               // company A's pass
        await Dispatch(w, At(9, 2), AccessWorld.TenantB);          // company B's pass sees no A event

        Assert.Empty(Lines(w));
        Assert.Empty(Lines(w, AccessWorld.TenantB));
        Assert.Equal(NotificationEventStatus.Done, w.Context(AccessWorld.TenantA).NotificationEvents.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task In_a_company_on_the_permission_model_a_timekeeper_hears_about_the_branch_they_hold()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        using (var db = w.Context(AccessWorld.TenantA))
        {
            var role = db.AccessRoles.Single(r => r.TemplateKey == AccessRoleTemplates.Timekeeper);
            db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = AccessWorld.TenantA, EmployeeId = w.E3, AccessRoleId = role.Id, Scope = AccessScopeType.Branch,
                LocationId = w.B1, CreatedByEmployeeId = w.Admin1,
            });
            db.SaveChanges();
        }
        Prefs(w, w.E3, [NotificationTypes.CheckIn]);               // a plain employee by role, a timekeeper at B1
        CheckIn(w, w.E1, At(9, 0));                                // B1
        CheckIn(w, w.E2, At(9, 1));                                // B2

        await Dispatch(w, At(9, 2));                               // Legacy: the role decides — nothing
        Assert.Empty(Lines(w));

        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.Tenants.Single(t => t.Id == AccessWorld.TenantA).AuthzMode = AuthzMode.Enforced;
            foreach (var e in db.NotificationEvents)
                (e.Status, e.NextAttemptUtc) = (NotificationEventStatus.Pending, At(9, 0));
            db.SaveChanges();
        }
        await Dispatch(w, At(9, 3));                               // Enforced: the grant decides — B1 only

        Assert.Equal(w.E1, Assert.Single(Lines(w)).SubjectEmployeeId);
    }

    // --- duplicates, offline, failures ---------------------------------------------------------------------------

    [Fact]
    public async Task An_event_processed_twice_after_a_crash_still_makes_one_line_per_person()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        CheckIn(w, w.E1, At(9, 2));
        await Dispatch(w, At(9, 2));

        using (var db = w.Context(AccessWorld.TenantA))
        {
            // As if the pass died after writing the lines but before its events were marked done.
            var e = db.NotificationEvents.Single();
            (e.Status, e.NextAttemptUtc) = (NotificationEventStatus.Pending, At(9, 0));
            db.SaveChanges();
        }
        await Dispatch(w, At(9, 5));

        Assert.Single(Lines(w));
        Assert.Single(Pushes(w));
    }

    [Fact]
    public async Task An_offline_scan_synced_late_keeps_its_own_time_and_goes_to_the_center_without_a_push()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        CheckIn(w, w.E1, At(9, 2), offline: true);

        await Dispatch(w, At(12, 30));                             // synced three and a half hours later

        var line = Assert.Single(Lines(w));
        Assert.True(line.WasOffline);
        Assert.Equal(At(9, 2), line.OccurredAtUtc);
        Assert.Empty(Pushes(w));
    }

    [Fact]
    public async Task A_check_out_is_its_own_type_and_a_check_out_undone_since_is_not_announced()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn, NotificationTypes.CheckOut]);
        var done = CheckIn(w, w.E1, At(9, 0));
        var undone = CheckIn(w, w.E2, At(9, 0));
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.AttendanceRecords.Single(r => r.Id == done.Id).CheckOutAtUtc = At(18, 4);
            db.NotificationEvents.Add(new NotificationEvent
            {
                Kind = NotificationEventKinds.CheckOut, SubjectEmployeeId = w.E1, AttendanceRecordId = done.Id,
                LocationId = w.B1, OccurredAtUtc = At(18, 4), CreatedAtUtc = At(18, 4), NextAttemptUtc = At(18, 4),
            });
            db.NotificationEvents.Add(new NotificationEvent
            {
                Kind = NotificationEventKinds.CheckOut, SubjectEmployeeId = w.E2, AttendanceRecordId = undone.Id,
                LocationId = w.B2, OccurredAtUtc = At(18, 5), CreatedAtUtc = At(18, 5), NextAttemptUtc = At(18, 5),
            });
            db.SaveChanges();
        }

        await Dispatch(w, At(18, 6));

        var outLine = Assert.Single(Lines(w), l => l.TypeKey == NotificationTypes.CheckOut);
        Assert.Equal(w.E1, outLine.SubjectEmployeeId);
        Assert.Equal("İşçi çıxış etdi", outLine.Title);
        Assert.EndsWith("işdən çıxış etdi.", outLine.Body);
    }

    [Fact]
    public async Task A_voided_check_in_is_not_announced()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        var r = CheckIn(w, w.E1, At(9, 2));
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.AttendanceRecords.Single(x => x.Id == r.Id).VoidedAtUtc = At(9, 3);
            db.SaveChanges();
        }

        await Dispatch(w, At(9, 4));

        Assert.Empty(Lines(w));
    }

    [Fact]
    public async Task A_failing_pass_puts_its_events_back_with_a_delay_and_gives_up_after_the_last_attempt()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        CheckIn(w, w.E1, At(9, 2));
        var broken = new FailNotificationSave();

        await Dispatch(w, At(9, 2), interceptor: broken);

        var e = w.Context(AccessWorld.TenantA).NotificationEvents.AsNoTracking().Single();
        Assert.Equal(NotificationEventStatus.Pending, e.Status);
        Assert.Equal(1, e.Attempts);
        Assert.True(e.NextAttemptUtc > At(9, 2));
        Assert.Contains("simulated", e.LastError);
        Assert.Empty(Lines(w));

        // Each retry once its delay has run out — an hour apart is past every backoff step.
        for (var i = 1; i <= NotificationDispatcher.MaxAttempts; i++)
            await Dispatch(w, At(9, 2).AddHours(i), interceptor: broken);
        Assert.Equal(NotificationEventStatus.Failed, w.Context(AccessWorld.TenantA).NotificationEvents.AsNoTracking().Single().Status);
    }

    // --- lateness by the person's own shift -----------------------------------------------------------------------

    [Fact]
    public async Task A_late_arrival_is_its_own_type_counted_from_the_shift_start_and_only_past_the_grace()
    {
        using var w = new AccessWorld();
        Shift(w, w.B1, new TimeOnly(9, 0), new TimeOnly(18, 0), grace: 15);
        Prefs(w, w.Admin2, [NotificationTypes.Late]);
        CheckIn(w, w.E1, At(9, 25));                               // 25 minutes, past the 15 of grace
        CheckIn(w, w.M1, At(9, 10));                               // inside the grace (B1 too)

        await Dispatch(w, At(9, 30));

        var line = Assert.Single(Lines(w));
        Assert.Equal(NotificationTypes.Late, line.TypeKey);
        Assert.Equal(25, line.Minutes);
        Assert.Equal("İşçi gecikdi", line.Title);
        Assert.EndsWith("işə 25 dəqiqə gecikdi.", line.Body);
    }

    [Fact]
    public async Task No_late_arrival_on_a_flexible_schedule_a_holiday_or_a_leave_day()
    {
        using var w = new AccessWorld();
        Shift(w, w.B1, new TimeOnly(9, 0), new TimeOnly(18, 0));
        Shift(w, w.B2, new TimeOnly(9, 0), new TimeOnly(18, 0));
        Shift(w, w.B3, new TimeOnly(9, 0), new TimeOnly(18, 0));
        using (var db = w.Context(AccessWorld.TenantA))
        {
            var flexible = new Schedule
            {
                TenantId = AccessWorld.TenantA, Name = "Elastik", ShiftStart = new TimeOnly(0, 0), ShiftEnd = new TimeOnly(23, 59),
                IsFlexible = true, WorkDaysMask = 127,
            };
            db.Schedules.Add(flexible);
            db.Employees.Single(e => e.Id == w.E1).ScheduleId = flexible.Id;
            db.NonWorkingDays.Add(new NonWorkingDay { TenantId = AccessWorld.TenantA, Date = Monday, Description = "Bayram", LocationId = w.B2 });
            db.LeaveRecords.Add(new LeaveRecord
            {
                TenantId = AccessWorld.TenantA, EmployeeId = w.E3, FromDate = Monday, ToDate = Monday, Type = LeaveType.Vacation,
                CreatedByEmployeeId = w.Admin1, CreatedAtUtc = At(8, 0),
            });
            db.SaveChanges();
        }
        Prefs(w, w.Admin1, [NotificationTypes.Late]);
        CheckIn(w, w.E1, At(11, 0));                               // flexible
        CheckIn(w, w.E2, At(11, 0));                               // B2's holiday
        CheckIn(w, w.E3, At(11, 0));                               // on leave

        await Dispatch(w, At(11, 1));

        Assert.Empty(Lines(w));
    }

    [Fact]
    public void Lateness_is_judged_by_the_persons_own_shift_window()
    {
        var day = new EffectiveShift(new TimeOnly(9, 0), new TimeOnly(18, 0), 15, 127, null, 1, null, "Gündüz");
        Assert.Null(NotificationLateness.MinutesLate(day, Monday, At(9, 15), Baku, dayOff: false, earlierStretch: false));
        Assert.Equal(16, NotificationLateness.MinutesLate(day, Monday, At(9, 16), Baku, dayOff: false, earlierStretch: false));
        Assert.Null(NotificationLateness.MinutesLate(day, Monday, At(9, 40), Baku, dayOff: true, earlierStretch: false));
        // Back after a field visit: a second stretch of a one-window day is not an arrival.
        Assert.Null(NotificationLateness.MinutesLate(day, Monday, At(14, 0), Baku, dayOff: false, earlierStretch: true));

        // Not a working day by the person's own week (Monday off).
        var offMonday = day with { WorkDaysMask = 127 & ~(1 << (int)DayOfWeek.Monday) };
        Assert.Null(NotificationLateness.MinutesLate(offMonday, Monday, At(9, 40), Baku, dayOff: false, earlierStretch: false));

        // A split day: the second block is judged against its own start, not the morning's.
        var split = day with { End = new TimeOnly(12, 0), SecondStart = new TimeOnly(14, 0), SecondEnd = new TimeOnly(18, 0) };
        Assert.Equal(30, NotificationLateness.MinutesLate(split, Monday, At(14, 30), Baku, dayOff: false, earlierStretch: true));
        Assert.Null(NotificationLateness.MinutesLate(split, Monday, At(14, 10), Baku, dayOff: false, earlierStretch: true));

        // A night shift filed under the evening it began: an arrival after midnight is late against 22:00.
        var night = day with { Start = new TimeOnly(22, 0), End = new TimeOnly(6, 0) };
        Assert.Equal(150, NotificationLateness.MinutesLate(night, Monday, At(0, 30, dayOffset: 1), Baku, dayOff: false, earlierStretch: false));

        // Flexible: never.
        var flexible = day with { IsFlexible = true };
        Assert.Null(NotificationLateness.MinutesLate(flexible, Monday, At(13, 0), Baku, dayOff: false, earlierStretch: false));
    }

    // --- the push: bursts, throttle, retries ---------------------------------------------------------------------

    private static async Task<int> DeliverAsync(AccessWorld w, IPushNotifier notifier, DateTime nowUtc, bool configured = true)
    {
        using var db = w.Context(AccessWorld.TenantA);
        return await new NotificationPush(NullLogger.Instance).DeliverAsync(db, () => notifier, configured, nowUtc);
    }

    /// <summary>Owed pushes, each about a different person unless <paramref name="about"/> names one.</summary>
    private static void Owed(
        AccessWorld w, Guid to, string type, DateTime atUtc, int count = 1, Guid? about = null, string? title = null, string? body = null)
    {
        using var db = w.Context(AccessWorld.TenantA);
        for (var i = 0; i < count; i++)
            db.NotificationDeliveries.Add(new NotificationDelivery
            {
                RecipientEmployeeId = to, TypeKey = type, Title = title ?? "İşçi giriş etdi", Body = body ?? $"X işə giriş etdi.\nB1 · 09:0{i}",
                Url = $"/admin/employees/{about ?? Guid.NewGuid()}", NextAttemptUtc = atUtc, CreatedAtUtc = atUtc.AddSeconds(i),
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task One_late_arrival_is_pushed_as_its_own_sentence_not_as_a_count()
    {
        using var w = new AccessWorld();
        var elvin = Guid.NewGuid();
        Owed(w, w.M1, NotificationTypes.CheckIn, At(9, 25), about: elvin, body: "Əliyev Elvin işə giriş etdi.\nB1 · 09:25");
        Owed(w, w.M1, NotificationTypes.Late, At(9, 25), about: elvin, title: "İşçi gecikdi", body: "Əliyev Elvin işə 25 dəqiqə gecikdi.\nB1 · 09:25");
        var push = new FakePush();

        await DeliverAsync(w, push, At(9, 26));

        var sent = Assert.Single(push.Sent);
        Assert.Equal(("İşçi gecikdi", "Əliyev Elvin işə 25 dəqiqə gecikdi.\nB1 · 09:25", (string?)$"/admin/employees/{elvin}"), sent);
        Assert.All(Pushes(w), d => Assert.Equal(NotificationDeliveryStatus.Digested, d.Status));
    }

    [Fact]
    public async Task One_owed_push_goes_as_it_is()
    {
        using var w = new AccessWorld();
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0));
        var push = new FakePush();

        Assert.Equal(1, await DeliverAsync(w, push, At(9, 0)));

        var sent = Assert.Single(push.Sent);
        Assert.Equal(("İşçi giriş etdi", "X işə giriş etdi.\nB1 · 09:00"), (sent.Title, sent.Body));
        Assert.Equal(NotificationDeliveryStatus.Sent, Assert.Single(Pushes(w)).Status);
    }

    [Fact]
    public async Task A_burst_for_one_person_is_one_summary_push_and_the_next_waits_a_minute()
    {
        using var w = new AccessWorld();
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0), count: 2);
        Owed(w, w.Admin1, NotificationTypes.Late, At(9, 0));
        var push = new FakePush();

        await DeliverAsync(w, push, At(9, 0));

        var summary = Assert.Single(push.Sent);
        Assert.Equal("3 yeni bildiriş: 2 giriş, 1 gecikmə", summary.Body);
        Assert.Equal("/admin/notifications", summary.Url);
        Assert.All(Pushes(w), d => Assert.Equal(NotificationDeliveryStatus.Digested, d.Status));

        // Thirty seconds later another arrival: held until the minute is up, then sent.
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0).AddSeconds(30));
        await DeliverAsync(w, push, At(9, 0).AddSeconds(30));
        Assert.Single(push.Sent);
        await DeliverAsync(w, push, At(9, 1).AddSeconds(1));
        Assert.Equal(2, push.Sent.Count);
    }

    [Fact]
    public async Task No_device_is_recorded_and_not_retried_a_failing_send_is_retried_then_given_up()
    {
        using var w = new AccessWorld();
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0));
        await DeliverAsync(w, new FakePush { Reached = 0 }, At(9, 0));
        Assert.Equal(NotificationDeliveryStatus.NoDevice, Assert.Single(Pushes(w)).Status);

        Owed(w, w.Admin2, NotificationTypes.CheckIn, At(9, 0));
        var broken = new FakePush { Throws = true };
        await DeliverAsync(w, broken, At(9, 0));
        var waiting = Pushes(w).Single(d => d.RecipientEmployeeId == w.Admin2);
        Assert.Equal((NotificationDeliveryStatus.Pending, 1), (waiting.Status, waiting.Attempts));
        Assert.True(waiting.NextAttemptUtc > At(9, 0));

        var later = At(9, 0);
        for (var i = 1; i < NotificationPush.MaxAttempts; i++)
        {
            later = later.AddHours(1);
            await DeliverAsync(w, broken, later);
        }
        Assert.Equal(NotificationDeliveryStatus.Failed, Pushes(w).Single(d => d.RecipientEmployeeId == w.Admin2).Status);
    }

    [Fact]
    public async Task No_push_to_an_account_deactivated_since_nor_on_a_server_without_push()
    {
        using var w = new AccessWorld();
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0));
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.Employees.Single(e => e.Id == w.Admin1).IsActive = false;
            db.SaveChanges();
        }
        var push = new FakePush();
        await DeliverAsync(w, push, At(9, 0));
        Assert.Empty(push.Sent);
        Assert.Equal(NotificationDeliveryStatus.Skipped, Assert.Single(Pushes(w)).Status);

        Owed(w, w.Admin2, NotificationTypes.CheckIn, At(9, 0));
        await DeliverAsync(w, push, At(9, 0), configured: false);
        Assert.Empty(push.Sent);
        Assert.Equal(NotificationDeliveryStatus.Skipped, Pushes(w).Single(d => d.RecipientEmployeeId == w.Admin2).Status);
    }

    // --- the producer: the event rides in the check-in's own save -------------------------------------------------

    [Fact]
    public async Task The_outbox_writes_an_event_only_where_somebody_wants_one_and_hears_a_change_of_settings_at_once()
    {
        using var w = new AccessWorld();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var outbox = new NotificationOutbox(cache, NullLogger<NotificationOutbox>.Instance);

        using (var db = w.Context(AccessWorld.TenantA))
        {
            var record = new AttendanceRecord { EmployeeId = w.E1, LocationId = w.B1, AttendanceDate = Monday, CheckInAtUtc = At(9, 0) };
            db.AttendanceRecords.Add(record);
            Assert.Null(await outbox.StageAsync(db, NotificationEventKinds.CheckIn, record, At(9, 0)));
            db.SaveChanges();
        }
        Assert.Empty(w.Context(AccessWorld.TenantA).NotificationEvents);

        // Somebody switches check-ins on through the settings endpoint, which forgets the cached «nobody».
        var settings = Settings(w, w.Admin1, cache);
        Assert.IsType<OkObjectResult>(await settings.SavePreferences(Request([NotificationTypes.CheckIn])));

        using (var db = w.Context(AccessWorld.TenantA))
        {
            var record = new AttendanceRecord { EmployeeId = w.E2, LocationId = w.B2, AttendanceDate = Monday, CheckInAtUtc = At(9, 5) };
            db.AttendanceRecords.Add(record);
            var e = await outbox.StageAsync(db, NotificationEventKinds.CheckIn, record, At(9, 5));
            db.SaveChanges();                                       // ONE save: the record and its event together
            Assert.NotNull(e);
            Assert.Equal(record.Id, e!.AttendanceRecordId);
        }
        Assert.Single(w.Context(AccessWorld.TenantA).NotificationEvents);
    }

    // --- the settings and the center ------------------------------------------------------------------------------

    private static NotificationsController Settings(AccessWorld w, Guid who, IMemoryCache? cache = null) =>
        new(w.Context(AccessWorld.TenantA), cache ?? new MemoryCache(new MemoryCacheOptions()), new HubTickets(new MemoryCache(new MemoryCacheOptions())))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", who.ToString()), new Claim("role", w.RoleOf(who).ToString())], "test")),
                },
            },
        };

    private static NotificationPreferencesRequest Request(
        string[] types, string mode = "all", Guid[]? branches = null, bool inApp = true, bool push = true, bool web = true) =>
        new(NotificationTypes.CategoryAttendance, types, mode, branches, inApp, push, web);

    // camelCase, as the API answers.
    private static JsonElement Json(IActionResult r) =>
        JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<ObjectResult>(r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string? Error(IActionResult r) =>
        Json(r).TryGetProperty("error", out var e) ? e.GetString() : null;

    [Fact]
    public async Task Settings_start_off_and_a_plain_employee_cannot_subscribe_to_colleagues()
    {
        using var w = new AccessWorld();

        var view = Json(await Settings(w, w.E1).GetPreferences(null));
        Assert.False(view.GetProperty("eligible").GetBoolean());
        Assert.All(view.GetProperty("types").EnumerateArray(), t => Assert.False(t.GetProperty("enabled").GetBoolean()));
        Assert.Equal(0, view.GetProperty("branches").GetArrayLength());

        var refused = await Settings(w, w.E1).SavePreferences(Request([NotificationTypes.CheckIn]));
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<ObjectResult>(refused).StatusCode);
        Assert.Equal("NotEligible", Error(refused));
    }

    [Fact]
    public async Task A_manager_picks_only_among_their_own_branches_and_the_page_lists_only_those()
    {
        using var w = new AccessWorld();

        var view = Json(await Settings(w, w.M2).GetPreferences(null));     // manages B2
        Assert.True(view.GetProperty("eligible").GetBoolean());
        Assert.Equal([w.B2], view.GetProperty("branches").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()));

        Assert.Equal("BranchOutOfScope", Error(await Settings(w, w.M2).SavePreferences(Request([NotificationTypes.CheckIn], "selected", [w.B1]))));
        Assert.Equal("NoBranchSelected", Error(await Settings(w, w.M2).SavePreferences(Request([NotificationTypes.CheckIn], "selected", []))));
        Assert.Equal("NoChannel", Error(await Settings(w, w.M2).SavePreferences(Request([NotificationTypes.CheckIn], inApp: false, push: false, web: false))));
        Assert.Equal("UnknownType", Error(await Settings(w, w.M2).SavePreferences(Request(["attendance.everything"]))));

        var saved = Json(await Settings(w, w.M2).SavePreferences(Request([NotificationTypes.Late, NotificationTypes.CheckIn], "selected", [w.B2], push: false)));
        Assert.Equal("selected", saved.GetProperty("branchMode").GetString());
        Assert.Equal([w.B2], saved.GetProperty("locationIds").EnumerateArray().Select(x => x.GetGuid()));
        Assert.False(saved.GetProperty("channels").GetProperty("push").GetBoolean());
        var stored = w.Context(AccessWorld.TenantA).NotificationPreferences.AsNoTracking().Single(p => p.EmployeeId == w.M2);
        Assert.Equal("attendance.check_in,attendance.late", stored.EnabledTypes);   // catalogue order, each once
    }

    private static void Line(AccessWorld w, Guid to, Guid branch, DateTime createdUtc, string type = NotificationTypes.CheckIn, Guid? tenant = null)
    {
        using var db = w.Context(tenant ?? AccessWorld.TenantA);
        db.Notifications.Add(new Notification
        {
            RecipientEmployeeId = to, TypeKey = type, Title = "İşçi giriş etdi", Body = "X işə giriş etdi.", LocationId = branch,
            BranchName = "B", OccurredAtUtc = createdUtc, CreatedAtUtc = createdUtc,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task The_center_pages_newest_first_without_repeats_and_shows_a_branch_only_while_it_may_be_seen()
    {
        using var w = new AccessWorld();
        for (var i = 0; i < 30; i++)
            Line(w, w.M1, i % 2 == 0 ? w.B1 : w.B2, At(9, 0).AddMinutes(i));

        var first = Json(await Settings(w, w.M1).List(null, null, null, null, 25));
        var ids = first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(25, ids.Count);
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = Json(await Settings(w, w.M1).List(null, null, null, cursor, 25));
        var rest = second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(5, rest.Count);
        Assert.Empty(ids.Intersect(rest));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(30, Json(await Settings(w, w.M1).UnreadCount()).GetProperty("unread").GetInt32());

        // B1 is taken away from M1: its history goes with it.
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.ManagedLocations.RemoveRange(db.ManagedLocations.Where(m => m.EmployeeId == w.M1 && m.LocationId == w.B1));
            db.SaveChanges();
        }
        Assert.Equal(15, Json(await Settings(w, w.M1).UnreadCount()).GetProperty("unread").GetInt32());
        var visible = Json(await Settings(w, w.M1).List(null, null, null, null, 50)).GetProperty("items").EnumerateArray();
        Assert.All(visible, x => Assert.Equal(w.B2, x.GetProperty("locationId").GetGuid()));
    }

    [Fact]
    public async Task Read_marks_are_ones_own_and_read_all_reads_every_visible_line()
    {
        using var w = new AccessWorld();
        Line(w, w.Admin1, w.B1, At(9, 0));
        Line(w, w.Admin1, w.B2, At(9, 1), NotificationTypes.Late);
        Line(w, w.Admin2, w.B1, At(9, 2));
        var others = w.Context(AccessWorld.TenantA).Notifications.AsNoTracking().Single(n => n.RecipientEmployeeId == w.Admin2).Id;

        Assert.IsType<NotFoundObjectResult>(await Settings(w, w.Admin1).MarkRead(others));

        var late = Json(await Settings(w, w.Admin1).List(NotificationTypes.Late, null, null, null, null)).GetProperty("items");
        Assert.Equal(1, late.GetArrayLength());

        Assert.Equal(2, Json(await Settings(w, w.Admin1).MarkAllRead(null)).GetProperty("marked").GetInt32());
        Assert.Equal(0, Json(await Settings(w, w.Admin1).UnreadCount()).GetProperty("unread").GetInt32());
        Assert.Equal(1, Json(await Settings(w, w.Admin2).UnreadCount()).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task A_line_from_another_company_is_never_listed()
    {
        using var w = new AccessWorld();
        Line(w, w.OtherCompanyManager, w.OtherBranch, At(9, 0), tenant: AccessWorld.TenantB);
        Line(w, w.Admin1, w.B1, At(9, 0));

        // Admin1 sees all of company A — and nothing of B, whose rows the tenant filter never returns.
        var items = Json(await Settings(w, w.Admin1).List(null, null, null, null, 50)).GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
    }

    [Fact]
    public void A_live_ticket_names_its_one_person_and_company_and_nothing_else_opens_the_hub()
    {
        var tickets = new HubTickets(new MemoryCache(new MemoryCacheOptions()));
        var person = Guid.NewGuid();
        var ticket = tickets.Issue(AccessWorld.TenantA, person);

        Assert.True(tickets.TryRead(ticket, out var tenant, out var who));
        Assert.Equal((AccessWorld.TenantA, person), (tenant, who));
        Assert.False(tickets.TryRead("not-a-ticket", out _, out _));
        Assert.False(tickets.TryRead(null, out _, out _));
        Assert.NotEqual(NotificationsHub.Group(AccessWorld.TenantA, person), NotificationsHub.Group(AccessWorld.TenantB, person));
    }

    // --- audit 27.11: each fix pinned by a test that failed before it ----------------------------------------------

    [Fact]
    public async Task A_manager_owed_one_push_is_not_kept_waiting_behind_an_admins_thousands()
    {
        using var w = new AccessWorld();
        // The admin's 2 500 rows are all older than the manager's one: a batch of the oldest rows never reached it.
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0), count: 2500);
        Owed(w, w.M1, NotificationTypes.Late, At(9, 50));
        var push = new FakePush();

        await DeliverAsync(w, push, At(10, 0));

        Assert.Equal(2, push.Sent.Count);
        Assert.Contains(push.Sent, p => p.Body == "2500 yeni bildiriş: 2500 giriş");
        Assert.Contains(push.Sent, p => p.Title == "İşçi giriş etdi");
        Assert.DoesNotContain(Pushes(w), d => d.Status == NotificationDeliveryStatus.Pending);
    }

    [Fact]
    public async Task A_pass_stopped_midway_keeps_what_it_already_sent_so_nothing_is_pushed_twice()
    {
        using var w = new AccessWorld();
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0));
        Owed(w, w.Admin2, NotificationTypes.CheckIn, At(9, 1));
        using var stop = new CancellationTokenSource();
        var push = new FakePush { CancelOnCall = 2, Cancel = stop };

        using (var db = w.Context(AccessWorld.TenantA))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new NotificationPush(NullLogger.Instance).DeliverAsync(db, () => push, true, At(9, 2), stop.Token));

        Assert.Equal(NotificationDeliveryStatus.Sent, Pushes(w).Single(d => d.RecipientEmployeeId == w.Admin1).Status);
        // The next pass sends the one that was cut short — and only that one.
        var again = new FakePush();
        await DeliverAsync(w, again, At(9, 3));
        Assert.Single(again.Sent);
        Assert.All(Pushes(w), d => Assert.Equal(NotificationDeliveryStatus.Sent, d.Status));
    }

    [Fact]
    public async Task A_slow_push_provider_ends_the_pass_at_its_budget_and_the_rest_wait_for_the_next()
    {
        using var w = new AccessWorld();
        Owed(w, w.Admin1, NotificationTypes.CheckIn, At(9, 0));
        Owed(w, w.Admin2, NotificationTypes.CheckIn, At(9, 1));
        var clock = new SteppingClock();
        var push = new FakePush { OnSend = () => clock.Advance(NotificationPush.PassBudget + TimeSpan.FromSeconds(1)) };

        using (var db = w.Context(AccessWorld.TenantA))
            await new NotificationPush(NullLogger.Instance, clock).DeliverAsync(db, () => push, true, At(9, 2));

        Assert.Single(push.Sent);
        Assert.Equal(NotificationDeliveryStatus.Pending, Pushes(w).Single(d => d.RecipientEmployeeId == w.Admin2).Status);
    }

    [Fact]
    public async Task An_event_whose_passes_keep_dying_is_given_up_instead_of_being_leased_for_ever()
    {
        using var w = new AccessWorld();
        Prefs(w, w.Admin1, [NotificationTypes.CheckIn]);
        CheckIn(w, w.E1, At(9, 2));
        CheckIn(w, w.E2, At(9, 3));
        using (var db = w.Context(AccessWorld.TenantA))
        {
            // E1's event: leased MaxAttempts times, never finished (the process died each time).
            var poisoned = db.NotificationEvents.Single(e => e.SubjectEmployeeId == w.E1);
            poisoned.Attempts = NotificationDispatcher.MaxAttempts;
            db.SaveChanges();
        }

        var (outcome, _) = await Dispatch(w, At(9, 5));

        Assert.Equal((1, 1), (outcome.Events, outcome.Failed));
        var events = w.Context(AccessWorld.TenantA).NotificationEvents.AsNoTracking().ToList();
        Assert.Equal(NotificationEventStatus.Failed, events.Single(e => e.SubjectEmployeeId == w.E1).Status);
        Assert.Equal(NotificationEventStatus.Done, events.Single(e => e.SubjectEmployeeId == w.E2).Status);
        Assert.Equal(w.E2, Assert.Single(Lines(w)).SubjectEmployeeId);
    }

    [Fact]
    public async Task Lateness_is_judged_by_the_hours_of_the_branch_the_scan_was_made_at_like_the_scan_itself()
    {
        using var w = new AccessWorld();
        Shift(w, w.B1, new TimeOnly(9, 0), new TimeOnly(18, 0), grace: 15);    // E1's home
        Shift(w, w.B2, new TimeOnly(10, 0), new TimeOnly(19, 0), grace: 15);   // where E1 worked today
        Prefs(w, w.Admin1, [NotificationTypes.Late]);
        using (var db = w.Context(AccessWorld.TenantA))
        {
            // 09:40 at B2: on time by B2's 10:00 (what the scan decided), 40 minutes late by home's 09:00.
            var record = new AttendanceRecord
            {
                EmployeeId = w.E1, LocationId = w.B2, AttendanceDate = Monday, CheckInAtUtc = At(9, 40), Status = AttendanceStatus.OnTime,
            };
            db.AttendanceRecords.Add(record);
            db.NotificationEvents.Add(new NotificationEvent
            {
                Kind = NotificationEventKinds.CheckIn, SubjectEmployeeId = w.E1, AttendanceRecordId = record.Id, LocationId = w.B2,
                OccurredAtUtc = At(9, 40), CreatedAtUtc = At(9, 40), NextAttemptUtc = At(9, 40),
            });
            db.SaveChanges();
        }

        await Dispatch(w, At(9, 41));

        Assert.Empty(Lines(w));
    }

    [Fact]
    public async Task The_badge_count_stops_at_what_it_can_show()
    {
        using var w = new AccessWorld();
        for (var i = 0; i < 150; i++)
            Line(w, w.Admin1, w.B1, At(9, 0).AddSeconds(i));

        var answer = Json(await Settings(w, w.Admin1).UnreadCount());

        Assert.Equal(99, answer.GetProperty("unread").GetInt32());
        Assert.True(answer.GetProperty("more").GetBoolean());
        Assert.False(Json(await Settings(w, w.M1).UnreadCount()).GetProperty("more").GetBoolean());
    }

    [Fact]
    public async Task Two_first_saves_of_the_settings_at_once_are_not_a_server_error()
    {
        using var w = new AccessWorld();
        var raced = new UniqueViolationOnce<NotificationPreference>();
        var c = new NotificationsController(
            w.Context(AccessWorld.TenantA, interceptor: raced), new MemoryCache(new MemoryCacheOptions()))
        {
            ControllerContext = Settings(w, w.Admin1).ControllerContext,
        };

        var result = Assert.IsAssignableFrom<ObjectResult>(await c.SavePreferences(Request([NotificationTypes.CheckIn])));

        Assert.True(result.StatusCode is null or 200);
        Assert.True(raced.Fired);
        Assert.Single(w.Context(AccessWorld.TenantA).NotificationPreferences.AsNoTracking());
    }

    [Fact]
    public void The_outbox_row_stays_inert_no_unique_index_and_no_reference_but_its_company()
    {
        using var w = new AccessWorld();
        var type = w.Context(AccessWorld.TenantA).Model.FindEntityType(typeof(NotificationEvent))!;

        // What lets it ride in a check-in's save without ever being the reason that save fails.
        Assert.DoesNotContain(type.GetIndexes(), i => i.IsUnique);
        Assert.All(type.GetForeignKeys(), fk => Assert.Equal(typeof(Tenant), fk.PrincipalEntityType.ClrType));
    }

    private sealed class SteppingClock : TimeProvider
    {
        private long _ticks = 1;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    /// <summary>The first save that adds a <typeparamref name="T"/> fails as Postgres fails a duplicate key.</summary>
    private sealed class UniqueViolationOnce<T> : SaveChangesInterceptor where T : class
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries<T>().Any(e => e.State == EntityState.Added))
            {
                Fired = true;
                throw new DbUpdateException("simulated race",
                    new Npgsql.PostgresException("duplicate key", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.UniqueViolation));
            }
            return ValueTask.FromResult(result);
        }
    }

    // --- fakes -----------------------------------------------------------------------------------------------------

    private sealed class FakePush : IPushNotifier
    {
        public int Reached { get; init; } = 1;
        public bool Throws { get; init; }
        /// <summary>On this call (1-based) the pass is stopped (a deploy's SIGTERM) before the send.</summary>
        public int CancelOnCall { get; init; }
        public CancellationTokenSource? Cancel { get; init; }
        public Action? OnSend { get; init; }
        public List<(string Title, string Body, string? Url)> Sent { get; } = [];
        private int _calls;

        public Task<int> NotifyEmployeesAsync(
            IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
        {
            if (++_calls == CancelOnCall && Cancel is not null)
            {
                Cancel.Cancel();
                throw new OperationCanceledException(Cancel.Token);
            }
            if (Throws)
                throw new HttpRequestException("simulated push endpoint down");
            Sent.Add((title, body, url));
            OnSend?.Invoke();
            return Task.FromResult(Reached);
        }
    }

    /// <summary>A database that refuses every save carrying a new notification line — the pass must roll back cleanly.</summary>
    private sealed class FailNotificationSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Notification>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated: the database is unavailable");
            return ValueTask.FromResult(result);
        }
    }
}
