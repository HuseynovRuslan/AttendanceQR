using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Amazon.Rekognition;
using Amazon.Runtime;
using Amazon.S3;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// EF Core / PostgreSQL. Connection string comes from appsettings.json.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Options binding.
builder.Services.Configure<QrTokenOptions>(
    builder.Configuration.GetSection(QrTokenOptions.SectionName));
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<InvitationOptions>(
    builder.Configuration.GetSection(InvitationOptions.SectionName));
builder.Services.Configure<MinioOptions>(
    builder.Configuration.GetSection(MinioOptions.SectionName));
builder.Services.Configure<RekognitionOptions>(
    builder.Configuration.GetSection(RekognitionOptions.SectionName));

// Security services.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IQrTokenService, QrTokenService>();
builder.Services.AddSingleton<INonceStore, MemoryCacheNonceStore>();
builder.Services.AddSingleton<ILoginLockoutStore, MemoryCacheLoginLockoutStore>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtService, JwtService>();

// Multi-tenancy: the current request's tenant, resolved from the JWT (OnTokenValidated) or the
// request Origin (middleware below). Scoped so the DbContext can read it per request for its query
// filter + insert stamping.
builder.Services.AddScoped<ITenantContext, TenantContext>();

// Business services (use the scoped DbContext, so they are scoped too).
builder.Services.AddScoped<IDeviceChangeService, DeviceChangeService>();
builder.Services.AddScoped<IAttendanceQueryService, AttendanceQueryService>();

// Photo-audit object storage (MinIO via the S3 SDK). The S3 client is a singleton pointed at the
// MinIO endpoint; path-style addressing is required for MinIO. When Storage:Minio:Endpoint is empty
// (e.g. local dev without MinIO) we still register a client so DI resolves — it simply targets a
// placeholder region and is never called (uploads are best-effort and the cleanup job stays idle).
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MinioOptions>>().Value;
    var config = new AmazonS3Config
    {
        ForcePathStyle = true,
        // AWS SDK v4 adds a streaming CRC "trailer" checksum by default, which S3-compatible stores
        // (Cloudflare R2, MinIO, B2) reject ("STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER not
        // implemented"). Only compute/validate checksums when the operation actually requires them.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    };
    if (!string.IsNullOrWhiteSpace(opts.Endpoint))
    {
        config.ServiceURL = $"{(opts.UseSsl ? "https" : "http")}://{opts.Endpoint}";
        // SigV4 signing region for the custom endpoint. R2 requires "auto"; MinIO ignores it.
        if (!string.IsNullOrWhiteSpace(opts.Region))
            config.AuthenticationRegion = opts.Region;
    }
    else
    {
        config.RegionEndpoint = Amazon.RegionEndpoint.USEast1; // placeholder so the client constructs; unused
    }
    return new AmazonS3Client(new BasicAWSCredentials(opts.AccessKey, opts.SecretKey), config);
});
builder.Services.AddScoped<IPhotoStorageService, MinioPhotoStorageService>();

// Face audit (AWS Rekognition). Graceful: with no AWS keys the service reports Enabled=false and the
// worker does nothing (records stay NotChecked) — so this can ship "off" and be enabled via env vars.
builder.Services.AddSingleton<IAmazonRekognition>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RekognitionOptions>>().Value;
    var region = Amazon.RegionEndpoint.GetBySystemName(string.IsNullOrWhiteSpace(opts.Region) ? "us-east-1" : opts.Region);
    var creds = new BasicAWSCredentials(
        string.IsNullOrWhiteSpace(opts.AccessKey) ? "unset" : opts.AccessKey,
        string.IsNullOrWhiteSpace(opts.SecretKey) ? "unset" : opts.SecretKey);
    return new AmazonRekognitionClient(creds, region);
});
builder.Services.AddScoped<IFaceMatchService, RekognitionFaceMatchService>();
builder.Services.AddSingleton<IFaceMatchQueue, FaceMatchQueue>();

// App options (time zone for shift/UTC math). Registered as a plain singleton so the
// Application/Infrastructure layers don't need an Options package reference.
var appOptions = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
builder.Services.AddSingleton(appOptions);

// Same reason — DeviceChangeService (Infrastructure) needs these without an Options dependency.
var deviceBindingOptions = builder.Configuration.GetSection(DeviceBindingOptions.SectionName)
    .Get<DeviceBindingOptions>() ?? new DeviceBindingOptions();
builder.Services.AddSingleton(deviceBindingOptions);

// Reporting: summary generation + query are scoped (DbContext); the Excel writer is stateless.
builder.Services.AddScoped<IDailySummaryService, DailySummaryService>();
builder.Services.AddScoped<IReportQueryService, ReportQueryService>();
builder.Services.AddSingleton<IExcelReportExporter, ExcelReportExporter>();

// Nightly summary job (~00:30 local) + startup gap-fill.
builder.Services.AddHostedService<DailySummaryJob>();

// Nightly photo-retention job (~01:00 local): prunes check-in selfies older than RetentionDays.
builder.Services.AddHostedService<PhotoCleanupJob>();

// Background face-match worker — drains the queue enqueued by check-ins / admin re-check.
builder.Services.AddHostedService<FaceMatchWorker>();

// JWT bearer authentication (login tokens).
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

// Fail fast if the security secrets weren't supplied (env vars in production, user-secrets or
// appsettings.Development.json locally). appsettings.json ships with empty placeholders.
if (string.IsNullOrWhiteSpace(jwt.SigningKey))
    throw new InvalidOperationException(
        "Jwt:SigningKey is not configured. Set the 'Jwt__SigningKey' environment variable.");
var qrToken = builder.Configuration.GetSection(QrTokenOptions.SectionName).Get<QrTokenOptions>()
              ?? new QrTokenOptions();
if (string.IsNullOrWhiteSpace(qrToken.Secret))
    throw new InvalidOperationException(
        "QrToken:Secret is not configured. Set the 'QrToken__Secret' environment variable.");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names verbatim ("sub", "role") instead of remapping to legacy URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };

        // JWTs here are long-lived (~100 years, no refresh flow) with no revocation list, so the
        // only way to invalidate an outstanding token is to check a version embedded at issuance
        // against the current DB value on every request. change-password bumps
        // Employee.TokenVersion, which immediately fails every other previously issued token here.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirstValue("sub");
                var tvClaim = context.Principal?.FindFirstValue("tv");
                if (!Guid.TryParse(sub, out var employeeId) || !int.TryParse(tvClaim, out var tokenVersion))
                {
                    context.Fail("TokenMalformed");
                    return;
                }

                // Resolve the tenant from the token FIRST, so the TokenVersion lookup (and everything
                // after) is scoped to this session's tenant. A token with no "tid" predates Phase 1,
                // and back then Bakı Abadlıq was the only tenant there was — so such a token can only
                // be theirs. These JWTs never expire, so those sessions are still out there; resolve
                // them here EXPLICITLY rather than leaning on a global default that would also catch
                // requests which simply failed to resolve. Removable once bax sessions have all turned
                // over (a TokenVersion bump for that tenant would force it).
                var tenantCtx = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
                if (Guid.TryParse(context.Principal?.FindFirstValue("tid"), out var tenantId))
                    tenantCtx.Resolve(tenantId);
                else
                    tenantCtx.Resolve(TenantDefaults.BakiAbadligId);

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var account = await db.Employees
                    .Where(e => e.Id == employeeId)
                    .Select(e => new { e.TokenVersion, e.IsActive })
                    .FirstOrDefaultAsync();

                if (account is null || account.TokenVersion != tokenVersion)
                {
                    context.Fail("TokenVersionMismatch");
                    return;
                }

                // Deactivation must bite immediately, whatever route set the flag — the admin edit
                // bumps TokenVersion, but a bulk import or a hand-run SQL update would not, and these
                // tokens never expire. Cheap: the row is already being read for the version.
                if (!account.IsActive)
                    context.Fail("AccountDeactivated");
            }
        };
    });
builder.Services.AddAuthorization();

// CORS: the SPA calls the API cross-origin. Every tenant lives at its own <slug>.qrlog.az subdomain,
// so instead of a fixed list we allow any https://*.qrlog.az origin (plus the apex and whatever
// Cors:AllowedOrigins adds for local dev). No cookies are used (the JWT rides the Authorization
// header), so credentials are intentionally NOT allowed.
const string SpaCorsPolicy = "SpaCors";
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
bool IsAllowedOrigin(string origin)
{
    if (corsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        return true;
    return Uri.TryCreate(origin, UriKind.Absolute, out var u)
           && u.Scheme == Uri.UriSchemeHttps
           && (u.Host.Equals("qrlog.az", StringComparison.OrdinalIgnoreCase)
               || u.Host.EndsWith(".qrlog.az", StringComparison.OrdinalIgnoreCase));
}
builder.Services.AddCors(options =>
    options.AddPolicy(SpaCorsPolicy, policy =>
        policy.SetIsOriginAllowed(IsAllowedOrigin).AllowAnyHeader().AllowAnyMethod()));

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Apply pending EF Core migrations at startup so the container is self-sufficient — no manual
// `dotnet ef database update` step on the server. Single-instance deployment, so there is no
// migration race. Retry briefly so a database still coming up on a cold start doesn't crash us.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // There is no request here, so nothing has resolved a tenant — and the tenant context is now
    // fail-closed. The blocks below (admin bootstrap, emergency PIN reset) have always operated on
    // the original tenant; say so explicitly. TenantSeed re-resolves to the tenant it creates.
    scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(TenantDefaults.BakiAbadligId);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < 10)
        {
            startupLogger.LogWarning(ex, "Database not ready (attempt {Attempt}/10); retrying in 3s…", attempt);
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }

    // Seed two default schedule templates ("Gündüz" / "Gecə") for any tenant that has none, so the
    // location form's picker is never empty. Idempotent (per-tenant "has any"); TenantId is set
    // explicitly so the auto-stamp leaves it, and IgnoreQueryFilters lets one pass cover every tenant.
    try
    {
        var tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync();
        var tenantsWithSchedules = await db.Schedules.IgnoreQueryFilters()
            .Select(s => s.TenantId).Distinct().ToListAsync();
        var needing = tenantIds.Except(tenantsWithSchedules).ToList();
        foreach (var tid in needing)
        {
            db.Schedules.Add(new Schedule
            {
                TenantId = tid, Name = "Gündüz", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 126,
            });
            db.Schedules.Add(new Schedule
            {
                TenantId = tid, Name = "Gecə növbəsi", ShiftStart = new TimeOnly(22, 0), ShiftEnd = new TimeOnly(6, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 126,
            });
        }
        if (needing.Count > 0)
        {
            await db.SaveChangesAsync();
            startupLogger.LogInformation("Seeded default schedules for {Count} tenant(s).", needing.Count);
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex, "Default-schedule seeding skipped (non-fatal).");
    }

    // First-run admin bootstrap: if Bootstrap:AdminEmail/AdminPassword are configured and no
    // Admin exists yet, create one — plus a starter location, since Employee.LocationId is
    // required. Idempotent: once any Admin exists this is skipped, so it is safe to leave the
    // env vars set. Solves the production chicken-and-egg (login needs a user; users are only
    // created by an admin; seed is Development-only).
    var adminEmail = app.Configuration["Bootstrap:AdminEmail"];
    var adminPassword = app.Configuration["Bootstrap:AdminPassword"];
    if (!string.IsNullOrWhiteSpace(adminPassword) && !System.Text.RegularExpressions.Regex.IsMatch(adminPassword, @"^\d{4}$"))
    {
        startupLogger.LogError(
            "Bootstrap:AdminPassword must be exactly 4 digits (the PIN format employees log in with) — skipping admin bootstrap.");
        adminPassword = null;
    }
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword)
        && !await db.Employees.AnyAsync(e => e.Role == EmployeeRole.Admin))
    {
        var location = await db.Locations.FirstOrDefaultAsync();
        if (location is null)
        {
            location = new Location
            {
                Name = "Baş ofis",
                Latitude = 40.4093,
                Longitude = 49.8671,
                RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0),
                ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15
            };
            db.Locations.Add(location);
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Employees.Add(new Employee
        {
            FullName = app.Configuration["Bootstrap:AdminFullName"] ?? "Administrator",
            Email = adminEmail.Trim(),
            Role = EmployeeRole.Admin,
            LocationId = location.Id,
            PasswordHash = hasher.Hash(adminPassword),
            IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        startupLogger.LogInformation("Bootstrapped first admin account: {Email}", adminEmail.Trim());
    }

    // Emergency admin PIN reset — for when the admin PIN is lost with no other recovery path
    // (there's no "forgot password" flow, and unlike Bootstrap this fires every startup while the
    // vars are set, deliberately NOT idempotent-by-admin-existing). Set Ops:ResetAdminEmail +
    // Ops:ResetAdminPassword, redeploy, confirm the log line below, then remove both vars — leaving
    // them set means every future restart re-applies this PIN.
    var resetEmail = app.Configuration["Ops:ResetAdminEmail"];
    var resetPassword = app.Configuration["Ops:ResetAdminPassword"];
    if (!string.IsNullOrWhiteSpace(resetEmail) && !string.IsNullOrWhiteSpace(resetPassword))
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(resetPassword, @"^\d{4}$"))
        {
            startupLogger.LogError(
                "Ops:ResetAdminPassword must be exactly 4 digits — skipping admin PIN reset.");
        }
        else
        {
            var target = await db.Employees
                .FirstOrDefaultAsync(e => e.Email == resetEmail.Trim() && e.Role == EmployeeRole.Admin);
            if (target is null)
            {
                startupLogger.LogError(
                    "Ops:ResetAdminEmail {Email} does not match any existing Admin account — skipping reset.",
                    resetEmail);
            }
            else
            {
                var resetHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                target.PasswordHash = resetHasher.Hash(resetPassword);
                target.TokenVersion++; // also invalidates any outstanding token for this account
                await db.SaveChangesAsync();
                startupLogger.LogWarning(
                    "Ops:ResetAdminPassword applied for {Email} — REMOVE Ops:ResetAdminEmail/ResetAdminPassword now.",
                    resetEmail);
            }
        }
    }

    // One-time tenant bootstrap: stand up a brand-new tenant + a starter location + its first admin,
    // so a company can be created before there's a super-admin UI. Idempotent by slug (safe to leave
    // the vars set). Set TenantSeed:Slug, TenantSeed:Name, TenantSeed:AdminPhone, TenantSeed:AdminPin
    // (4 digits); redeploy; the log line confirms; then the vars can be removed.
    var seedSlug = app.Configuration["TenantSeed:Slug"]?.Trim().ToLowerInvariant();
    var seedName = app.Configuration["TenantSeed:Name"];
    var seedPhone = app.Configuration["TenantSeed:AdminPhone"];
    var seedPin = app.Configuration["TenantSeed:AdminPin"];
    if (!string.IsNullOrWhiteSpace(seedSlug) && !string.IsNullOrWhiteSpace(seedPhone)
        && !string.IsNullOrWhiteSpace(seedPin) && System.Text.RegularExpressions.Regex.IsMatch(seedPin, @"^\d{4}$"))
    {
        if (await db.Tenants.AnyAsync(t => t.Slug == seedSlug))
        {
            startupLogger.LogInformation("TenantSeed: tenant '{Slug}' already exists — skipping.", seedSlug);
        }
        else
        {
            var display = string.IsNullOrWhiteSpace(seedName) ? seedSlug : seedName.Trim();
            var newTenant = new Tenant { Name = display, Slug = seedSlug, DisplayName = display };
            db.Tenants.Add(newTenant);
            await db.SaveChangesAsync();

            // Everything added after this belongs to the new tenant — the auto-stamp reads the request
            // tenant, and there is no request here, so point it at the tenant we just created.
            scope.ServiceProvider.GetRequiredService<AttendanceQR.Infrastructure.Multitenancy.ITenantContext>()
                .Resolve(newTenant.Id);

            var starterLocation = new Location
            {
                Name = "Baş ofis",
                Latitude = 40.4093,
                Longitude = 49.8671,
                RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0),
                ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15
            };
            db.Locations.Add(starterLocation);
            await db.SaveChangesAsync();

            var seedHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            db.Employees.Add(new Employee
            {
                FullName = "Admin",
                Email = $"admin-{seedSlug}@baki.local",
                PhoneNumber = AttendanceQR.Api.PhoneNumbers.Normalize(seedPhone),
                Role = EmployeeRole.Admin,
                LocationId = starterLocation.Id,
                PasswordHash = seedHasher.Hash(seedPin),
                IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow,
                MustChangePin = true
            });
            await db.SaveChangesAsync();

            startupLogger.LogWarning(
                "TenantSeed: created tenant '{Slug}' + starter location + admin {Phone}. REMOVE TenantSeed:* vars now.",
                seedSlug, seedPhone);
        }
    }
}

// Ensure the photo-audit bucket exists (once, at startup). Non-fatal and skipped entirely when
// storage is unconfigured — the app still runs; photo upload/read just no-op gracefully.
var minioStartup = app.Configuration.GetSection(MinioOptions.SectionName).Get<MinioOptions>() ?? new MinioOptions();
if (!string.IsNullOrWhiteSpace(minioStartup.Endpoint))
{
    using var storageScope = app.Services.CreateScope();
    var s3 = storageScope.ServiceProvider.GetRequiredService<IAmazonS3>();
    var storageLogger = storageScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        if (!await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(s3, minioStartup.BucketName))
        {
            await s3.PutBucketAsync(new Amazon.S3.Model.PutBucketRequest { BucketName = minioStartup.BucketName });
            storageLogger.LogInformation("Created MinIO bucket '{Bucket}'.", minioStartup.BucketName);
        }
    }
    catch (Exception ex)
    {
        storageLogger.LogWarning(ex, "Could not ensure MinIO bucket '{Bucket}' exists — photo audit may be degraded.", minioStartup.BucketName);
    }
}

// No UseHttpsRedirection: TLS is terminated by the reverse proxy (Coolify) in production and the
// container listens on plain HTTP :8080; locally the API is served over HTTP too.

app.UseCors(SpaCorsPolicy);

app.UseAuthentication();

// Resolve the tenant for anonymous requests (login, activate, kiosk) from the request Origin's
// subdomain — authenticated requests are already resolved from the JWT in OnTokenValidated. Runs
// before authorization/controllers so the DbContext query filter is scoped by the time they query.
//
// /api/tenant is the one controller that must answer an unattributable request: it reads the
// un-scoped Tenants registry (never another tenant's rows), and each of its endpoints has a correct
// tenant-less answer — allow-tls is called by Caddy itself with no Origin, and branding/manifest
// return neutral defaults rather than guess an identity. Everything else is rejected below.
static bool TenantOptional(PathString path) =>
    path.StartsWithSegments("/api/tenant");

app.Use(async (context, next) =>
{
    var tenant = context.RequestServices.GetRequiredService<ITenantContext>();
    if (!tenant.IsResolved)
    {
        var slug = TenantSlug.FromRequest(context.Request);
        if (slug is not null)
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var id = await db.Tenants
                .Where(t => t.Slug == slug && t.IsActive)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync();
            if (id.HasValue)
                tenant.Resolve(id.Value);
        }
    }

    // Fail closed. An API request we cannot attribute to a tenant used to fall through to Bakı
    // Abadlıq's data — with several tenants live that is a login pool and a write target belonging
    // to someone else. Reject it here so it surfaces as a clear 400 instead of the wrong company's
    // rows (the tenant context would otherwise throw deeper in, as a 500).
    if (!tenant.IsResolved
        && context.Request.Path.StartsWithSegments("/api")
        && !TenantOptional(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "TenantUnresolved" });
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllers();

app.Run();
