using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Automation;

/// <summary>Registers the machine layer.</summary>
public static class AutomationServiceCollectionExtensions
{
    /// <summary>
    /// Wires up one <see cref="IEquipmentGateway"/>, chosen by whether real equipment is configured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a capability check, not an environment check.</b> The question asked is "is a
    /// Modbus host configured?", never "is this Production?". Asking the second question is how a
    /// staging deployment ends up quietly driving a customer's conveyors, and how a production
    /// deployment with a missing config section silently talks to nothing. The same reasoning
    /// governs the OTLP exporter in the API's <c>Program.cs</c> and the Redis fallback in
    /// <c>DependencyInjection.cs</c>: ask what you have, not where you are.
    /// </para>
    /// <para>
    /// <b>The default is the simulator</b>, so cloning this repository and pressing run gives a
    /// warehouse that does not exist. Reaching a real PLC requires somebody to have typed a host
    /// name on purpose — which is the same safety default the mailer takes with
    /// <c>EmailTransport.Log</c>.
    /// </para>
    /// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddAutomation(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AutomationOptions>()
            .Bind(configuration.GetSection(AutomationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingletonTimeProvider();

        services.AddSingleton<IEquipmentGateway>(provider =>
        {
            AutomationOptions options = provider.GetRequiredService<IOptions<AutomationOptions>>().Value;
            TimeProvider clock = provider.GetRequiredService<TimeProvider>();
            ILoggerFactory loggers = provider.GetRequiredService<ILoggerFactory>();

            if (options.Modbus is { } modbus && !string.IsNullOrWhiteSpace(modbus.Host))
            {
                // On a real installation the fleet is commissioning data — somebody wrote down
                // which machine sits at which register. It is built from the simulation section's
                // vehicle count here only so the two adapters are interchangeable in a demo; a
                // real deployment would bind a list of machines with their own codes.
                IReadOnlyList<EquipmentDescriptor> fleet =
                [
                    .. Enumerable.Range(0, options.Simulation.Vehicles)
                        .Select(i => new EquipmentDescriptor(EquipmentId.New(), $"LGV-{i + 1:00}", EquipmentKind.Agv)),
                ];

                return new ModbusEquipmentGateway(
                    fleet, modbus, clock, loggers.CreateLogger<ModbusEquipmentGateway>());
            }

            return new SimulatedPlcGateway(
                options.Simulation, clock, loggers.CreateLogger<SimulatedPlcGateway>());
        });

        return services;
    }

    /// <summary>
    /// Registers the system clock only if nothing else already has.
    /// </summary>
    /// <remarks>
    /// The API host registers <see cref="TimeProvider"/> as part of its own composition. A worker
    /// that calls <see cref="AddAutomation"/> on its own needs one too, and registering a second
    /// would shadow a test's <c>FakeTimeProvider</c> — so this adds one only when the container
    /// does not already have it.
    /// </remarks>
    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
