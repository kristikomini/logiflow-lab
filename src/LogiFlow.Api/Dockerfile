# =============================================================================
# LogiFlow.Api — the production image.
#
# Build from the REPOSITORY ROOT, not from this folder:
#
#     docker build -f src/LogiFlow.Api/Dockerfile -t logiflow-api .
#
# The context has to be the root because central package management lives there
# (Directory.Packages.props), so does the shared build configuration, and so do
# the projects this one references. A Dockerfile whose context is its own folder
# cannot see any of that — which is why the `-f` form exists.
#
# Covered in: course/module-13-deployment/ — golden rule 10: "aspnet runtime
# image, not sdk; restore before copying source; run as non-root."
# =============================================================================

# ── Stage 1: build ───────────────────────────────────────────────────────────
# The SDK image is ~800 MB. Nothing from it reaches the final image except the
# compiled output, which is the entire point of a multi-stage build.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# RESTORE BEFORE COPYING SOURCE.
# Docker caches each layer and invalidates everything after the first line whose
# inputs changed. Source changes on every commit; the dependency graph changes
# perhaps monthly. Copying only the project files first means the restore layer
# survives a source edit, and `docker build` after a one-line change takes
# seconds instead of minutes.
#
# The packages.lock.json files come along deliberately: with
# ContinuousIntegrationBuild set below, RestoreLockedMode makes a restore that
# would resolve anything different fail instead. An image is the last place you
# want a dependency you never tested.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/LogiFlow.Domain/LogiFlow.Domain.csproj src/LogiFlow.Domain/
COPY src/LogiFlow.Domain/packages.lock.json src/LogiFlow.Domain/
COPY src/LogiFlow.Application/LogiFlow.Application.csproj src/LogiFlow.Application/
COPY src/LogiFlow.Application/packages.lock.json src/LogiFlow.Application/
COPY src/LogiFlow.Infrastructure/LogiFlow.Infrastructure.csproj src/LogiFlow.Infrastructure/
COPY src/LogiFlow.Infrastructure/packages.lock.json src/LogiFlow.Infrastructure/
COPY src/LogiFlow.Api/LogiFlow.Api.csproj src/LogiFlow.Api/
COPY src/LogiFlow.Api/packages.lock.json src/LogiFlow.Api/

ENV ContinuousIntegrationBuild=true
RUN dotnet restore src/LogiFlow.Api/LogiFlow.Api.csproj

# Now the source. Everything below this line reruns on any code change.
COPY src/ src/

RUN dotnet publish src/LogiFlow.Api/LogiFlow.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    # The publish output is what ships, so warnings are errors here too — a
    # container build is not the place to relax a rule the CI enforces.
    -p:TreatWarningsAsErrors=true

# ── Stage 2: the image that actually runs ────────────────────────────────────
# `aspnet`, not `sdk`: ~100 MB instead of ~800, and no compiler, no NuGet client
# and no source on a production host. The smaller attack surface is the point;
# the smaller pull is a bonus.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# curl, purely so that `docker compose` can health-check this container.
#
# This is a real trade-off, written down rather than hidden: a health probe in
# Compose runs INSIDE the container, so the container needs a client. Under
# Kubernetes you would delete this layer — the kubelet probes over HTTP from
# outside, which is why module 13's golden rule 11 puts liveness and readiness
# in the orchestrator's YAML and not here. See deploy/k8s/ for that version.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# App_Data holds the .eml files the File mail transport writes. Created here and
# owned by the app user, because a process that cannot write its own data
# directory fails at the first email rather than at startup.
RUN mkdir -p /app/App_Data/mail && chown -R $APP_UID:$APP_UID /app/App_Data

# NON-ROOT. $APP_UID is 1654, defined by the base image; the user already exists.
# A container process running as root is root on the host kernel if anything
# escapes, and nothing in this application needs a privileged port or a
# privileged file.
USER $APP_UID

# 8080 is the .NET 8+ default for a non-root container: binding 80 requires
# privileges the user above deliberately does not have.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "LogiFlow.Api.dll"]
