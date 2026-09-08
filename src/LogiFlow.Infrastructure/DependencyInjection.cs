using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Application.Features.Orders;
using LogiFlow.Application.Features.Reporting;
using LogiFlow.Infrastructure.Mailing;
using LogiFlow.Infrastructure.Messaging;
using LogiFlow.Infrastructure.Persistence;
using LogiFlow.Infrastructure.Persistence.Interceptors;
using LogiFlow.Infrastructure.Persistence.Outbox;
using LogiFlow.Infrastructure.Persistence.Queries;
using LogiFlow.Infrastructure.Persistence.Repositories;
using LogiFlow.Infrastructure.Persistence.Seed;
using LogiFlow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogiFlow.Infrastructure;

/// <summary>Registers everything the Infrastructure layer provides.</summary>
public static class DependencyInjection
{
    /// <summary>Adds persistence, repositories, read models and infrastructure services.</summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Application configuration, for connection strings.</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException(
                "Connection string 'SqlServer' is missing. Set it in appsettings.json, an "
                + "environment variable (ConnectionStrings__SqlServer), or user-secrets.");

        AddPersistence(services, connectionString);
        AddRepositories(services);
        AddReadModels(services);
        AddServices(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers the database and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For a process that needs the data but is not the application</b> — the warehouse control
    /// system in <c>LogiFlow.Wcs</c>. It deliberately does not call
    /// <see cref="AddInfrastructure"/>, because that also registers
    /// <c>OutboxProcessor</c> as a hosted service: a second process running it would publish every
    /// queued message twice, which is the "a nightly job in a service that now runs three
    /// replicas" problem with the replicas being different applications.
    /// </para>
    /// <para>
    /// Adding a hosted service inside a general-purpose <c>AddX</c> is what makes this a trap in
    /// the first place. It is worth knowing that a container registration can start a background
    /// worker you did not ask for.
    /// </para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Application configuration; supplies the SqlServer connection string.</param>
    public static IServiceCollection AddLogiFlowDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException(
                "Connection string 'SqlServer' is missing. Set it in appsettings.json, an "
                + "environment variable (ConnectionStrings__SqlServer), or user-secrets.");

        AddPersistence(services, connectionString);
        return services;
    }

    private static void AddPersistence(IServiceCollection services, string connectionString)
    {
        // Interceptors are resolved from DI (the domain-event one needs IDispatcher), so they
        // must be registered before the DbContext that consumes them.
        services.AddScoped<DomainEventDispatchingInterceptor>();

        services.AddDbContext<LogiFlowDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                // Where EF writes the __EFMigrationsHistory table. Pinning it to our schema keeps
                // a shared database tidy and avoids a collision with another service's history.
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "logiflow");

                // ── Transient fault handling ─────────────────────────────────────────────
                // SQL Server drops connections. Azure SQL throttles and drops them routinely
                // (error 40501, 40613, 49918...). Without this, a perfectly healthy application
                // returns 500s during a failover that lasts three seconds.
                //
                // The catch: this is what makes IUnitOfWork.ExecuteInTransactionAsync have to
                // use an execution strategy. See the remarks on that method.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);

                sql.CommandTimeout(30);
            });

            options.AddInterceptors(
                serviceProvider.GetRequiredService<DomainEventDispatchingInterceptor>());

            // ── Development-only diagnostics ─────────────────────────────────────────────
            // EnableSensitiveDataLogging puts PARAMETER VALUES in the log. Invaluable when
            // debugging a query; a GDPR incident in production, because customer emails and
            // addresses end up in your log aggregator. Guarded by an environment check, never
            // by "I'll remember to turn it off".
            if (IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<DatabaseSeeder>();

        // The default transport for committed events: a log line. The API replaces it with the
        // SignalR one by registering that AFTER this call, because the last registration of a
        // service type is the one a single GetRequiredService resolves. (All of them are returned
        // for IEnumerable<T> - which is why "register twice" is a bug in one shape and a feature
        // in the other.)
        services.AddScoped<IIntegrationEventPublisher, LoggingIntegrationEventPublisher>();

        // Singleton, and it therefore CANNOT take a DbContext. It uses IServiceScopeFactory -
        // see the remarks on OutboxProcessor for why this trips people up.
        services.AddHostedService<OutboxProcessor>();
    }

    private static void AddRepositories(IServiceCollection services)
    {
        // Scoped, matching the DbContext they wrap. A singleton repository holding a scoped
        // DbContext is the classic captive-dependency bug: the container throws at startup if
        // validation is on, and leaks a context across requests if it is not.
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
    }

    private static void AddReadModels(IServiceCollection services)
    {
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<IReportingQueries, ReportingQueries>();
    }

    private static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // TimeProvider.System as a singleton. Tests replace this one registration with a
        // FakeTimeProvider and the whole application's sense of time becomes controllable.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();

        // Options, a transport, the redirect guard, the durable queue and the delivery worker.
        // One call, because those five things are one subsystem - see Mailing/.
        services.AddMailing(configuration);

        // CurrentUser is scoped and mutable: the API layer's middleware injects the request's
        // ClaimsPrincipal into it. Registered as both the concrete type and the interface so the
        // middleware can resolve it to write, and handlers resolve it to read.
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

        // ── Cache ────────────────────────────────────────────────────────────────────────
        string? redis = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redis))
        {
            // Falls back to an in-memory distributed cache so the application runs without Redis.
            // Note the name: it implements IDistributedCache but is NOT distributed - each
            // instance has its own copy. Fine for one process, silently wrong for two.
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "logiflow:";
            });
        }

        services.AddScoped<ICacheService, CacheService>();
    }

    private static bool IsDevelopment() =>
        string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
}
