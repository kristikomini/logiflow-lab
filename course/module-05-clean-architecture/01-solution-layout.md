# 1. The solution layout — what folder holds what

> Part of [Module 05 — Clean Architecture and Domain-Driven Design](README.md), section 1.
> Next: [2. Entities vs value objects](02-entities-and-value-objects.md)

---

Every other chapter in this module explains *why* a piece of code belongs where it does. This one
is the map: the projects an enterprise .NET solution is cut into, the folders inside each, the
kind of file that lives in each folder, and — the part tutorials skip — **the rule that tells you
which one a new file belongs to**.

It describes this repository, so every path below is real and clickable.

---

## 1. Four projects, and why exactly four

```
LogiFlow.slnx
│
├── src/
│   ├── LogiFlow.Domain            ← business rules.   References: NOTHING
│   ├── LogiFlow.Application       ← use cases.        References: Domain
│   ├── LogiFlow.Infrastructure    ← the outside world. References: Application (+ Domain)
│   └── LogiFlow.Api               ← HTTP.             References: all three
│
├── tests/
│   ├── LogiFlow.Domain.Tests           unit, no I/O, milliseconds
│   ├── LogiFlow.Application.Tests      unit, handlers over fakes
│   ├── LogiFlow.Infrastructure.Tests   EF Core against a real database
│   ├── LogiFlow.Api.IntegrationTests   WebApplicationFactory, real pipeline
│   └── LogiFlow.ArchitectureTests      asserts the arrows above still point inward
│
├── Directory.Build.props          how every project compiles
├── Directory.Packages.props       which version of every package (declared once)
└── global.json                    which SDK builds it
```

The project count is not a style choice — **each boundary is a dependency you want the compiler
to enforce.** `Domain` cannot call EF Core because EF Core is not reachable from there. Open
[`LogiFlow.Domain.csproj`](../../src/LogiFlow.Domain/LogiFlow.Domain.csproj): there is not a
single `PackageReference` or `ProjectReference` in it. That emptiness *is* the architecture.

If a boundary is not enforced, it does not exist. Folders inside one project are a convention;
project references are a compile error. That is the whole argument for four projects instead of
four folders.

---

## 2. `LogiFlow.Domain` — the rules, and nothing else

Organised **by business concept**, not by technical kind. There is no `Entities/` folder, because
"entity" is not a word the business uses.

```
LogiFlow.Domain/
├── Orders/                    ← one folder per aggregate
│   ├── Order.cs                   the aggregate ROOT — the only entry point from outside
│   ├── OrderLine.cs               a child entity, reachable only through Order
│   ├── OrderNumber.cs             a value object owned by this aggregate
│   ├── OrderStatus.cs             the lifecycle, as an enum / state machine
│   ├── OrderErrors.cs             the failures this aggregate can return, named once
│   ├── OrderSpecifications.cs     reusable query predicates for this aggregate
│   └── Events/OrderEvents.cs      OrderSubmitted, OrderCancelled — past tense, immutable
│
├── Catalog/      Product.cs + Events/
├── Customers/    Customer.cs + Events/
├── Inventory/    stock levels, reservations + Events/
├── Shipping/     shipments, carriers + Events/
├── Automation/   equipment / warehouse-control concepts + Events/
│
├── ValueObjects/              ← shared across aggregates, owned by none
│   Money.cs · Currency.cs · Address.cs · EmailAddress.cs · Sku.cs · Weight.cs
│
├── Common/                    ← the base types the concepts are built from
│   Entity.cs · AggregateRoot.cs · IDomainEvent.cs
│   Ids.cs · IStronglyTypedId.cs      OrderId, ProductId — not bare Guids
│   Specifications/                   the specification base machinery
│
├── Results/                   Result.cs · Error.cs  ← expected failure as a return value
└── Exceptions/                only for things that are genuinely bugs
```

**What a file in here may contain:** types, business rules, invariants, arithmetic.
**What it may never contain:** `DbContext`, `HttpClient`, `IConfiguration`, `ILogger`, attributes
from any ORM, `async` that touches I/O, or `DateTime.UtcNow` read directly (inject a clock).

**The test for "does this go in Domain?"** — *would this rule still be true if the application
were a paper form processed by hand?* An order cannot ship before it is paid: yes, Domain. The
order id must be unique in the `orders` table: no — that is a storage concern.

### The kinds of type you will find, by name

| Kind | Identity | Mutable | Example here |
|---|---|---|---|
| **Aggregate root** | yes, an id | yes, through its own methods | `Order`, `Product`, `Customer` |
| **Entity (child)** | yes, inside the aggregate | only via the root | `OrderLine` |
| **Value object** | none — equal by value | no, you replace it | `Money`, `Address`, `Sku` |
| **Strongly-typed id** | it *is* the identity | no | `OrderId`, `ProductId` |
| **Domain event** | none | no | `OrderSubmitted` |
| **Enum / state** | none | no | `OrderStatus` |
| **Specification** | none | no | `OrderSpecifications` |
| **Error** | none | no | `OrderErrors.AlreadySubmitted` |

One folder per aggregate, named with a plural business noun. If you cannot name the folder
without reaching for a technical word, you have not found the concept yet.

---

## 3. `LogiFlow.Application` — the use cases

Domain knows the rules; Application knows **what the system does**. It orchestrates: load, call a
domain method, save, publish. It holds no business rules of its own — if you are writing an `if`
about money in here, it belongs one layer further in.

```
LogiFlow.Application/
├── Features/                  ← one folder per aggregate, one FILE per use case
│   ├── Orders/
│   │   ├── CreateOrder.cs         command + validator + handler, ALL IN ONE FILE
│   │   ├── SubmitOrder.cs
│   │   ├── CancelOrder.cs
│   │   ├── AddOrderLine.cs
│   │   ├── GetOrderById.cs        a query — read side
│   │   ├── SearchOrders.cs
│   │   ├── IOrderQueries.cs       the read contract (bypasses the repository, on purpose)
│   │   ├── OrderDtos.cs           what leaves the layer — never the entity itself
│   │   └── EventHandlers/         reactions to domain events
│   └── Products/  Inventory/  Shipments/  Reporting/
│
├── Abstractions/              ← interfaces the OUTER layers must implement
│   ├── Data/       IRepositories.cs · IUnitOfWork.cs
│   ├── Messaging/  IDispatcher.cs · Dispatcher.cs · Messages.cs · IIntegrationEventPublisher.cs
│   ├── Mailing/    IEmailSender.cs · IEmailQueue.cs · EmailMessage.cs
│   ├── Services/   Services.cs   (clock, current user, id generation…)
│   └── Automation/ IEquipmentGateway.cs · ITransportOrderStore.cs
│
├── Behaviors/                 ← the cross-cutting pipeline, one concern per file
│   ValidationBehavior.cs · LoggingBehavior.cs · TransactionBehavior.cs · CachingBehavior.cs
│
├── Common/                    Pagination.cs and friends
├── Mailing/                   composition that is use-case, not transport
└── DependencyInjection.cs     AddApplication() — this layer registers itself
```

### The two conventions that matter here

**A. The vertical slice.** `CreateOrder.cs` holds the command record, its FluentValidation
validator, *and* the handler — three types, one file. This is the opposite of the
`Commands/ Validators/ Handlers/` habit, and it is deliberate: those three change together every
single time. Three folders means three navigations per edit, and retiring a feature becomes
archaeology. **Everything that changes together lives together; deleting a feature deletes one
file.**

**B. `Abstractions/` is the inversion.** `IOrderRepository` is declared here and implemented in
Infrastructure. The arrow points *inward* — Infrastructure depends on Application, never the
reverse. That is dependency inversion, which is not the same thing as "having a DI container", a
confusion worth being precise about in an interview.

Note what is **not** here: no `IRepository<T>` with `GetAll()`. A generic repository returning
`IQueryable` leaks the database into every caller and makes the interface a lie. Contracts are
written per aggregate, exposing the queries the use cases actually need —
[`IRepositories.cs`](../../src/LogiFlow.Application/Abstractions/Data/IRepositories.cs) argues
this at length.

---

## 4. `LogiFlow.Infrastructure` — everything that is not your code

One folder per external thing. Each folder answers one question: *how do we actually talk to X?*

```
LogiFlow.Infrastructure/
├── Persistence/
│   ├── LogiFlowDbContext.cs           the ONLY place EF Core is configured
│   ├── UnitOfWork.cs                  implements Application's IUnitOfWork
│   ├── Configurations/                IEntityTypeConfiguration<T> — mapping lives HERE,
│   │                                  never as attributes on the domain entity
│   │   OrderConfiguration.cs · CatalogConfigurations.cs · …
│   ├── Conventions/                   model-wide rules (typed ids, decimal precision)
│   ├── Interceptors/
│   │   DomainEventDispatchingInterceptor.cs   events fire on SaveChanges, in the transaction
│   ├── Repositories/                  OrderRepository.cs — the write side
│   ├── Queries/                       OrderQueries.cs — the read side, projecting to DTOs
│   ├── Outbox/                        reliable publish: message row committed with the data
│   ├── Migrations/                    generated, committed, never hand-edited after shipping
│   ├── Seed/                          reference data
│   └── DesignTimeDbContextFactory.cs  so `dotnet ef` works without booting the API
│
├── Messaging/                 the real bus / broker client
├── Mailing/                   the real SMTP sender
├── Automation/                the real equipment gateway
├── Services/    Services.cs   real clock, real id generator
└── DependencyInjection.cs     AddInfrastructure(configuration) — binds every interface above
```

**The rule for this project:** every public class in here implements an interface defined in
Application. If a class in Infrastructure has no interface behind it, ask why no inner layer ever
needed it — usually it means logic drifted outward and belongs in Application or Domain.

**Read and write are separated on purpose.** `Repositories/` loads whole aggregates so business
rules can run against them. `Queries/` never loads an aggregate — it projects straight to a DTO,
returns exactly the columns a screen needs, and is free to be hand-written SQL. A read path that
goes through a repository loads objects and their rules to build a list you were only going to
display.

---

## 5. `LogiFlow.Api` — the edge

The thinnest project. Its job: turn HTTP into a command, hand it to the dispatcher, turn the
`Result` back into a status code. Nothing else.

```
LogiFlow.Api/
├── Program.cs                 composition root: the WHOLE app is wired here, in order
├── Endpoints/                 minimal-API groups, one file per area
│   OrderEndpoints.cs · CatalogAndReportingEndpoints.cs · ReportingEndpointsV2.cs
├── Infrastructure/            edge concerns, not business ones
│   GlobalExceptionHandler.cs      unhandled exception → ProblemDetails
│   ResultExtensions.cs            Result<T> → 200 / 400 / 404, in one place
│   Authentication.cs              auth wiring + CurrentUserMiddleware
│   ApiVersioning.cs
├── RealTime/                  OrderTrackingHub.cs (SignalR) + its publisher
├── Grpc/  Protos/             the same use cases over a second transport
├── Extensions/                small DI / builder helpers
├── appsettings.json           +  appsettings.Development.json
└── Properties/launchSettings.json
```

An endpoint body should be about five lines. If there is an `if` in it about business state, that
`if` has escaped from the Domain.

### Middleware: the ordering *is* the behaviour

Middleware is not a folder — it is a **sequence** in `Program.cs`, and the order is the single
most common source of "it works on my machine" bugs. Each item wraps everything below it, on the
way in and again on the way out.

```
app.UseExceptionHandler()      ← FIRST: must wrap everything to catch anything
app.UseStatusCodePages()
app.UseSerilogRequestLogging() ← after the handler, so it logs the final status
app.UseHsts() / UseHttpsRedirection()
app.UseRouting()               ← from here on, the matched endpoint is known
app.UseAuthentication()        ← who are you        (never before UseRouting)
app.UseAuthorization()         ← are you allowed    (never before Authentication)
app.UseRateLimiter()
app.UseMiddleware<CurrentUserMiddleware>()   ← needs the authenticated principal
app.UseOutputCache()
app.MapApiVersion("v1") / MapHub / MapGrpcService / MapHealthChecks   ← the terminals
```

Three orderings to be able to justify on the spot:

- **Exception handling first**, or an exception thrown inside logging never reaches it.
- **Authentication before Authorization** — you cannot check permissions for a user you have not
  identified yet.
- **Routing before both** — endpoint metadata (`[Authorize]`, rate-limit policies) is only known
  once routing has matched.

Write your own middleware only for something genuinely per-request and transport-level:
correlation ids, tenant resolution, current user. Validation, transactions, use-case logging and
caching are **not** middleware here — they are `Behaviors/` in Application, because they must
also apply when a command arrives over gRPC, from a background job, or in a test. Middleware is
HTTP-only by construction.

---

## 6. Where does a new file go? — the decision, in order

Ask these top to bottom and stop at the first yes.

1. **Is it a rule that would be true on paper, without a computer?** → `Domain/<Aggregate>/`
2. **Is it something the user can ask the system to do?** → `Application/Features/<Aggregate>/`,
   one file: command + validator + handler.
3. **Does it only read data for a screen?** → a query in `Features/…`, implemented in
   `Infrastructure/Persistence/Queries/`. Do not route it through a repository.
4. **Does it talk to something outside the process?** → `Infrastructure/<Thing>/`, behind an
   interface declared in `Application/Abstractions/`.
5. **Does it apply to every use case, whatever the transport?** → `Application/Behaviors/`.
6. **Does it apply to every HTTP request specifically?** → middleware in `Api/Infrastructure/`.
7. **Is it about turning HTTP into a call?** → `Api/Endpoints/`.

If a file seems to fit two of these, it is doing two things. Split it.

---

## 7. What goes wrong, by name

| Symptom | What it actually means |
|---|---|
| `Domain/Services/OrderService.cs` doing all the work | anemic domain model — entities are structs and the rules live outside them |
| `[Table]`, `[Column]` attributes on `Order` | persistence has leaked into Domain; use `IEntityTypeConfiguration` |
| `Application` referencing `Microsoft.EntityFrameworkCore` | the inversion is gone; the layer is decorative |
| `IRepository<T>.GetAll()` | the database is now queryable from everywhere |
| Entities returned from endpoints | your public API is your schema; you can refactor neither |
| A 400-line `Program.cs` full of business `if`s | the composition root became the application |
| `Commands/ Validators/ Handlers/` folders | three navigations per change, and features cannot be deleted cleanly |

---

## Break something

```bash
dotnet test tests/LogiFlow.ArchitectureTests
```

Now add `using Microsoft.EntityFrameworkCore;` to any file under `src/LogiFlow.Domain/`. The build
goes red before a test even runs, because the package is not reachable from there. Then move
`OrderRepository` from Infrastructure into Application and watch the architecture tests name the
violated arrow. Conventions decay; tests do not.

📂 [`tests/LogiFlow.ArchitectureTests/LayeringTests.cs`](../../tests/LogiFlow.ArchitectureTests/LayeringTests.cs)

---

## What to remember

- Four projects because four **compile-time** boundaries; folders enforce nothing.
- `Domain` references nothing at all — that empty `.csproj` is the design.
- Domain folders are named after business concepts, one per aggregate, never `Entities/`.
- A use case is **one file**: command + validator + handler.
- Interfaces are declared by the inner layer and implemented by the outer one.
- Writes go through repositories and load aggregates; reads project straight to DTOs.
- Middleware is HTTP-only and order-dependent; anything transport-independent is a pipeline
  behaviour.
- `Program.cs` wires; it never decides.

**Code:** [`src/`](../../src/) ·
[`LogiFlow.Domain.csproj`](../../src/LogiFlow.Domain/LogiFlow.Domain.csproj) ·
[`Features/Orders/CreateOrder.cs`](../../src/LogiFlow.Application/Features/Orders/CreateOrder.cs) ·
[`Abstractions/Data/IRepositories.cs`](../../src/LogiFlow.Application/Abstractions/Data/IRepositories.cs) ·
[`Api/Program.cs`](../../src/LogiFlow.Api/Program.cs)

**Next:** [2. Entities vs value objects](02-entities-and-value-objects.md)
