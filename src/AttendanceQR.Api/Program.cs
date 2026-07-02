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

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
