# Hetzner + Coolify

Getting this repository onto a server you own, with a domain and a certificate,
deploying itself again every time you push to `main`.

Everything here is additive: no application code changes, no new dependency, and
nothing that alters how the project runs on your machine. The two compose files
at the repository root are the whole deployment.

---

## 1. What there is to deploy

Two independent stacks. They share nothing but a repository, so deploy either,
both, or one now and one later.

| | **Academy** | **LogiFlow demo** |
|---|---|---|
| Compose file | `docker-compose.coolify.yml` | `docker-compose.coolify-demo.yml` |
| What it is | The tutorial site **and** accounts, sign-in, cross-device progress sync | The logistics application: Blazor UI, API, SQL Server, Redis |
| Containers | 1 | 4 |
| RAM | ~512 MB | ~3.5 GB (SQL Server alone insists on 2 GB) |
| Disk | ~1 GB | ~6 GB |
| CPU architecture | **any** — the .NET images are multi-arch | **x86 only** — no arm64 SQL Server image exists |
| Smallest Hetzner box | CX22 / CAX11 (4 GB), shared with Coolify | CX32 / CPX31 (8 GB) |
| Runs in | `Production` | `Development`, knowingly — [read why](#7-the-logiflow-demo-stack) |
| Public surface | the whole site | the UI only; the API is unreachable from outside |

**Start with the Academy.** It is the one thing in this repository that is
genuinely worth a server: it is the entire course plus sign-in, it needs no
database engine, and it is what makes "answer questions on a laptop, review
cards on a phone" true rather than theoretical. The demo stack is a portfolio
piece with real caveats attached.

> **If the site is currently on Cloudflare** (`wrangler.jsonc`), note what
> changes. That deployment is assets-only, so it has no accounts API — sign-in
> and sync fail there and always have. Once the Academy is on Coolify, the
> Cloudflare copy becomes a second, read-only clone of the same course with a
> broken account button. Either retire it, or leave it as a static mirror and
> point people at the Coolify domain.
>
> Making the Cloudflare copy talk to the Coolify API is possible but is the
> worse arrangement: it is no longer same-origin, so it needs the Cloudflare
> hostname added to `Academy__AllowedOrigins` **and** every visitor to type the
> API address into the account panel by hand. One origin is the whole reason
> the Academy is a single container.

---

## 2. Before you touch Coolify

**On Hetzner.**

* An x86 server (CX/CPX) if you want the demo stack; a CAX (Ampere) is cheaper
  and fine for the Academy alone.
* In the Cloud Console firewall, inbound **22**, **80** and **443**. Port 80 is
  not optional — Let's Encrypt's HTTP-01 challenge uses it, and without it every
  certificate request fails with an error that talks about DNS instead.
* Coolify's own dashboard sits on **8000**. Open it to your own address only, or
  leave it closed and reach it over an SSH tunnel.

**Coolify.** If it is not installed yet, the vendor's documented one-liner is

```bash
curl -fsSL https://cdn.coollabs.io/coolify/install.sh | sudo bash
```

Then set **Settings → Instance Domain** before deploying anything. Coolify uses
it to decide it is allowed to request certificates at all, and a deploy done
before it is set comes up on plain HTTP with no obvious reason why.

**DNS, before the first deploy, not after.** An `A` record for each hostname you
intend to use, pointed at the server's IPv4 address (and `AAAA` at its IPv6 if
you have one).

```
academy.example.com.   A   203.0.113.10
demo.example.com.      A   203.0.113.10      # only if you deploy the demo stack
```

Let's Encrypt is checked at deploy time. Deploying while DNS is still
propagating gets you a working site on `http://` and a certificate error, and
the fix is to redeploy once the record resolves.

---

## 3. Connecting the repository

Coolify → **Sources → + Add → GitHub**, and follow it through creating a GitHub
App. Give it access to `LogiFlow` alone rather than every repository.

The GitHub App is worth the extra two minutes over the public-repository option,
because it is what buys the automatic part: Coolify registers a webhook, so a
push to `main` redeploys on its own with nothing to click.

---

## 4. Path A — the Academy

This is the default: Coolify clones the repository onto the server and builds
the image there. Nothing else is involved.

**Create the resource.**

1. **Projects → + Add → New Project**, then open its `production` environment.
2. **+ New Resource → Private Repository (with GitHub App)** → pick `LogiFlow`.
3. Branch `main`. **Build Pack: `Docker Compose`** — not Nixpacks, not
   Dockerfile. Nixpacks will try to guess how to build a .NET repository and
   guess wrong.
4. Set:
   * **Base Directory** `/`
   * **Docker Compose Location** `/docker-compose.coolify.yml`

Coolify parses the file and shows one service, `academy`.

**Set the secret.** Open **Environment Variables → Developer view** and paste
[`academy.env.example`](academy.env.example) with a real key in it:

```bash
openssl rand -base64 48
```

`ACADEMY_JWT_SIGNING_KEY` is **required**. The service validates it at startup
and refuses to boot without it — deliberately, because the alternative is a
service that comes up green and then 500s on the first sign-in an hour later.
Mark it as a secret so it stops appearing in logs and build output.

**Set the domain.** On the `academy` service, put `https://academy.example.com`
in the FQDN field. The `SERVICE_FQDN_ACADEMY_8080` line in the compose file has
already told Coolify which container port to route it to.

**Deploy.** The first build pulls the .NET SDK image and takes roughly 5–10
minutes on a shared-vCPU box. Later builds reuse the restore layer and take
about one.

---

## 5. Checking it actually worked

```bash
curl -sS https://academy.example.com/api/health
```

```bash
curl -sS -o /dev/null -w '%{http_code}\n' https://academy.example.com/chapters/03-oop.html
```

Then in a browser, the part that only a browser can tell you:

1. Open the site and register an account. The account panel should **not** ask
   you for an API address — the page is served by the API, so
   `site/assets/account.js` resolves it from `location.origin` on its own. If it
   does ask, the site is being served from somewhere else and you have deployed
   the wrong thing.
2. Answer a quiz question, then open the same domain on your phone and sign in.
   The progress should follow you. That round trip is the entire point of
   putting this on a server.
3. In Coolify's **Logs**, look for `Serving the tutorial site from /app/site`.
   Its absence is the failure described in the troubleshooting table below.

---

## 6. Keeping it alive

### Backups — the one thing that will actually bite you

Every account, every synced profile and every review schedule is one SQLite file
in the `academy-data` volume. Nothing else in the deployment holds state, and
nothing regenerates it.

Install `sqlite3` on the host and back it up with SQLite's own online-backup
command rather than `cp`. A running database has `-wal` and `-shm` files beside
it, and copying the three of them while they are being written is how you get a
backup that restores to yesterday's data or to nothing:

```bash
sudo apt-get install -y sqlite3
```

```bash
VOL=$(docker volume ls -q | grep academy-data | head -1) && DB="$(docker volume inspect "$VOL" --format '{{ .Mountpoint }}')/academy.db" && sudo mkdir -p /var/backups/academy && sudo sqlite3 "$DB" ".backup '/var/backups/academy/academy-$(date +%F).db'"
```

That is safe to run against a live database. Put it in `cron`, keep a fortnight,
and copy them off the machine — a backup that only exists on the server is a
backup of the disk you are afraid of losing. Coolify's **Scheduled Tasks** can
run the same thing on a schedule if you would rather see it in the UI.

To restore: stop the resource, replace `academy.db` in the volume, delete any
`-wal` and `-shm` beside it, start it again.

### Updating

Push to `main`. The webhook from step 3 does the rest.

Coolify keeps the old container running until the new one passes the health
check in the compose file, so a build that fails to start leaves the previous
version serving traffic.

### Watch the disk

A weekly `docker image prune -af --filter until=168h` on cron. Server-side
builds accumulate layers, and 40 GB disappears faster than it sounds. The
compose files already cap container logs at 3 × 10 MB per service, which is the
other classic way a small VPS fills up.

---

## 7. The LogiFlow demo stack

Optional, and read `docker-compose.coolify-demo.yml`'s header before you deploy
it. The short version:

**The API runs in `Development` mode on a server.** Two things the demo cannot
work without exist only in that environment: `/api/dev/token`, which is the only
way the Blazor UI gets a JWT at all, and the startup migration-and-seed that
creates the database. `/api/dev/token` mints an **Admin** token for any email
that asks for one.

The mitigation is that the API is not reachable from the internet. It has no
domain, no published port and no proxy route; only the `web` container can talk
to it, over the internal compose network. Give a domain to `web` and to nothing
else, and consider putting Coolify's Basic Auth in front of even that.

This is a demo of a codebase, not a production deployment of it. The honest
production shape — migrations as a separate job, real authentication, several
replicas — is `deploy/k8s/api.yaml` and `course/module-13-deployment/`.

Otherwise the steps are section 4 with two changes: **Docker Compose Location**
is `/docker-compose.coolify-demo.yml`, and the variables to paste are
[`demo.env.example`](demo.env.example) — a generated `sa` password and a
generated API signing key, both required.

Give it 10–15 minutes on the first deploy. SQL Server takes a minute to become
healthy, and only then does the API start migrating and seeding.

---

## 8. Path B — build on GitHub, pull on the server

Use this when the server is too small to build comfortably. .NET images are
built with the ~800 MB SDK image, and doing that on a 4 GB box that is also
running Coolify, a proxy and the app being replaced is how a deploy becomes an
OOM kill.

1. Run **Actions → Publish images → Run workflow**
   ([`publish-images.yml`](../../.github/workflows/publish-images.yml)). Pick
   `linux/arm64` if the server is a CAX.
2. Make the packages public at `github.com/users/<you>/packages`. Otherwise
   Coolify needs registry credentials, which is a Docker login on the host and
   one more thing to rotate.
3. In the compose file, replace the service's `build:` block:

   ```yaml
   # build:
   #   context: .
   #   dockerfile: src/LogiFlow.Academy.Api/Dockerfile
   image: ghcr.io/<you>/logiflow-academy:latest
   ```

4. Redeploy. Coolify pulls instead of building, and the deploy takes seconds.

The workflow then rebuilds on every push to `main` that touches `src/`, `site/`
or the build configuration. Point Coolify at the `:latest` tag and redeploy, or
pin the commit-SHA tag if you would rather promote deliberately.

---

## 9. When it does not work

| Symptom | Cause | Fix |
|---|---|---|
| Container exits at once; logs say `Academy:Jwt:SigningKey must be set to at least 32 bytes` | The env var is missing or shorter than 32 bytes | Set `ACADEMY_JWT_SIGNING_KEY`. Working as designed — see section 4 |
| `/api/health` answers, but every page is a 404 | The image has no `site/` in it | Check nothing added `site/` to `.dockerignore`. The exception is called out in that file; it is easy to "tidy" away |
| Endless redirect, or the page loads over HTTPS but links come back `http://` | `UseHttpsRedirection` cannot see that the proxy already did TLS | `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` — already in both compose files; confirm it survived an edit |
| Everyone gets `429` on sign-in after a few attempts | Same cause: without forwarded headers every visitor is the proxy's single IP, so they share one rate-limit bucket | Same fix. Raise `ACADEMY_AUTH_REQUESTS_PER_MINUTE` if it persists |
| All accounts gone after a redeploy | The `academy-data` volume was removed or never mounted | Check the `volumes:` block survived. This is silent — there is no error, just an empty database |
| `no such column: Username` on first sign-in | The volume holds a database from an older schema. `EnsureCreated` cannot alter what it did not build | The exception message spells out the options. Export from the site's *area riservata* first if anything in it matters |
| Build killed, no error, exit 137 | Out of memory building .NET on a small box | Path B |
| SQL Server never becomes healthy, restarts forever | Under 2 GB of RAM, **or** an arm64 host | Bigger machine, or an x86 one. There is no arm64 SQL Server image to fall back to |
| Certificate never issues | Port 80 closed, DNS not resolving yet, or Instance Domain unset | Section 2, in that order |

---

## 10. What this does not do

Named honestly, because the gaps are the interesting part:

* **No off-site backups.** Section 6 writes them to the same disk. Copying them
  somewhere else is a decision about where, and it is yours.
* **No migrations for the Academy.** It uses `EnsureCreated`, which is the right
  trade for one instance and one small schema and stops being right the moment
  the schema has to evolve without losing data. `Program.cs` says so at the call
  site.
* **One instance of everything.** Deploying is therefore a few seconds of
  downtime. Two instances of the Academy would need SQL Server instead of
  SQLite; two of the Blazor UI would need sticky sessions or a backplane.
* **No telemetry sink.** `Otlp__Endpoint` is empty, so instrumentation runs and
  ships nowhere. Point it at a collector to change that — the application code
  does not move, which is the argument for OpenTelemetry in the first place.
