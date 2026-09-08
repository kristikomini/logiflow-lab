using LogiFlow.Infrastructure.Automation;
using LogiFlow.Wcs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ---------------------------------------------------------------------------------------------
// THE WAREHOUSE CONTROL SYSTEM
// ---------------------------------------------------------------------------------------------
// A worker, not a web application. It has no HTTP surface and does not want one: a WCS that
// stops dispatching because an app pool recycled at 03:00 is a stopped line, with product
// backing up behind it and somebody phoning you.
//
//     cd src/LogiFlow.Wcs
//     dotnet run
//
// With no configuration at all this runs a SIMULATED warehouse — no PLC, no hardware, no
// licence, no network. Set Automation:Modbus:Host and the same dispatcher drives a real
// device instead, because everything above IEquipmentGateway is unchanged either way.
//
// Note what decides that: whether a host is configured, NOT whether this is Production. A
// capability check, like the OTLP exporter and the Redis fallback elsewhere in this
// repository. Asking "which environment is this?" is how a staging deployment ends up
// quietly driving a customer's conveyors.
//
// Covered in: course/module-28-industrial-and-ot/01-the-boundary.md
// ---------------------------------------------------------------------------------------------

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAutomation(builder.Configuration);

// Order matters only for readability; both are hosted services and start concurrently. The
// dispatcher subscribes before the driver ticks, so nothing is missed either way.
builder.Services.AddHostedService<TransportDispatcher>();
builder.Services.AddHostedService<SimulationDriver>();

await builder.Build().RunAsync();
