# 1. Four projects with dependencies pointing inward

- **Status:** Accepted
- **Date:** 2026-08-11

## Context

This repository exists to be *read* by somebody preparing for a .NET job in Emilia-Romagna, and
to be recognisable to the people interviewing them. Almost every posting in that market names
either Clean Architecture, DDD, or "layered" explicitly; the ones that do not still expect a
candidate to be able to draw the layers on a whiteboard and say where a rule belongs.

A single-project API would have been smaller, faster to read, and completely fine for an
application this size. It would also have taught nothing about the question that actually comes
up: *where does this code go?*

## Decision

Four projects — `Domain`, `Application`, `Infrastructure`, `Api` — with dependencies pointing
strictly inward, and **architecture tests that fail the build** when they do not.

The tests are the decisive part. A layering convention that lives in a diagram lasts until the
first deadline. `LogiFlow.ArchitectureTests` makes "Domain references EF Core" a red build
rather than a code-review comment somebody might not make.

## Alternatives considered

- **One project.** Right for the size of the application, wrong for its purpose. Nothing would
  have forced the domain to stay free of EF Core, and the discipline is the lesson.
- **Vertical slices** (a folder per feature, no layer projects). Genuinely a better fit for many
  real systems and increasingly the fashionable answer. Rejected because it is not what this
  market's interviews ask about, and because the dependency rule is easier to *see* when a
  project reference makes it physical. Worth revisiting if the market moves.
- **Layers as folders in one project.** All of the ceremony, none of the enforcement — a `using`
  is all it takes to cross a boundary that nothing checks.

## Consequences

- Four `.csproj` files, four sets of usings, and a mapping step at every boundary. That is real
  friction and it is paid on every feature.
- Some duplication between domain entities and DTOs that a single-project design would not have.
  This is the cost of the boundary, not an accident, and it is labelled as such where it appears.
- `Domain` has **zero NuGet packages**, which is the property the whole arrangement exists to
  produce: business rules testable in milliseconds with no database, no mocks, no host.
- The Academy service (`src/LogiFlow.Academy.Api`) deliberately does **not** follow this. It is
  six files and one job, and imposing four projects on it would have been cargo cult. That
  contrast is itself instructive — the architecture tests prove the two systems share nothing.
