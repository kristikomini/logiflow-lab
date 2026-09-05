using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LogiFlow.Web.Contracts;

namespace LogiFlow.Web.Services;

/// <summary>
/// The UI's single door onto the LogiFlow API.
/// </summary>
/// <remarks>
/// <para>
/// <b>A typed client, not <c>HttpClient</c> injected into components.</b> Every request goes
/// through here, which means the base address, the auth handler, the JSON options, the resilience
/// pipeline and the error translation are configured once. A component that reaches for
/// <c>HttpClient</c> directly bypasses all five.
/// </para>
/// <para>
/// <b>Every method returns a result, never throws for an expected failure.</b> A 404 on an order
/// id and a 409 on submitting an already-submitted order are ordinary outcomes of a UI where the
/// user can type a URL or leave a tab open. In Blazor Server this matters more than usual: an
/// unhandled exception tears down the circuit and the user's screen goes blank mid-task.
/// </para>
/// Covered in: <c>course/module-18-blazor/README.md</c>
/// </remarks>
/// <param name="http">The configured, authenticated client.</param>
/// <param name="logger">Log sink.</param>
public sealed class LogiFlowApiClient(HttpClient http, ILogger<LogiFlowApiClient> logger)
{
    /// <summary>Name used for the typed client's registration.</summary>
    public const string ClientName = "logiflow-api";

    // Matches the API: camelCase, case-insensitive on read. Created once - constructing
    // JsonSerializerOptions per call defeats its internal metadata cache and is a genuine
    // performance bug, not a style preference.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Searches orders with filtering, sorting and paging.</summary>
    /// <param name="request">The filter, sort and page state.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<ApiResult<PagedResult<OrderSummary>>> SearchOrdersAsync(
        OrderSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The API binds this with [AsParameters] from the QUERY STRING, so this is built by hand
        // rather than serialised as a body. Omitted parameters mean "no constraint" - which is
        // why null filters are skipped entirely instead of being sent as empty strings.
        var query = new StringBuilder("/api/orders?");

        Append(query, "page", request.Page.ToString(CultureInfo.InvariantCulture));
        Append(query, "pageSize", request.PageSize.ToString(CultureInfo.InvariantCulture));
        Append(query, "sortBy", request.SortBy.ToString());
        Append(query, "sortDirection", request.SortDirection.ToString());

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            Append(query, "searchTerm", request.SearchTerm.Trim());
        }

        if (request.Status is not null)
        {
            Append(query, "status", request.Status.Value.ToString());
        }

        if (request.MinimumTotal is not null)
        {
            Append(query, "minimumTotal", request.MinimumTotal.Value.ToString(CultureInfo.InvariantCulture));
        }

        return await GetAsync<PagedResult<OrderSummary>>(query.ToString(), cancellationToken);
    }

    /// <summary>Reads a single order in full.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<ApiResult<OrderDetail>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        GetAsync<OrderDetail>(
            FormattableString.Invariant($"/api/orders/{orderId}"),
            cancellationToken);

    /// <summary>Opens a new draft order.</summary>
    /// <param name="customerId">Who the order is for.</param>
    /// <param name="currencyCode">ISO currency code.</param>
    /// <param name="shippingAddress">Where it goes, if known yet.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<ApiResult<Guid>> CreateOrderAsync(
        Guid customerId,
        string currencyCode,
        Address? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage? response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(
                    new { customerId, currencyCode, shippingAddress },
                    options: SerializerOptions),
            },
            cancellationToken);

        if (response is null)
        {
            return ApiResult<Guid>.Failure(UnreachableError());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<Guid>.Failure(await ReadProblemAsync(response, cancellationToken));
            }

            Guid id = await response.Content.ReadFromJsonAsync<Guid>(SerializerOptions, cancellationToken);
            return ApiResult<Guid>.Success(id);
        }
    }

    /// <summary>Adds a product to a draft order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="productId">The product.</param>
    /// <param name="quantity">Units to add.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<ApiResult> AddLineAsync(
        Guid orderId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default) =>
        PostAsync(
            FormattableString.Invariant($"/api/orders/{orderId}/lines"),
            new { orderId, productId, quantity },
            cancellationToken);

    /// <summary>Places the order, reserving stock and confirming to the customer.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<ApiResult> SubmitOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        PostAsync(
            FormattableString.Invariant($"/api/orders/{orderId}/submit"),
            new { orderId },
            cancellationToken);

    /// <summary>Cancels an order that has not yet shipped.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="reason">Why. The API requires a non-empty reason.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<ApiResult> CancelOrderAsync(
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default) =>
        PostAsync(
            FormattableString.Invariant($"/api/orders/{orderId}/cancel"),
            new { orderId, reason },
            cancellationToken);

    private static void Append(StringBuilder query, string key, string value)
    {
        if (query[^1] != '?')
        {
            query.Append('&');
        }

        query.Append(key).Append('=').Append(Uri.EscapeDataString(value));
    }

    private async Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            cancellationToken);

        if (response is null)
        {
            return ApiResult<T>.Failure(UnreachableError());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<T>.Failure(await ReadProblemAsync(response, cancellationToken));
            }

            T? value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);

            return value is null
                ? ApiResult<T>.Failure(new ApiError("Api.EmptyBody", "The API returned an empty response.", 200))
                : ApiResult<T>.Success(value);
        }
    }

    private async Task<ApiResult> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions),
            },
            cancellationToken);

        if (response is null)
        {
            return ApiResult.Failure(UnreachableError());
        }

        using (response)
        {
            return response.IsSuccessStatusCode
                ? ApiResult.Success()
                : ApiResult.Failure(await ReadProblemAsync(response, cancellationToken));
        }
    }

    /// <summary>
    /// Sends a request, converting transport failures into <c>null</c> rather than exceptions.
    /// </summary>
    /// <remarks>
    /// The factory delegate exists because <see cref="HttpRequestMessage"/> cannot be reused, and
    /// the resilience pipeline may retry. Passing an already-built message and letting the handler
    /// retry it throws <c>InvalidOperationException</c> on the second attempt - a classic and
    /// well-hidden bug.
    /// </remarks>
    private async Task<HttpResponseMessage?> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(requestFactory(), cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "The LogiFlow API could not be reached at {BaseAddress}.", http.BaseAddress);
            return null;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancellation the caller did not ask for is a timeout. Distinguishing the two
            // matters: a user navigating away is not an incident, a timing-out API is.
            logger.LogError(ex, "The LogiFlow API timed out.");
            return null;
        }
    }

    private async Task<ApiError> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int status = (int)response.StatusCode;

        try
        {
            ProblemDocument? problem = await response.Content
                .ReadFromJsonAsync<ProblemDocument>(SerializerOptions, cancellationToken);

            if (problem is not null)
            {
                return new ApiError(
                    problem.Code ?? FallbackCode(response.StatusCode),
                    problem.Detail ?? problem.Title ?? DefaultMessage(response.StatusCode),
                    status,
                    problem.Errors);
            }
        }
        catch (JsonException)
        {
            // A non-JSON error body - a proxy's HTML 502 page, most often. Fall through to the
            // status-based message rather than showing the user raw markup.
        }
        catch (NotSupportedException)
        {
            // Content-Type was not JSON. Same treatment.
        }

        return new ApiError(FallbackCode(response.StatusCode), DefaultMessage(response.StatusCode), status);
    }

    private static ApiError UnreachableError() => ApiError.Transport(
        "The LogiFlow API is not responding. Start it with `dotnet run` in src/LogiFlow.Api, " +
        "and check that docker compose is up.");

    private static string FallbackCode(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Api.Unauthenticated",
        HttpStatusCode.Forbidden => "Api.Forbidden",
        HttpStatusCode.NotFound => "Api.NotFound",
        HttpStatusCode.Conflict => "Api.Conflict",
        HttpStatusCode.TooManyRequests => "Api.RateLimited",
        _ => "Api.Error",
    };

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        // 401 here almost always means the dev token could not be minted, so the message points
        // at the actual cause rather than telling the user to log in to a UI with no login.
        HttpStatusCode.Unauthorized =>
            "Not authenticated. The API rejected the development token - check that it is running " +
            "in the Development environment.",
        HttpStatusCode.Forbidden => "Your token does not carry the role this action requires.",
        HttpStatusCode.NotFound => "That order no longer exists.",
        HttpStatusCode.Conflict => "The order changed before this action could be applied. Reload and try again.",
        HttpStatusCode.TooManyRequests => "Too many requests. Wait a minute and try again.",
        _ => "The API returned an unexpected error.",
    };
}
