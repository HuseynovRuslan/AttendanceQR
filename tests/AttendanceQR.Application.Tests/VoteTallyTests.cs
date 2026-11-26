using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The monthly vote's count. A ballot and its tally used to be two saves: two colleagues voting for the same
/// person at the same moment both inserted a tally row and the second got a 500 after its ballot was already
/// in — a vote lost and a voter told they had already voted — and two increments of an existing row wrote
/// the same number. On Postgres the two now go in one transaction and the count is a single
/// INSERT … ON CONFLICT … DO UPDATE.
///
/// The race itself needs Postgres: the in-memory provider has no transactions, runs no SQL and enforces no
/// unique index. So these pin what can be pinned without one — the count through the in-memory path, and
/// that the statement Postgres runs names the table, the columns and the unique index the model really has
/// (a renamed column or index would otherwise be found by the first voter, as a 500).
/// </summary>
public class VoteTallyTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b7");

    [Fact]
    public async Task Votes_for_one_person_add_up_in_one_row_and_another_person_gets_their_own()
    {
        using var w = new World();
        var (a, b, c, d) = (w.Person(), w.Person(), w.Person(), w.Person());

        Assert.IsType<OkObjectResult>(await w.VoteAsync(a, c));
        Assert.IsType<OkObjectResult>(await w.VoteAsync(b, c));
        Assert.IsType<OkObjectResult>(await w.VoteAsync(c, d));

        using var check = w.Context();
        var tallies = check.MonthlyVoteTallies.AsNoTracking().ToList();
        Assert.Equal(2, tallies.Count);
        var forC = tallies.Single(t => t.CandidateEmployeeId == c);
        Assert.Equal(2, forC.Votes);
        Assert.Equal(w.Branch, forC.LocationId);
        Assert.Equal(1, tallies.Single(t => t.CandidateEmployeeId == d).Votes);
        Assert.Equal(3, check.MonthlyVoteBallots.AsNoTracking().Count());
    }

    [Fact]
    public void The_count_statement_names_the_tallys_unique_index_and_its_real_columns()
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        // Never connected to: the real provider's model is all this reads.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none").Options, tenant);
        var tally = db.Model.FindEntityType(typeof(MonthlyVoteTally))!;
        static string Q(string name) => $"\"{name}\"";

        var (candidate, location) = (Guid.NewGuid(), Guid.NewGuid());
        var period = new DateOnly(2026, 11, 1);
        var sql = VoteController.TallyUpsert(TenantId, period, location, candidate);
        var text = sql.Format;

        var table = tally.GetTableName()!;
        Assert.Contains($"INSERT INTO {Q(table)} (", text);
        // Every column of the table is written — a NOT NULL column left out would refuse the first vote.
        foreach (var column in tally.GetProperties().Select(p => p.GetColumnName()))
            Assert.Contains(Q(column), text);

        // The conflict target is exactly the unique index, in its order; anything else and Postgres answers
        // "no unique or exclusion constraint matching the ON CONFLICT specification" to every vote.
        var unique = Assert.Single(tally.GetIndexes(), i => i.IsUnique);
        Assert.Contains($"ON CONFLICT ({string.Join(", ", unique.Properties.Select(p => Q(p.GetColumnName())))})", text);
        // Added to what the row holds at that moment, never a number computed beforehand.
        Assert.Contains($"{Q("Votes")} = {Q(table)}.{Q("Votes")} + 1", text);

        // The company is written explicitly: raw SQL is not stamped by SaveChanges.
        var args = sql.GetArguments();
        Assert.Contains(TenantId, args.OfType<Guid>());
        Assert.Contains(candidate, args.OfType<Guid>());
        Assert.Contains(location, args.OfType<Guid>());
        Assert.Contains(period, args.OfType<DateOnly>());
    }

    // ───────────────────────────── harness ─────────────────────────────

    private sealed class World : IDisposable
    {
        public Guid Branch { get; } = Guid.NewGuid();
        private readonly string _name = $"vote-tally-{Guid.NewGuid()}";
        private readonly TenantContext _tenant = new();
        private readonly AppDbContext _seed;

        public World()
        {
            _tenant.Resolve(TenantId);
            _seed = Context();
            _seed.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            _seed.Locations.Add(new Location { Id = Branch, TenantId = TenantId, Name = "Mərkəz", IsActive = true, WorkDaysMask = 127, QrVersion = 1 });
            // Open across the whole of this month and past both ends, so a run never lands on a closed ballot.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var period = new DateOnly(today.Year, today.Month, 1);
            _seed.VoteCampaigns.Add(new VoteCampaign
            {
                TenantId = TenantId, Period = period, StartsOn = period.AddDays(-1), EndsOn = period.AddMonths(1).AddDays(1),
            });
            _seed.SaveChanges();
        }

        public AppDbContext Context() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options, _tenant);

        public Guid Person()
        {
            var id = Guid.NewGuid();
            _seed.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = $"İşçi {id.ToString()[..4]}", Email = $"{id:N}@t.local",
                LocationId = Branch, Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
            });
            _seed.SaveChanges();
            return id;
        }

        /// <summary>One vote, on a context of its own as each request has.</summary>
        public async Task<IActionResult> VoteAsync(Guid voter, Guid candidate)
        {
            await using var db = Context();
            var controller = new VoteController(db, new AppOptions { TimeZone = "UTC" })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("sub", voter.ToString()), new Claim("role", nameof(EmployeeRole.Employee))], "test")),
                    },
                },
            };
            return await controller.Cast(new VoteRequest(candidate));
        }

        public void Dispose() => _seed.Dispose();
    }
}
