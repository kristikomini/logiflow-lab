using System.Reflection;
using FluentValidation;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace LogiFlow.Application;

/// <summary>
/// Registers everything the Application layer provides.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each layer owns its own registration.</b> <c>Program.cs</c> calls
/// <c>AddApplication()</c> and <c>AddInfrastructure(config)</c> and knows nothing about what is
/// inside either. Without this, the composition root becomes a 300-line file that has to be
/// edited every time anyone anywhere adds a class — and it becomes a merge-conflict magnet.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/01-dependency-injection.md</c>
/// </remarks>
public static class DependencyInjection
{
    /// <summary>Adds handlers, validators, the dispatcher and the behaviour pipeline.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Assembly assembly = typeof(DependencyInjection).Assembly;

        services.AddScoped<IDispatcher, Dispatcher>();

        RegisterHandlers(services, assembly, typeof(IRequestHandler<,>));
        RegisterHandlers(services, assembly, typeof(IEventHandler<>));

        // FluentValidation ships its own scanner. `includeInternalTypes` matters because our
        // validators are public but handlers are internal, and the default scan skips internals.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        RegisterPipeline(services);

        return services;
    }

    /// <summary>
    /// Registers the behaviour pipeline. <b>Order is everything.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first registered is the outermost. The request travels down and the response back up:
    /// </para>
    /// <code>
    ///  request ──► Logging ──► Validation ──► Caching ──► Transaction ──► Handler
    ///  response ◄── Logging ◄── Validation ◄── Caching ◄── Transaction ◄── Handler
    /// </code>
    /// <para>Each position is a decision, not an accident:</para>
    /// <list type="number">
    ///   <item><description>
    ///     <b>Logging outermost</b> — so its timing covers everything, including validation and
    ///     the cache lookup. Put it inside and your "request took 4ms" excludes the 300ms
    ///     validation query you did not know you had.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Validation before caching</b> — never cache anything derived from an invalid
    ///     request, and never let a malformed key reach Redis.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Caching before the transaction</b> — a cache hit must not open a database
    ///     transaction. That is most of the point of caching.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Transaction innermost</b> — the transaction should be open for the shortest
    ///     possible time, wrapping only the handler. Every layer outside it that holds it open
    ///     is a lock somebody else is waiting on.
    ///   </description></item>
    /// </list>
    /// </remarks>
    private static void RegisterPipeline(IServiceCollection services)
    {
        // Open generics: one registration covers every (TRequest, TResponse) pair. The container
        // closes the type when it resolves. Note that behaviours constrained to
        // `where TResponse : Result` are simply skipped for pipelines that do not satisfy the
        // constraint - the DI container honours generic constraints rather than throwing.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
    }

    /// <summary>
    /// Finds every concrete implementation of <paramref name="openHandlerInterface"/> in
    /// <paramref name="assembly"/> and registers it against each closed interface it implements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The alternative is a hand-maintained list</b> of 40 <c>AddScoped</c> lines that someone
    /// forgets to update, producing a runtime "no handler registered" error in whichever
    /// environment happens to exercise that path first.
    /// </para>
    /// <para>
    /// <b>The trade-off, stated honestly:</b> scanning costs reflection at startup (single-digit
    /// milliseconds here) and makes registration implicit — you cannot ctrl-click from an
    /// interface to find where it was registered. It also does not work under Native AOT
    /// trimming without extra care. For an application of this size it is clearly worth it;
    /// for a library it usually is not.
    /// </para>
    /// </remarks>
    private static void RegisterHandlers(IServiceCollection services, Assembly assembly, Type openHandlerInterface)
    {
        // GetTypes() over GetExportedTypes(): handlers are deliberately `internal`, so nothing
        // outside the Application layer can call one directly instead of going through the
        // dispatcher - which would skip the whole behaviour pipeline.
        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            foreach (Type iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != openHandlerInterface)
                {
                    continue;
                }

                // Registered against the closed interface, e.g.
                // IRequestHandler<SubmitOrderCommand, Result> -> SubmitOrderCommandHandler.
                services.AddScoped(iface, type);
            }
        }
    }
}
