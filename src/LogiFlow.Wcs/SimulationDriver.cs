using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Infrastructure.Automation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Wcs;

/// <summary>
/// Drives the simulated warehouse forward in real time. Does nothing when the gateway is real.
/// </summary>
/// <remarks>
/// <para>
/// The simulator advances only when somebody calls <see cref="SimulatedPlcGateway.TickAsync"/>,
/// which is what lets a test run a simulated hour in milliseconds. In a running process something
/// has to do that on a real timer, and this is it.
/// </para>
/// <para>
/// <b>Why the tick is not on <see cref="IEquipmentGateway"/>.</b> Nothing drives a real conveyor
/// forward by calling a method. Putting a tick on the port would let application code depend on
/// something only the fake can do — and then the day a real gateway is registered, the code that
/// quietly relied on it stops working for a reason nobody can see. So the driver knows it has a
/// simulator, and everything above the port does not.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
/// <param name="gateway">Whatever gateway was registered.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class SimulationDriver(
    IEquipmentGateway gateway,
    TimeProvider timeProvider,
    ILogger<SimulationDriver> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (gateway is not SimulatedPlcGateway simulator)
        {
            logger.LogInformation("Gateway is real ({Gateway}); no simulation driver needed", gateway.Description);
            return;
        }

        logger.LogInformation("Driving {Gateway}", simulator.Description);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await simulator.TickAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(TickInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        finally
        {
            if (simulator.DroppedSamples > 0)
            {
                // Worth saying out loud rather than leaving in a metric nobody queries: it means
                // the dispatcher could not keep up with the floor, and the live view was stale.
                logger.LogWarning(
                    "{Dropped} telemetry samples were dropped — the consumer fell behind the machines",
                    simulator.DroppedSamples);
            }
        }
    }
}
