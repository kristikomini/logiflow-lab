using LogiFlow.Web.Components;
using LogiFlow.Web.Services;

// ═════════════════════════════════════════════════════════════════════════════════════════
//  THE COMPOSITION ROOT — WEB
//
//  Deliberately much smaller than the API's. This process owns no database, no domain rules and
//  no transactions; it renders HTML and calls one HTTP service. If this file ever grows a
//  DbContext, something has gone architecturally wrong.
//
//  Covered in: course/module-18-blazor/README.md
// ═════════════════════════════════════════════════════════════════════════════════════════

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── Blazor ───────────────────────────────────────────────────────────────────────────────
// AddInteractiveServerComponents wires up the SignalR circuit that carries UI events to the
// server and DOM diffs back. Without it, components render once as static HTML and no button
// ever does anything - which is the single most common "my Blazor app is dead" question.
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// ── API settings, validated at startup ───────────────────────────────────────────────────
// The same ValidateOnStart pattern the API uses for its JWT options (module 15): a bad base URL
// should stop the process at deploy time, not surface as a confusing "connection refused" on
// the first page load.
builder.Services
    .AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .Validate(
        o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
        $"{ApiOptions.SectionName}:BaseUrl must be an absolute URL, e.g. http://localhost:5199")
    .ValidateOnStart();

// ── HTTP clients ─────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAccessTokenProvider, DevAccessTokenProvider>();
builder.Services.AddTransient<AccessTokenHandler>();

// The token-minting client. UNAUTHENTICATED on purpose: giving it the AccessTokenHandler would
// make fetching a token require a token.
builder.Services.AddHttpClient(DevAccessTokenProvider.ClientName, (services, client) =>
{
    client.BaseAddress = new Uri(GetApiBaseUrl(services));
});

// The typed client every page uses.
builder.Services
    .AddHttpClient<LogiFlowApiClient>(LogiFlowApiClient.ClientName, (services, client) =>
    {
        client.BaseAddress = new Uri(GetApiBaseUrl(services));
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler<AccessTokenHandler>();

WebApplication app = builder.Build();

// ═════════════════════════════════════════════════════════════════════════════════════════
//  THE PIPELINE
//
//  Same rules as the API (module 15): the request travels down this list and the response comes
//  back up, and order is behaviour.
// ═════════════════════════════════════════════════════════════════════════════════════════

if (!app.Environment.IsDevelopment())
{
    // createScopeForErrors matters here: the error page is rendered by a component, and without
    // its own DI scope it would reuse the scope of the request that just failed - which may be
    // exactly what is broken.
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serves wwwroot with content hashing and pre-compression. The .NET 9+ replacement for
// UseStaticFiles; the @Assets["app.css"] in App.razor resolves against the manifest it builds.
app.MapStaticAssets();

// REQUIRED for Blazor Web Apps, and must sit before MapRazorComponents. Interactive components
// post back over the circuit, and antiforgery validation is what stops a third-party page from
// driving your UI. Omit it and form posts fail with an error that names none of this.
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

// ═════════════════════════════════════════════════════════════════════════════════════════

// Reads the configured API base URL. A local function rather than a captured variable because
// the option is resolved from DI per client, which keeps ValidateOnStart as the single source of
// truth for whether the value is usable.
static string GetApiBaseUrl(IServiceProvider services) =>
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value.BaseUrl;
