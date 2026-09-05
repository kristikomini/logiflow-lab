using LogiFlow.Api.Infrastructure;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Features.Reporting;
using LogiFlow.Domain.Results;

namespace LogiFlow.Api.Endpoints;

/// <summary>
/// Version 2 of the sales report — the one endpoint in this API whose shape changed.
/// </summary>
/// <remarks>
/// <para>
/// A versioning scheme with only one version is a scheme nobody has tested. This exists so the
/// mechanism in <see cref="ApiVersioning"/> has something real to carry, and because the change
/// it makes is the single most common breaking change in a REST API's life.
/// </para>
/// <para>
/// <b>v1 returns a bare JSON array.</b> <c>[ { "period": ..., "revenue": ... }, ... ]</c>. It is
/// the obvious thing to write and it is a dead end, for a reason that only shows up later: there
/// is nowhere to put anything else. The day somebody asks for the total across the window, or
/// for paging because a two-year daily report is 730 objects, or for the currency the amounts
/// are in, the answer is a new top-level shape — and a client doing <c>response.map(...)</c>
/// breaks on the first byte.
/// </para>
/// <para>
/// <b>v2 returns an envelope.</b> The rows move under <c>data</c>, and the things the report is
/// *about* — the window, the bucket size, the total — sit beside them. Every future addition is
/// then a new property on an object, which is the one kind of change a JSON consumer tolerates
/// by default. The rule generalises: <b>never return a bare array from an endpoint you intend
/// to keep</b>. (There is a second, historical reason — a top-level JSON array was once
/// executable as JavaScript, which made it a cross-site data-leak vector. Modern browsers
/// closed that, but the design argument was correct for its own sake and outlived the exploit.)
/// </para>
/// <para>
/// Note what does <b>not</b> change: the query, the handler, the domain. This is entirely a
/// presentation concern, which is exactly why it lives in the API layer and reshapes what the
/// dispatcher already returns. If a version bump forces a change in Application or Domain, the
/// version is not what changed — the requirement is.
/// </para>
/// Covered in: <c>course/module-15-aspnetcore-in-depth/02-api-versioning.md</c>
/// </remarks>
public static class ReportingEndpointsV2
{
    /// <summary>Registers the v2 <c>reports</c> routes.</summary>
    /// <param name="app">The version group to map onto.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapReportingEndpointsV2(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app
            .MapGroup("/reports")
            .WithTags("Reporting")
            .RequireAuthorization(AuthorizationPolicies.Analyst);

        group.MapGet("/sales", async (
                DateTimeOffset from,
                DateTimeOffset to,
                SalesPeriod period,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result<IReadOnlyList<SalesByPeriodDto>> result = await dispatcher.SendAsync(
                    new GetSalesReportQuery(from, to, period),
                    cancellationToken);

                // Reshape the success case only. The failure path stays the shared one in
                // ResultExtensions, so a validation error looks identical on both versions —
                // the changed success body is the whole scope of the version bump, and a
                // version that also changes the error contract is two changes wearing one coat.
                Result<SalesReportResponse> shaped = result.IsSuccess
                    ? Result.Success(SalesReportResponse.Create(from, to, period, result.Value))
                    : Result.Failure<SalesReportResponse>(result.Error);

                return shaped.ToHttpResult();
            })
            .WithName("GetSalesReportV2")
            .WithSummary("Revenue and volume, bucketed — in an envelope that can grow.")
            .Produces<SalesReportResponse>()
            .ProducesValidationProblem();

        return app;
    }
}

/// <summary>The v2 sales report body.</summary>
/// <param name="Data">The rows, exactly as v1 returned them at the top level.</param>
/// <param name="From">Start of the window that was asked for.</param>
/// <param name="To">End of the window that was asked for.</param>
/// <param name="Period">The bucket size the rows are grouped by.</param>
/// <param name="Count">How many rows came back, so a caller need not measure the array.</param>
/// <param name="TotalRevenue">The sum across the window — the field v1 had nowhere to put.</param>
/// <param name="Currency">ISO code the monetary fields are in. Per report, not per row.</param>
public sealed record SalesReportResponse(
    IReadOnlyList<SalesByPeriodDto> Data,
    DateTimeOffset From,
    DateTimeOffset To,
    SalesPeriod Period,
    int Count,
    decimal TotalRevenue,
    string Currency)
{
    /// <summary>Wraps the v1 rows in the v2 envelope. Named Create, not From — the record already has a From property.</summary>
    /// <param name="from">Start of the window.</param>
    /// <param name="to">End of the window.</param>
    /// <param name="period">The bucket size.</param>
    /// <param name="rows">The rows the query produced.</param>
    /// <returns>The envelope.</returns>
    public static SalesReportResponse Create(
        DateTimeOffset from,
        DateTimeOffset to,
        SalesPeriod period,
        IReadOnlyList<SalesByPeriodDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return new SalesReportResponse(
            rows,
            from,
            to,
            period,
            rows.Count,
            rows.Sum(r => r.TotalRevenue),
            // Every row carries the same currency, so it is a property of the report, not of a
            // row — v1 repeated it 730 times in a two-year daily report. An empty window has no
            // currency to report at all, which is a more honest answer than guessing "EUR".
            rows.Count > 0 ? rows[0].Currency : string.Empty);
    }
}
