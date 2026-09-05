# 6. Sending email

> Part of [Module 10 — Cross-cutting concerns](README.md), section 6.
> Previous: [5. Rate limiting](README.md#5-rate-limiting) ·
> Next: [Module 11 — Observability](../module-11-observability/)

---

Almost every business system sends email, almost every tutorial stops at `SmtpClient.Send`, and the
gap between those two facts is where a surprising amount of production pain lives. This chapter is
the mailing system in `src/LogiFlow.Infrastructure/Mailing/` — what it does, and why each part of it
is not optional.

The one-line summary: **sending email is not the hard part. Sending it exactly once, from inside a
transaction, without blocking the request, without mailing your customers from staging, and knowing
afterwards whether it arrived — that is the hard part.**

## The naive version, and its four bugs

```csharp
public async Task HandleAsync(OrderSubmittedDomainEvent e, CancellationToken ct)
{
    var customer = await customers.GetAsync(e.CustomerId, ct);
    await smtp.SendAsync(customer.Email, $"Order {e.OrderNumber} confirmed", body, ct);
}
```

This is `SendConfirmationOnOrderSubmitted`, which is still in this repository, still wrong, and
labelled as such in its own remarks. Four things are broken:

| Bug | What it looks like in production |
|---|---|
| **Inside the transaction** | an SMTP round trip holds a database transaction open. Locks are held for as long as the mail server takes to answer, which on a bad day is thirty seconds. |
| **Before the commit** | the transaction rolls back later, the email has already gone. The customer has a confirmation for an order that does not exist. |
| **No retry** | the mail server is restarting. The email is lost, permanently, and nobody finds out. |
| **No idempotency** | the event is redelivered, the request is retried, the deploy replays a batch. The customer gets it twice. |

Each of these is fixed by a different piece of the system below. None of them is fixed by being
more careful.

## The shape

```
   ┌─ your transaction ──────────────────────────────────────────┐
   │  order.Submit()                                             │
   │  emailQueue.EnqueueAsync(message)   ← a row, same context   │
   │  SaveChangesAsync()                 ← ONE commit            │
   └─────────────────────────────────────────────────────────────┘
                              │
                              ▼   (seconds later, another process, maybe another machine)
   ┌─ EmailDeliveryService ──────────────────────────────────────┐
   │  claim a batch atomically        ← UPDATE … OUTPUT, READPAST │
   │  for each: IEmailSender.SendAsync                            │
   │     success → MarkSent      failure → MarkFailed(backoff)    │
   │  SaveChangesAsync                                            │
   └─────────────────────────────────────────────────────────────┘
                              │
                   ┌──────────┴──────────┐
                   ▼                     ▼
        RedirectingEmailSender      (guard: staging cannot mail customers)
                   │
       ┌───────────┼───────────┐
       ▼           ▼           ▼
    Logging      File        Smtp        (chosen by Mailing:Transport)
```

Six small classes. Every one of them exists because of a specific failure.

## 1. The message is a type

```csharp
EmailMessage.Create(to, subject, textBody, htmlBody, category, deduplicationKey)
```

Three strings became a record because everything mail grows — a plain-text alternative, a category,
an idempotency key — would otherwise be another parameter on every implementation and every call
site.

Two rules are enforced in the factory rather than trusted to callers:

**The text body is required and the HTML one is optional.** That is deliberately the opposite of
what most systems do. Terminal clients, screen readers, watch previews and corporate gateways that
strip HTML all fall back to the text part, and "please enable HTML to view this email" is not a
message those readers can read. Spam filters score a missing text part too.

**The subject is flattened.** A header ends at a CRLF, so this:

```csharp
subject = "Order shipped\r\nBcc: everyone@competitor.example\r\n"
```

is **header injection** — it turns transactional mail into somebody else's distribution list.
MimeKit encodes headers properly and would not be fooled, but two of the transports here write a
file and a log line by hand. The defence belongs where the value is created, not in each place it
is consumed.

## 2. Enqueue, do not send

`IEmailQueue.EnqueueAsync` writes a row through **the caller's `DbContext`** and does not call
`SaveChangesAsync`. That single design decision fixes the first two bugs in the table above:

- The order and the intent to email commit in one transaction, or neither does.
- No SMTP round trip happens inside the transaction; nothing waits on a mail server.

This is the [outbox pattern](../module-06-efcore/07-outbox-pattern.md) applied to email, and the
integration test that proves it is four lines: enqueue inside a transaction, roll it back, assert
the row is gone.

What you give up is immediacy — delivery happens on the worker's next tick, a few seconds later.
For transactional mail that is invisible. For a one-time password it is not, and the answer there
is a shorter poll or a wake-on-write signal, never a synchronous send. Durability is the part you
cannot add afterwards.

### Idempotency is a unique index

```csharp
deduplicationKey: $"shipment-dispatched:{trackingNumber.Value}"
```

Derived from the **fact**, never from the moment. `Guid.NewGuid()` as a key is the same as no key.
A unique filtered index on the column makes the guarantee real; the `EXISTS` check in
`DatabaseEmailQueue` is only an optimisation for the common case.

One T-SQL subtlety worth knowing: **SQL Server treats `NULL`s as equal for uniqueness**, unlike the
SQL standard and unlike PostgreSQL. Without `HasFilter("[DeduplicationKey] IS NOT NULL")`, the
second message ever queued without a key would be rejected.

## 3. Claiming a batch, atomically

The outbox processor in this repository reads pending rows, publishes them, then marks them done —
and its own chapter admits the flaw: run two instances and both read the same rows. The mail worker
does it properly, in one statement:

```sql
WITH due AS (
    SELECT TOP (@batchSize) Id, AttemptCount, NextAttemptUtc
    FROM logiflow.QueuedEmails WITH (ROWLOCK, UPDLOCK, READPAST)
    WHERE SentAtUtc IS NULL AND AbandonedAtUtc IS NULL AND NextAttemptUtc <= @now
    ORDER BY NextAttemptUtc, Id
)
UPDATE due
SET AttemptCount = AttemptCount + 1, NextAttemptUtc = @leaseUntil
OUTPUT INSERTED.Id;
```

| Clause | Why it is there |
|---|---|
| CTE with `TOP … ORDER BY` | select and claim in one statement — no window between deciding and taking. `UPDATE TOP (n)` alone cannot be ordered, and an unordered queue is not a queue. |
| `UPDLOCK` | take the update lock while reading. Without it two workers each hold a shared lock and both want to upgrade: deadlock. |
| `READPAST` | skip rows another worker holds instead of blocking. Without it, two workers serialise into one. |
| `ROWLOCK` | discourage lock escalation to page or table, which would defeat `READPAST`. |
| `OUTPUT INSERTED.Id` | learn what you claimed in the same round trip. Update-then-select is a second race. |
| `AttemptCount + 1` **here** | a message that crashes the worker still runs out of lives. Counting after a successful send means a poison message is retried forever. |
| `NextAttemptUtc = @leaseUntil` | a **visibility lease**. If the process dies mid-send the lease expires and another worker picks the message up — recovery, without a distributed lock. |

PostgreSQL says the same thing as `SELECT … FOR UPDATE SKIP LOCKED` inside a CTE with `RETURNING`.
The shape is identical; the text is not, which is why all of it lives in one small class.

**Why raw ADO.NET and not `FromSql`?** The same reason `OrderNumberGenerator` uses a `DbCommand`:
EF's raw-SQL helpers are composable query builders that may wrap your statement in a derived table,
and a data-modifying statement cannot be wrapped.

## 4. Retry: which failures, and how long

```csharp
throw new EmailDeliveryException(message, isTransient: (int)smtp.StatusCode is >= 400 and < 500);
```

In SMTP a **4xx reply means "not now"** and a **5xx reply means "not ever"**. Retrying
`550 mailbox does not exist` five times produces five identical rejections and damages your sending
reputation; giving up on `421 too many connections` loses mail that would have gone through ten
seconds later. Only the transport understands SMTP, so it is the transport that translates — and
everything above it stays free of any knowledge that SMTP exists.

The backoff is **exponential with full jitter**:

```csharp
delay = random(0, min(base * 2^(attempt-1), 1 hour))
```

Exponential because an overloaded server needs less traffic, not the same traffic on a fixed timer.
Jittered because without it a hundred messages that failed together retry together forever — the
**thundering herd**, reproducing the spike that caused the failure.

After `MaxAttempts`, the message is **abandoned**: dead-lettered, never retried, and never deleted
by the retention sweep. A message a customer should have received and did not is evidence, and a
cleanup job that erases its own bad news is worse than no cleanup job.

## 5. The guard that prevents the classic incident

Somebody restores a production backup into staging to reproduce a bug. Staging's SMTP settings were
copied from production. A test run then emails four thousand real customers that their order has
been cancelled.

This happens somewhere every month. It is always found by the customers, and it is always
preventable by this much configuration:

```json
"Mailing": {
  "RedirectAllTo": "qa@logiflow.example",
  "AllowedRecipientDomains": [ "logiflow.example" ]
}
```

`RedirectingEmailSender` is a **decorator**: no transport knows it exists, none had to change to
gain the protection, and a fifth transport added next year gets it for free. The alternative — an
`if (isProduction)` inside each transport — is the same check written four times, and the fourth is
the one somebody forgets.

Note the two guards answer different questions. The redirect asks *"where should mail go instead?"*
and keeps the message readable by whoever is testing. The allow-list asks *"who may we mail at
all?"* and is useful alone in an environment that should reach the QA team and nobody else.

## 6. Four transports, one interface

| `Mailing:Transport` | What it does | When |
|---|---|---|
| `Log` | writes the message to the log, delivers nothing | the default. `git clone && dotnet run` mails nobody. |
| `File` | writes a real `.eml` per message | building templates: double-click the file and see exactly what the customer sees |
| `Smtp` | sends, over one pooled connection | staging and production — and locally against Mailpit |

The `.eml` transport is the direct descendant of `<smtp deliveryMethod="SpecifiedPickupDirectory">`
from .NET Framework's `web.config`, which did not survive the move to .NET Core. Rebuilding it is
fifteen lines and it is still the fastest way to iterate on an email template.

For a real SMTP path with a zero blast radius, `docker compose up -d` starts **Mailpit** on
`localhost:1025` with an inbox at <http://localhost:8025>. The `docker` launch profile points the
API at it.

### Why MailKit and not `System.Net.Mail.SmtpClient`

The framework's `SmtpClient` still compiles, and Microsoft's own documentation has recommended
against it for years — it predates async properly, cannot negotiate modern STARTTLS cleanly, and
leaks connections if disposed wrongly. The remarks section names MailKit as the replacement, which
is a rare thing for a framework to do and a good answer to "why did you take a dependency instead
of using the BCL?"

MimeKit comes with it and is the half that matters: RFC 2047 header encoding (an `à` in "Società"
cannot appear raw in a header), transfer encoding, multipart boundaries, a unique `Message-Id`.

### One connection, one lock

`SmtpEmailSender` is a **singleton holding a connection**. The obvious implementation connects,
authenticates, sends one message and disconnects — twenty TCP handshakes, twenty TLS negotiations
and twenty `AUTH` exchanges to deliver a batch of twenty, and many relays rate-limit *connections*
far more aggressively than messages.

MailKit's `SmtpClient` is explicitly not thread-safe, so a shared one needs a lock — and an
async-friendly one, because `lock` cannot be held across an `await`. That is `SemaphoreSlim` with
`WaitAsync`. The consequence is that sends serialise, which is exactly why the worker sends its
batch sequentially: `Task.WhenAll` over twenty messages that immediately queue on a semaphore is
cost with no benefit.

## 7. HTML that survives

Email is not the web. Gmail strips `<style>` blocks in some contexts, Outlook renders through
Word's HTML engine — no flexbox, no grid, unreliable `padding` — and essentially nothing supports
external stylesheets. Inline styles on simple block elements are the subset that works everywhere.

`EmailBodyBuilder` describes the content **once** and renders both bodies from it. The usual
failure is writing the HTML first and treating the text part as an afterthought, at which point
they drift and nobody notices, because the developer's own client renders HTML.

Everything interpolated is HTML-escaped — and not only against attackers. One `&` in
"Rossi & Figli" is invalid markup; one `<` in a support agent's cancellation note silently eats the
rest of the paragraph.

## 8. Configuration that fails at startup

```csharp
services.AddOptions<MailingOptions>()
    .Bind(configuration.GetSection(MailingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Without `ValidateOnStart`, options validate lazily — so a typo in the SMTP host is discovered by
the first customer who does not get a confirmation, hours later, inside a background worker. With
it, the deployment fails in front of the person who caused it.

Two gotchas:

- **`ValidateDataAnnotations` lives in `Microsoft.Extensions.Options.DataAnnotations`**, not in
  `Microsoft.Extensions.Options`. Until that package is referenced, your `[Required]` and `[Range]`
  attributes do nothing at all. ASP.NET Core apps get it transitively; class libraries do not,
  which is exactly where options classes usually live.
- Cross-field rules need an `IValidateOptions<T>`. `MailingOptionsValidator` reports **every**
  failure at once, because a validator that stops at the first turns a five-minute fix into five
  deployments.

One of its rules is worth calling out: the delivery lease must exceed the SMTP timeout. If it does
not, a slow send outlives its own lease, another worker claims the message, and the customer gets
it twice — under precisely the conditions where duplicates are most visible.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

Cancel an order, then look at the queue before the worker's next tick:

```sql
SELECT Id, Category, Recipient, AttemptCount, NextAttemptUtc, SentAtUtc, AbandonedAtUtc
FROM logiflow.QueuedEmails ORDER BY QueuedAtUtc DESC;
```

The row is there, unsent, written by the same transaction that cancelled the order. A couple of
seconds later `App_Data/mail` contains an `.eml` file — open it in a mail client. Now stop the API
before the tick and restart it: the message is still delivered, because durability came from the
database rather than from the process staying alive.

To watch the retry policy work, point `Mailing:Transport` at `Smtp` with nothing listening on port
1025. Each attempt is logged, `AttemptCount` climbs, `NextAttemptUtc` moves further out each time,
and after five attempts `AbandonedAtUtc` is set and the row stops being picked up.

## What to remember

- Enqueue in the caller's transaction; never send inside one.
- The guarantee is at-least-once, so the deduplication key must come from the fact, not the moment.
- A unique **filtered** index is what makes idempotency real — `NULL`s are equal in SQL Server.
- Claim rows atomically with `UPDLOCK, READPAST` and `OUTPUT`, or two instances send everything twice.
- Count the attempt when you claim, not when you finish, or a poison message is immortal.
- The lease is the recovery mechanism: it must outlast the send timeout.
- 4xx is "not now" and 5xx is "not ever". Retry only the first, exponentially, with jitter.
- Abandoned messages are evidence: dead-letter them, keep them, alert on them.
- Redirect all mail in every non-production environment. The incident it prevents is routine.
- Always send a plain-text part, always escape the HTML one, and never trust an inline `<style>`.
- Validate the configuration at startup, and reference the DataAnnotations package that makes it work.

**Code:** [`Mailing/`](../../src/LogiFlow.Infrastructure/Mailing/) ·
[`EmailMessage.cs`](../../src/LogiFlow.Application/Abstractions/Mailing/EmailMessage.cs) ·
[`QueuedEmailStore.cs`](../../src/LogiFlow.Infrastructure/Mailing/QueuedEmailStore.cs) ·
[`EmailQueueTests.cs`](../../tests/LogiFlow.Api.IntegrationTests/EmailQueueTests.cs)

**Next:** [Module 11 — Observability](../module-11-observability/)
