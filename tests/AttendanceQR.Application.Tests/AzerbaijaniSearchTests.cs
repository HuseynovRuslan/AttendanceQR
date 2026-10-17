using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Searching names in Azerbaijani (SearchText). Found by the staging E2E suite on 2026-10-17: the admin's staff
/// search lowered the typed text in .NET and the name in Postgres, which disagree about «İ» — «İsmayılov» found
/// nobody, «İsmayılov» included. One rule now, said once in C# and once in SQL.
/// </summary>
public class AzerbaijaniSearchTests
{
    // ─────────────────────────── the rule ───────────────────────────

    [Theory]
    // The capital dotted İ, the bug itself.
    [InlineData("İsmayılov Anar", "İsmayılov")]
    [InlineData("İsmayılov Anar", "ismayılov")]
    [InlineData("İsmayılov Anar", "İSMAYILOV")]
    // Keyboards without «ı» / «İ»: the dot is not a reason to miss a colleague.
    [InlineData("İsmayılov Anar", "Ismayilov")]
    [InlineData("İsmayılov Anar", "ismayilov")]
    [InlineData("İsmayılov Anar", "ISMAYILOV")]
    [InlineData("Ilqar Qasımov", "ılqar")]
    [InlineData("Ilqar Qasımov", "İlqar")]
    [InlineData("Ilqar Qasımov", "ilqar")]
    // The rest of the alphabet, both ways.
    [InlineData("Əliyev Şükür", "əliyev")]
    [InlineData("Əliyev Şükür", "ƏLİYEV")]
    [InlineData("Əliyev Şükür", "ŞÜKÜR")]
    [InlineData("Çingiz Ağayev", "çİngİz ağa")]
    [InlineData("Göyçə Ünal", "GÖYÇƏ ÜNAL")]
    // A combining dot some tools write after an «i».
    [InlineData("İsmayılov", "i̇smayılov")]
    public void A_name_is_found_however_its_letters_were_typed(string name, string typed) =>
        Assert.Contains(SearchText.Fold(typed), SearchText.Fold(name));

    [Theory]
    // Different letters stay different: «ə» is not «e», «ş» is not «s», «ç» is not «c».
    [InlineData("Əliyev", "eliyev")]
    [InlineData("Şükür", "sukur")]
    [InlineData("Çingiz", "cingiz")]
    public void Different_letters_stay_different(string name, string typed) =>
        Assert.DoesNotContain(SearchText.Fold(typed), SearchText.Fold(name));

    [Fact]
    public void Folding_keeps_digits_and_ids_as_they_are()
    {
        Assert.Equal("0501234567", SearchText.Fold("0501234567"));
        Assert.Equal("a1b2c3d4", SearchText.Fold("A1B2C3D4"));
        Assert.Equal(string.Empty, SearchText.Fold(null));
    }

    [Fact]
    public void The_two_spellings_of_the_rule_line_up()
    {
        // translate(x, From, To) maps From[k] to To[k] and deletes the rest of From; the C# body must do the same.
        Assert.Equal(SearchText.From.Length - 1, SearchText.To.Length);
        Assert.Equal(SearchText.To, SearchText.Fold(SearchText.From));
    }

    // ─────────────────────────── the admin's staff search ───────────────────────────

    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000a2a2");

    private sealed class NoPhotos : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult(key);
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private static AdminController Admin(AppDbContext db) =>
        new(db, Options.Create(new InvitationOptions()), new PasswordHasher(),
            new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())),
            new AppOptions { TimeZone = "Asia/Baku" }, new NoPhotos(), NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", nameof(EmployeeRole.Admin))], "test")),
                },
            },
        };

    private static AppDbContext InMemory()
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"az-search-{Guid.NewGuid()}").Options, tenant);
        var branch = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
        db.Locations.Add(new Location { Id = branch, TenantId = TenantId, Name = "Mərkəz", IsActive = true, QrVersion = 1 });
        foreach (var (name, position) in new[]
                 {
                     ("İsmayılov Anar İlham", "Bağban"), ("Ilqar Qasımov", "İşçi"), ("Əliyev Şükür", "Sürücü"),
                     ("Həsənova Aynur", "İnzibatçı"),
                 })
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = TenantId, FullName = name, Position = position, Role = EmployeeRole.Employee,
                LocationId = branch, IsActive = true, PasswordHash = "x", Email = $"{Guid.NewGuid():N}@a.local",
            });
        db.SaveChanges();
        return db;
    }

    [Theory]
    [InlineData("İsmayılov", "İsmayılov Anar İlham")]
    [InlineData("ISMAYILOV", "İsmayılov Anar İlham")]
    [InlineData("ismayilov", "İsmayılov Anar İlham")]
    [InlineData("ilham", "İsmayılov Anar İlham")]
    [InlineData("İLQAR", "Ilqar Qasımov")]
    [InlineData("ƏLİYEV", "Əliyev Şükür")]
    [InlineData("şükür", "Əliyev Şükür")]
    // The job title is searched by the same rule.
    [InlineData("inzibatçı", "Həsənova Aynur")]
    public void The_admin_staff_search_finds_the_person(string typed, string expected)
    {
        using var db = InMemory();
        var found = Admin(db).FilteredEmployees(typed, null, null, null, false).Select(e => e.FullName).ToList();
        Assert.Equal([expected], found);
    }

    [Fact]
    public void The_rule_runs_in_the_database_not_in_the_application()
    {
        // Compiled against the real PostgreSQL provider, never connected (EmployeeListQueryTranslationTests).
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none").Options,
            tenant);

        var sql = Admin(db).FilteredEmployees("İsmayılov", null, null, null, false).ToQueryString();

        Assert.Contains("lower(translate(", sql);
        Assert.Contains(SearchText.From, sql);
        Assert.Contains(SearchText.To, sql);
        // The typed text is folded by the same rule before it is sent.
        Assert.Contains("ismayilov", sql);
        Assert.DoesNotContain("İsmayılov", sql);
        Assert.Contains("TenantId", sql);
    }
}
