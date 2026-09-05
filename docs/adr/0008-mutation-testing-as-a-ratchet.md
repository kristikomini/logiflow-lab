# 8. Mutation testing, with the threshold set as a ratchet

- **Status:** Accepted
- **Date:** 2026-09-05

## Context

The repository advertises **234 passing tests** and an architecture-test suite that keeps the
design honest. That is a claim about tests *running*. It is not a claim about them *testing*
anything, and the two are much further apart than they look — a test that calls a method and
asserts nothing passes forever and covers every line in it.

Line coverage cannot tell the difference. Mutation testing can: change the production code —
flip a `>` to `>=`, remove a `!`, return `null` instead of a value — and rerun the suite. A
mutant that **survives** is a change to the code that no test noticed.

## Decision

`dotnet stryker` over `LogiFlow.Domain`, pinned in `.config/dotnet-tools.json`, configured in
`stryker-config.json`, run weekly in CI and on demand.

**The break threshold is a ratchet, not a target.** The first full run scored **33.74%**, so
`break` is set to **30** — just below it. That is not the score we want; it is the score we have,
and a threshold below it means the number can only go up, while a threshold above it means the
job is red from day one and everybody learns to ignore it.

`high` (70) and `low` (50) are the actual targets and only affect the report's colours.

## The first run, in full, because it is the interesting part

22 minutes. Of the mutants generated:

- **272 were never covered by any test at all** — over half the mutable surface of the domain.
- **223 were tested**, and only about a third of those were killed.

Per file, worst first:

| File | Score | Survived | What that means |
| --- | --- | --- | --- |
| `Orders/Shipment.cs` | **0.00%** | 44 | 70 mutants, none killed. The shipping aggregate is effectively untested. |
| `Orders/OrderSpecifications.cs` | **0.00%** | 22 | Every specification could be inverted and no test would notice. |
| `ValueObjects/Money.cs` | **9.68%** | 52 | 78 mutants on the type that handles *money*. |
| `ValueObjects/Weight.cs` | 20.00% | 13 | |
| `ValueObjects/Address.cs` | 42.31% | 5 | |
| `ValueObjects/Sku.cs` | 50.00% | 2 | |
| `ValueObjects/Currency.cs` | 77.78% | 2 | |
| `Orders/OrderStatus.cs` | **100%** | 0 | What good looks like. |

`Money.cs` is the one to sit with. It is the type this codebase makes the most noise about —
`readonly record struct`, `decimal` not `double`, an ADR's worth of reasoning in its comments —
and 52 of its 78 mutants survive. The arithmetic is exercised incidentally by tests that are
*about* something else, and incidental exercise is not a test.

## Alternatives considered

- **Line coverage with a threshold.** Cheap, fast, and games itself: a suite with no assertions
  can hit 90%. It measures which lines ran.
- **Mutating everything, not just Domain.** Infrastructure and API mutants mostly need a database
  or a host, so the run would take hours and most survivors would be noise about wiring. Domain
  is where the rules are and where a surviving mutant is unambiguously a missing test.
- **`break: 65` and living with a red job** until the score gets there. Rejected on the same
  principle `tools/doctor.cs` is built on: a gate people learn to ignore is worse than no gate.
- **No threshold at all.** Then the score can silently fall, which is the failure this exists to
  prevent.

## Consequences

- **The advertised test count is now qualified.** 234 tests kill a third of domain mutants. That
  is a more honest sentence than "234 tests, all green", and it is in `tests/README.md`.
- Every time the score rises, `break` should rise with it. That is the ratchet, and it is a
  manual step on purpose — an automatic one would let a bad week lower it.
- The run is slow because the suite runs once per mutant. It stays out of the pull-request path.
- **It produces a to-do list, in priority order, that no other tool here produces.** `Shipment.cs`
  and `OrderSpecifications.cs` are not "under-covered" in some abstract sense — they are two files
  where the tests would not notice if the code were wrong.
- `ignore-mutations: ["String"]` is on: mutating string literals mostly produces changed log
  messages and error text, which are real mutants and not interesting ones. Turn it off if error
  message content ever becomes part of a contract.
