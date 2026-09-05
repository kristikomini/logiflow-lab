using System.Text.Json.Serialization;

namespace LogiFlow.Web.Services;

/// <summary>Supplies a bearer token for outgoing API calls.</summary>
public interface IAccessTokenProvider
{
    /// <summary>Returns a valid token, minting or refreshing one if necessary.</summary>
    /// <param name="cancellationToken">Cancels the mint request.</param>
    /// <returns>The token, or <c>null</c> if one could not be obtained.</returns>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>Settings for talking to the LogiFlow API.</summary>
public sealed class ApiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "LogiFlowApi";

    /// <summary>Base address of the API, including scheme and port.</summary>
    public string BaseUrl { get; init; } = "http://localhost:5199";

    /// <summary>Identity the development token is minted for.</summary>
    public string DevEmail { get; init; } = "ui@logiflow.local";

    /// <summary>Roles requested on the development token.</summary>
    /// <remarks>
    /// <c>Admin</c> alone is not enough: the reporting endpoints require the <c>Analyst</c>
    /// policy and inventory requires <c>WarehouseStaff</c>. Policies are evaluated against the
    /// roles actually on the token, so asking for one role and calling an endpoint that needs
    /// another produces a <b>403, not a 401</b> - see module 15.
    /// </remarks>
    public IReadOnlyList<string> DevRoles { get; init; } = ["Admin", "Analyst", "WarehouseStaff"];
}

/// <summary>
/// Mints and caches a development JWT from the API's <c>/api/dev/token</c> endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is scaffolding, and it is labelled as such deliberately.</b> A real deployment
/// replaces this entire class with OpenID Connect: the user signs in against Entra ID, Keycloak
/// or whatever the company runs, and this app exchanges that for an access token. Nothing else in
/// the UI changes, because everything downstream only knows <see cref="IAccessTokenProvider"/>.
/// That is the payoff for putting an interface here rather than calling the endpoint inline.
/// </para>
/// <para>
/// <b>The token never reaches the browser.</b> Because this is InteractiveServer, the token lives
/// in server memory and the browser holds only a SignalR circuit. That removes the entire class
/// of "where do I store the JWT" problems that a WebAssembly front end has to solve - and it is
/// the same argument for a backend-for-frontend in a JavaScript stack.
/// </para>
/// <para>
/// Registered as a singleton, so one token is shared by every user of this dev instance. That is
/// fine for scaffolding and completely wrong for a real one, where the token is per-user and
/// therefore scoped.
/// </para>
/// </remarks>
/// <param name="httpClientFactory">Creates the unauthenticated client used to mint tokens.</param>
/// <param name="options">API settings.</param>
/// <param name="logger">Log sink.</param>
public sealed class DevAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<ApiOptions> options,
    ILogger<DevAccessTokenProvider> logger) : IAccessTokenProvider
{
    /// <summary>
    /// The named client used to mint tokens.
    /// </summary>
    /// <remarks>
    /// A <b>separate</b> client from the one the token handler decorates. Minting a token through
    /// the authenticated client would make the handler ask this provider for a token in order to
    /// send the request that fetches the token - infinite recursion, and a stack overflow rather
    /// than a helpful error.
    /// </remarks>
    public const string ClientName = "logiflow-token";

    // Renew slightly early. A token that expires between the check and the server reading it
    // produces an intermittent 401 that is miserable to reproduce.
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsUsable())
        {
            return _token;
        }

        // One caller mints; the rest wait and then find the fresh token already cached. Without
        // the gate, a page that fires six parallel API calls on first render mints six tokens.
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (IsUsable())
            {
                return _token;
            }

            HttpClient client = httpClientFactory.CreateClient(ClientName);

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/dev/token",
                new DevTokenRequest(options.Value.DevEmail, options.Value.DevRoles),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Could not mint a development token: the API returned {StatusCode}. " +
                    "Is it running, and is it in the Development environment?",
                    (int)response.StatusCode);

                return null;
            }

            DevTokenResponse? token = await response.Content
                .ReadFromJsonAsync<DevTokenResponse>(cancellationToken);

            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                logger.LogError("The development token endpoint returned no token.");
                return null;
            }

            _token = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds);

            logger.LogInformation("Minted a development token, valid until {ExpiresAt:u}.", _expiresAt);

            return _token;
        }
        catch (HttpRequestException ex)
        {
            // Swallowed on purpose and reported as "no token". The UI renders a readable banner
            // from the resulting 401 rather than showing the user a stack trace, and the log line
            // above carries the detail an operator needs.
            logger.LogError(ex, "The API is unreachable at {BaseUrl}.", options.Value.BaseUrl);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsUsable() =>
        _token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewalMargin;

    private sealed record DevTokenRequest(string Email, IReadOnlyList<string> Roles);

    private sealed record DevTokenResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds);
}

/// <summary>
/// Attaches the bearer token to every outgoing API request.
/// </summary>
/// <remarks>
/// A <see cref="DelegatingHandler"/> rather than a line in each method: it runs for every request
/// through the typed client, including ones added later by someone who has never read this file.
/// It is the <c>HttpClient</c> pipeline's equivalent of middleware, and it composes the same way.
/// </remarks>
/// <param name="tokenProvider">Supplies the token.</param>
public sealed class AccessTokenHandler(IAccessTokenProvider tokenProvider) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? token = await tokenProvider.GetTokenAsync(cancellationToken);

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        // No token still sends the request. The API answers 401, the client turns that into a
        // readable ApiError, and the user sees "not authenticated" instead of a blank page.
        return await base.SendAsync(request, cancellationToken);
    }
}
