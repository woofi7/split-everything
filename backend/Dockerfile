# Build the API. Restore is its own layer so a code change does not re-download
# every package.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY SplitEverything.sln ./
COPY src/SplitEverything.Domain/*.csproj src/SplitEverything.Domain/
COPY src/SplitEverything.Application/*.csproj src/SplitEverything.Application/
COPY src/SplitEverything.Infrastructure/*.csproj src/SplitEverything.Infrastructure/
COPY src/SplitEverything.Api/*.csproj src/SplitEverything.Api/
COPY tests/SplitEverything.Tests/*.csproj tests/SplitEverything.Tests/
RUN dotnet restore src/SplitEverything.Api/SplitEverything.Api.csproj

COPY . .
RUN dotnet publish src/SplitEverything.Api/SplitEverything.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Two things this image cannot do without, neither of which the runtime carries.
#
# libgssapi-krb5-2: Npgsql looks for Kerberos while working out how to authenticate,
# and without it the first command on every start failed with "Cannot load library
# libgssapi_krb5.so.2" before succeeding on a retry. Postgres authenticates with a
# password here; the library is only present so the probe finds what it looks for.
#
# curl: the health check below had nothing to make a request with. This image has no
# wget, no curl and no netcat, so the check failed every time with "wget: not found"
# and the container was marked unhealthy from its first breath - which also blocked
# anything waiting on it being healthy. curl earns its two megabytes twice over:
# once for the check, once for whoever is inside the container asking why.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*

# Receipts live on a mounted volume; the directory has to exist and be writable
# by the non-root user before the app starts.
RUN mkdir -p /data/receipts && chown -R app:app /data
USER app

COPY --from=build --chown=app:app /app ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 8080

# Liveness only: readiness checks the database and would keep the container
# marked unhealthy through a brief Postgres restart.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD ["curl", "-fsS", "http://127.0.0.1:8080/api/health"]

ENTRYPOINT ["dotnet", "SplitEverything.Api.dll"]
