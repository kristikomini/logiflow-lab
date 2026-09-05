# =============================================================================
# LogiFlow.Web — the Blazor UI, as an image.
#
#     docker build -f src/LogiFlow.Web/Dockerfile -t logiflow-web .
#
# Same shape as src/LogiFlow.Api/Dockerfile, which carries the full commentary
# on why the restore comes before the source and why the runtime image is not
# the SDK one. Two differences are worth naming:
#
#   * This project references NOTHING. It is a client of the API over HTTP, so
#     its restore layer copies one .csproj, not four. That is the architecture
#     showing up in the build file.
#
#   * It is InteractiveServer Blazor, so each browser tab holds a SignalR
#     circuit with server-side state. Scaling this horizontally needs sticky
#     sessions or a backplane — see the compose file, which runs one instance of
#     this and two of the API for exactly that reason.
# =============================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/LogiFlow.Web/LogiFlow.Web.csproj src/LogiFlow.Web/
COPY src/LogiFlow.Web/packages.lock.json src/LogiFlow.Web/

ENV ContinuousIntegrationBuild=true
RUN dotnet restore src/LogiFlow.Web/LogiFlow.Web.csproj

COPY src/LogiFlow.Web/ src/LogiFlow.Web/

RUN dotnet publish src/LogiFlow.Web/LogiFlow.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:TreatWarningsAsErrors=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "LogiFlow.Web.dll"]
