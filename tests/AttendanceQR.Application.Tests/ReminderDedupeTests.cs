using AttendanceQR.Api.Jobs;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The 5-minute reminder sweep and the reminders it already sent.
///
/// It used to insert a reminder every time its condition still held and leave the unique (company, employee,
/// type, day) index to refuse the repeat — the 10-minute windows span two sweeps, the 09:00–10:00 missed-checkout
/// hour twelve — which was ~930 refused INSERTs a day, almost every database error the application raised, and
/// it swallowed any other failure of that insert without a word. Each sweep now skips what is on record before
/// writing; the index stays as the guard for two sweeps racing, and only its own 23505 is quiet.
///
/// The in-memory store enforces no unique index, so a repeat that is not skipped shows here as a second row and a
/// second push. The race itself (two sweeps between one's read and its write) needs Postgres; its 23505 is
/// staged with an interceptor.
/// </summary>
public class ReminderDedupeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly DateOnly Today = new(2026, 11, 24);

    /// <summary>A moment on <see cref="Today"/>, Baku wall time (UTC+4, no DST), as UTC.</summary>
    private static DateTime Baku(int hour, int minute) =>
        DateTime.SpecifyKind(Today.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Utc).AddHours(-4);

    [Fact]
    public async Task Every_sweep_through_a_window_sends_each_reminder_once()
    {
        using var w = new World();
        var early = w.Person();
        var forgot = w.Person();
        // Yesterday's day that never closed: reported once in this morning's 09:00–10:00 hour.
        w.Day(forgot, Today.AddDays(-1), checkInUtc: Baku(9, 0).AddDays(-1));

        // Every five minutes from 08:50 to 09:55: the check-in window (shift at 09:00) is two sweeps, the
        // missed-checkout hour twelve.
        for (var at = Baku(8, 50); at <= Baku(9, 55); at = at.AddMinutes(5))
        {
            w.Clock.Now = at;
            await w.Job.SweepAsync(default);
        }

        using var db = w.Context();
        var rows = db.EmployeeNotifications.AsNoTracking().ToList()
            .Select(n => (n.EmployeeId, n.Type, n.RelatedDate)).OrderBy(x => x.ToString()).ToList();
        Assert.Equal(new[]
        {
            (early, EmployeeNotificationType.CheckInSoon, Today),
            (forgot, EmployeeNotificationType.CheckInSoon, Today),
            (forgot, EmployeeNotificationType.MissedCheckOut, Today.AddDays(-1)),
        }.OrderBy(x => x.ToString()).ToList(), rows);
        Assert.Equal(3, w.Push.Sent.Count);
        Assert.Empty(w.Log.Errors);
    }

    [Fact]
    public async Task A_reminder_already_on_record_is_not_written_or_pushed_again()
    {
        using var w = new World();
        var early = w.Person();
        using (var db = w.Context())
        {
            db.EmployeeNotifications.Add(new EmployeeNotification
            {
                EmployeeId = early, Type = EmployeeNotificationType.CheckInSoon, RelatedDate = Today, Title = "t", Body = "b",
            });
            await db.SaveChangesAsync();
        }

        w.Clock.Now = Baku(8, 55);
        await w.Job.SweepAsync(default);

        using var check = w.Context();
        Assert.Single(check.EmployeeNotifications.AsNoTracking());
        Assert.Empty(w.Push.Sent);
    }

    [Fact]
    public async Task Another_sweep_winning_the_race_is_quiet_and_any_other_failure_is_logged_and_skipped()
    {
        // The first reminder's insert meets the index's own 23505 (another sweep got there first), the second a
        // different failure; the third goes through.
        var refusals = new Queue<Exception?>([
            new DbUpdateException("simulated: another sweep inserted it first",
                new Npgsql.PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR",
                    Npgsql.PostgresErrorCodes.UniqueViolation, constraintName: ReminderJob.OneReminderIndex)),
            new DbUpdateException("simulated: the connection dropped"),
            null,
        ]);
        using var w = new World(new RefuseNotifications(refusals));
        w.Person();
        w.Person();
        w.Person();

        w.Clock.Now = Baku(8, 55);
        await w.Job.SweepAsync(default);

        // Neither refused reminder was pushed — the race's winner pushed its own; the failure is not "sent".
        Assert.Single(w.Push.Sent);
        // The race is not an error; the other failure is, with its exception — it used to vanish without a line.
        var error = Assert.Single(w.Log.Errors);
        Assert.Contains("connection dropped", error.Exception?.Message);
        // And the sweep carried on past both.
        using var check = w.Context();
        Assert.Single(check.EmployeeNotifications.AsNoTracking());
    }

    [Fact]
    public void The_quiet_23505_is_the_one_from_the_reminders_own_index()
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        // Never connected to: the real provider's model is all this reads.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none").Options, tenant);
        var unique = Assert.Single(db.Model.FindEntityType(typeof(EmployeeNotification))!.GetIndexes(), i => i.IsUnique);
        Assert.Equal(ReminderJob.OneReminderIndex, unique.GetDatabaseName());
    }

    // ───────────────────────────── harness ─────────────────────────────

    private sealed class World : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly Guid _branch = Guid.NewGuid();
        public PinnedClock Clock { get; } = new();
        public FakePush Push { get; } = new();
        public ListLogger Log { get; } = new();
        public ReminderJob Job { get; }

        public World(IInterceptor? interceptor = null)
        {
            var name = $"reminders-{Guid.NewGuid()}";
            var services = new ServiceCollection();
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped(sp =>
            {
                var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name);
                if (interceptor is not null)
                    options.AddInterceptors(interceptor);
                return new AppDbContext(options.Options, sp.GetRequiredService<ITenantContext>());
            });
            services.AddScoped<IPushNotifier>(_ => Push);
            _services = services.BuildServiceProvider();

            Job = new ReminderJob(_services.GetRequiredService<IServiceScopeFactory>(),
                new PushOptions { PublicKey = "public", PrivateKey = "private" },
                new AppOptions { TimeZone = "Asia/Baku" }, Log, Clock);

            using var db = Context();
            db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            db.Locations.Add(new Location
            {
                Id = _branch, TenantId = TenantId, Name = "Mərkəz", IsActive = true, QrVersion = 1, WorkDaysMask = 127,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            });
            db.SaveChanges();
        }

        /// <summary>A context of its own, resolved to the company — as the job's own scope is.</summary>
        public AppDbContext Context()
        {
            var scope = _services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(TenantId);
            return scope.ServiceProvider.GetRequiredService<AppDbContext>();
        }

        public Guid Person()
        {
            var id = Guid.NewGuid();
            using var db = Context();
            db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = $"İşçi {id.ToString()[..4]}", Email = $"{id:N}@t.local",
                LocationId = _branch, Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
            });
            db.SaveChanges();
            return id;
        }

        public void Day(Guid employee, DateOnly date, DateTime checkInUtc)
        {
            using var db = Context();
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                TenantId = TenantId, EmployeeId = employee, LocationId = _branch, AttendanceDate = date,
                CheckInAtUtc = checkInUtc, Status = AttendanceStatus.OnTime,
            });
            db.SaveChanges();
        }

        public void Dispose() => _services.Dispose();
    }

    /// <summary>Refuses the next reminder inserts, one queued answer per save (null lets it through).</summary>
    private sealed class RefuseNotifications(Queue<Exception?> answers) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<EmployeeNotification>().Any(e => e.State == EntityState.Added)
                && answers.Count > 0 && answers.Dequeue() is { } refusal)
                throw refusal;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PinnedClock : TimeProvider
    {
        public DateTime Now { get; set; }
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed class FakePush : IPushNotifier
    {
        public List<(Guid[] To, string Title)> Sent { get; } = [];
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
        {
            Sent.Add((employeeIds.ToArray(), title));
            return Task.FromResult(employeeIds.Count);
        }
    }

    private sealed class ListLogger : ILogger<ReminderJob>
    {
        public List<(string Message, Exception? Exception)> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add((formatter(state, exception), exception));
        }
    }
}
