# deploy/

Everything about getting LogiFlow onto something that is not your laptop.

Nothing in here is imported by a build or referenced by a project. It is read, adapted, and
mostly it is here so that the deployment module has something concrete to point at — a rule
about probes is worth about a tenth of a manifest that shows the probes.

## The images

Three Dockerfiles, all built **from the repository root**:

```bash
docker build -f src/LogiFlow.Api/Dockerfile          -t logiflow-api .
docker build -f src/LogiFlow.Web/Dockerfile          -t logiflow-web .
docker build -f src/LogiFlow.Academy.Api/Dockerfile  -t logiflow-academy .
```

The `-f` matters. The context has to be the root, because central package management
(`Directory.Packages.props`), the shared build settings and the referenced projects all live
above `src/`. A Dockerfile whose context is its own folder cannot see any of them.

Each one is a multi-stage build: the SDK image compiles, the `aspnet` image runs. That is
~100 MB instead of ~800, and — more to the point — no compiler, no NuGet client and no source
on a host that is serving traffic.

### Running the whole thing in containers

```bash
docker compose --profile app up -d --build
```

| | |
| --- | --- |
| The UI | <http://localhost:8080> |
| API instance 1 | <http://localhost:8081/scalar/v1> |
| API instance 2 | <http://localhost:8082/scalar/v1> |
| The tutorial + accounts | <http://localhost:5280> |
| Telemetry | <http://localhost:18888> |
| Captured email | <http://localhost:8025> |

**Two API instances is the point of this profile.** The README says the in-memory cache
fallback "is silently wrong the moment you scale out"; here you can watch it. Both instances
share the Redis, so the cache is genuinely distributed. Take that away —

```bash
docker compose --profile app stop api api-2
# unset ConnectionStrings__Redis on both services in docker-compose.yml
docker compose --profile app up -d api api-2
```

— then write through `:8081` and read through `:8082`. The second instance serves its own
stale copy, cheerfully, with nothing in any log to say so. That is the failure the interface
was hiding.

Without `--profile app` nothing above starts, and `docker compose up -d` still gives you the
plain dependency stack the root README describes.

## Kubernetes

[`k8s/api.yaml`](k8s/api.yaml) is not a production manifest — there is no Ingress, no TLS and
no autoscaler, because those depend on a cluster you and I do not share. It exists to show the
four decisions that *are* the same everywhere:

1. **Two probes that ask different questions.** Liveness is "is this process wedged?" and must
   depend on nothing external — a liveness probe that checks the database will restart every
   healthy pod during a database blip, turning a recoverable outage into a total one. Readiness
   is "should this pod get traffic?" and *should* check the database, because failing it removes
   the pod from the Service without restarting it, and it rejoins by itself.
2. **A startup probe**, so the other two can use short intervals without a slow cold start
   looking like a wedged process.
3. **Non-root, read-only root filesystem, all capabilities dropped.** The image already runs
   as UID 1654; saying it again in the manifest means the cluster enforces it even if somebody
   rebuilds the image without the `USER` line.
4. **Migrations as a Job, not a startup step.** Three replicas starting together means three
   processes migrating one database at once. See
   [module 13](../course/module-13-deployment/04-migrations-in-ci.md).

The `HEALTHCHECK` question is worth spelling out, because the two platforms genuinely differ.
Compose runs its health check *inside* the container, so the image needs a client — which is
why the Dockerfiles install `curl` and say so. Kubernetes probes over HTTP from *outside*, so
on that path the `curl` layer is dead weight and you would delete it.

## Coolify, on a Hetzner box

The two files above are read and adapted. [`coolify/`](coolify/) is the one path
in here that is meant to be *run*: two compose files at the repository root that
Coolify deploys straight from GitHub, with a domain and a certificate.

| | |
| --- | --- |
| The tutorial site and its accounts | `docker-compose.coolify.yml` |
| LogiFlow itself, as a demo | `docker-compose.coolify-demo.yml` |

DNS, secrets, backups and the failure modes: [`coolify/README.md`](coolify/README.md).
