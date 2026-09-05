# 6. State machines as data

> Part of [Module 05 — Clean Architecture and Domain-Driven Design](README.md), section 6.
> Previous: [5. Domain events](04-domain-events.md) · Back to [the module](README.md)

---

Most business objects have a lifecycle, and most codebases express it as `if` statements scattered
across the methods that change it. That works until somebody adds a seventh status, and then the
question "can an order go from Shipped to Cancelled?" has no single place to look — only eleven
methods to read and hope.

The alternative is to make the transitions **data**, in one place:

```csharp
public static class OrderStateMachine
{
    private static readonly FrozenDictionary<OrderStatus, OrderStatus[]> Allowed =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Draft]     = [OrderStatus.Submitted, OrderStatus.Cancelled],
            [OrderStatus.Submitted] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Shipped,   OrderStatus.Cancelled],
            [OrderStatus.Shipped]   = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = [],
        }.ToFrozenDictionary();

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out OrderStatus[]? targets) && Array.IndexOf(targets, to) >= 0;

    public static IReadOnlyList<OrderStatus> NextStates(OrderStatus from) => …;
    public static bool IsTerminal(OrderStatus status)  => NextStates(status).Count == 0;
    public static bool IsEditable(OrderStatus status)  => status == OrderStatus.Draft;
}
```

Six lines of table. The entire lifecycle of an order is visible at once, and a new developer can
answer any question about it in ten seconds without reading a single method.

## What the table buys you

**One place to look, and one place to change.** Adding a `Returned` status is an edit to this
dictionary plus the enum. Adding it to an `if`-chain design is an audit of every method that
touches `Status`.

**It answers questions the `if` version cannot.** `NextStates` is what the API uses to tell a client
which buttons to enable, and what the UI uses to render available actions. With logic buried in
`if` statements there is no way to *ask* the domain what is allowed — you can only try something and
see if it fails, which means the UI duplicates the rules and then drifts from them.

**`IsTerminal` falls out for free** — a state with no outgoing transitions is terminal, by
definition, rather than by a second list somebody has to maintain in step.

**The impossible transition is documented by its absence.** Look at `Shipped`: the only way out is
`Delivered`. That is a deliberate business statement — once it is on a lorry, "cancel" is a returns
process, not a state change — and writing it as data forces the conversation to happen once, with
the business, rather than being decided accidentally by whoever wrote the cancel method.

**`FrozenDictionary`** because it is built once and read constantly. It costs more to construct and
is meaningfully faster to read than `Dictionary`, which is exactly the right trade for a static
lookup table. See [module 20](../module-20-equality-and-collections/).

## Wiring it into the aggregate

The table only helps if it is the *only* route. In `Order`, every transition goes through it:

```csharp
public Result Submit()
{
    if (!OrderStateMachine.CanTransition(Status, OrderStatus.Submitted))
        return OrderErrors.InvalidTransition(Status, OrderStatus.Submitted);

    if (_lines.Count == 0)          return OrderErrors.EmptyOrder;
    if (ShippingAddress is null)    return OrderErrors.ShippingAddressRequired;

    Status = OrderStatus.Submitted;
    SubmittedAtUtc = DateTimeOffset.UtcNow;
    Raise(new OrderSubmittedDomainEvent(Id, CustomerId, Total));
    return Result.Success();
}
```

Note the two kinds of check, and that they are not the same kind of thing:

- **`CanTransition`** — is this move legal *at all*, in the lifecycle?
- **The guards below it** — is this particular move legal *right now*, given this order's contents?

The first is structural and lives in the table. The second is contextual and lives in the method. A
state machine library that tries to express "an order must have a shipping address" as part of the
transition table usually ends up less readable than this, which is why the table here stays
deliberately dumb.

**`Status` has a private setter.** The table is the rule only because there is no other door — the
same argument as read-only collections in [aggregates](03-aggregates.md).

## `IsEditable`, and why it is here rather than everywhere

```csharp
public bool IsEditable => OrderStateMachine.IsEditable(Status);
```

`AddLine`, `RemoveLine` and `ChangeQuantity` all check `IsEditable` rather than each testing
`Status == OrderStatus.Draft`. When the business later decides that a `Submitted` order may still
have lines removed until it is confirmed, that is **one edit**. With the comparison inlined in five
methods it is five edits and a bug in the one you missed.

This is the smallest, most repeatable version of the whole lesson: name the rule, put it in one
place, and call the name.

## The alternative designs, and when they win

**The `if`-chain.** Fine for two states. Past three it stops being readable and past five it stops
being correct.

**A `State` class per state, with polymorphic methods** — the Gang of Four State pattern. Genuinely
better when each state has substantially *different behaviour* rather than merely different
permitted transitions. Here every state does the same thing (allow or refuse), so a table is the
smaller answer. Reach for the pattern when `Draft.Submit()` and `Confirmed.Submit()` would contain
real, different logic.

**A workflow engine** (Elsa, Workflow Core, or a BPMN product). Right when the process is genuinely
long-running, must survive restarts, involves human approval steps and timers, and needs to be
changed by someone who is not a developer. Enormous overkill for six statuses, and worth saying so
in an interview — the ability to say "we did not need one" reads better than knowing the API of one.

## The mistakes

**Storing the enum's `int` without pinning the values.** `OrderStatus.Draft = 1` is explicit here,
deliberately. Let the compiler assign them and somebody alphabetises the enum members one day, and
every row in the database silently means something different. This is a genuine production
catastrophe and it is prevented by six characters.

**A `switch` over the status that is not exhaustive.** C# will warn on a `switch` *expression* that
misses a case; a `switch` *statement* with no `default` will not. Module 01's exhaustiveness discussion
is the safety net, and "add a `Bronze` tier and watch the build fail" is one of the exercises in
[SOLUTIONS.md](../SOLUTIONS.md).

**Letting the UI keep its own copy of the rules.** The button-enabling logic must come from
`NextStates`, or the day someone edits the table the UI keeps offering an action the server refuses.

**Transitions that skip the table.** One `Status = OrderStatus.Cancelled` written directly in a
handler, and the table is now documentation rather than enforcement.

## Try it

Add a `Returned` value to `OrderStatus` and allow `Delivered → Returned` in the table. Then run:

```bash
dotnet build LogiFlow.slnx
```

The build fails, in the switch expressions that map status to a DTO — the compiler telling you
exactly where the new state needs handling. That failure is the feature: an exhaustive switch over a
pinned enum turns "we forgot about the new status" from a production bug into a compile error.

## What to remember

- Transitions belong in one table, not spread across the methods that perform them.
- The table lets you *ask* what is allowed — which is how the UI stays in step.
- Structural legality goes in the table; contextual guards stay in the method.
- Terminal states fall out of the table rather than needing a second list.
- Name the rule (`IsEditable`) and call the name, so changing it is one edit.
- Pin enum values explicitly. Never let the compiler number persisted states.
- A state class per state wins when behaviour differs, not just permissions.
- A workflow engine is for long-running, human-in-the-loop processes. Six statuses is not that.

**Code:** [`Orders/OrderStatus.cs`](../../src/LogiFlow.Domain/Orders/OrderStatus.cs) ·
[`Orders/Order.cs`](../../src/LogiFlow.Domain/Orders/Order.cs)

**Back to:** [Module 05](README.md)
