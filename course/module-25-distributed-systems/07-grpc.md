# 7. gRPC, and when it is the right answer

> Part of [Module 25 — Distributed systems and integration](README.md).
> Previous: [6. Pushing to a browser: SignalR, and where it belongs](06-real-time.md)

---

The interview question is "when would you use gRPC instead of REST?", and the answer that gets
marked down is "it's faster". It is faster. That is rarely why you would choose it, and saying so
first suggests you have read a benchmark rather than shipped a service.

The reason is the **contract**.

## What actually changes

With JSON over HTTP, the contract between two services is a document — an OpenAPI file, a wiki
page, a Slack message — that both sides have agreed to follow. Nothing enforces it. A field the
server stops sending becomes a `null` in the client, at runtime, in production, three weeks later.

With gRPC the contract is a file that is **compiled into both ends**:

```proto
service OrderLookup {
  rpc GetOrder (GetOrderRequest) returns (OrderReply);
  rpc StreamOrders (StreamOrdersRequest) returns (stream OrderReply);
}
```

`Grpc.Tools` runs `protoc` during the build and generates C# from it. There is no `OrderReply.cs`
in this repository to open — it does not exist until you build. Change the `.proto`, rebuild, and
the call sites that no longer match **fail to compile**. That is the feature. Everything else is
a consequence.

## Field numbers are the wire format

```proto
message OrderReply {
  string order_id = 1;
  string order_number = 2;
  ...
}
```

`1` and `2` are what is transmitted. The *names* exist for humans and code generators and are not
on the wire at all — which is most of where the size saving comes from, since JSON ships every
property name in every object of every array.

The rule that follows is absolute: **a field can be renamed freely and can never be renumbered or
reused.** Reusing a retired number means old bytes are silently reinterpreted as a different field.
Nothing throws. The value is just wrong, in a service you did not deploy.

Numbers 1–15 encode in one byte and 16+ in two, so the fields on your hot message go first.

## Three things proto3 cannot say

**No decimal.** `double` loses precision on exactly the values money cares about, and int64-of-cents
requires both ends to agree on the scale forever. This codebase sends money as a **string**, which
round-trips C#'s `decimal` exactly and costs a parse. `GrpcOrderLookupTests` asserts that round trip
specifically, so that "simplifying" it to a double fails a test instead of a reconciliation.

**No nullable scalars.** A missing string is `""` and a missing number is `0`, indistinguishable
from a real empty string or a real zero. An unsubmitted order therefore sends `""` for
`submitted_at_utc`. That is a limitation, not a design choice, and `google.protobuf.Timestamp`
exists for the cases where the difference matters.

**No native UUID.** Ids go as strings.

## Streaming is the part REST has no answer to

```proto
rpc StreamOrders (StreamOrdersRequest) returns (stream OrderReply);
```

The REST version is `GET /orders?page=n` in a loop: request, wait for the whole page to be built
and serialized, parse it, ask for the next. Every page is a round trip, and the server holds a
whole page in memory to serialize it.

The gRPC version is one call, with rows arriving as they are written. Nothing is buffered, and a
slow consumer applies backpressure through HTTP/2 flow control without anyone writing backpressure
code.

> **Where this implementation stops being honest, said out loud.** `StreamOrders` still reads a
> page at a time from the database, so it streams to the client but not from the database.
> Streaming all the way down means `IAsyncEnumerable` through the query layer. That is the right
> next step and it is left as an exercise rather than pretended.

## Status codes are not HTTP's

```csharp
throw new RpcException(new Status(StatusCode.InvalidArgument, "order_id is not a GUID."));
```

gRPC has its own status enum — `InvalidArgument`, `NotFound`, `PermissionDenied`, `Unauthenticated`,
`DeadlineExceeded`. Throwing anything else hands the caller `Unknown` plus a stack trace it cannot
branch on, which is the gRPC equivalent of returning 500 for a validation failure.

## What you give up

Be able to say this part; it is what separates an opinion from a preference.

- **A browser cannot call it** without a proxy (gRPC-Web, Envoy). The fetch API cannot control
  HTTP/2 frames.
- **You cannot curl it**, and it is unreadable in a log or a packet capture. Debugging by eye is
  gone.
- **Every consumer needs the toolchain**, which is a real ask of a partner integrating with you.
- **It needs HTTP/2 end to end.** A load balancer or corporate proxy that terminates at HTTP/1.1
  breaks it, and the error names neither HTTP/2 nor the proxy.

That last one has a local version worth knowing, because it costs people an evening: Kestrel
negotiates HTTP/2 automatically **over TLS**, via ALPN. Over plain HTTP there is no ALPN, so it has
to be told:

```json
"Kestrel": { "EndpointDefaults": { "Protocols": "Http1AndHttp2" } }
```

Without it, gRPC fails over `http://` and works over `https://`, which is a genuinely confusing
pair of symptoms.

## So: when?

**Use gRPC** between your own services, at volume, where both ends are yours and deploy together —
and especially where you want the compiler to police the contract.

**Use REST** at the edge: browsers, partners, anyone who will curl it, anything that has to be
readable in a log or cached by an intermediary.

`LogiFlow.Api` does both, over the same query handlers. The transport differs; the behaviour must
not. `GrpcOrderLookupTests` asserts the two surfaces agree, which is the test that stops them
drifting into two implementations of the same feature.

## Try it

```bash
dotnet test tests/LogiFlow.Api.IntegrationTests --filter GrpcOrderLookupTests
```

One of those tests prints the two payload sizes for the same order. It asserts only the direction,
not a ratio — the REST DTO is genuinely richer (lines, address, allowed transitions), so it is not
a like-for-like comparison, and a test pretending otherwise would be the dishonest kind.

## What to remember

1. **The contract is compiled, not documented.** That is the reason; speed is a side effect.
2. **Field numbers are the wire.** Rename freely, never renumber, never reuse.
3. **proto3 has no decimal and no nullable scalars.** Money goes as a string here, and the reason
   is written next to it.
4. **Streaming is the capability REST lacks**, and it is the one that changes designs.
5. **Internal traffic, not your public edge.** A browser cannot call it and neither can curl.
