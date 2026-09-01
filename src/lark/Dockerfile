# lark — the send worker. No ingress, no port.
#
# On the aspnet runtime rather than the smaller `runtime` image, because
# libs/observability carries the correlation-id middleware and so pulls in
# ASP.NET. Worth the handful of megabytes to keep the redaction list and the
# logging setup in one place; splitting the library would be the alternative
# and is not worth two projects yet.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/lark/MorganHacks.Lark/MorganHacks.Lark.csproj           src/lark/MorganHacks.Lark/
COPY src/lark/MorganHacks.Lark.Data/MorganHacks.Lark.Data.csproj src/lark/MorganHacks.Lark.Data/
COPY libs/observability/MorganHacks.Observability/MorganHacks.Observability.csproj \
     libs/observability/MorganHacks.Observability/
COPY global.json ./
COPY src/lark/Directory.Build.props src/lark/
COPY libs/Directory.Build.props     libs/
RUN dotnet restore src/lark/MorganHacks.Lark/MorganHacks.Lark.csproj

COPY src/lark/ src/lark/
COPY libs/ libs/
RUN dotnet publish src/lark/MorganHacks.Lark/MorganHacks.Lark.csproj \
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
USER $APP_UID

# No EXPOSE. lark takes no inbound traffic — the SES webhook is served by
# atlas precisely so this stays a worker.
ENTRYPOINT ["dotnet", "MorganHacks.Lark.dll"]
