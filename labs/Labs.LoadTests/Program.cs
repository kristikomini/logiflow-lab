using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace Labs.LoadTests;

/// <summary>
/// Load tests against a running LogiFlow API.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a benchmark, and the difference is the whole point of the project.</b>
/// <c>Labs.Benchmarks</c> measures a method: one thread, no network, no database, nanoseconds.
/// It answers "is this code fast?" and it answers it very precisely.
/// </para>
/// <para>
/// Nothing it measures can tell you what happens when fifty callers arrive at once. That is
/// where a connection pool of finite size, a database taking locks, a thread pool growing one
/// thread per second, and a <c>rowversion</c> column arbitrating between writers all start to
/// matter — and none of those exist in a microbenchmark. A system whose every method is fast
/// can still fall over at forty concurrent users, and the usual reason is that somebody only
/// ever measured the methods.
/// </para>
/// <para>
/// <b>Running it</b> — the API must be up first:
/// </para>
/// <code>
/// cd src/LogiFlow.Api &amp;&amp; dotnet run
/// cd labs/Labs.LoadTests &amp;&amp; dotnet run
/// </code>
/// <para>
/// Deliberately NOT in <c>LogiFlow.slnf</c>: a test that needs a server is not a unit test, and
/// putting it in the CI run would make a green build depend on something CI does not start.
/// </para>
/// Covered in: <c>course/module-14-performance/</c>
/// </remarks>
public static class Program
{
    private static readonly Uri BaseAddress =
        new(Environment.GetEnvironmentVariable("LOGIFLOW_API") ?? "http://localhost:5199");

    /// <summary>How many distinct authenticated identities the load is spread across.</summary>
    /// <remarks>
    /// The API rate-limits at 100 requests per minute PER USER. The read scenario injects 50/s
    /// for 30 seconds - 1,500 requests - so it needs at least 15 identities to stay under the
    /// limit, and 60 leaves room for the other two scenarios running alongside it. Raise the
    /// injection rate and this has to move with it, or the run measures throttling again.
    /// </remarks>
    private const int IdentityCount = 60;

    /// <summary>Entry point.</summary>
    /// <param name="args">Scenario name, or nothing to run them all.</param>
    /// <returns>0 when every scenario met its thresholds.</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"LogiFlow load tests against {BaseAddress}");
        Console.WriteLine();

        HttpClient[] clients;
        string token;
        try
        {
            token = await GetTokenAsync("setup@logiflow.local");

            // == A POOL OF IDENTITIES, and the reason for it is a finding, not a workaround ==
            //
            // The first run of this file drove every scenario through ONE token, and 96% of
            // requests came back 429. The rate limiter in Program.cs partitions by authenticated
            // user at 100 requests per minute, so a single identity firing 50/s is a single user
            // being throttled - and what the run measured was the rate limiter, precisely and
            // uselessly.
            //
            // Worth remembering whenever a load test produces a suspiciously flat number: the
            // first thing a load test usually finds is your own protection. The fix is not to
            // raise the limit. It is to generate load shaped like the traffic being modelled,
            // and real traffic arrives from many users.
            clients = await Task.WhenAll(Enumerable
                .Range(0, IdentityCount)
                .Select(async i => CreateClient(await GetTokenAsync($"load{i}@logiflow.local"))));
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Cannot reach the API at {BaseAddress}: {ex.Message}");
            Console.Error.WriteLine("Start it first:  cd src/LogiFlow.Api && dotnet run");
            return 2;
        }

        (Guid customerId, Guid productId, Guid contendedOrderId) = await SeedAsync(token);

        string? only = args.FirstOrDefault();

        NBomber.Contracts.ScenarioProps[] scenarios =
        [
            ReadHeavy(clients),
            OrderCreation(clients, customerId),
            ContendedWrites(clients, contendedOrderId, productId),
        ];

        if (only is not null)
        {
            scenarios = [.. scenarios.Where(s => s.ScenarioName.Contains(only, StringComparison.OrdinalIgnoreCase))];

            if (scenarios.Length == 0)
            {
                Console.Error.WriteLine($"No scenario matches '{only}'.");
                return 2;
            }
        }

        NBomber.Contracts.Stats.NodeStats stats = NBomberRunner
            .RegisterScenarios(scenarios)
            // Writes an HTML report with the latency distribution per scenario. Read the
            // percentile table, not the summary line at the top.
            .WithReportFolder("load-reports")
            .Run();

        // A load test that always exits 0 is a chart, not a test. The assertions below are what
        // make it able to fail — and they are on PERCENTILES, never on the mean. A mean latency
        // of 40 ms is compatible with one caller in twenty waiting two seconds, and it is the
        // one in twenty who files the ticket.
        return stats.ScenarioStats.All(s => s.Ok.Latency.Percent99 < 2000) ? 0 : 1;
    }

    /// <summary>
    /// The shape most APIs actually see: many readers, few writers.
    /// </summary>
    private static NBomber.Contracts.ScenarioProps ReadHeavy(HttpClient[] clients)
    {
        return Scenario.Create("read_heavy", async context =>
        {
            HttpClient client = Pick(clients, context);

            HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/orders?page=1&pageSize=25", UriKind.Relative));

            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail(statusCode: ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // INJECT, not KeepConstant. The difference is the one thing to take away from this
            // file: KeepConstant holds N concurrent clients, so when the server slows down the
            // clients wait and the ARRIVAL RATE falls with it. The system is then never pushed
            // past what it can handle, and the test reports a comfortable latency for a load
            // that never happened. Inject sends N requests per second regardless of whether the
            // previous ones finished — which is what real users do, and it is the only way to
            // find the point where queues start growing.
            Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    /// <summary>
    /// Writes: a full transaction per request, through the behaviour pipeline and the outbox.
    /// </summary>
    private static NBomber.Contracts.ScenarioProps OrderCreation(HttpClient[] clients, Guid customerId)
    {
        return Scenario.Create("order_creation", async context =>
        {
            HttpClient client = Pick(clients, context);

            HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/v1/orders", UriKind.Relative),
                new
                {
                    customerId,
                    currencyCode = "EUR",
                    shippingAddress = new
                    {
                        line1 = "Via Emilia 9",
                        line2 = (string?)null,
                        city = "Modena",
                        region = (string?)null,
                        postalCode = "41100",
                        countryCode = "IT",
                    },
                });

            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail(statusCode: ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // A tenth of the read rate. Writes take a transaction and a row lock; running them
            // at read volume measures the database's patience, not the application's.
            Simulation.Inject(rate: 5, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    /// <summary>
    /// The interesting one: many writers, one row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReservationConcurrencyTests</c> fires twenty parallel reservations against one stock
    /// item and asserts the total never goes negative. That test proves the guard is CORRECT.
    /// It says nothing about what the guard COSTS.
    /// </para>
    /// <para>
    /// This does. Every caller here writes to the same order, so they queue behind the same
    /// <c>rowversion</c> — and the measured p95 is roughly ten times the p50, which is what
    /// serialising on one row looks like from outside.
    ///
    /// <b>At 20 requests per second it does not actually produce many 409s</b>, and that is
    /// worth stating rather than dressing up: each write finishes in ~40 ms, so most of them
    /// never overlap. Raise the injection rate until conflicts appear and the throughput curve
    /// stops being a straight line — the shape people mean when they say a system "does not
    /// scale", even though nothing is broken and no test is red. Finding that rate is the
    /// exercise; the scenario is the instrument.
    /// </para>
    /// <para>
    /// So 409 is counted as a SUCCESS here, not a failure. It is the system working exactly as
    /// designed. What the report shows instead is how much of your throughput that correctness
    /// costs under contention, which is the number nobody has until they measure it.
    /// </para>
    /// </remarks>
    private static NBomber.Contracts.ScenarioProps ContendedWrites(HttpClient[] clients, Guid orderId, Guid productId)
    {
        return Scenario.Create("contended_writes", async context =>
        {
            HttpClient client = Pick(clients, context);

            // Every request in this scenario writes to the SAME order, so every one of them
            // reads the same rowversion and all but one loses the race.
            HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri($"/api/v1/orders/{orderId}/lines", UriKind.Relative),
                new { productId, quantity = 1 });

            if (response.IsSuccessStatusCode)
            {
                return Response.Ok(statusCode: "200");
            }

            // 409 Conflict: optimistic concurrency did its job. Counted separately so the report
            // shows the contention rate rather than burying it in a failure count.
            return response.StatusCode == System.Net.HttpStatusCode.Conflict
                ? Response.Ok(statusCode: "409-conflict")
                : Response.Fail(statusCode: ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20)));
    }

    // ── Setup ────────────────────────────────────────────────────────────────────────────

    /// <summary>Picks the identity for one request, round-robin across the pool.</summary>
    /// <remarks>
    /// Round-robin rather than random: it spreads requests evenly across rate-limit partitions
    /// by construction, where random would leave some identities near their limit and others
    /// idle, and the resulting 429s would read as a server problem.
    /// </remarks>
    private static HttpClient Pick(HttpClient[] clients, NBomber.Contracts.IScenarioContext context) =>
        clients[(int)(context.InvocationNumber % clients.Length)];

    private static HttpClient CreateClient(string token)
    {
        HttpClient client = new() { BaseAddress = BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> GetTokenAsync(string email)
    {
        using HttpClient client = new() { BaseAddress = BaseAddress };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/dev/token", UriKind.Relative),
            new { email, roles = new[] { "Admin", "Analyst", "WarehouseStaff" } });

        response.EnsureSuccessStatusCode();

        JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    /// <summary>Finds a customer and a product, and opens the one order the writers will fight over.</summary>
    /// <remarks>
    /// Reads the seeded reference data rather than creating its own. A load test that seeds
    /// thousands of rows per run measures the seeding as much as the system, and leaves a
    /// database that differs between runs — so two runs are not comparable, which is the only
    /// thing a load test is for.
    /// </remarks>
    private static async Task<(Guid CustomerId, Guid ProductId, Guid ContendedOrderId)> SeedAsync(string token)
    {
        using HttpClient client = CreateClient(token);

        JsonElement customers = await GetAsync(client, "/api/v1/reports/customers?minimumOrders=0");
        JsonElement lowStock = await GetAsync(client, "/api/v1/reports/low-stock");

        if (customers.GetArrayLength() == 0 || lowStock.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "No seeded customers or stock. Start the API once in Development so it seeds.");
        }

        Guid customerId = customers[0].GetProperty("customerId").GetGuid();
        Guid productId = lowStock[0].GetProperty("productId").GetGuid();

        // ONE order, opened here, that every writer in the contention scenario will target.
        HttpResponseMessage created = await client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new
            {
                customerId,
                currencyCode = "EUR",
                shippingAddress = new
                {
                    line1 = "Via Emilia 9",
                    line2 = (string?)null,
                    city = "Modena",
                    region = (string?)null,
                    postalCode = "41100",
                    countryCode = "IT",
                },
            });

        created.EnsureSuccessStatusCode();

        Guid contendedOrderId = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync())
            .RootElement
            .GetGuid();

        return (customerId, productId, contendedOrderId);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(new Uri(url, UriKind.Relative));
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
