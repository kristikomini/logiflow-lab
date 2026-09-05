# docs/

Engineering documentation about the repository. **Not course material** — that is `course/`,
and the two have different jobs:

| | Answers | Written for |
| --- | --- | --- |
| `course/` | *How does this work, and why is it true?* | somebody learning |
| `docs/` | *Why is this repository the way it is?* | somebody maintaining it |

A comment in `Money.cs` explains why money is a `decimal`. That is teaching. An ADR explains
why this codebase chose Clean Architecture over a vertical slice — a decision with alternatives,
a cost, and a date — and it exists so that the next person to ask "why is this split into four
projects?" gets an answer rather than a shrug.

## What is in here

- [`CONTENT-BACKLOG.md`](CONTENT-BACKLOG.md) — topics real job postings asked for that the course
  does not yet teach, audited posting by posting. The to-do list for `course/` and `site/`.
- [`adr/`](adr/) — why this repository is the way it is.

## Architecture decision records

[`adr/`](adr/) holds them, newest last. Each one is a page: the situation, the decision, and
what it cost — including the ones that turned out to be wrong, which are marked superseded
rather than deleted.

**They are also the single most interview-relevant document in this repository.** "Tell me about
a technical decision you made and what you traded away" is asked in every mid-level interview,
and the honest answer is very hard to improvise. Each of these is that answer, written down
while the reasoning was still fresh.

| | Decision | Status |
| --- | --- | --- |
| [0001](adr/0001-clean-architecture.md) | Four projects with dependencies pointing inward | Accepted |
| [0002](adr/0002-result-instead-of-exceptions.md) | `Result<T>` for expected failures, exceptions for bugs | Accepted |
| [0003](adr/0003-hand-written-mediator.md) | A hand-written dispatcher instead of MediatR | Accepted |
| [0004](adr/0004-transactional-outbox.md) | A transactional outbox for anything crossing a process boundary | Accepted |
| [0005](adr/0005-url-segment-api-versioning.md) | Version in the URL, with a rewrite for the pre-versioning paths | Accepted |
| [0006](adr/0006-rate-limit-after-authentication.md) | Rate limiting after authentication, not before | Accepted — supersedes the original placement |
| [0007](adr/0007-grpc-internal-rest-at-the-edge.md) | gRPC between services, REST at the edge | Accepted |
| [0008](adr/0008-mutation-testing-as-a-ratchet.md) | Mutation testing, with the threshold set as a ratchet | Accepted |

To add one, copy [`adr/0000-template.md`](adr/0000-template.md) and take the next number.
**Never renumber and never delete** — a superseded decision is more useful than a missing one,
because the question it answered will be asked again.

## What is deliberately not here

- **How to run the thing** — the root [`README.md`](../README.md).
- **How anything works** — [`course/`](../course/), which is roughly half the repository.
- **Deployment** — [`deploy/`](../deploy/README.md).
- **The tutorial site** — [`site/README.md`](../site/README.md).

Four READMEs is already one more than most people will read. Nothing goes here that has a
natural home in one of them.
