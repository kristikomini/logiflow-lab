using LogiFlow.Application;
using LogiFlow.Infrastructure;
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
// The database, and ONLY the database. AddInfrastructure would also start the outbox
// processor here, and two processes draining one outbox publishes everything twice.
builder.Services.AddLogiFlowDatabase(builder.Configuration);

// The Application layer comes with it, and not for tidiness: the DbContext is built with the
// domain-event interceptor, which resolves IDispatcher on every SaveChanges. Without this the
// worker starts, dispatches happily, and then throws on the first write — which is exactly what
// it did, because a unit test with an in-memory store never touches that path. Registers no
// hosted services of its own, so nothing here runs twice.
builder.Services.AddApplication();

// The dispatcher is registered as a singleton AND as a hosted service, deliberately: the
// persister needs the same instance, and AddHostedService<T>() on its own would construct a
// second one that shares nothing.
builder.Services.AddSingleton<TransportDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TransportDispatcher>());

// Order matters only for readability; all three start concurrently. The dispatcher subscribes
// before the driver ticks, so nothing is missed either way.
builder.Services.AddHostedService<TransportOrderPersister>();
builder.Services.AddHostedService<SimulationDriver>();

await builder.Build().RunAsync();
