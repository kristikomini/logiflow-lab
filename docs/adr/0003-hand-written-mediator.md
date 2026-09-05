# 3. A hand-written dispatcher instead of MediatR

- **Status:** Accepted
- **Date:** 2026-08-16

## Context

CQRS here needs one thing: send a request object, get the right handler to run, with a pipeline
of cross-cutting behaviours around it. MediatR is the standard answer and every advert that
mentions CQRS assumes it.

Two things made the standard answer the wrong one for *this* repository. MediatR moved to a paid
licence for commercial use in 2024 — irrelevant for a study repository and immediately relevant
to anyone who copies a pattern from here into work. And a reader who has only ever called
`_mediator.Send(command)` cannot answer "how does it find the handler?", which is exactly the
follow-up an interviewer asks.

## Decision

A hand-written `Dispatcher` — about 200 lines in `Application/Abstractions/Messaging/` — doing
closed-generic resolution from the DI container and composing the behaviour pipeline by folding
a list of delegates in reverse.

The pipeline order is fixed and commented: **Logging → Validation → Caching → Transaction**,
with a note at each position saying what breaks at any other ordering.

## Alternatives considered

- **MediatR.** The right answer on a commercial team that has bought it. Rejected here on the
  licence, and because using it hides the exact mechanism this repository is trying to teach.
- **Calling handlers directly.** Simplest of all, and it loses the pipeline — so validation,
  caching and transactions move into every handler.
- **A source generator.** Fastest at runtime, no reflection, and roughly ten times the code to
  read. The wrong trade for a codebase whose primary output is comprehension.

## Consequences

- ~200 lines to maintain that a package would have maintained. They have not changed since they
  were written.
- No notifications, no streaming, no pre/post processors — the parts of MediatR this codebase
  does not use are simply absent, which is also why the file is readable.
- The reflection cost of resolving a closed generic per dispatch is measurable and irrelevant at
  this scale; the container caches it.
- Anyone moving this pattern to a team should use MediatR or Wolverine, and will now understand
  what it is doing for them. That was the point.
