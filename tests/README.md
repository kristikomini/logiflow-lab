# tests/

Six suites, 234 tests, and one number that matters more than either.

```bash
dotnet test LogiFlow.slnf                      # all six — 234 tests
dotnet test tests/LogiFlow.Domain.Tests        # 30 tests, no database, milliseconds
```

| Suite | Tests | Needs | What it is for |
| --- | ---: | --- | --- |
| `LogiFlow.Domain.Tests` | 30 | nothing | Business rules. No mocks, no database, no host. |
| `LogiFlow.Application.Tests` | 30 | nothing | Handlers, with NSubstitute standing in for I/O only. |
| `LogiFlow.Infrastructure.Tests` | 41 | nothing | Retry policy, redirect guard, options validation, MIME. |
| `LogiFlow.ArchitectureTests` | 8 | nothing | The dependency graph. These keep the design honest. |
| `LogiFlow.Academy.Api.Tests` | 83 | nothing | The accounts service, on SQLite `:memory:`. |
| `LogiFlow.Api.IntegrationTests` | 42 | SQL Server | Real HTTP through the real pipeline, plus the versioning, SignalR and gRPC surfaces. |

The integration tests create a throwaway `LogiFlow_Test_{guid}` database and drop it afterwards.
Point them somewhere else with `LOGIFLOW_TEST_SQL`.

`labs/Labs.Exercises` is **not** in this list and **not** in `LogiFlow.slnf`. It is 113 tests
that are deliberately red — the homework. CI asserts they stay that way, because a green
`Labs.Exercises` means somebody committed their answers over the exercises and destroyed them
for every future reader.

---

## The number that matters more

**234 tests, all green, and a mutation score of 33.74%.**

Both are true, and the second one qualifies the first. A passing test proves the test ran; it
does not prove the test would notice if the code were wrong. Mutation testing asks exactly that
— change the production code and see whether anything goes red — and on the first full run, two
thirds of the domain's mutants survived.

The worst of it, from [ADR 8](../docs/adr/0008-mutation-testing-as-a-ratchet.md):

| File | Score | Survived |
| --- | --- | ---: |
| `Orders/Shipment.cs` | **0.00%** | 44 |
| `Orders/OrderSpecifications.cs` | **0.00%** | 22 |
| `ValueObjects/Money.cs` | **9.68%** | 52 |
| `Orders/OrderStatus.cs` | 100% | 0 |

`Money.cs` is the one worth sitting with. It is the type this repository makes the most noise
about — `readonly record struct`, `decimal` not `double`, a paragraph of reasoning per method —
and 52 of its 78 mutants survive. Its arithmetic is exercised incidentally by tests that are
*about* something else, and incidental exercise is not a test.

```bash
dotnet tool restore
dotnet stryker          # ~22 minutes, HTML report in StrykerOutput/
```

This is left visible rather than fixed quietly, for the same reason the rest of the repository
labels its shortcuts: **a test suite you cannot characterise is a test suite you are trusting on
faith.** The score is a ratchet — `break` in `stryker-config.json` sits just below the current
number, so it can only go up.

If you want somewhere to start, `Shipment.cs` is 70 unkilled mutants and the most valuable
afternoon in this repository.

---

## What each suite is allowed to do

**Domain tests use no test doubles at all.** If a domain rule needs a mock to test, the rule has
a dependency it should not have — the test failing to be writable is the design feedback.

**Application tests mock I/O and nothing else.** Repositories and the clock, substituted. Never
another handler, never the dispatcher: a test that mocks the thing under test passes regardless.

**Integration tests use a real SQL Server**, because the in-memory provider is not a database —
no constraints, no transactions, no concurrency. Module 12's golden rule, applied to this folder.

**Architecture tests are the cheapest insurance here.** Eight tests, milliseconds, and they turn
"Domain must not reference EF Core" from a convention somebody might enforce in review into a
red build.
