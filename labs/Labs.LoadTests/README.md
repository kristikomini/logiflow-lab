# Labs.LoadTests

What the **system** does under concurrent load — the question `Labs.Benchmarks` cannot answer.

```bash
# terminal 1
cd src/LogiFlow.Api && dotnet run

# terminal 2
cd labs/Labs.LoadTests && dotnet run
dotnet run read          # just one scenario
```

Set `LOGIFLOW_API` to point somewhere other than `http://localhost:5199`.

## Why this is not a benchmark

`Labs.Benchmarks` measures a method: one thread, no network, no database, nanoseconds, and it
answers *"is this code fast?"* very precisely.

None of what it measures tells you what happens when fifty callers arrive at once. That is where
a connection pool of finite size, a database taking locks, a thread pool growing one thread per
second, and a `rowversion` column arbitrating between writers all start to matter — and none of
those exist in a microbenchmark. **A system whose every method is fast can still fall over at
forty concurrent users**, and the usual reason is that somebody only ever measured the methods.

## The three scenarios

| | Rate | What it is for |
| --- | --- | --- |
| `read_heavy` | 50/s for 30s | The shape most APIs actually see: many readers, few writers. |
| `order_creation` | 5/s for 30s | A full transaction per request, through the behaviour pipeline and the outbox. |
| `contended_writes` | 20/s for 20s | Every request writes the **same order**, so they queue behind one `rowversion`. |

## `Inject`, not `KeepConstant`

The one thing to take from this project.

`KeepConstant` holds N concurrent clients. When the server slows down, those clients wait — so
the **arrival rate falls with it**, the system is never pushed past what it can handle, and the
run reports a comfortable latency for a load that never happened.

`Inject` sends N requests per second regardless of whether the previous ones finished. That is
what real users do, and it is the only way to find the point where queues start growing.

## What the first run found

Two things, and neither was in the plan.

**A bug in the code being tested.** The rate limiter partitioned on `User.Identity?.Name` and ran
*before* `UseAuthentication`, so `User` was empty and every authenticated caller fell through to
the IP — one budget for everybody behind a NAT. 96% of requests came back 429. Every unit test,
integration test and code review had passed it, because a test that makes ten requests never
reaches a limit of a hundred. See
[ADR 6](../../docs/adr/0006-rate-limit-after-authentication.md). After the fix: 2,050 requests,
zero failures.

**A bug in this project.** The first version acquired its `HttpClient` with `using` inside the
scenario factory, so the client was disposed before the scenario ran and all 550 requests failed
with *"Cannot access a disposed object"* — at full speed, which is at least a fast way to find
out. The lambda outlives the method that builds it; that is the whole lesson and there is a
comment on it in `Program.cs`.

## Reading the report

`load-reports/` gets an HTML report per run. **Read the percentile table, not the summary line.**
A mean latency of 40 ms is entirely compatible with one caller in twenty waiting two seconds, and
it is the one in twenty who files the ticket. The process exits non-zero if any scenario's p99
goes above 2 seconds — a load test that always exits 0 is a chart, not a test.

## Two things to know

**It is not in `LogiFlow.slnf`.** A test that needs a running server is not a unit test, and
putting it in the CI run would make a green build depend on something CI does not start.

**NBomber is free for personal use only.** It prints a licence notice on every run: *"THIS
VERSION IS FREE ONLY FOR PERSONAL USE. You can't use it for an organization."* That is fine for
a study repository and it is **not** fine if you lift this into work — check the current terms,
or use k6, which is open source and does the same job from a `.js` file.
