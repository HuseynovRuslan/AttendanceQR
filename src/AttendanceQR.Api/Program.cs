using System.Text;
using System.Text.Json.Serialization;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
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

// Security services.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IQrTokenService, QrTokenService>();
builder.Services.AddSingleton<INonceStore, MemoryCacheNonceStore>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtService, JwtService>();

// Business services (use the scoped DbContext, so they are scoped too).
builder.Services.AddScoped<IDeviceChangeService, DeviceChangeService>();
builder.Services.AddScoped<IAttendanceQueryService, AttendanceQueryService>();

// App options (time zone for shift/UTC math). Registered as a plain singleton so the
// Application/Infrastructure layers don't need an Options package reference.
var appOptions = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
builder.Services.AddSingleton(appOptions);

// Reporting: summary generation + query are scoped (DbContext); the Excel writer is stateless.
builder.Services.AddScoped<IDailySummaryService, DailySummaryService>();
builder.Services.AddScoped<IReportQueryService, ReportQueryService>();
builder.Services.AddSingleton<IExcelReportExporter, ExcelReportExporter>();

// Nightly summary job (~00:30 local) + startup gap-fill.
builder.Services.AddHostedService<DailySummaryJob>();

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
    });
builder.Services.AddAuthorization();

// CORS: the SPA calls the API cross-origin. Allowed origins come from configuration
// ("Cors:AllowedOrigins", comma-separated) — the Vite dev server locally, the deployed
// frontend domain in production. No cookies are used (the JWT travels in the Authorization
// header), so credentials are intentionally NOT allowed.
const string SpaCorsPolicy = "SpaCors";
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
    options.AddPolicy(SpaCorsPolicy, policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

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
}

// No UseHttpsRedirection: TLS is terminated by the reverse proxy (Coolify) in production and the
// container listens on plain HTTP :8080; locally the API is served over HTTP too.

app.UseCors(SpaCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
