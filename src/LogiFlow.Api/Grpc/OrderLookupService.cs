using System.Globalization;
using Grpc.Core;
using LogiFlow.Application.Common;
using LogiFlow.Application.Features.Orders;
using LogiFlow.Domain.Orders;
using Microsoft.AspNetCore.Authorization;

namespace LogiFlow.Api.Grpc;

/// <summary>
/// The internal, service-to-service view of orders. Same data as the REST endpoints, different
/// wire format and a different set of trade-offs.
/// </summary>
/// <param name="orders">The read side — the same one the REST endpoints use.</param>
/// <remarks>
/// <para>
/// <b>Why this exists next to a perfectly good REST API.</b> Not to replace it. gRPC is for the
/// calls that happen between YOUR services, at volume, where both ends are yours and you can
/// deploy them together. REST is for the calls that come from a browser, a partner, or a curl
/// command written by somebody you will never meet. The two surfaces here share the same query
/// handlers on purpose: the transport differs, the behaviour must not.
/// </para>
/// <para>
/// <b>What you actually gain</b>, in the order that matters:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>A contract that is compiled, not documented.</b> <c>orders.proto</c> generates both the
///     server base class and the client. A field the server stops sending is a compile error in
///     the client's build, not a null in its logs three weeks later.
///   </description></item>
///   <item><description>
///     <b>A smaller, faster wire format.</b> Binary, with field numbers instead of names — the
///     tests in <c>GrpcOrderLookupTests</c> measure it against the JSON rather than asserting it.
///     Roughly a third the bytes here. Worth having; rarely the deciding factor.
///   </description></item>
///   <item><description>
///     <b>Streaming as a first-class shape.</b> <c>StreamOrders</c> is the one thing REST has no
///     good answer to — see the comment on it below.
///   </description></item>
/// </list>
/// <para>
/// <b>What you give up.</b> A browser cannot call this without a proxy (gRPC-Web or Envoy),
/// because the fetch API cannot control HTTP/2 frames. It is unreadable in a log and unpokeable
/// with curl. Every consumer needs the toolchain. And it needs HTTP/2 end to end, so a load
/// balancer or corporate proxy that terminates at HTTP/1.1 breaks it in a way that is genuinely
/// unpleasant to diagnose. Choose it for internal traffic; do not put it on your public edge
/// because it benchmarks well.
/// </para>
/// Covered in: <c>course/module-25-distributed-systems/07-grpc.md</c>
/// </remarks>
[Authorize]
internal sealed class OrderLookupService(IOrderQueries orders) : OrderLookup.OrderLookupBase
{
    /// <inheritdoc />
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(request.OrderId, out Guid id))
        {
            // An RpcException with a status code, not a thrown ArgumentException. gRPC has its
            // own status enum and it is NOT HTTP's — InvalidArgument, NotFound, PermissionDenied,
            // Unauthenticated. Throwing anything else gives the caller `Unknown` plus a stack
            // trace it cannot act on, which is the gRPC equivalent of returning 500 for a
            // validation failure.
            throw new RpcException(new Status(StatusCode.InvalidArgument, "order_id is not a GUID."));
        }

        OrderDetailDto order = await orders.GetDetailAsync(new OrderId(id), context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "No such order."));

        return new OrderReply
        {
            OrderId = order.Id.ToString(),
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId.ToString(),
            CustomerName = order.CustomerName,
            Status = order.Status.ToString(),
            TotalAmount = order.Total.ToString(CultureInfo.InvariantCulture),
            Currency = order.Currency,
            LineCount = order.Lines.Count,
            CreatedAtUtc = order.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),

            // proto3 cannot express "absent" for a scalar, so null becomes "". Documented in the
            // .proto next to the field, because a consumer will otherwise read the empty string
            // as a real value at some point.
            SubmittedAtUtc = order.SubmittedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <inheritdoc />
    public override async Task StreamOrders(
        StreamOrdersRequest request,
        IServerStreamWriter<OrderReply> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        // ── Why streaming is the interesting part ────────────────────────────────────────
        // The REST version of this is GET /api/v1/orders?page=n, and the client loops: request,
        // wait for the full page to be built and serialized, parse it, ask for the next one.
        // Every page is a round trip, and the server holds a whole page in memory to serialize
        // it.
        //
        // Here the client makes ONE call and rows arrive as they are written. Nothing is
        // buffered, the connection stays open, and a slow consumer applies backpressure through
        // HTTP/2 flow control without anybody writing backpressure code.
        //
        // The honest caveat: this still reads a page at a time from the database, so it is not
        // streaming all the way down. Making it so would mean IAsyncEnumerable through the query
        // layer — which is the right next step and is left as an exercise rather than pretended.
        int remaining = Math.Clamp(request.Max, 1, 500);
        int page = 1;

        while (remaining > 0 && !context.CancellationToken.IsCancellationRequested)
        {
            PagedResult<OrderSummaryDto> batch = await orders.SearchAsync(
                new SearchOrdersQuery { Page = page, PageSize = Math.Min(remaining, 50) },
                context.CancellationToken);

            if (batch.Items.Count == 0)
            {
                break;
            }

            foreach (OrderSummaryDto order in batch.Items)
            {
                await responseStream.WriteAsync(
                    new OrderReply
                    {
                        OrderId = order.Id.ToString(),
                        OrderNumber = order.OrderNumber,
                        CustomerId = order.CustomerId.ToString(),
                        CustomerName = order.CustomerName,
                        Status = order.Status.ToString(),
                        TotalAmount = order.TotalAmount.ToString(CultureInfo.InvariantCulture),
                        Currency = order.Currency,
                        LineCount = order.LineCount,
                        CreatedAtUtc = order.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                        SubmittedAtUtc = order.SubmittedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                    },
                    context.CancellationToken);

                remaining--;
            }

            if (!batch.HasNext)
            {
                break;
            }

            page++;
        }
    }
}
