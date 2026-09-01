# harbor — the gateway. The only thing exposed to the internet.
#
# Built from the repository root, because harbor references
# libs/observability for logging and the correlation id.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/harbor/MorganHacks.Harbor/MorganHacks.Harbor.csproj src/harbor/MorganHacks.Harbor/
COPY libs/observability/MorganHacks.Observability/MorganHacks.Observability.csproj \
     libs/observability/MorganHacks.Observability/
COPY global.json ./
COPY src/harbor/Directory.Build.props src/harbor/
COPY libs/Directory.Build.props       libs/
RUN dotnet restore src/harbor/MorganHacks.Harbor/MorganHacks.Harbor.csproj

COPY src/harbor/ src/harbor/
COPY libs/ libs/
RUN dotnet publish src/harbor/MorganHacks.Harbor/MorganHacks.Harbor.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "MorganHacks.Harbor.dll"]
