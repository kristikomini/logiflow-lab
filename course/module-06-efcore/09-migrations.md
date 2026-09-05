# 9. Migrations

> Part of [Module 06 — EF Core in depth](README.md), section 9.
> Previous: [8. The transactional outbox](07-outbox-pattern.md) · Back to [the module](README.md)

---

A migration is a **versioned, ordered, reviewable description of a schema change**. EF generates it
by diffing your model against a snapshot of the last one, and that generation is the useful part —
the review is where the value is.

```bash
dotnet ef migrations add AddOrderNotes \
  --project src/LogiFlow.Infrastructure \
  --startup-project src/LogiFlow.Api
```

Two projects, because the model lives in Infrastructure and the configuration — the connection
string, the DI registration — lives in the API. This repository also ships a
[`DesignTimeDbContextFactory`](../../src/LogiFlow.Infrastructure/Persistence/DesignTimeDbContextFactory.cs)
so the tooling can build a context without starting the application.

## What gets generated

Three files, and you should look at all three:

| File | Contains |
|---|---|
| `20260811095935_AddOrderNotes.cs` | `Up` and `Down` — the change and its reverse |
| `…_AddOrderNotes.Designer.cs` | the model snapshot **at that migration** |
| `LogiFlowDbContextModelSnapshot.cs` | the model snapshot **now** |

The timestamp prefix is the ordering. The snapshot is what the next diff compares against — which is
why the snapshot file **must be committed**, and why a merge conflict in it is a genuine conflict
rather than noise (see below).

## Read the migration before you run it

This is the whole discipline, and the step people skip.

EF's diff is good and it is not clairvoyant. It cannot know intent, so a rename looks exactly like a
delete plus an add:

```csharp
// What EF generates when you rename a property:
migrationBuilder.DropColumn(name: "Notes", table: "Orders");
migrationBuilder.AddColumn<string>(name: "Comments", table: "Orders", ...);

// What you meant:
migrationBuilder.RenameColumn(name: "Notes", table: "Orders", newName: "Comments");
```

The generated version compiles, runs, and **destroys every value in that column**. Nothing warns you.
Editing the generated file is not a hack — it is the expected workflow, and it is why the tool
generates a file you can read rather than applying a diff directly.

The same applies to adding a non-nullable column to a table with rows: EF emits a default that may be
nonsense for your domain. Usually the honest sequence is three migrations — add nullable, backfill,
then make it required.

## Never `EnsureCreated`, and never migrate at startup

```csharp
// ✗ EnsureCreated: no migration history at all. Cannot be evolved, only dropped.
await db.Database.EnsureCreatedAsync();

// ✗ Migrate on startup: convenient in a tutorial, wrong in production.
await db.Database.MigrateAsync();
```

`EnsureCreated` and migrations are mutually exclusive: a database created that way has no
`__EFMigrationsHistory` table, so the first real migration fails and the only recovery is dropping it.

Migrating at startup is worse because it *works*, until:

- **Two instances start at once** and both run the migration. One fails; sometimes both partially
  apply.
- **The app needs schema permissions in production**, so a compromised application can `DROP TABLE`.
- **A failed migration takes the deployment down** in a way you cannot see or roll back cleanly.

Instead, generate a script and run it as its own deployment step:

```bash
dotnet ef migrations script --idempotent \
  --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api \
  --output migrate.sql
```

`--idempotent` wraps every migration in a check against the history table, so running it twice is
safe. That script is reviewable by a DBA, runnable by a pipeline, and diffable in a pull request —
see [module 13 section 3](../module-13-deployment/04-migrations-in-ci.md).

## Expand and contract

The rule that makes zero-downtime deployment possible: **during a deploy, the old and new versions of
your application both run against the same database.** So no single migration may break the old
version.

Renaming `Notes` to `Comments` in one step breaks every running instance of the previous release, for
as long as the rollout takes.

The safe sequence is three releases:

1. **Expand** — add `Comments`, keep `Notes`. Deploy code that writes both and reads `Notes`.
2. **Migrate** — backfill `Comments` from `Notes`. Deploy code that reads `Comments`.
3. **Contract** — a later release drops `Notes`, once nothing reads it.

Slower, and it is the difference between a deployment and an outage. It is also why the same
discipline appears in the [outbox](07-outbox-pattern.md) and in
[Business Central extensions](../../site/chapters/33-business-central.html): you never delete
somebody's data in the same step that stops using it.

## The merge conflict

Two developers each add a migration on their own branch. Both edit
`LogiFlowDbContextModelSnapshot.cs`. Git reports a conflict.

**Do not hand-merge the snapshot.** It is generated, it is large, and a subtly wrong merge produces a
model that diffs incorrectly for the rest of the project's life.

```bash
# Take main's version, remove yours, regenerate on top.
dotnet ef migrations remove --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api
git checkout --theirs src/LogiFlow.Infrastructure/Persistence/Migrations/LogiFlowDbContextModelSnapshot.cs
dotnet ef migrations add YourMigrationAgain --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api
```

`migrations remove` only works on a migration that has **not been applied to a shared database**. Once
it is out in the world, the only correct move is a new migration that corrects it forward.

## Data in migrations

`migrationBuilder.Sql("UPDATE ...")` is legitimate for a backfill and is the right home for it —
versioned, ordered, applied exactly once. Two cautions:

- **A backfill over millions of rows locks the table.** Batch it, or run it as a separate job the
  migration merely enables.
- **Reference data is not a migration concern.** Seeding countries and currencies belongs in
  `DatabaseSeeder`, which is idempotent and can run repeatedly.

## The mistakes

**Not reading the generated file.** The rename-as-drop is the classic; there are others.

**Editing an applied migration.** The history table records that it ran. Change it and every
environment silently diverges. Always correct forward.

**A `Down` you have never tested.** Most teams roll forward rather than down in production, which is
fine — but then say so, rather than pretending you have a rollback.

**Committing the migration and not the snapshot.** The next developer's diff regenerates your change.

**A migration named `Update1`.** Six of them and nobody can find anything. Name the change.

## Try it

```bash
dotnet ef migrations add AddOrderNotes --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api
```

Read the generated `Up`. Then rename the property in the entity, add another migration, and read
*that* one — a `DropColumn` and an `AddColumn`, which would silently delete the data. Fix it by hand
to a `RenameColumn`, then:

```bash
dotnet ef migrations script --idempotent --output migrate.sql
```

and read the SQL you would actually ship.

## What to remember

- A migration is a reviewable file. Read it before you run it, every time.
- EF cannot see a rename — it emits drop plus add, and that deletes data.
- Never `EnsureCreated` in anything you intend to evolve.
- Never migrate at startup: concurrent instances, excess permissions, unclear failures.
- Ship an `--idempotent` script as its own deployment step.
- Expand, migrate, contract — old and new code run against the same schema during a rollout.
- Never hand-merge the model snapshot; remove, take theirs, regenerate.
- Never edit an applied migration. Correct forward.
- Backfills belong in migrations; reference data belongs in an idempotent seeder.

**Code:** [`Migrations/`](../../src/LogiFlow.Infrastructure/Persistence/Migrations/) ·
[`DesignTimeDbContextFactory.cs`](../../src/LogiFlow.Infrastructure/Persistence/DesignTimeDbContextFactory.cs) ·
[`DatabaseSeeder.cs`](../../src/LogiFlow.Infrastructure/Persistence/Seed/DatabaseSeeder.cs)

**Back to:** [Module 06](README.md)
