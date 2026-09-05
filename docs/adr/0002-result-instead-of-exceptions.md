# 2. `Result<T>` for expected failures, exceptions for bugs

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

"Order not found" and "the database is unreachable" are both failures, and treating them the
same way is the most common error-handling mistake in a .NET codebase. The first is a normal
outcome of a normal request — it will happen thousands of times a day and nothing is wrong. The
second is an emergency.

Using exceptions for both means the emergency is buried in the noise, every handler grows a
`try/catch` that swallows too much, and the compiler has nothing to say about which calls can
fail.

There is also a measured cost. `Labs.Benchmarks` puts throwing at roughly **4,500× the cost** of
returning a value on this machine. Irrelevant for a genuinely exceptional case; very relevant
for one that fires on every third request.

## Decision

Expected failures return `Result` / `Result<T>`. Exceptions are reserved for bugs and for
infrastructure that is genuinely broken.

`Result` carries an `Error` with a stable machine-readable code, which `ResultExtensions` maps
to RFC 9457 Problem Details at the HTTP boundary. A validation failure and a not-found are then
the same shape to a client, and neither unwinds a stack.

## Alternatives considered

- **Exceptions for everything.** The framework default and what most tutorials do. Rejected on
  the cost above and, more importantly, on honesty: an exception says "this should not happen",
  and a customer typing a bad SKU is something that should absolutely happen.
- **Nullable returns.** Free and idiomatic, but a `null` cannot say *why*. "Not found", "not
  authorised" and "not in a state that allows this" all collapse into one answer.
- **A discriminated-union library** (OneOf, LanguageExt). Better ergonomics than a hand-rolled
  `Result`, at the cost of a dependency in `Domain` — which has none — and of asking every
  reader to learn a second vocabulary before they can read a handler.

## Consequences

- Callers must check `IsSuccess` before reading `.Value`, and forgetting throws with a message
  that says so. That is a *runtime* failure where a real discriminated union would give a
  compile error, and it is the honest weakness of this approach in C# today.
- `Result` propagation makes some methods noisier than the exception version would be.
- Pipeline behaviours can inspect a `Result` and short-circuit without catching anything, which
  is what keeps `TransactionBehavior` simple.
- Worth revisiting if C# gets real discriminated unions. The runtime check on `.Value` is the
  only part of this that is a workaround rather than a choice.
