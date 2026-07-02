# Backend image for AttendanceQR.Api — multi-stage.
# Build CONTEXT is the repository ROOT (so all referenced projects are available):
#   docker build -f src/AttendanceQR.Api/Dockerfile -t attendanceqr-api .

# ---- build stage: full SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first so `restore` is cached until a .csproj actually changes.
COPY src/AttendanceQR.Domain/AttendanceQR.Domain.csproj          src/AttendanceQR.Domain/
COPY src/AttendanceQR.Application/AttendanceQR.Application.csproj src/AttendanceQR.Application/
COPY src/AttendanceQR.Infrastructure/AttendanceQR.Infrastructure.csproj src/AttendanceQR.Infrastructure/
COPY src/AttendanceQR.Api/AttendanceQR.Api.csproj                src/AttendanceQR.Api/
RUN dotnet restore src/AttendanceQR.Api/AttendanceQR.Api.csproj

# Copy the rest of the source and publish a trimmed, release build.
COPY src/ src/
RUN dotnet publish src/AttendanceQR.Api/AttendanceQR.Api.csproj \
    -c Release -o /app --no-restore

# ---- runtime stage: small ASP.NET runtime only (no SDK, no source) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql probes for the Kerberos/GSSAPI library on connect; without it the first DB
# connection logs a noisy "libgssapi_krb5.so.2: cannot open shared object file" and fails
# once before falling back to password auth. Installing it keeps cold-start logs clean.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# The official .NET runtime images ship a non-root user "app" (UID 1654) — use it.
# Files copied above are world-readable, so no chown is needed to run them.
USER app

# Coolify proxies to this port; ASPNETCORE_ENVIRONMENT defaults to Production (so
# appsettings.Development.json is never read here — and it isn't in the image anyway).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AttendanceQR.Api.dll"]
