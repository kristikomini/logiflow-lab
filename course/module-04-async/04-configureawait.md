# 3. `ConfigureAwait`, and why CA2007 is off here

> Part of [Module 04 — Async and concurrency](README.md), section 3.
> Previous: [2. The state machine](README.md#2-the-state-machine) ·
> Next: [4. Parallelism](README.md#4-parallelism)

---

```ini
# ConfigureAwait — see course/module-04-async/04-configureawait.md
dotnet_diagnostic.CA2007.severity = none
```

`CA2007` — *"Consider calling ConfigureAwait on the awaited task"* — is switched **off** in this
repository's `.editorconfig`. That is a deliberate decision with a reason, and "we turned off the
analyser that was annoying us" is not it.

## What `ConfigureAwait(false)` actually does

When you `await`, the compiler captures the current **synchronisation context** (or task scheduler)
and resumes the continuation on it. `ConfigureAwait(false)` says: *do not bother — resume anywhere*.

That matters only where a synchronisation context exists:

| Environment | Sync context? | Consequence |
|---|---|---|
| Classic ASP.NET (System.Web) | yes | continuation must return to the request context |
| WinForms / WPF | yes | continuation must return to the UI thread |
| **ASP.NET Core** | **no** | there is nothing to capture |
| Console apps | no | nothing to capture |

**ASP.NET Core removed the synchronisation context**, deliberately, in its first version. There is
nothing to return to, so `ConfigureAwait(false)` in an ASP.NET Core application changes precisely
nothing about behaviour.

## So why is the analyser off?

Because the codebase this analyser is protecting does not exist here.

`CA2007` exists for **library authors**, who do not know where their code will run. A library
awaited from a WPF application will, by default, marshal every continuation back to the UI thread —
one hop per `await`, thousands of times, for no reason. Worse, a library that internally blocks on
its own async work will **deadlock** in that host and not in the tests, which were run in a console.

For library code the rule is real:

```csharp
// In a NuGet package, this is correct and important.
HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
```

For an ASP.NET Core application it is noise: `.ConfigureAwait(false)` appended to every `await` in
the codebase, obscuring the actual logic, to change nothing.

> **The rule: on in libraries, off in applications.** This repository is an application.

The honest cost of switching it off: if `LogiFlow.Domain` or `LogiFlow.Application` were ever
extracted and shipped as a package, the analyser would need re-enabling for those projects and a
fair amount of code would change. That is a real trade, made knowingly, and it is exactly what the
comment in `.editorconfig` is there to record.

## What does not change

Turning off `CA2007` does **not** make blocking safe. The rules from
[section 3](README.md#3-the-rules-that-prevent-the-classic-failures) are unaffected:

```csharp
var result = SomeAsyncMethod().Result;               // still wrong
SomeAsyncMethod().Wait();                            // still wrong
SomeAsyncMethod().GetAwaiter().GetResult();          // still wrong
```

In a context *with* a synchronisation context this deadlocks: the continuation needs the context,
the context is held by the blocked thread, neither proceeds. `ConfigureAwait(false)` on the inner
await is the classic "fix" for that deadlock — and it is a workaround, not a solution, because it
only works if you control every await in the chain.

In ASP.NET Core it does not deadlock. It **starves the thread pool** instead: a thread parked
waiting for work that needs a thread. That fails under load rather than immediately, which is worse,
because you ship it.

`CA1849` — *"call the async method"* — is the analyser that catches this, and it **is** enabled.
That is the pairing worth understanding: the analyser about *style in libraries* is off; the
analyser about *a real bug in any host* is on.

## The one place it still matters here

`ConfigureAwait` also has a `ConfigureAwaitOptions` overload (from .NET 8) with genuinely useful
members, notably `SuppressThrowing` for fire-and-forget cleanup where you have already logged the
failure:

```csharp
await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
```

And in a **Blazor Server** component the calculus changes completely — there *is* a synchronisation
context, and the rules from [module 18](../module-18-blazor/) apply. `src/LogiFlow.Web` is a Blazor
app, so if you are working in there, the analyser being off repo-wide does not mean the concern is
absent; it means you have to think rather than be told.

Same for the desktop world: everything in
[site chapter 31](../../site/chapters/31-desktop-wpf-winforms.html) about the UI thread is this
topic wearing different clothes.

And the host where this stops being a style question and becomes an outage: **ASP.NET Framework
4.x**. `AspNetSynchronizationContext` allows one thread at a time on a request, so a blocking
`.Result` there does not waste a thread — it deadlocks the request permanently, and
`ConfigureAwait(false)` is what breaks the cycle. That is why the old advice sounds so much more
urgent than the modern one: both are correct, for different runtimes. If the company has a legacy
web application as well as new services — and in this region many do — you will meet both hosts in
the same week. [Site chapter 16b](../../site/chapters/16b-aspnet-framework.html) reproduces the
deadlock and explains the migration.

Worth noticing the direction of danger: blocking code moved *from* Framework *to* Core stops
deadlocking and starts quietly starving the thread pool instead. The bug does not announce that it
was fixed; it changes disguise.

## Interview answer

This comes up, and most candidates get it half right.

> **"Should you use `ConfigureAwait(false)`?"**
>
> "In library code, yes — you do not know your host, and defaulting to marshalling back to a UI or
> request context costs a hop per await and can deadlock a caller who blocks. In an ASP.NET Core
> application, no: there is no synchronisation context to capture, so it changes nothing and adds
> noise to every line. We have CA2007 off for that reason and CA1849 on, because blocking on async
> is a real bug in any host — in ASP.NET Core it starves the thread pool instead of deadlocking,
> which is harder to spot."

The half that most people miss is "ASP.NET Core has no synchronisation context". Saying it is the
difference between having read about this and having understood it.

## Try it

```bash
dotnet run --project labs/Labs.Playground deadlock
dotnet run --project labs/Labs.Playground context
```

The first demonstrates the classic block-on-async deadlock in a host that *has* a synchronisation
context. The second prints what is captured and where continuations resume, with and without
`ConfigureAwait(false)` — which makes the whole topic concrete in about thirty seconds.

Then set `dotnet_diagnostic.CA2007.severity = warning` in `.editorconfig`, rebuild, and look at how
many warnings you get in `src/`. That number is the noise the setting is suppressing.

## What to remember

- `ConfigureAwait(false)` says "do not resume on the captured context".
- ASP.NET Core has **no synchronisation context**, so it changes nothing there.
- On in libraries, off in applications. CA2007 is off here because this is an application.
- The cost of that choice: extracting a project as a package means re-enabling it.
- It is not a fix for blocking. `.Result` and `.Wait()` are wrong regardless.
- In ASP.NET Core blocking starves the thread pool rather than deadlocking — later, and worse.
- CA1849 stays on, because that one catches a real bug in any host.
- Blazor Server and desktop UIs *do* have a context. The concern returns there.

**Code:** [`.editorconfig`](../../.editorconfig) ·
[`GlobalExceptionHandler.cs`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs) ·
[module 18](../module-18-blazor/)

**Next:** [4. Parallelism](README.md#4-parallelism)
