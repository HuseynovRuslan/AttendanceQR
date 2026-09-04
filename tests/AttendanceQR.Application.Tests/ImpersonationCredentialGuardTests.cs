using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AttendanceQR.Api;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
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
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The other half of the handover (ImpersonationHandoverTests covers who the console borrows): what a
/// borrowed session may NOT do.
///
/// An impersonation token's "sub" is the customer's own admin. The PIN gate lets that session work even
/// while the account is still on the temporary PIN — which on the day a company is created it always
/// is — and the ONLY thing that makes the exemption safe is that the session cannot come away with a
/// credential for the account it is borrowing. If it could, the hour-long session would become an
/// ordinary never-expiring login for somebody else's admin, and the temporary PIN written on the
/// customer's handover slip would silently stop working.
///
/// Every route that hands back a credential (or repoints the identifier a credential is delivered to)
/// is pinned here, because two of them are not obvious: reset-pin returns the new PIN in plaintext, and
/// an email/phone edit moves where a forgot-PIN reset lands. Delete any one of these guards and a test
/// here fails.
/// </summary>
public class ImpersonationCredentialGuardTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f8");
    private const string RightPin = "5273";
    private const string NewPin = "8471";

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid OperatorId { get; } = Guid.NewGuid();
        /// <summary>The customer's own admin — the account the console borrows. Still on the temp PIN.</summary>
        public Guid BorrowedAdminId { get; } = Guid.NewGuid();
        /// <summary>A second admin of the same company, to prove the rule is not just about self.</summary>
        public Guid OtherAdminId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        public Guid NotActivatedId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"imp-credential-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "Musteri", Slug = "musteri", DisplayName = "Musteri", IsActive = true,
            });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantId, Name = "Filial", Latitude = 40.4, Longitude = 49.8,
                RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
            });
            // The day-zero shape: created by the console with a temporary PIN they have not used yet.
            Db.Employees.Add(Person(BorrowedAdminId, "Musteri Admini", EmployeeRole.Admin, mustChangePin: true,
                phone: "+994501111111"));
            Db.Employees.Add(Person(OtherAdminId, "Ikinci Admin", EmployeeRole.Admin, phone: "+994502222222"));
            Db.Employees.Add(Person(StaffId, "Adi Isci", EmployeeRole.Employee, phone: "+994503333333"));
            Db.Employees.Add(Person(NotActivatedId, "Aktivlesmemis", EmployeeRole.Admin, activated: false,
                phone: "+994504444444"));
            Db.SaveChanges();
        }

        private Employee Person(
            Guid id, string name, EmployeeRole role, bool mustChangePin = false, bool activated = true,
            string? phone = null) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = Branch,
            IsActive = true, ActivatedAtUtc = activated ? DateTime.UtcNow.AddDays(-1) : null,
            PasswordHash = "hashed:" + RightPin, MustChangePin = mustChangePin,
            PhoneNumber = PhoneNumbers.Normalize(phone), Email = null,
        };

        /// <summary>The operator's session: sub is the BORROWED admin, "imp" names the operator.</summary>
        private ClaimsPrincipal Impersonating() => new(new ClaimsIdentity(new[]
        {
            new Claim("sub", BorrowedAdminId.ToString()),
            new Claim("role", nameof(EmployeeRole.Admin)),
            new Claim("imp", OperatorId.ToString()),
        }, "test"));

        private static ClaimsPrincipal Ordinary(Guid id) => new(new ClaimsIdentity(new[]
        {
            new Claim("sub", id.ToString()),
            new Claim("role", nameof(EmployeeRole.Admin)),
        }, "test"));

        public AdminController Admin(bool impersonating) =>
            new(Db, Microsoft.Extensions.Options.Options.Create(new InvitationOptions()),
                new StubHasher(), new StubLockout(), new AppOptions(), new StubPhotoStorage(),
                NullLogger<AdminController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = impersonating ? Impersonating() : Ordinary(BorrowedAdminId),
                    },
                },
            };

        public AuthController Auth(bool impersonating)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            return new AuthController(
                Db, tenant, new StubHasher(), new StubJwt(), new StubLockout(), new StubPhotoStorage(),
                new StubFaceMatch(), new StubPush(), Cache, NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = impersonating ? Impersonating() : Ordinary(BorrowedAdminId),
                    },
                },
            };
        }

        public AdminPinResetController PinResets(bool impersonating) =>
            new(Db, new StubHasher(), new StubLockout())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = impersonating ? Impersonating() : Ordinary(BorrowedAdminId),
                    },
                },
            };

        public Employee Row(Guid id) => Db.Employees.IgnoreQueryFilters().AsNoTracking().Single(e => e.Id == id);

        public void Dispose()
        {
            Db.Dispose();
            Cache.Dispose();
        }
    }

    // --- stubs -------------------------------------------------------------------

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubLockout : ILoginLockoutStore
    {
        public int LockoutMinutes => 15;
        public bool IsLockedOut(string key) => false;
        public int RecordFailure(string key) => 5;
        public void RecordSuccess(string key) { }
    }

    private sealed class StubJwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token-for:" + employee.Id;
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes, bool readOnly = false) => "imp";
    }

    private sealed class StubPhotoStorage : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubFaceMatch : IFaceMatchService
    {
        public bool Enabled => false;
        public Task<FaceMatchOutcome> CompareAsync(byte[] r, byte[] c, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));
        public Task<int> DetectFaceCountAsync(byte[] p, CancellationToken ct = default) => Task.FromResult(-1);
    }

    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url = null, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private static void AssertRefused(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Contains("NotDuringImpersonation", obj.Value!.ToString());
    }

    // --- the two auth endpoints the commit closed (previously untested) ------------

    [Fact]
    public async Task Set_initial_pin_refuses_an_impersonation_session_and_leaves_the_credential_alone()
    {
        using var h = new Harness();

        AssertRefused(await h.Auth(impersonating: true).SetInitialPin(new SetInitialPinRequest(NewPin)));

        // The customer's handover PIN is untouched, unconsumed, and still forced on their first login.
        var row = h.Row(h.BorrowedAdminId);
        Assert.Equal("hashed:" + RightPin, row.PasswordHash);
        Assert.True(row.MustChangePin);
        Assert.Equal(0, row.TokenVersion);
    }

    [Fact]
    public async Task Change_password_refuses_an_impersonation_session_and_leaves_the_credential_alone()
    {
        using var h = new Harness();

        AssertRefused(await h.Auth(impersonating: true).ChangePassword(new ChangePasswordRequest(RightPin, NewPin)));

        var row = h.Row(h.BorrowedAdminId);
        Assert.Equal("hashed:" + RightPin, row.PasswordHash);
        Assert.True(row.MustChangePin);
        Assert.Equal(0, row.TokenVersion);
    }

    [Fact]
    public async Task The_customer_themselves_still_sets_their_own_pin()
    {
        // The refusal is about the BORROWER, not the account: the customer's own first login must still
        // get them off the temporary PIN, or the handover has broken the thing it was protecting.
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Auth(impersonating: false).SetInitialPin(new SetInitialPinRequest(NewPin)));
        Assert.False(h.Row(h.BorrowedAdminId).MustChangePin);
    }

    // --- the PIN gate's exemption (previously untested) ----------------------------

    [Theory]
    // Not on a temporary PIN: nothing to gate, impersonating or not.
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    // On a temporary PIN: the person is held at the "set your PIN" screen...
    [InlineData(true, false, true)]
    // ...but a support session borrowing that account is not that person, and a company created today
    // has an admin who is ALWAYS in this state — gating it is what forced operators to enrol
    // themselves inside the customer's company.
    [InlineData(true, true, false)]
    public void The_gate_flags_a_person_on_a_temporary_pin_but_never_a_support_session(
        bool mustChangePin, bool impersonating, bool expected)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (impersonating) claims.Add(new Claim("imp", Guid.NewGuid().ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        Assert.Equal(expected, TemporaryPinGate.ShouldFlag(mustChangePin, principal));
    }

    // --- the token the client reads ------------------------------------------------

    [Fact]
    public void An_impersonation_token_never_carries_the_forced_pin_flag()
    {
        // "mcp" is a client instruction: while it is set, AdminRoute/ProtectedRoute redirect to the
        // forced "set your PIN" screen — whose only button now refuses an impersonation session. On a
        // day-zero tenant (the borrowed admin is always on a temp PIN) that combination is a dead end
        // with no admin screen behind it, so the flag must not ride on this token.
        var jwt = new JwtService(Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Issuer = "qrlog", Audience = "qrlog", SigningKey = new string('k', 48), ExpiryMinutes = 60,
        }));
        var admin = new Employee
        {
            Id = Guid.NewGuid(), TenantId = TenantId, FullName = "Musteri Admini",
            Role = EmployeeRole.Admin, MustChangePin = true, PasswordHash = "h",
        };

        var impersonation = new JwtSecurityTokenHandler().ReadJwtToken(
            jwt.GenerateImpersonationToken(admin, Guid.NewGuid(), 60));
        Assert.DoesNotContain(impersonation.Claims, c => c.Type == "mcp");
        Assert.Contains(impersonation.Claims, c => c.Type == "imp");

        // The customer's own login still gets the flag — the forced change is theirs, and it is read
        // from Employee.MustChangePin either way.
        var ordinary = new JwtSecurityTokenHandler().ReadJwtToken(jwt.GenerateToken(admin));
        Assert.Contains(ordinary.Claims, c => c.Type == "mcp" && c.Value == "1");
    }

    // --- reset-pin: the plaintext PIN in the response --------------------------------

    [Fact]
    public async Task Reset_pin_refuses_the_account_the_session_is_borrowing()
    {
        // The hole the auth guards left open: this endpoint hands the new PIN back in plaintext, so
        // without the refusal an operator could mint a permanent credential for the customer's admin —
        // and the PIN on the customer's handover slip would stop working with nothing to explain why.
        using var h = new Harness();

        AssertRefused(await h.Admin(impersonating: true).ResetPin(h.BorrowedAdminId));

        var row = h.Row(h.BorrowedAdminId);
        Assert.Equal("hashed:" + RightPin, row.PasswordHash);
        Assert.Equal(0, row.TokenVersion);
    }

    [Fact]
    public async Task Reset_pin_refuses_any_other_admin_of_the_company_too()
    {
        using var h = new Harness();

        AssertRefused(await h.Admin(impersonating: true).ResetPin(h.OtherAdminId));
        Assert.Equal("hashed:" + RightPin, h.Row(h.OtherAdminId).PasswordHash);
    }

    [Fact]
    public async Task Reset_pin_still_works_for_ordinary_staff_during_a_support_session()
    {
        // Handing out staff temporary PINs IS the setup work; an employee's PIN opens nothing but their
        // own scan screen. The guard must not cost the feature its purpose.
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Admin(impersonating: true).ResetPin(h.StaffId));
        Assert.NotEqual("hashed:" + RightPin, h.Row(h.StaffId).PasswordHash);
    }

    [Fact]
    public async Task An_admin_signed_in_as_themselves_can_still_reset_their_own_pin()
    {
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Admin(impersonating: false).ResetPin(h.BorrowedAdminId));
    }

    [Fact]
    public async Task Reinvite_refuses_an_admin_because_the_activation_token_is_a_credential()
    {
        using var h = new Harness();

        AssertRefused(await h.Admin(impersonating: true).Reinvite(h.NotActivatedId));
        Assert.Null(h.Row(h.NotActivatedId).InvitationTokenHash);
    }

    // --- the login identifier ---------------------------------------------------------

    [Fact]
    public async Task The_borrowed_accounts_phone_number_cannot_be_repointed_by_the_session()
    {
        // Quieter than a PIN reset and just as final: Login matches on the phone number and a forgot-PIN
        // reset is delivered to it, so moving it moves the account.
        using var h = new Harness();

        AssertRefused(await h.Admin(impersonating: true).Update(h.BorrowedAdminId, Update(h, "+994559999999")));

        Assert.Equal(PhoneNumbers.Normalize("+994501111111"), h.Row(h.BorrowedAdminId).PhoneNumber);
    }

    [Fact]
    public async Task Everything_else_about_an_admin_row_is_still_editable_during_a_support_session()
    {
        // Setting the company up means editing these rows — branch, hours, position. Only the
        // identifier is refused.
        using var h = new Harness();

        var request = Update(h, "+994501111111") with { Position = "Direktor" };
        Assert.IsType<OkObjectResult>(await h.Admin(impersonating: true).Update(h.BorrowedAdminId, request));
        Assert.Equal("Direktor", h.Row(h.BorrowedAdminId).Position);
    }

    [Fact]
    public async Task The_companys_own_admin_can_still_change_their_number()
    {
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(
            await h.Admin(impersonating: false).Update(h.BorrowedAdminId, Update(h, "+994559999999")));
        Assert.Equal(PhoneNumbers.Normalize("+994559999999"), h.Row(h.BorrowedAdminId).PhoneNumber);
    }

    private static EmployeeUpdateRequest Update(Harness h, string phone) => new(
        FullName: "Musteri Admini", Email: null, LocationId: h.Branch, Role: EmployeeRole.Admin,
        IsActive: true, PhoneNumber: phone);

    [Fact]
    public async Task The_forgot_pin_queue_will_not_resolve_a_request_against_an_admin_either()
    {
        // The other door to a plaintext PIN. An employee (or anyone typing an admin's number into the
        // login screen) can file a reset request anonymously, so without this the operator would only
        // have to file one against the account they are borrowing and then resolve it themselves.
        using var h = new Harness();
        var request = new PinResetRequest { TenantId = TenantId, EmployeeId = h.BorrowedAdminId };
        h.Db.PinResetRequests.Add(request);
        h.Db.SaveChanges();

        AssertRefused(await h.PinResets(impersonating: true).Resolve(request.Id));

        // Refused BEFORE the request is claimed, so the customer's own admin can still resolve it.
        Assert.Equal(PinResetStatus.Pending,
            h.Db.PinResetRequests.IgnoreQueryFilters().AsNoTracking().Single().Status);
        Assert.Equal("hashed:" + RightPin, h.Row(h.BorrowedAdminId).PasswordHash);
    }

    // --- no permanent admin left behind ------------------------------------------------

    [Fact]
    public async Task A_support_session_cannot_appoint_a_new_admin()
    {
        // The 60-minute bound is only worth something if nothing outlives it. Invite returns the new
        // account's activation token to the CALLER, so an admin created here is one the operator can
        // sign in as forever — and nothing inside the tenant would say where it came from.
        using var h = new Harness();

        AssertRefused(await h.Admin(impersonating: true).Invite(new InviteRequest(
            FullName: "Gizli Admin", Email: null, LocationId: h.Branch, Role: EmployeeRole.Admin,
            PhoneNumber: "+994557777777")));

        Assert.False(h.Db.Employees.IgnoreQueryFilters().Any(e => e.FullName == "Gizli Admin"));
    }

    [Fact]
    public async Task A_support_session_can_still_add_the_companys_staff()
    {
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Admin(impersonating: true).Invite(new InviteRequest(
            FullName: "Yeni Isci", Email: null, LocationId: h.Branch, Role: EmployeeRole.Employee,
            PhoneNumber: "+994558888888")));
    }

    [Fact]
    public async Task An_admin_row_inside_a_bulk_import_fails_that_row_only()
    {
        // Same rule, per row: bulk-import returns each new account's temporary PIN to the caller, so an
        // "Admin" cell in the spreadsheet is the same planted key. The rest of the sheet still imports.
        using var h = new Harness();

        var result = await h.Admin(impersonating: true).BulkImport(new BulkInviteRequest(
            h.Branch, EmployeeRole.Employee,
            new[]
            {
                new BulkInviteRow("Adi Isci Bir", PhoneNumber: "+994551111111"),
                new BulkInviteRow("Gizli Admin", PhoneNumber: "+994552222222", RoleName: "Admin"),
            }));

        var ok = Assert.IsType<OkObjectResult>(result);
        var failed = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            ok.Value!.GetType().GetProperty("failed")!.GetValue(ok.Value)!);
        var refusedRow = Assert.Single(failed.Cast<object>());
        Assert.Equal("NotDuringImpersonation", refusedRow.GetType().GetProperty("error")!.GetValue(refusedRow));
        Assert.False(h.Db.Employees.IgnoreQueryFilters().Any(e => e.FullName == "Gizli Admin"));
        Assert.True(h.Db.Employees.IgnoreQueryFilters().Any(e => e.FullName == "Adi Isci Bir"));
    }

    [Fact]
    public async Task A_staff_member_cannot_be_promoted_to_admin_by_a_support_session_either()
    {
        using var h = new Harness();

        var request = new EmployeeUpdateRequest(
            FullName: "Adi Isci", Email: null, LocationId: h.Branch, Role: EmployeeRole.Admin,
            IsActive: true, PhoneNumber: "+994503333333");

        AssertRefused(await h.Admin(impersonating: true).Update(h.StaffId, request));
        Assert.Equal(EmployeeRole.Employee, h.Row(h.StaffId).Role);
    }
}
