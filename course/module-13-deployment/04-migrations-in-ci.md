# 3. Migrations in CI, not at startup

> Part of [Module 13 — Configuration, security and deployment](README.md), section 3.
> Previous: [1. Configuration precedence](02-configuration.md) ·
> Next: [4. Containerising](README.md#4-containerising)

---

```csharp
// The line in almost every tutorial. Do not ship it.
await app.Services.GetRequiredService<LogiFlowDbContext>().Database.MigrateAsync();
```

It works. That is the problem — it works right up until the first time it matters, and then it fails
in a way that is hard to see and hard to undo.

**Four reasons it is wrong:**

**Concurrency.** Two instances starting together — a rolling deploy, a scaled-out App Service, a
Kubernetes rollout — both run the migration. EF takes a lock in recent versions, so usually one waits
rather than corrupting; but the second instance is now blocked behind a long migration, its health
checks fail, and the orchestrator starts killing pods mid-migration.

**Permissions.** The application's own connection string now needs `ALTER`/`CREATE`/`DROP` on the
schema, permanently. So a SQL injection hole or a compromised container can drop tables rather than
merely read them. Least privilege says the runtime account should have `SELECT`, `INSERT`, `UPDATE`,
`DELETE` and nothing else.

**Visibility.** A failed migration surfaces as a container that will not start. The actual error is
somewhere in the application log, and the rollback story is "redeploy the old image", which does not
undo a half-applied schema change.

**Review.** Nobody sees the SQL before it runs against production. In many Italian *gestionale* shops
a DBA will simply refuse this, and they are right to.

## Ship a script instead

```bash
dotnet ef migrations script --idempotent \
  --project src/LogiFlow.Infrastructure \
  --startup-project src/LogiFlow.Api \
  --output artifacts/migrate.sql
```

**`--idempotent` is the flag that matters.** It wraps each migration in a check against
`__EFMigrationsHistory`, so the script is safe to run twice, safe to run against an environment that
is already partly up to date, and safe to re-run after a partial failure.

The output is a file you can **read, review, diff and attach to a release**. That single property is
most of the argument.

## The pipeline shape

```yaml
stages:
  - stage: Build
    jobs:
      - job: BuildAndTest
        steps:
          - script: dotnet build LogiFlow.slnx -c Release
          - script: dotnet test LogiFlow.slnf -c Release --no-build
          - script: |
              dotnet tool install --global dotnet-ef
              dotnet ef migrations script --idempotent \
                --project src/LogiFlow.Infrastructure \
                --startup-project src/LogiFlow.Api \
                --output $(Build.ArtifactStagingDirectory)/migrate.sql
          - publish: $(Build.ArtifactStagingDirectory)
            artifact: drop

  - stage: DeployStaging
    jobs:
      - deployment: Staging
        strategy:
          runOnce:
            deploy:
              steps:
                - task: SqlAzureDacpacDeployment@1     # 1. schema first
                  inputs: { deployType: SqlTask, sqlFile: $(Pipeline.Workspace)/drop/migrate.sql }
                - task: AzureWebApp@1                  # 2. then the code
```

Three properties worth naming, because they are the interview answer:

**Build once, deploy many.** The artifact built in the first stage is the artifact that reaches
production. Rebuilding per environment means production runs something that was never tested.

**Schema before code**, and the two are separate steps with separate failure modes. A migration that
fails stops the deployment *before* any new code is running.

**Environments are pipeline environments with approvals**, not branches. Production gets a manual
gate; staging does not.

Migrations also need a **more privileged connection string than the application's**, supplied only to
that step. That is the whole point of separating them.

## Expand and contract

Because schema and code deploy separately — and because a rolling deployment runs old and new code
**at the same time** — no single migration may break the version already running.

Renaming `Notes` to `Comments` in one step breaks every live instance of the previous release for the
length of the rollout.

The safe sequence spans three releases:

1. **Expand** — add `Comments`, keep `Notes`. Code writes both, reads `Notes`.
2. **Migrate** — backfill `Comments`. Next release reads `Comments`, still writes both.
3. **Contract** — a later release stops writing `Notes`; a later migration drops it.

Slower, and it is the difference between a deployment and an outage. The same discipline appears in
[the outbox](../module-06-efcore/07-outbox-pattern.md) and in
[Business Central extensions](../../site/chapters/33-business-central.html): never delete somebody's
data in the same step that stops using it.

**Additive changes are safe** — a nullable column, a new table, a new index (build it `ONLINE` on a
large table). **Destructive changes need the dance** — drop, rename, narrow a type, add `NOT NULL`.

## Long-running migrations

A backfill over a large table locks it, and a lock during a deployment is an outage.

- **Batch it.** Update ten thousand rows at a time in a loop with a delay, rather than one statement
  over ten million.
- **Or separate it entirely.** The migration adds the column; a background job fills it; a later
  release starts reading it. That is expand-and-contract with the expensive part outside the
  deployment window.
- **Time it.** A migration that takes four minutes on production data must not be discovered during
  the deploy. Test it against a restored copy of production, not against your seeded local database.

## Rollback, honestly

Most teams **roll forward**: if a migration is wrong, write another that corrects it. That is a
legitimate strategy and it is what `Down` methods being untested really means in practice.

Say that plainly rather than claiming a rollback capability you have never exercised. And note that
some changes cannot be rolled back at all — a dropped column takes its data with it, which is the
strongest argument for contract steps happening long after everything else.

## The mistakes

**`MigrateAsync()` at startup.** All four reasons above.

**`EnsureCreated`.** No migration history at all; the database cannot be evolved, only dropped.

**A script that is not idempotent.** Cannot be safely re-run after a partial failure.

**One connection string for both.** The application keeps schema permissions forever.

**Testing the migration only against an empty database.** Every interesting failure — a backfill
default, a `NOT NULL` on existing rows, a unique index over duplicate data — needs real data to
appear.

**A rename in one release.** Breaks the running version for the length of the rollout.

## Try it

```bash
dotnet ef migrations script --idempotent \
  --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api \
  --output migrate.sql
```

Read it. Every migration is wrapped in
`IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'…')`, which is exactly
what makes re-running safe. Run it twice against the local database and confirm the second run is a
no-op.

Then add a `NOT NULL` column with no default to a table that has rows, generate the script, and read
what EF produced — before it runs anywhere real.

## What to remember

- Never migrate at startup: concurrency, excess permissions, poor visibility, no review.
- Generate an `--idempotent` script in the build and run it as its own deployment step.
- Build once, deploy many. The tested artifact is the deployed artifact.
- Schema first, then code, with separate failure modes.
- The migration step gets a privileged connection string; the application does not.
- Expand, migrate, contract — old and new code run together during a rollout.
- Batch large backfills, or move them out of the deployment entirely.
- Roll forward, and say so rather than claiming an untested rollback.

**Code:** [`Migrations/`](../../src/LogiFlow.Infrastructure/Persistence/Migrations/) ·
[`DesignTimeDbContextFactory.cs`](../../src/LogiFlow.Infrastructure/Persistence/DesignTimeDbContextFactory.cs)

**Next:** [4. Containerising](README.md#4-containerising)
