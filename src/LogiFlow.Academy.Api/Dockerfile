# =============================================================================
# LogiFlow.Academy.Api — the tutorial site and its accounts, as one image.
#
#     docker build -f src/LogiFlow.Academy.Api/Dockerfile -t logiflow-academy .
#     docker run -p 5280:8080 -v academy:/app/App_Data logiflow-academy
#
# This is the one image in the repository worth actually deploying somewhere:
# it is the whole tutorial plus sign-in, it needs no database server, and it is
# how several people (or one person on a laptop and a phone) share progress.
#
# Two things distinguish it from the other two Dockerfiles:
#
#   * IT SHIPS site/. Program.cs walks up from the content root looking for a
#     folder with site/index.html in it, so the copy below lands the site where
#     that search finds it. Miss this and the service starts, serves its API
#     perfectly, and 404s every page — which is a much more confusing failure
#     than not starting at all.
#
#   * IT NEEDS A VOLUME. SQLite writes to App_Data, and so does the generated
#     development signing key. Without a volume, every account and every synced
#     profile disappears when the container is replaced — the classic way to
#     lose data in a container without ever seeing an error.
#
# Covered in: src/LogiFlow.Academy.Api/README.md
# =============================================================================

# PINNED TO THE PATCH, and it has to stay in step with global.json.
#
# global.json asks for 10.0.300 with "rollForward": "disable", so the SDK in
# this image must be EXACTLY that. The floating `sdk:10.0` tag is whatever
# Microsoft published most recently (currently 10.0.400), and the mismatch
# fails the restore below with "A compatible .NET SDK was not found" — a
# message that names global.json but not the tag, which is the confusing half.
#
# Bump global.json and this line together, or not at all.
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/LogiFlow.Academy.Api/LogiFlow.Academy.Api.csproj src/LogiFlow.Academy.Api/
COPY src/LogiFlow.Academy.Api/packages.lock.json src/LogiFlow.Academy.Api/

ENV ContinuousIntegrationBuild=true
RUN dotnet restore src/LogiFlow.Academy.Api/LogiFlow.Academy.Api.csproj

COPY src/LogiFlow.Academy.Api/ src/LogiFlow.Academy.Api/

RUN dotnet publish src/LogiFlow.Academy.Api/LogiFlow.Academy.Api.csproj \
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

# The tutorial itself. Copied at the end because it changes far more often than
# the service does, so it should invalidate as little as possible above it.
COPY site/ /app/site/

RUN mkdir -p /app/App_Data && chown -R $APP_UID:$APP_UID /app/App_Data

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Where the SQLite file and the signing key live. Mount a volume here or lose
# both on every deploy.
VOLUME /app/App_Data

ENTRYPOINT ["dotnet", "LogiFlow.Academy.Api.dll"]
