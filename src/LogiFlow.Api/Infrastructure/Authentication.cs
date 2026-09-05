using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LogiFlow.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

namespace LogiFlow.Api.Infrastructure;

/// <summary>Named authorization policies. Constants, never magic strings at call sites.</summary>
/// <remarks>
/// <b>Policies, not roles, at the endpoint.</b> Writing
/// <c>.RequireAuthorization(Roles = "Admin,Manager")</c> scatters the rule across every endpoint,
/// so changing who may manage the catalogue means finding all of them. A named policy is defined
/// once and referenced everywhere — and the definition can later become "has the
/// catalog:write scope" or "is in this Entra group" without touching a single endpoint.
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>May create products and change prices.</summary>
    public const string CatalogManager = nameof(CatalogManager);

    /// <summary>May move stock and dispatch shipments.</summary>
    public const string WarehouseStaff = nameof(WarehouseStaff);

    /// <summary>May read reports.</summary>
    public const string Analyst = nameof(Analyst);
}

/// <summary>JWT signing and validation settings, bound from configuration.</summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Who issued the token.</summary>
    public string Issuer { get; init; } = "logiflow";

    /// <summary>Who the token is for.</summary>
    public string Audience { get; init; } = "logiflow-api";

    /// <summary>
    /// The HMAC signing key.
    /// </summary>
    /// <remarks>
    /// <b>This must never be committed.</b> The value in <c>appsettings.Development.json</c> is
    /// a throwaway for local use only. In production it comes from an environment variable, a
    /// key vault, or user-secrets — see <c>course/module-13-deployment/02-configuration.md</c>.
    /// Anyone holding this key can mint a token for any user with any role.
    /// </remarks>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Token lifetime in minutes.</summary>
    public int ExpiryMinutes { get; init; } = 60;
}

/// <summary>Wires up JWT bearer authentication and the authorization policies.</summary>
public static class AuthenticationSetup
{
    /// <summary>Minimum HMAC-SHA256 key length in bytes. Shorter keys are rejected by the framework.</summary>
    private const int MinimumKeyBytes = 32;

    /// <summary>Adds authentication and authorization.</summary>
    public static IServiceCollection AddLogiFlowAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            // Validation at STARTUP, not at first request. A missing signing key should stop the
            // process from starting, loudly, rather than produce 500s an hour later when the
            // first user tries to log in. ValidateOnStart is the switch that makes it eager.
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey),
                "Jwt:SigningKey is not configured. Set it via user-secrets or the environment.")
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.SigningKey) >= MinimumKeyBytes,
                $"Jwt:SigningKey must be at least {MinimumKeyBytes} bytes for HMAC-SHA256.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Resolved lazily so the validated JwtOptions is available.
                IServiceProvider provider = services.BuildServiceProvider();
                JwtOptions jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Every one of these matters. Turning any of them off is a real vulnerability:
                    ValidateIssuer = true,              // else a token from any issuer is accepted
                    ValidateAudience = true,            // else a token for another service is accepted
                    ValidateLifetime = true,            // else expired tokens work forever
                    ValidateIssuerSigningKey = true,    // else a forged token is accepted
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

                    // Default is FIVE MINUTES of leeway, which means a "revoked" token keeps
                    // working for five minutes after expiry. Rarely what anyone intends.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // Development only: the default handler hides why a token was rejected, which is
                // correct for production and infuriating while building a client.
                options.IncludeErrorDetails = true;

                // ── The WebSocket authentication gotcha ────────────────────────────────────
                //
                // The browser's WebSocket API takes a URL and a subprotocol list. It does NOT
                // let you set request headers, so `Authorization: Bearer ...` is simply not
                // available on the handshake — and SignalR's JavaScript client therefore puts
                // the token in the query string as `access_token` instead.
                //
                // Nothing reads it unless you write this. The symptom is a hub that returns 401
                // to a client holding a token that works perfectly on every REST endpoint, which
                // sends people looking at CORS and at the token itself for an afternoon.
                //
                // Two things keep the query-string exception narrow, and both matter: it applies
                // ONLY to paths under /hubs, and only when the header is absent. A token in a URL
                // is a token in the server's access log, in the browser's history and in any
                // proxy in between — acceptable for a WebSocket handshake that has no
                // alternative, and not acceptable anywhere that does.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token)
                            && context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
                            && context.Request.Query.TryGetValue("access_token", out StringValues token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.CatalogManager,
                policy => policy.RequireRole("Admin", "CatalogManager"));

            options.AddPolicy(
                AuthorizationPolicies.WarehouseStaff,
                policy => policy.RequireRole("Admin", "WarehouseStaff"));

            options.AddPolicy(
                AuthorizationPolicies.Analyst,
                policy => policy.RequireRole("Admin", "Analyst", "CatalogManager"));

            // Endpoints that do not opt out are authenticated. Secure by default: forgetting
            // .RequireAuthorization() leaves an endpoint closed, not open. The inverse default
            // is how endpoints get shipped unprotected.
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}

/// <summary>
/// Issues development tokens so the API can be exercised without an identity provider.
/// </summary>
/// <remarks>
/// <b>This is not a login system.</b> There is no password check, no user store, and no refresh
/// token — it exists so <c>logiflow.http</c> and the integration tests can obtain a bearer token.
/// A real deployment delegates this to Entra ID, Auth0, Keycloak or IdentityServer, and the
/// endpoint is removed. It is registered only in Development for that reason.
/// </remarks>
public static class DevTokenEndpoint
{
    /// <summary>Registers <c>POST /api/dev/token</c>. Development only.</summary>
    public static IEndpointRouteBuilder MapDevTokenEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/dev/token", (DevTokenRequest request, IOptions<JwtOptions> jwtOptions) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                JwtOptions jwt = jwtOptions.Value;

                var credentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    SecurityAlgorithms.HmacSha256);

                List<Claim> claims =
                [
                    new(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
                    new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                    new(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
                    new(ClaimTypes.Email, request.Email),
                ];

                claims.AddRange((request.Roles ?? ["Admin"]).Select(role => new Claim(ClaimTypes.Role, role)));

                var token = new JwtSecurityToken(
                    issuer: jwt.Issuer,
                    audience: jwt.Audience,
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(jwt.ExpiryMinutes),
                    signingCredentials: credentials);

                return Results.Ok(new
                {
                    accessToken = new JwtSecurityTokenHandler().WriteToken(token),
                    expiresInSeconds = jwt.ExpiryMinutes * 60,
                });
            })
            .AllowAnonymous()
            .WithTags("Development")
            .WithSummary("Mints a development JWT. Never registered outside Development.");

        return app;
    }
}

/// <summary>Request body for the development token endpoint.</summary>
/// <param name="Email">Email to embed in the token.</param>
/// <param name="Roles">Roles to grant. Defaults to <c>Admin</c>.</param>
public sealed record DevTokenRequest(string Email, string[]? Roles);

/// <summary>
/// Copies the authenticated principal into the scoped <see cref="CurrentUser"/> so the
/// Application layer can read it without referencing ASP.NET Core.
/// </summary>
/// <param name="next">The next middleware.</param>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    /// <summary>Runs the middleware.</summary>
    public Task InvokeAsync(HttpContext context, CurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentUser);

        currentUser.SetPrincipal(context.User);

        return next(context);
    }
}
