# 2. Vertical slices

> Part of [Module 08 — CQRS and the mediator pattern](README.md), section 2.
> Previous: [1. What CQRS actually is](01-what-cqrs-actually-is.md) ·
> Next: [3. Building a mediator](03-building-a-mediator.md)

---

Once every operation is a request and a handler, you have to decide where those files live. The
default in most tutorials is to group by *kind*:

```
Application/
  Commands/          CreateOrderCommand.cs, SubmitOrderCommand.cs, CancelOrderCommand.cs, …
  Handlers/          CreateOrderCommandHandler.cs, SubmitOrderCommandHandler.cs, …
  Validators/        CreateOrderCommandValidator.cs, …
  Dtos/              OrderDto.cs, OrderSummaryDto.cs, …
```

It looks tidy in a screenshot and it is wrong, for a reason that is easy to state: **you never change
"all the commands". You change one feature.** Adding a field to "create order" means opening four
folders, and the four files you need are never next to each other.

## Group by feature instead

```
Application/Features/Orders/
  CreateOrder.cs        command + handler + validator, one file
  SubmitOrder.cs
  CancelOrder.cs
  AddOrderLine.cs
  GetOrderById.cs
  SearchOrders.cs
  OrderDtos.cs
  IOrderQueries.cs
  EventHandlers/
    OrderSubmittedHandlers.cs
```

Everything one change needs is in one file. Deleting the feature is deleting the file. A new joiner
asked to "fix the cancel-order bug" opens `CancelOrder.cs` and is finished looking.

```csharp
// CreateOrder.cs — the whole slice
public sealed record CreateOrderCommand(CustomerId CustomerId, IReadOnlyList<OrderLineDto> Lines)
    : ICommand<Result<Guid>>;

internal sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.Lines).NotEmpty();
    }
}

public sealed class CreateOrderCommandHandler(
    ICustomerRepository customers,
    IOrderRepository orders,
    IOrderNumberGenerator numbers) : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateOrderCommand request, CancellationToken ct)
    {
        …
    }
}
```

Three types, one file, about eighty lines. The validator is `internal` — nothing outside this
assembly calls it, and the pipeline finds it by convention.

## Why this is not "just a folder preference"

**It optimises for the axis that actually varies.** Requirements arrive per feature, never per layer.
Whatever changes together should live together — the plain-language version of high cohesion.

**Merge conflicts collapse.** Two developers on two features touch two files. In the by-kind layout
they both edit `Handlers/` and both edit `Dtos/`, and the diff review is a puzzle.

**Coupling becomes visible.** If `SubmitOrder.cs` needs something from `Shipments/`, you can see it in
the `using` list. Shared helpers do not accumulate invisibly in a `Common` folder that eventually
depends on everything.

**Each slice can be as simple as it deserves.** `GetOrderById` is a projection and eight lines.
`SubmitOrder` loads an aggregate and coordinates stock. In a by-kind layout there is constant
pressure to make them look alike — a base handler, a shared mapper — and the simple one pays for the
complex one's ceremony.

## Slices and layers are not alternatives

This is the part that gets muddled, and it is worth being precise about because "vertical slice
architecture" is sometimes sold as a replacement for clean architecture.

**Layers say which way dependencies point.** Domain ← Application ← Infrastructure. That is enforced
here by [`LayeringTests`](../../tests/LogiFlow.ArchitectureTests/LayeringTests.cs).

**Slices say how to organise code inside a layer.**

They are orthogonal, and this repository uses both:

```
LogiFlow.Domain/           Orders/, Customers/, Inventory/, Shipping/     ← by concept
LogiFlow.Application/      Features/Orders/, Features/Inventory/, …       ← by slice
LogiFlow.Infrastructure/   Persistence/, Services/                        ← by technical concern
```

Infrastructure is grouped by technology on purpose: `Configurations/`, `Repositories/`, `Queries/`,
`Interceptors/`. That is the one place where "all the EF configuration" genuinely is a thing you
change together — usually when upgrading EF Core or changing a convention.

The rule generalises: **group by what changes together.** In the application layer that is the
feature. In infrastructure it is the technology.

## The honest costs

**Some duplication.** Two slices may each have a small mapping function that looks similar. Resist
extracting it until the third case proves the shape — a premature shared helper couples two features
that had no reason to know about each other, and it is much harder to unpick than the duplication was
to tolerate.

**A `Common/` folder that grows.** `Pagination`, `Result` extensions, a `TimeProvider` wrapper.
Genuinely shared things belong there; feature-specific things that were dragged there "in case" are
how it turns into a dependency magnet. Review what lands in it.

**Big files, if a slice is complex.** Eighty lines is fine. Four hundred means the feature wants
splitting into a folder — `Features/Orders/CreateOrder/` with three files — which is a normal
escalation, not an admission of defeat.

## The mistakes

**Sharing a DTO across slices because the fields match today.** The moment one screen needs an extra
field you either bloat both or perform a painful split. Two records with the same shape are cheaper
than one shared record with two masters.

**A `BaseCommandHandler<T>`.** Cross-cutting concerns belong in
[behaviours](05-pipeline-behaviors.md), which compose. Inheritance does not, and a base class forces
every slice to be shaped like the average one.

**Slicing the domain by feature too.** The domain is organised by *concept* — `Orders`, `Inventory` —
because an aggregate is not a use case. `Features/` belongs to the application layer.

**Believing slices replace layers.** They answer different questions. Drop the dependency rule and
your slices will happily reference EF Core from the domain.

## Try it

Pick a feature and count the files you touch to add a field to it. In this layout it is one, plus a
DTO. Then imagine the same change in a by-kind layout: the command, the handler, the validator, the
DTO, the mapper — five files in five folders, and a merge conflict with anyone else working that day.

Then look at [`Features/Orders/`](../../src/LogiFlow.Application/Features/Orders/) and note that you
can list every operation an order supports by reading the file names.

## What to remember

- Group by feature, not by artefact type. You change features, never "all the handlers".
- Command, handler and validator in one file; deleting the feature is deleting the file.
- Slices and layers are orthogonal: layers set dependency direction, slices organise within a layer.
- Group by what changes together — features in the application layer, technology in infrastructure.
- Tolerate small duplication; wait for the third case before extracting.
- Watch what accumulates in `Common/`.
- Cross-cutting behaviour goes in the pipeline, never in a base handler class.

**Code:** [`Features/`](../../src/LogiFlow.Application/Features/) ·
[`LayeringTests.cs`](../../tests/LogiFlow.ArchitectureTests/LayeringTests.cs)

**Next:** [3. Building a mediator](03-building-a-mediator.md)
