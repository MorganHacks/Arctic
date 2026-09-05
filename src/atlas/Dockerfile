# atlas — the API.
#
# Built from the repository root rather than from src/atlas, because atlas
# references libs/observability, libs/audit and lark's data library. A context
# of src/atlas cannot see any of them, and the alternative — copying them in
# beforehand — is a build that works in CI and not on a laptop.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so restore is cached against dependency changes rather
# than against every source edit. This is most of the build time.
COPY src/atlas/MorganHacks.Api/MorganHacks.Api.csproj                 src/atlas/MorganHacks.Api/
COPY src/atlas/MorganHacks.Identity/MorganHacks.Identity.csproj       src/atlas/MorganHacks.Identity/
COPY src/atlas/MorganHacks.Applications/MorganHacks.Applications.csproj src/atlas/MorganHacks.Applications/
COPY src/atlas/MorganHacks.Profiles/MorganHacks.Profiles.csproj       src/atlas/MorganHacks.Profiles/
COPY src/atlas/MorganHacks.Migrations/MorganHacks.Migrations.csproj   src/atlas/MorganHacks.Migrations/
COPY src/lark/MorganHacks.Lark.Data/MorganHacks.Lark.Data.csproj      src/lark/MorganHacks.Lark.Data/
COPY libs/observability/MorganHacks.Observability/MorganHacks.Observability.csproj \
     libs/observability/MorganHacks.Observability/
COPY libs/audit/MorganHacks.Audit/MorganHacks.Audit.csproj \
     libs/audit/MorganHacks.Audit/
COPY libs/features/MorganHacks.Features/MorganHacks.Features.csproj \
     libs/features/MorganHacks.Features/
COPY global.json ./
COPY src/atlas/Directory.Build.props src/atlas/
COPY src/lark/Directory.Build.props  src/lark/
COPY libs/Directory.Build.props      libs/
RUN dotnet restore src/atlas/MorganHacks.Api/MorganHacks.Api.csproj

COPY src/atlas/ src/atlas/
COPY src/lark/MorganHacks.Lark.Data/ src/lark/MorganHacks.Lark.Data/
COPY libs/ libs/
RUN dotnet publish src/atlas/MorganHacks.Api/MorganHacks.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Npgsql probes for Kerberos support on every connection. Without this library
# it still connects, but prints "Cannot load library libgssapi_krb5.so.2" to
# stderr at every startup — a permanent error-shaped line in the logs for a
# feature we do not use, which is how people learn to ignore real ones.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Non-root. The image ships an unprivileged user for exactly this, and a
# container that does not need root should not have it.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "MorganHacks.Api.dll"]
