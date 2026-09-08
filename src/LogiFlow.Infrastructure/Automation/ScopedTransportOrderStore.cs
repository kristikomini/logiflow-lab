using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using Microsoft.Extensions.DependencyInjection;

namespace LogiFlow.Infrastructure.Automation;

/// <summary>
/// A singleton-safe <see cref="ITransportOrderStore"/> that opens a scope per call.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because of the oldest bug in .NET hosting.</b> The dispatcher is a
/// <c>BackgroundService</c>, which is a <b>singleton</b>, and <see cref="EfTransportOrderStore"/>
/// needs a <c>DbContext</c>, which is <b>scoped</b>. Injecting the scoped thing into the singleton
/// either fails at startup under scope validation, or — worse, if validation is off — captures one
/// context for the lifetime of the process: a change tracker that grows for weeks and a connection
/// that dies at the first network blip and never recovers. <c>OutboxProcessor</c> has the same
/// problem and solves it the same way.
/// </para>
/// <para>
/// <b>Why a wrapper rather than putting the scope in the dispatcher.</b> Because then the
/// dispatcher would need <see cref="IServiceScopeFactory"/> — a DI concern — in a class whose
/// entire testability rests on being constructible by hand. The scoping is an infrastructure
/// detail, so it lives in Infrastructure, and the worker sees only the port.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/01-dependency-injection.md</c>
/// </remarks>
/// <param name="scopeFactory">Creates a DI scope per call.</param>
public sealed class ScopedTransportOrderStore(IServiceScopeFactory scopeFactory) : ITransportOrderStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TransportOrder>> LoadOpenAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<EfTransportOrderStore>()
            .LoadOpenAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyCollection<TransportOrder> orders, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<EfTransportOrderStore>()
            .SaveAsync(orders, ct)
            .ConfigureAwait(false);
    }
}
