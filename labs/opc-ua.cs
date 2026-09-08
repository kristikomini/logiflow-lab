// This repository pins every package version centrally, in Directory.Packages.props. A
// file-based app inherits that, and then cannot carry its own versions — so this one opts
// out for itself. That is deliberate rather than lazy: these two packages are not used by
// anything in the solution, and putting them in the central manifest would imply they are.
// The pin still exists, it is just on the next two lines instead.
#:property ManagePackageVersionsCentrally=false
#:package OPCFoundation.NetStandard.Opc.Ua.Server@1.5.378.156
#:package OPCFoundation.NetStandard.Opc.Ua.Client@1.5.378.156

// ═══════════════════════════════════════════════════════════════════════════════════════
//  opc-ua — the same machine, described instead of numbered
//
//      dotnet run labs/opc-ua.cs
//
//  Run `dotnet run modbus` in Labs.Playground first. That demo reads a temperature as the
//  number 225 from register 40004, and you only know it is 22.5 °C because a spreadsheet
//  said `tenths`. This one starts a real OPC UA server, connects a real client to it over
//  opc.tcp, and asks the same question — and gets back a value that knows its own name, its
//  own type, its own unit, its own quality and its own age.
//
//  WHY THIS IS NOT IN Labs.Playground. Every demo in that project is dependency-free and
//  starts instantly; this one pulls the OPC Foundation stack and writes a self-signed
//  certificate to disk on first run. A .NET 10 file-based app keeps that weight out of the
//  solution — `#:package` above is the whole project file. Same reason tools/ works this way.
// ═══════════════════════════════════════════════════════════════════════════════════════

using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using ClientSession = Opc.Ua.Client.Session;
using IClientSession = Opc.Ua.Client.ISession;
using Subscription = Opc.Ua.Client.Subscription;
using MonitoredItem = Opc.Ua.Client.MonitoredItem;

const string EndpointUrl = "opc.tcp://localhost:48400/LogiFlow";

string pki = Path.Combine(Path.GetTempPath(), "logiflow-opcua-pki");

Console.WriteLine("── 1. The certificate, which is where every OPC UA project actually starts ──\n");

ApplicationInstance app = new()
{
    ApplicationName = "LogiFlowDemo",
    ApplicationType = ApplicationType.ClientAndServer,
};

ApplicationConfiguration config = await app.Build("urn:localhost:LogiFlow:Demo", "uri:logiflow:demo")
    .AsServer([EndpointUrl])
    .AddUnsecurePolicyNone()                    // see the note at the end of this section
    .AddUserTokenPolicy(UserTokenType.Anonymous)
    .AsClient()
    .AddSecurityConfiguration("CN=LogiFlowDemo, O=LogiFlow, C=IT", pki)
    .SetAutoAcceptUntrustedCertificates(true)   // ← the line you must not ship
    .Create();

// Creates a self-signed application certificate on first run and reuses it afterwards.
bool certificateOk = await app.CheckApplicationInstanceCertificatesAsync(
    silent: true, lifeTimeInMonths: null, ct: default);

Console.WriteLine($"   application certificate: {(certificateOk ? "present" : "MISSING")}");
Console.WriteLine($"   certificate store:       {pki}");
Console.WriteLine();
Console.WriteLine("   OPC UA is mutually authenticated by X.509 certificate, not by a password.");
Console.WriteLine("   Client and server each hold one, and each must TRUST the other's before a");
Console.WriteLine("   session is allowed. That is the single most common support call in this");
Console.WriteLine("   corner of the industry: the first connection to a new server always fails,");
Console.WriteLine("   and the fix is somebody moving a .der file from the `rejected` folder into");
Console.WriteLine("   the `trusted` folder — on both machines, usually over a phone call.");
Console.WriteLine();
Console.WriteLine("   SetAutoAcceptUntrustedCertificates(true) is above so this demo runs. It");
Console.WriteLine("   means `trust anyone who connects`, and shipping it to a plant turns the");
Console.WriteLine("   authentication story into decoration. It belongs in a commissioning tool");
Console.WriteLine("   and a test fixture; never in the thing you leave behind.\n");

await app.StartAsync(new DemoServer());
Console.WriteLine($"   server listening on {EndpointUrl}\n");

using IClientSession session = await ClientSession.Create(
    config,
    new ConfiguredEndpoint(null, new EndpointDescription(EndpointUrl)),
    updateBeforeConnect: true,
    sessionName: "logiflow-demo",
    sessionTimeout: 30_000,
    identity: new UserIdentity(),
    preferredLocales: null);

Console.WriteLine($"   session connected: {session.Connected}\n");

// ── 2. Browsing ─────────────────────────────────────────────────────────────────────
Console.WriteLine("── 2. The address space — you can ask what is there ─────────────────────────\n");

NodeId ovenFolder = new("Line1.Oven", (ushort)session.NamespaceUris.GetIndex(DemoNodeManager.Namespace));

ReferenceDescriptionCollection children = await BrowseAsync(session, ovenFolder);

Console.WriteLine($"   Line1.Oven contains {children.Count} nodes:\n");
foreach (ReferenceDescription child in children)
{
    Console.WriteLine($"      {child.BrowseName.Name,-14} {child.NodeClass}");
}

Console.WriteLine();
Console.WriteLine("   Modbus cannot do this. There is no request that means `what registers do");
Console.WriteLine("   you have, and what are they called` — the answer lives in a spreadsheet");
Console.WriteLine("   that is emailed to you, and it goes stale the first time an electrician");
Console.WriteLine("   changes something without telling anyone.\n");

// ── 3. Reading ──────────────────────────────────────────────────────────────────────
Console.WriteLine("── 3. A value that describes itself ─────────────────────────────────────────\n");

NodeId temperatureNode = new("Line1.Oven.Temperature", (ushort)session.NamespaceUris.GetIndex(DemoNodeManager.Namespace));

Node describedNode = await session.ReadNodeAsync(temperatureNode);
DataValue reading = await session.ReadValueAsync(temperatureNode);

Console.WriteLine($"   name             {describedNode.DisplayName}");
Console.WriteLine($"   description      {describedNode.Description}");
Console.WriteLine($"   value            {reading.Value}");
Console.WriteLine($"   type             {reading.Value?.GetType().Name}");
Console.WriteLine($"   status           {reading.StatusCode}");
Console.WriteLine($"   source timestamp {reading.SourceTimestamp:HH:mm:ss.fff}   ← when the MACHINE says it was true");
Console.WriteLine($"   server timestamp {reading.ServerTimestamp:HH:mm:ss.fff}   ← when the SERVER saw it");
Console.WriteLine($"   gap              {(reading.ServerTimestamp - reading.SourceTimestamp).TotalMilliseconds:0} ms   ← and here is the honest part\n");

Console.WriteLine("   That gap is zero, and it is zero because this demo is cheating: the server");
Console.WriteLine("   and the `machine` are the same process, with no scan cycle, no fieldbus and");
Console.WriteLine("   no network in between. There is nothing for the value to be late by.");
Console.WriteLine();
Console.WriteLine("   On real equipment they differ, and the difference is the number worth");
Console.WriteLine("   watching. A PLC scan finished some milliseconds ago; a gateway read it some");
Console.WriteLine("   milliseconds after that; only then did it reach you. Alarm on that gap");
Console.WriteLine("   growing and you find a struggling gateway before anyone on the floor does.");
Console.WriteLine("   Keep only ServerTimestamp — the default in most quick implementations, and");
Console.WriteLine("   a single column in most schemas — and you have thrown away the ability to");
Console.WriteLine("   notice at all.");
Console.WriteLine();

Console.WriteLine("   Four things Modbus does not carry, all of them in the protocol rather than");
Console.WriteLine("   in your code:");
Console.WriteLine();
Console.WriteLine("      a name           — and a description, written by whoever built the machine,");
Console.WriteLine("                         travelling with the value instead of beside it in an .xls");
Console.WriteLine("      a real type      — a Double, not two guessed 16-bit words in a guessed order");
Console.WriteLine("      a status code    — Good/Uncertain/Bad. A sensor can say `I do not know`,");
Console.WriteLine("                         which is not the same as zero and must never be averaged");
Console.WriteLine("                         with real readings. Storing quality is not optional.");
Console.WriteLine("      two timestamps   — and the gap between them is your latency, measurable,");
Console.WriteLine("                         for free, without adding a clock of your own");
Console.WriteLine();

// ── 4. Subscribing ──────────────────────────────────────────────────────────────────
Console.WriteLine("── 4. Subscription, not polling ─────────────────────────────────────────────\n");

Subscription subscription = new(session.DefaultSubscription)
{
    // How often the SERVER sends you a batch of whatever changed.
    PublishingInterval = 250,
};

MonitoredItem monitored = new(subscription.DefaultItem)
{
    StartNodeId = temperatureNode,
    AttributeId = Attributes.Value,

    // How often the server LOOKS. This is the number that decides whether you can see a
    // 60 ms fault pulse, and it is server-side work — asking every device for 10 ms on
    // 5,000 tags is how you bring a PLC to its knees from the comfort of your desk.
    SamplingInterval = 50,

    // If the server samples faster than it publishes, the extra samples queue instead of
    // being thrown away. Leave this at 1 and you get "the value now", not the history.
    QueueSize = 10,
    DiscardOldest = true,
};

int received = 0;

monitored.Notification += (item, _) =>
{
    foreach (DataValue value in item.DequeueValues())
    {
        received++;
        Console.WriteLine($"      {value.SourceTimestamp:HH:mm:ss.fff}   {value.Value,6:0.0} °C   {value.StatusCode}");
    }
};

subscription.AddItem(monitored);
session.AddSubscription(subscription);
await subscription.CreateAsync();

Console.WriteLine($"   publishing every {subscription.PublishingInterval} ms, sampling every {monitored.SamplingInterval} ms.");
Console.WriteLine("   The oven is drifting; nobody is polling. Watching for two seconds:\n");

await Task.Delay(2000);

// Stop it here rather than at the end of the file. The server keeps publishing until the
// subscription is deleted, and a notification landing in the middle of the paragraph below
// is exactly the interleaving a live screen has to cope with — instructive once, then noise.
await subscription.DeleteAsync(silent: true);

Console.WriteLine($"\n   {received} values arrived without a single request being sent.\n");

Console.WriteLine("   Note the two intervals. Sampling is how often the server reads the device;");
Console.WriteLine("   publishing is how often it talks to you. They are separate on purpose, so");
Console.WriteLine("   one slow network link cannot force you to miss fast events — the samples");
Console.WriteLine("   queue and arrive together, each with the timestamp it actually had.");
Console.WriteLine();
Console.WriteLine("   The knob not shown here is the DEADBAND: `only tell me when it moves by");
Console.WriteLine("   more than 0.5 °C`. On a real plant that setting is the difference between");
Console.WriteLine("   a database that fills up in a week and one that fills up in a decade, and");
Console.WriteLine("   it is the first thing to ask about when somebody says OPC UA is slow.\n");

await session.CloseAsync();
await app.StopAsync();

Console.WriteLine("── What to take to an interview ─────────────────────────────────────────────\n");
Console.WriteLine("   Modbus  — a number at an address. Free, universal, in every device ever");
Console.WriteLine("             made, and everything it means lives outside the protocol.");
Console.WriteLine("   OPC UA  — a described, typed, timestamped, quality-stamped value in a");
Console.WriteLine("             browsable address space, with certificates and subscriptions.");
Console.WriteLine();
Console.WriteLine("   You will meet both, usually in the same plant, often in the same week. The");
Console.WriteLine("   useful thing to say is not that one is better: it is that OPC UA moves the");
Console.WriteLine("   spreadsheet into the protocol, and the spreadsheet is what was going to go");
Console.WriteLine("   wrong.");

static async Task<ReferenceDescriptionCollection> BrowseAsync(IClientSession session, NodeId node)
{
    BrowseDescription description = new()
    {
        NodeId = node,
        BrowseDirection = BrowseDirection.Forward,
        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
        IncludeSubtypes = true,
        NodeClassMask = (uint)(NodeClass.Variable | NodeClass.Object),
        ResultMask = (uint)BrowseResultMask.All,
    };

    BrowseResponse response = await session.BrowseAsync(
        requestHeader: null,
        view: null,
        requestedMaxReferencesPerNode: 0,
        nodesToBrowse: [description],
        ct: default);

    return response.Results[0].References;
}

// ═══════════════════════════════════════════════════════════════════════════════════════
//  The server half — a small oven that drifts
// ═══════════════════════════════════════════════════════════════════════════════════════

internal sealed class DemoServer : StandardServer
{
    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        => new(server, configuration, null, [new DemoNodeManager(server, configuration)]);
}

internal sealed class DemoNodeManager : CustomNodeManager2
{
    /// <summary>
    /// A namespace URI, not a number. Node ids are qualified by it, so two vendors' nodes can
    /// sit in one address space without colliding — the thing a register map cannot do.
    /// </summary>
    public const string Namespace = "http://logiflow.example/line1";

    private readonly Timer _drift;
    private BaseDataVariableState? _temperature;
    private double _celsius = 22.5;

    public DemoNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, Namespace)
    {
        _drift = new Timer(Drift, null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            ushort ns = NamespaceIndexes[0];

            FolderState oven = new(null)
            {
                NodeId = new NodeId("Line1.Oven", ns),
                BrowseName = new QualifiedName("Oven", ns),
                DisplayName = "Oven",
                TypeDefinitionId = ObjectTypeIds.FolderType,
            };

            _temperature = new BaseDataVariableState(oven)
            {
                NodeId = new NodeId("Line1.Oven.Temperature", ns),
                BrowseName = new QualifiedName("Temperature", ns),
                DisplayName = "Oven temperature",
                Description = "Chamber temperature, thermocouple TC-1",
                DataType = DataTypeIds.Double,
                TypeDefinitionId = VariableTypeIds.AnalogItemType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 50,
                Value = _celsius,
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow,
            };

            BaseDataVariableState state = new(oven)
            {
                NodeId = new NodeId("Line1.Oven.State", ns),
                BrowseName = new QualifiedName("State", ns),
                DisplayName = "Machine state",
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = "Running",
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow,
            };

            oven.AddChild(_temperature);
            oven.AddChild(state);

            AddPredefinedNode(SystemContext, oven);
        }
    }

    private void Drift(object? _)
    {
        lock (Lock)
        {
            if (_temperature is null)
            {
                return;
            }

            _celsius += Random.Shared.NextDouble() - 0.45;

            _temperature.Value = Math.Round(_celsius, 1);

            // The SOURCE timestamp: when the MACHINE says this was true. Here that is genuinely
            // now, because this process IS the machine — which is why the client prints a zero
            // gap and says so rather than pretending otherwise. Behind a real PLC this is the
            // scan time, it is already in the past, and keeping it separate from the server's
            // own clock is the only way anyone can measure how stale a reading is.
            _temperature.Timestamp = DateTime.UtcNow;

            _temperature.ClearChangeMasks(SystemContext, includeChildren: false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _drift.Dispose();
        }

        base.Dispose(disposing);
    }
}
